namespace KosZawal;

public static class SyncConsole
{
    public static async Task RunAsync()
    {
        var database = new NfzDatabase(AppPaths.DatabaseFile());
        var progress = new Progress<SyncProgress>(update =>
            Console.WriteLine($"[{update.Stage}] {update.Detail}"));
        await new NfzSync().RunAsync(database, progress, CancellationToken.None);
        var year = database.Years().FirstOrDefault();
        if (year == 0)
        {
            Console.WriteLine("Brak danych.");
            return;
        }

        var kpi = database.Compute(year, ReportCatalog.InfarctAll);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        Console.WriteLine($"Rok {kpi.Year}");
        Console.WriteLine($"Odsetek krajowy: {kpi.NationalShare.ToString("0.0%", culture)}");
        Console.WriteLine($"Hospitalizacje KOS: {kpi.KosHospitalizations.ToString("N0", culture)} / {kpi.Hospitalizations.ToString("N0", culture)}");
        Console.WriteLine($"Ośrodki: {kpi.Centers}");
        Console.WriteLine($"Województwa: {kpi.VoivodeshipsWithCenters}");
        foreach (var row in kpi.ByVoivodeship)
            Console.WriteLine($"  {row.Centers,3}  {row.Name}  {row.Share.ToString("0.0%", culture)}");
    }
}
