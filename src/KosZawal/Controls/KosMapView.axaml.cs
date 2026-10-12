using Avalonia;
using Avalonia.Controls;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling;

namespace KosZawal.Controls;

public enum MapDisplayMode
{
    OsmHospitals,
    Administrative,
    Leaders,
}

public partial class KosMapView : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<VoivodeshipStat>?> RegionsProperty =
        AvaloniaProperty.Register<KosMapView, IReadOnlyList<VoivodeshipStat>?>(nameof(Regions));

    public static readonly StyledProperty<IReadOnlyList<HospitalPin>?> PinsProperty =
        AvaloniaProperty.Register<KosMapView, IReadOnlyList<HospitalPin>?>(nameof(Pins));

    public static readonly StyledProperty<string> HoverTextProperty =
        AvaloniaProperty.Register<KosMapView, string>(nameof(HoverText), "");

    private MemoryLayer? _borders;
    private MemoryLayer? _points;
    private MapDisplayMode _mode = MapDisplayMode.OsmHospitals;
    private bool _ready;

    public KosMapView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => EnsureMap();
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RegionsProperty || change.Property == PinsProperty)
            RefreshLayers();
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
                "Widok administracyjny: granice województw oraz stolice i główne miasta. Bez pinezek szpitali.",
            MapDisplayMode.Leaders =>
                "Liderzy województw: w każdym województwie ośrodek KOS z największą liczbą hospitalizacji zawałowych w wybranym roku i typie.",
            _ =>
                "Kafelki OpenStreetMap. Punkty: ośrodki KOS-zawał (złoty = położenie przybliżone).",
        };
        RefreshLayers();
        FitPoland();
    }

    private void StyleModeButtons()
    {
        SetButtonStyle(OsmButton, _mode == MapDisplayMode.OsmHospitals);
        SetButtonStyle(AdminButton, _mode == MapDisplayMode.Administrative);
        SetButtonStyle(LeadersButton, _mode == MapDisplayMode.Leaders);
    }

    private static void SetButtonStyle(Button button, bool active)
    {
        button.Classes.Set("action", active);
        button.Classes.Set("quiet", !active);
    }

    private void EnsureMap()
    {
        if (_ready)
            return;
        _ready = true;

        var map = new Map { CRS = "EPSG:3857" };
        map.Layers.Add(OpenStreetMap.CreateTileLayer("KosZawal/1.0 (https://github.com/signatps/nfz-kos-app)"));

        _borders = new MemoryLayer("Województwa") { Style = null };
        _points = new MemoryLayer("Punkty") { Style = null };
        map.Layers.Add(_borders);
        map.Layers.Add(_points);

        map.Info += OnMapInfo;
        MapControl.Map = map;
        StyleModeButtons();
        FitPoland();
        RefreshLayers();
    }

    private void RefreshLayers()
    {
        if (!_ready || _borders is null || _points is null)
            return;

        switch (_mode)
        {
            case MapDisplayMode.Administrative:
                _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: true).ToList();
                _points.Features = VoivodeshipGeo.CityFeatures(capitalsOnly: false).ToList();
                break;
            case MapDisplayMode.Leaders:
                _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: true).ToList();
                _points.Features = VoivodeshipGeo.LeaderFeatures(Pins).ToList();
                break;
            default:
                _borders.Features = VoivodeshipGeo.BorderFeatures(Regions, fillByShare: false).ToList();
                _points.Features = VoivodeshipGeo.HospitalFeatures(Pins).ToList();
                break;
        }

        _borders.FeaturesWereModified();
        _points.FeaturesWereModified();
        MapControl.Map?.RefreshData();
        MapControl.Refresh();
    }

    private void FitPoland()
    {
        if (MapControl.Map is null)
            return;
        MapControl.Map.Navigator.ZoomToBox(VoivodeshipGeo.PolandExtent());
    }

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var info = e.GetMapInfo([_borders!, _points!]);
        var feature = info.Feature;
        if (feature is null)
        {
            HoverText = _mode switch
            {
                MapDisplayMode.Administrative => "Najedź na województwo albo miasto.",
                MapDisplayMode.Leaders => "Najedź na lidera województwa.",
                _ => "Najedź na ośrodek KOS-zawał.",
            };
            return;
        }

        if (feature["name"] is string name && feature["woj"] is string woj)
        {
            if (feature["leader"] is true)
            {
                var hosp = Convert.ToDouble(feature["hosp"] ?? 0);
                var share = Convert.ToDouble(feature["share"] ?? 0);
                HoverText = $"Lider {woj}: {name} · {hosp:N0} hospitalizacji · KOS {share:0.0%}";
                return;
            }

            if (feature["hosp"] is not null)
            {
                var hosp = Convert.ToDouble(feature["hosp"]);
                var share = Convert.ToDouble(feature["share"] ?? 0);
                var approx = feature["quality"] is string quality && quality != "nominatim"
                    ? " · położenie przybliżone"
                    : "";
                HoverText = $"{name} · {woj} · {hosp:N0} hosp. · KOS {share:0.0%}{approx}";
                return;
            }

            var capital = feature["capital"] is true ? "stolica województwa" : "miasto";
            HoverText = $"{name} · {woj} · {capital}";
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
}
