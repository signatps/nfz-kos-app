using Avalonia.Controls;
using Avalonia.Data;
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
            return;
        }

        if (extension == "xlsx" && bundle.Rows.Count > TableExporter.XlsxRowLimit)
        {
            model.Status = $"Ten wynik ma {bundle.Rows.Count:N0} wierszy. XLSX zapisuje do {TableExporter.XlsxRowLimit:N0}. Zawęź filtry albo wybierz CSV.";
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
        if (extension == "csv")
            TableExporter.WriteCsv(path, bundle.Dataset, bundle.Rows);
        else
            TableExporter.WriteXlsx(path, bundle.Dataset, bundle.Rows);
        model.Status = $"Zapisano {bundle.Rows.Count:N0} wierszy do {path}";
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
