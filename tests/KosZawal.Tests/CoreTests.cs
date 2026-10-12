using System.Text.Json;
using KosZawal;
using Xunit;

namespace KosZawal.Tests;

public class DsrParserTests
{
    [Fact]
    public void RepeatMaskKeepsEarlierColumns()
    {
        const string json = """
            {
              "ValueDicts": { "D0": ["Dolnośląskie", "Mazowieckie"], "D1": ["NIE", "TAK"] },
              "PH": [{
                "DM0": [
                  {"S":[{"N":"G0","T":4},{"N":"G1","T":1,"DN":"D0"},{"N":"G2","T":4},{"N":"G3","T":1,"DN":"D1"}], "C":[2025, 0, 100, 1]},
                  {"C":[80], "R":11}
                ]
              }],
              "IC": true
            }
            """;
        using var document = JsonDocument.Parse(json);
        var rows = DsrParser.Parse(document.RootElement);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2025L, rows[0][0]);
        Assert.Equal("Dolnośląskie", rows[0][1]);
        Assert.Equal(100L, rows[0][2]);
        Assert.Equal("TAK", rows[0][3]);
        Assert.Equal(2025L, rows[1][0]);
        Assert.Equal("Dolnośląskie", rows[1][1]);
        Assert.Equal(80L, rows[1][2]);
        Assert.Equal("TAK", rows[1][3]);
        Assert.True(DsrParser.IsComplete(document.RootElement));
    }
}

public class MetaDatesTests
{
    [Fact]
    public void FormatsAspNetJsonDate()
    {
        var text = MetaDates.Format("/Date(1782197836247)/");
        Assert.DoesNotContain("/Date", text);
        Assert.Contains("2026", text);
    }
}

public class KpiTests
{
    [Fact]
    public void NationalShareUsesAllHospitalizations()
    {
        var path = Path.Combine(Path.GetTempPath(), "kos-test-" + Guid.NewGuid().ToString("N") + ".db");
        using var database = new NfzDatabase(path);
        using var transaction = database.BeginSync();
        database.InsertPage(
            ReportCatalog.ById("placowki"),
            ReportCatalog.PlacowkiColumns.Select(column => column.Source).ToList(),
            [
                Row(2025, "Mazowieckie", "Szpital A", 100, "TAK"),
                Row(2025, "Mazowieckie", "Szpital B", 300, "NIE"),
            ],
            transaction);
        database.InsertPage(
            ReportCatalog.ById("kos"),
            ReportCatalog.KosColumns.Select(column => column.Source).ToList(),
            [
                KosRow(2025, "Mazowieckie", "Szpital A", 100, 0.4),
            ],
            transaction);
        transaction.Commit();

        var kpi = database.Compute(2025, ReportCatalog.InfarctAll);
        Assert.Equal(400, kpi.Hospitalizations);
        Assert.Equal(40, kpi.KosHospitalizations, 3);
        Assert.Equal(0.10, kpi.NationalShare, 3);
        Assert.Equal(0.40, kpi.ShareAmongKosCenters, 3);
        Assert.Equal(1, kpi.Centers);
        Assert.Equal(1, kpi.VoivodeshipsWithCenters);
        Assert.Equal(1, kpi.ByVoivodeship.Single(row => row.Name == "Mazowieckie").Centers);
    }

    private static object?[] Row(int year, string woj, string name, int hosp, string kos)
    {
        var values = new object?[ReportCatalog.PlacowkiColumns.Count];
        values[0] = year;
        values[1] = woj;
        values[2] = name;
        values[3] = ReportCatalog.InfarctAll;
        values[4] = hosp;
        values[13] = kos;
        return values;
    }

    private static object?[] KosRow(int year, string woj, string name, int hosp, double share)
    {
        var values = new object?[ReportCatalog.KosColumns.Count];
        values[0] = year;
        values[1] = woj;
        values[2] = name;
        values[3] = ReportCatalog.InfarctAll;
        values[4] = hosp;
        values[5] = share;
        return values;
    }
}
