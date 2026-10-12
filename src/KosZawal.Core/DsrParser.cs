using System.Globalization;
using System.Text.Json;

namespace KosZawal;

/// <summary>
/// Decoder for the compressed result sets Power BI returns from public reports.
/// A bit set in R means “repeat this column from the previous row”.
/// A bit set in Ø means the column is null and is absent from C.
/// Dictionary indexes are resolved through ValueDicts.
/// </summary>
public static class DsrParser
{
    public static List<object?[]> Parse(JsonElement dataSource)
    {
        var dictionaries = new Dictionary<string, JsonElement[]>(StringComparer.Ordinal);
        if (dataSource.TryGetProperty("ValueDicts", out var dictsElement))
        {
            foreach (var property in dictsElement.EnumerateObject())
            {
                var values = new List<JsonElement>();
                foreach (var item in property.Value.EnumerateArray())
                    values.Add(item.Clone());
                dictionaries[property.Name] = values.ToArray();
            }
        }

        if (!dataSource.TryGetProperty("PH", out var primary) || primary.GetArrayLength() == 0)
            return [];

        var hierarchy = primary[0];
        if (!hierarchy.TryGetProperty("DM0", out var nodes))
            return [];

        JsonElement[]? schema = null;
        object?[]? current = null;
        var rows = new List<object?[]>();

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("S", out var schemaElement))
            {
                schema = schemaElement.EnumerateArray().Select(item => item.Clone()).ToArray();
                current ??= new object?[schema.Length];
            }

            if (schema is null || current is null)
                continue;

            var repeatMask = ReadInt(node, "R");
            var nullMask = ReadInt(node, "Ø");
            if (nullMask == 0)
                nullMask = ReadInt(node, "\u00d8");

            var values = new List<JsonElement>();
            if (node.TryGetProperty("C", out var packed))
            {
                foreach (var item in packed.EnumerateArray())
                    values.Add(item);
            }

            var row = (object?[])current.Clone();
            var cursor = 0;
            for (var column = 0; column < schema.Length; column++)
            {
                if ((repeatMask & (1 << column)) != 0)
                    continue;

                if ((nullMask & (1 << column)) != 0)
                {
                    row[column] = null;
                    continue;
                }

                if (cursor >= values.Count)
                {
                    row[column] = null;
                    continue;
                }

                var raw = values[cursor++];
                string? dictionaryName = null;
                if (schema[column].TryGetProperty("DN", out var dictionaryProperty))
                    dictionaryName = dictionaryProperty.GetString();

                row[column] = Decode(raw, dictionaryName, dictionaries);
            }

            current = row;
            rows.Add((object?[])row.Clone());
        }

        return rows;
    }

    public static bool IsComplete(JsonElement dataSource)
    {
        if (!dataSource.TryGetProperty("IC", out var flag))
            return true;
        return flag.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => true,
        };
    }

    public static JsonElement? RestartTokens(JsonElement dataSource)
    {
        if (!dataSource.TryGetProperty("RT", out var tokens))
            return null;
        return tokens.Clone();
    }

    private static object? Decode(JsonElement raw, string? dictionaryName, Dictionary<string, JsonElement[]> dictionaries)
    {
        if (!string.IsNullOrEmpty(dictionaryName)
            && dictionaries.TryGetValue(dictionaryName, out var dictionary)
            && raw.ValueKind == JsonValueKind.Number
            && raw.TryGetInt32(out var index)
            && index >= 0
            && index < dictionary.Length)
        {
            raw = dictionary[index];
        }

        return raw.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => raw.GetString(),
            JsonValueKind.Number when raw.TryGetInt64(out var whole) => whole,
            JsonValueKind.Number => raw.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => raw.ToString(),
        };
    }

    private static int ReadInt(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }
}
