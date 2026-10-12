using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace KosZawal;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public async Task BootAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var started = DateTime.UtcNow;
        try
        {
            var database = new NfzDatabase(AppPaths.DatabaseFile());
            ApplySeed(database);
            if (!database.HasData)
            {
                SetStatus("Pobieram raport NFZ. To może potrwać około minuty.");
                var progress = new Progress<SyncProgress>(update => SetStatus(update.Detail));
                await new NfzSync().RunAsync(database, progress, CancellationToken.None);
            }
            else
            {
                SetStatus("Otwieram lokalną bazę.");
            }

            var wait = TimeSpan.FromMilliseconds(900) - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait);

            var main = new MainWindow();
            main.ShowData(database);
            desktop.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
    }

    private static void ApplySeed(NfzDatabase database)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://KosZawal/Assets/hospitals.json"));
            var hits = JsonSerializer.Deserialize<List<SeedHospital>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? [];
            database.ApplyGeocodes(hits
                .Where(hit => hit.Lat is not null && hit.Lon is not null && hit.Woj is not null && hit.Name is not null)
                .Select(hit => new GeoHit(hit.Woj!, hit.Name!, hit.Lat!.Value, hit.Lon!.Value, hit.Quality ?? "nominatim")));
        }
        catch
        {
            // Mapa użyje środka województwa, gdy brak słownika współrzędnych.
        }
    }

    private sealed class SeedHospital
    {
        public string? Woj { get; set; }
        public string? Name { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
        public string? Quality { get; set; }
    }
}
