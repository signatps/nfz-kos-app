using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Tiling;

namespace KosZawal.Controls;

public enum MapDisplayMode
{
    OsmHospitals,
    Leaders,
    Administrative,
}

public partial class KosMapView : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<VoivodeshipStat>?> RegionsProperty =
        AvaloniaProperty.Register<KosMapView, IReadOnlyList<VoivodeshipStat>?>(nameof(Regions));

    public static readonly StyledProperty<IReadOnlyList<HospitalPin>?> PinsProperty =
        AvaloniaProperty.Register<KosMapView, IReadOnlyList<HospitalPin>?>(nameof(Pins));

    public static readonly StyledProperty<string> HoverTextProperty =
        AvaloniaProperty.Register<KosMapView, string>(nameof(HoverText), "");

    public static readonly StyledProperty<string?> SelectedHospitalKeyProperty =
        AvaloniaProperty.Register<KosMapView, string?>(nameof(SelectedHospitalKey));

    private MemoryLayer? _mask;
    private MemoryLayer? _borders;
    private MemoryLayer? _points;
    private MapDisplayMode _mode = MapDisplayMode.OsmHospitals;
    private bool _ready;
    private bool _building;

    public KosMapView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (IsVisible)
                EnsureMap();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            EnsureMap();
        if (change.Property == RegionsProperty || change.Property == PinsProperty)
            RefreshLayers();
    }

    public IReadOnlyList<VoivodeshipStat>? Regions
    {
        get => GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    public IReadOnlyList<HospitalPin>? Pins
    {
        get => GetValue(PinsProperty);
        set => SetValue(PinsProperty, value);
    }

    public string HoverText
    {
        get => GetValue(HoverTextProperty);
        set => SetValue(HoverTextProperty, value);
    }

    public string? SelectedHospitalKey
    {
        get => GetValue(SelectedHospitalKeyProperty);
        set => SetValue(SelectedHospitalKeyProperty, value);
    }

    private void ShowOsm(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetMode(MapDisplayMode.OsmHospitals);

    private void ShowAdmin(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetMode(MapDisplayMode.Administrative);

    private void ShowLeaders(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetMode(MapDisplayMode.Leaders);

    private void SetMode(MapDisplayMode mode)
    {
        _mode = mode;
        StyleModeButtons();
        HintText.Text = mode switch
        {
            MapDisplayMode.Administrative =>
                "Widok administracyjny Polski: województwa oraz stolice i główne miasta.",
            MapDisplayMode.Leaders =>
                "Tylko liderzy województw (złoty trójkąt): ośrodek KOS z największą liczbą hospitalizacji. Kliknij punkt.",
            _ =>
                "Ośrodki KOS na mapie Polski. Złoty trójkąt = lider województwa, kropka = pozostałe. Kliknij punkt.",
        };
        RefreshLayers();
        FitPoland();
    }

    private void StyleModeButtons()
    {
        SetButtonStyle(OsmButton, _mode == MapDisplayMode.OsmHospitals);
        SetButtonStyle(LeadersButton, _mode == MapDisplayMode.Leaders);
        SetButtonStyle(AdminButton, _mode == MapDisplayMode.Administrative);
    }

    private static void SetButtonStyle(Button button, bool active)
    {
        button.Classes.Set("action", active);
        button.Classes.Set("quiet", !active);
    }

    private void EnsureMap()
    {
        if (_ready || _building)
            return;
        _building = true;

        try
        {
            var map = new Map
            {
                CRS = "EPSG:3857",
                BackColor = Mapsui.Styles.Color.FromArgb(255, 243, 240, 232),
            };
            map.Layers.Add(OpenStreetMap.CreateTileLayer("KosZawal/1.0 (https://github.com/signatps/nfz-kos-app)"));

            _mask = new MemoryLayer("Maska") { Style = null };
            _borders = new MemoryLayer("Województwa") { Style = null };
            _points = new MemoryLayer("Punkty") { Style = null };
            map.Layers.Add(_mask);
            map.Layers.Add(_borders);
            map.Layers.Add(_points);

            var extent = VoivodeshipGeo.PolandExtent();
            map.Navigator.OverridePanBounds = extent;
            map.Tapped += OnMapTapped;
            map.Info += OnMapInfo;

            MapControl.Map = map;
            StyleModeButtons();
            _ready = true;

            // Maskę i pinezki dopiero po przypięciu MapControl — unikamy ciężkiej pracy w konstruktorze.
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_mask is not null)
                    {
                        _mask.Features = [VoivodeshipGeo.NeighborMaskFeature()];
                        _mask.FeaturesWereModified();
                    }

                    RefreshLayers();
                    FitPoland();
                }
                catch (Exception ex)
                {
                    HoverText = "Warstwy mapy: " + ex.Message;
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            HoverText = "Mapa nie mogła się załadować: " + ex.Message;
            _ready = false;
        }
        finally
        {
            _building = false;
        }
    }

    private void RefreshLayers()
    {
        if (!_ready || _borders is null || _points is null)
            return;

        try
        {
            switch (_mode)
            {
                case MapDisplayMode.Administrative:
                    _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: true).ToList();
                    _points.Features = VoivodeshipGeo.CityFeatures(capitalsOnly: false).ToList();
                    break;
                case MapDisplayMode.Leaders:
                    _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: true).ToList();
                    _points.Features = VoivodeshipGeo.HospitalFeatures(Pins, leadersOnly: true).ToList();
                    break;
                default:
                    _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: false).ToList();
                    _points.Features = VoivodeshipGeo.HospitalFeatures(Pins, leadersOnly: false).ToList();
                    break;
            }

            _borders.FeaturesWereModified();
            _points.FeaturesWereModified();
            MapControl.Map?.RefreshData();
            MapControl.Refresh();
        }
        catch (Exception ex)
        {
            HoverText = "Odświeżenie mapy: " + ex.Message;
        }
    }

    private void FitPoland()
    {
        try
        {
            if (MapControl.Map is null)
                return;
            MapControl.Map.Navigator.ZoomToBox(VoivodeshipGeo.PolandExtent());
        }
        catch
        {
            // Ignoruj — viewport ustawi się przy pierwszym renderze.
        }
    }

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        try
        {
            if (e.GestureType is not (GestureType.SingleTap or GestureType.DoubleTap))
                return;
            if (_points is null)
                return;

            var info = e.GetMapInfo([_points]);
            if (info.Feature is null)
                return;
            if (info.Feature["kind"] is not "hospital")
                return;
            if (info.Feature["name"] is string name && info.Feature["woj"] is string woj)
            {
                SelectedHospitalKey = woj + "\u001f" + name;
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            HoverText = "Kliknięcie: " + ex.Message;
        }
    }

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        try
        {
            if (_borders is null || _points is null)
                return;

            var info = e.GetMapInfo([_borders, _points]);
            var feature = info.Feature;
            if (feature is null)
            {
                HoverText = _mode == MapDisplayMode.Administrative
                    ? "Najedź na województwo albo miasto."
                    : "Najedź na ośrodek albo kliknij, by otworzyć szczegóły.";
                return;
            }

            if (feature["kind"] is "hospital" && feature["name"] is string name && feature["woj"] is string woj)
            {
                var hosp = Convert.ToDouble(feature["hosp"] ?? 0);
                var share = Convert.ToDouble(feature["share"] ?? 0);
                var leader = feature["leader"] is true ? " · lider województwa" : "";
                var approx = feature["quality"] is string quality && quality != "nominatim"
                    ? " · położenie przybliżone"
                    : "";
                HoverText = $"{name} · {woj} · {hosp:N0} hosp. · KOS {share:0.0%}{leader}{approx}";
                return;
            }

            if (feature["kind"] is "city" && feature["name"] is string city && feature["woj"] is string cityWoj)
            {
                var capital = feature["capital"] is true ? "stolica województwa" : "miasto";
                HoverText = $"{city} · {cityWoj} · {capital}";
                return;
            }

            if (feature["name"] is string regionName)
            {
                var stat = (Regions ?? []).FirstOrDefault(row =>
                    string.Equals(row.Name, regionName, StringComparison.CurrentCultureIgnoreCase));
                HoverText = stat is null
                    ? regionName
                    : $"{stat.Name}: {stat.Centers} ośrodków KOS, udział zawałów {stat.Share:0.0%}";
            }
        }
        catch
        {
            // Hover nie może wywalić procesu.
        }
    }
}
