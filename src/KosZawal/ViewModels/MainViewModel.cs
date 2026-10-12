using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KosZawal;

public partial class NavItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Kind { get; init; }
    [ObservableProperty] private bool isActive;
}

public sealed class RegionRow
{
    public required string Name { get; init; }
    public required int Centers { get; init; }
    public required string ShareText { get; init; }
    public required double Bar { get; init; }
}

public sealed class MapSideItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
}

public sealed class GridRow
{
    public GridRow(string[] cells) => Cells = cells;
    public string[] Cells { get; }
}

public partial class MainViewModel : ObservableObject
{
    private readonly NfzDatabase _database;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pl-PL");
    private bool _loading;

    public MainViewModel(NfzDatabase database)
    {
        _database = database;
        Nav =
        [
            new NavItem { Id = "summary", Label = "Start", Kind = "heart" },
            new NavItem { Id = "placowki", Label = "Placówki", Kind = "hospital" },
            new NavItem { Id = "kos", Label = "KOS", Kind = "contract" },
            new NavItem { Id = "trendy", Label = "Trendy", Kind = "trend" },
            new NavItem { Id = "mapa", Label = "Mapa", Kind = "map" },
            new NavItem { Id = "wojewodztwa", Label = "Woj.", Kind = "regions" },
            new NavItem { Id = "metodyka", Label = "Metodyka", Kind = "book" },
            new NavItem { Id = "eksport", Label = "Eksport", Kind = "export" },
        ];
        Current = Nav[0];
        ExportDataset = "Placówki";
    }

    public ObservableCollection<NavItem> Nav { get; }
    public ObservableCollection<int> Years { get; } = [];
    public ObservableCollection<string> VoivodeshipOptions { get; } = [];
    public ObservableCollection<GridRow> Rows { get; } = [];
    public ObservableCollection<RegionRow> RegionRows { get; } = [];
    public ObservableCollection<HospitalPin> Pins { get; } = [];
    public ObservableCollection<MapSideItem> MapSideList { get; } = [];
    public string[] InfarctTypes { get; } = ReportCatalog.InfarctTypes;
    public string[] ExportChoices { get; } =
    [
        "Placówki", "KOS-zawał", "Trendy", "Województwa", "Podsumowanie KPI",
    ];

    [ObservableProperty] private NavItem current;
    [ObservableProperty] private int year;
    [ObservableProperty] private string infarctType = ReportCatalog.InfarctAll;
    [ObservableProperty] private string voivodeshipOption = "Cała Polska";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool trendsAggregateOnly = true;
    [ObservableProperty] private bool exportUsesFilters = true;
    [ObservableProperty] private string exportDataset = "Placówki";
    [ObservableProperty] private KpiSnapshot? kpi;
    [ObservableProperty] private string provenance = "";
    [ObservableProperty] private string methodology = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string tableCaption = "";
    [ObservableProperty] private string mapHover = "Najedź na województwo albo ośrodek.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string[] headers = [];

    public string ShareText => Kpi is null ? "—" : Kpi.NationalShare.ToString("0.0%", _culture);
    public string CentersText => Kpi is null ? "—" : Kpi.Centers.ToString("N0", _culture);
    public string RegionsText => Kpi is null ? "—" : Kpi.VoivodeshipsWithCenters.ToString("N0", _culture);
    public string CasesText => Kpi is null
        ? "—"
        : $"{Kpi.KosHospitalizations.ToString("N0", _culture)} z {Kpi.Hospitalizations.ToString("N0", _culture)}";
    public string AmongCentersText => Kpi is null ? "—" : Kpi.ShareAmongKosCenters.ToString("0.0%", _culture);
    public bool IsSummary => Current.Id == "summary";
    public bool IsTable => Current.Id is "placowki" or "kos" or "trendy" or "wojewodztwa";
    public bool IsMap => Current.Id == "mapa";
    public bool IsMethodology => Current.Id == "metodyka";
    public bool IsExport => Current.Id == "eksport";
    public bool IsTrends => Current.Id == "trendy";
    public IReadOnlyList<MonthPoint> Monthly => Kpi?.Monthly ?? [];
    public IReadOnlyList<VoivodeshipStat> RegionStats => Kpi?.ByVoivodeship ?? [];

    public event Action? TableChanged;

    public void Activate(NavItem item)
    {
        Current = item;
        foreach (var nav in Nav)
            nav.IsActive = nav == item;
        NotifyPages();
        ReloadPage();
    }

    public void Load()
    {
        _loading = true;
        Years.Clear();
        foreach (var value in _database.Years())
            Years.Add(value);
        if (Years.Count > 0 && !Years.Contains(Year))
            Year = Years.Contains(2025) ? 2025 : Years[0];

        VoivodeshipOptions.Clear();
        VoivodeshipOptions.Add("Cała Polska");
        foreach (var name in _database.Voivodeships())
            VoivodeshipOptions.Add(name);
        if (!VoivodeshipOptions.Contains(VoivodeshipOption))
            VoivodeshipOption = "Cała Polska";

        var refreshed = MetaDates.Format(_database.Meta("refreshed_at"));
        var synced = MetaDates.Format(_database.Meta("synced_at"));
        Provenance = $"Raport NFZ „zawały”. Odświeżenie modelu: {refreshed}. Pobrano lokalnie: {synced}.";
        Methodology = BuildMethodology();
        _loading = false;
        Activate(Current);
    }

    partial void OnYearChanged(int value) => ReloadPage();
    partial void OnInfarctTypeChanged(string value) => ReloadPage();
    partial void OnVoivodeshipOptionChanged(string value) => ReloadPage();
    partial void OnSearchTextChanged(string value) => ReloadPage();
    partial void OnTrendsAggregateOnlyChanged(bool value) => ReloadPage();

    partial void OnKpiChanged(KpiSnapshot? value)
    {
        OnPropertyChanged(nameof(ShareText));
        OnPropertyChanged(nameof(CentersText));
        OnPropertyChanged(nameof(RegionsText));
        OnPropertyChanged(nameof(CasesText));
        OnPropertyChanged(nameof(AmongCentersText));
        OnPropertyChanged(nameof(Monthly));
        OnPropertyChanged(nameof(RegionStats));
        RegionRows.Clear();
        if (value is null)
            return;
        var max = value.ByVoivodeship.Count == 0 ? 1 : Math.Max(1, value.ByVoivodeship.Max(row => row.Centers));
        foreach (var row in value.ByVoivodeship.OrderByDescending(item => item.Centers))
        {
            RegionRows.Add(new RegionRow
            {
                Name = row.Name,
                Centers = row.Centers,
                ShareText = row.Share.ToString("0.0%", _culture),
                Bar = row.Centers / (double)max,
            });
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Status = "Pobieram dane z raportu NFZ…";
        try
        {
            var progress = new Progress<SyncProgress>(update => Status = update.Detail);
            await new NfzSync().RunAsync(_database, progress, CancellationToken.None);
            Load();
            Status = "Baza lokalna została odświeżona.";
        }
        catch (Exception ex)
        {
            Status = "Nie udało się pobrać danych: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ExportBundle? BuildExport()
    {
        if (ExportDataset == "Podsumowanie KPI")
        {
            if (Kpi is null)
                return null;
            var dataset = new DatasetDefinition(
                "kpi",
                "Podsumowanie",
                "Podsumowanie",
                [
                    new("wskaznik", "wskaznik", "Wskaźnik", ColumnKind.Text),
                    new("wartosc", "wartosc", "Wartość", ColumnKind.Text),
                ]);
            var rows = new List<object?[]>
            {
                new object?[] { "Rok", Year.ToString(_culture) },
                new object?[] { "Typ zawału", InfarctType },
                new object?[] { "Odsetek zawałów objętych KOS w skali kraju", Kpi.NationalShare },
                new object?[] { "Hospitalizacje w KOS", Kpi.KosHospitalizations },
                new object?[] { "Wszystkie hospitalizacje zawałowe", Kpi.Hospitalizations },
                new object?[] { "Szpitale realizujące KOS-zawał", Kpi.Centers },
                new object?[] { "Województwa z realizatorami", Kpi.VoivodeshipsWithCenters },
                new object?[] { "Odsetek w ośrodkach realizujących program", Kpi.ShareAmongKosCenters },
            };
            foreach (var row in Kpi.ByVoivodeship)
            {
                rows.Add(new object?[]
                {
                    $"Ośrodki · {row.Name}",
                    row.Centers.ToString(_culture),
                });
            }

            return new ExportBundle(dataset, rows);
        }

        var definition = ReportCatalog.Datasets.First(item => item.Title == ExportDataset);
        int? year = ExportUsesFilters ? Year : null;
        var type = ExportUsesFilters ? InfarctType : null;
        var woj = ExportUsesFilters ? SelectedVoivodeship() : null;
        var search = ExportUsesFilters ? SearchText : null;
        var aggregate = ExportUsesFilters && definition.Id == "trendy" && TrendsAggregateOnly;
        var rowsFromDb = _database.ExportRows(definition.Id, year, type, woj, search, aggregate).ToList();
        return new ExportBundle(definition, rowsFromDb);
    }

    private void ReloadPage()
    {
        if (_loading || Year == 0)
            return;

        Kpi = _database.Compute(Year, InfarctType);
        if (IsTable)
            LoadTable();
        if (IsMap)
            LoadPins();
        NotifyPages();
    }

    private void LoadPins()
    {
        Pins.Clear();
        MapSideList.Clear();
        foreach (var pin in _database.Hospitals(Year, InfarctType))
            Pins.Add(pin);

        var leaders = Pins
            .GroupBy(pin => pin.Voivodeship, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(pin => pin.Hospitalizations).ThenBy(pin => pin.Name).First())
            .OrderByDescending(pin => pin.Hospitalizations)
            .ToList();
        foreach (var leader in leaders)
        {
            MapSideList.Add(new MapSideItem
            {
                Title = $"{leader.Voivodeship}: {leader.Name}",
                Subtitle = $"{leader.Hospitalizations.ToString("N0", _culture)} hospitalizacji · KOS {(leader.KosShare ?? 0).ToString("0.0%", _culture)}"
                    + (leader.Quality == "nominatim" ? "" : " · położenie przybliżone"),
            });
        }
    }

    private string BuildMethodology()
    {
        var report = _database.Meta("methodology") ?? MethodologyText.Fallback;
        var comparison = BuildYearComparison();
        return report.Trim()
               + "\n\n"
               + MethodologyText.KpiExplanation.Trim()
               + "\n\n"
               + comparison;
    }

    private string BuildYearComparison()
    {
        var type = InfarctType;
        var lines = new List<string>
        {
            "Porównanie wskaźników aplikacji dla lat 2024 i 2025",
            "",
            $"Typ zawału: {type}",
            "Ta sama formuła dla obu lat — różnice wynikają wyłącznie z danych sprawozdanych w raporcie NFZ, nie z innej metodyki aplikacji.",
            "",
        };

        foreach (var year in new[] { 2025, 2024 })
        {
            if (!_database.Years().Contains(year))
            {
                lines.Add($"{year}: brak danych w lokalnej bazie.");
                continue;
            }

            var kpi = _database.Compute(year, type);
            lines.Add(
                $"{year}: udział krajowy {kpi.NationalShare.ToString("0.0%", _culture)} "
                + $"({kpi.KosHospitalizations.ToString("N0", _culture)} z {kpi.Hospitalizations.ToString("N0", _culture)}); "
                + $"ośrodki KOS {kpi.Centers.ToString("N0", _culture)}; "
                + $"województwa {kpi.VoivodeshipsWithCenters.ToString("N0", _culture)}; "
                + $"udział w ośrodkach KOS {kpi.ShareAmongKosCenters.ToString("0.0%", _culture)}.");
        }

        lines.Add("");
        lines.Add(
            "Uwaga do komunikatów prasowych: odsetek „w skali kraju” w aplikacji to ważona średnia odsetków KOS po wszystkich hospitalizacjach zawałowych (placówki). "
            + "Nie korygujemy zgonów poza tym, co już zrobił raport (KOS liczy hospitalizacje niezakończone zgonem). "
            + "Zakładka Województwa / „Program KOS-zawał” w raporcie to inny wskaźnik: odsetek świadczeniodawców aktywnych w programie, nie odsetek przypadków.");
        return string.Join('\n', lines);
    }

    private void LoadTable()
    {
        var datasetId = Current.Id switch
        {
            "kos" => "kos",
            "trendy" => "trendy",
            "wojewodztwa" => "wojewodztwa",
            _ => "placowki",
        };
        var page = _database.Query(
            datasetId,
            Year,
            InfarctType,
            SelectedVoivodeship(),
            SearchText,
            datasetId == "trendy" && TrendsAggregateOnly,
            4000);
        Headers = page.Headers.ToArray();
        Rows.Clear();
        foreach (var row in page.Rows)
        {
            var cells = new string[page.Headers.Count];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = TableExporter.Format(i < row.Length ? row[i] : null, page.Kinds[i], excel: false);
            Rows.Add(new GridRow(cells));
        }

        var shown = Rows.Count.ToString("N0", _culture);
        var total = page.Total.ToString("N0", _culture);
        TableCaption = page.Total > Rows.Count
            ? $"Widok pokazuje {shown} z {total} wierszy. Eksport zapisuje cały wynik filtrów."
            : $"{total} wierszy";
        TableChanged?.Invoke();
    }

    private string? SelectedVoivodeship() =>
        string.IsNullOrWhiteSpace(VoivodeshipOption) || VoivodeshipOption == "Cała Polska"
            ? null
            : VoivodeshipOption;

    private void NotifyPages()
    {
        OnPropertyChanged(nameof(IsSummary));
        OnPropertyChanged(nameof(IsTable));
        OnPropertyChanged(nameof(IsMap));
        OnPropertyChanged(nameof(IsMethodology));
        OnPropertyChanged(nameof(IsExport));
        OnPropertyChanged(nameof(IsTrends));
    }
}

public sealed record ExportBundle(DatasetDefinition Dataset, IReadOnlyList<object?[]> Rows);
