using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace KosZawal;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void ShowData(NfzDatabase database)
    {
        var model = new MainViewModel(database);
        DataContext = model;
        model.TableChanged += RebuildGrid;
        model.Load();
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (DataContext is not MainViewModel model)
            return;
        TableGrid.Columns.Clear();
        for (var i = 0; i < model.Headers.Length; i++)
        {
            var index = i;
            TableGrid.Columns.Add(new DataGridTextColumn
            {
                Header = model.Headers[i],
                Binding = new Binding($"Cells[{index}]"),
                Width = new DataGridLength(160),
            });
        }
    }

    private void GoNav(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && DataContext is MainViewModel model)
        {
            var item = model.Nav.First(nav => nav.Id == id);
            model.Activate(item);
        }
    }

    private void SelectHospitalFromList(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key } && DataContext is MainViewModel model)
            model.SelectedHospitalKey = key;
    }

    private async void SaveCsv(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SaveAsync("csv");

    private async void SaveXlsx(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SaveAsync("xlsx");

    private async Task SaveAsync(string extension)
    {
        if (DataContext is not MainViewModel model)
            return;
        var bundle = model.BuildExport();
        if (bundle is null)
        {
            model.Status = "Najpierw pobierz dane.";
            await ShowNoticeAsync("Eksport", "Najpierw pobierz dane z raportu NFZ.");
            return;
        }

        if (extension == "xlsx" && bundle.Rows.Count > TableExporter.XlsxRowLimit)
        {
            var warning =
                $"Ten wynik ma {bundle.Rows.Count:N0} wierszy. XLSX zapisuje do {TableExporter.XlsxRowLimit:N0}. Zawęź filtry albo wybierz CSV.";
            model.Status = warning;
            await ShowNoticeAsync("Eksport XLSX", warning);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Eksport danych NFZ",
            SuggestedFileName = $"kos-zawal-{bundle.Dataset.Id}.{extension}",
            FileTypeChoices =
            [
                new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] },
            ],
        });
        if (file is null)
            return;

        var path = file.Path.LocalPath;
        try
        {
            if (extension == "csv")
                TableExporter.WriteCsv(path, bundle.Dataset, bundle.Rows);
            else
                TableExporter.WriteXlsx(path, bundle.Dataset, bundle.Rows);
        }
        catch (Exception ex)
        {
            model.Status = "Błąd zapisu: " + ex.Message;
            await ShowNoticeAsync("Eksport nieudany", ex.Message);
            return;
        }

        var message = $"Zapisano {bundle.Rows.Count:N0} wierszy zbioru „{bundle.Dataset.Title}” do:\n{path}";
        model.Status = message.Replace('\n', ' ');
        await ShowNoticeAsync("Eksport zakończony", message);
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#F3F0E8")),
        };
        var ok = new Button
        {
            Content = "OK",
            Classes = { "action" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
        };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new Border
        {
            Padding = new Avalonia.Thickness(22),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22,
                    },
                    ok,
                },
            },
        };
        await dialog.ShowDialog(this);
    }
}

public sealed class BarWidthConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BarWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var ratio = value is double number ? number : 0;
        return Math.Max(6, ratio * 220);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
