using System.Globalization;
using System.Text.RegularExpressions;

namespace KosZawal;

public static partial class MetaDates
{
    private static readonly Regex AspNetDate = AspNetDateRegex();

    public static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "—";

        var match = AspNetDate.Match(raw);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var millis))
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(millis).ToLocalTime();
            return date.ToString("yyyy-MM-dd HH:mm", CultureInfo.GetCultureInfo("pl-PL"));
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.GetCultureInfo("pl-PL"));

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            if (number > 10_000_000_000)
                return DateTimeOffset.FromUnixTimeMilliseconds(number).ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.GetCultureInfo("pl-PL"));
            if (number > 1_000_000_000)
                return DateTimeOffset.FromUnixTimeSeconds(number).ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.GetCultureInfo("pl-PL"));
        }

        return raw;
    }

    [GeneratedRegex(@"^/Date\((-?\d+)(?:[+-]\d+)?\)/$")]
    private static partial Regex AspNetDateRegex();
}
