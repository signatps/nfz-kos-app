using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace KosZawal;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("pl-PL");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("Task", e.Exception);
            e.SetObserved();
        };

        if (args.Contains("--sync"))
        {
            SyncConsole.RunAsync().GetAwaiter().GetResult();
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .AfterSetup(_ =>
                {
                    Dispatcher.UIThread.UnhandledException += (_, e) =>
                    {
                        WriteCrash("UI", e.Exception);
                        e.Handled = true;
                        TryShowError(e.Exception);
                    };
                })
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrash("Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrash(string source, Exception? ex)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KosZawal");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {source}: {ex}\n\n");
        }
        catch
        {
            // Ostatnia deska ratunku — nie wolno wywalić handlera.
        }
    }

    private static void TryShowError(Exception ex)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var owner = desktop.MainWindow;
            if (owner is null)
                return;

            var dialog = new Avalonia.Controls.Window
            {
                Title = "KOS-zawał — błąd",
                Width = 560,
                Height = 260,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.TextBlock
                {
                    Text = "Wystąpił błąd (zapisano też w %LOCALAPPDATA%\\KosZawal\\crash.log):\n\n" + ex.Message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(20),
                },
            };
            _ = dialog.ShowDialog(owner);
        }
        catch
        {
            // Ignore.
        }
    }
}
