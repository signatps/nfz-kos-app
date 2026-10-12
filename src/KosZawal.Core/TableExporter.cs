using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace KosZawal;

public static class TableExporter
{
    public const int XlsxRowLimit = 80_000;

    public static void WriteCsv(string path, DatasetDefinition dataset, IEnumerable<object?[]> rows)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(";", dataset.Columns.Select(column => Csv(column.Header))));
        foreach (var row in rows)
        {
            var cells = new string[dataset.Columns.Count];
            for (var i = 0; i < dataset.Columns.Count && i < row.Length; i++)
                cells[i] = Csv(Format(row[i], dataset.Columns[i].Kind, excel: false));
            writer.WriteLine(string.Join(";", cells));
        }
    }

    public static void WriteXlsx(string path, DatasetDefinition dataset, IReadOnlyList<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(dataset.Title.Length > 31 ? dataset.Title[..31] : dataset.Title);
        for (var column = 0; column < dataset.Columns.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = dataset.Columns[column].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16324F");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var column = 0; column < dataset.Columns.Count && column < row.Length; column++)
            {
                var kind = dataset.Columns[column].Kind;
                var cell = sheet.Cell(rowIndex + 2, column + 1);
                var number = NfzDatabase.ToNumber(row[column]);
                if (kind is ColumnKind.Number or ColumnKind.Integer or ColumnKind.Percent && number is not null)
                {
                    cell.Value = number.Value;
                    cell.Style.NumberFormat.Format = kind == ColumnKind.Percent ? "0.0%" : "#,##0.00";
                }
                else
                {
                    cell.Value = Format(row[column], kind, excel: true);
                }
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 40);
        workbook.SaveAs(path);
    }

    public static string Format(object? value, ColumnKind kind, bool excel)
    {
        if (value is null || value is DBNull)
            return "";

        var culture = CultureInfo.GetCultureInfo("pl-PL");
        if (kind == ColumnKind.Percent && NfzDatabase.ToNumber(value) is double percent)
            return excel ? percent.ToString("0.0%", culture) : percent.ToString("0.0%", culture);

        if (kind is ColumnKind.Number or ColumnKind.Integer && NfzDatabase.ToNumber(value) is double number)
        {
            if (kind == ColumnKind.Integer || Math.Abs(number - Math.Round(number)) < 0.0000001)
                return Math.Round(number).ToString("N0", culture);
            return number.ToString("N2", culture);
        }

        return Convert.ToString(value, culture) ?? "";
    }

    private static string Csv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
