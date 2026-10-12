using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace KosZawal.Controls;

public class PolandMap : Control
{
    public static readonly StyledProperty<IReadOnlyList<VoivodeshipStat>?> RegionsProperty =
        AvaloniaProperty.Register<PolandMap, IReadOnlyList<VoivodeshipStat>?>(nameof(Regions));

    public static readonly StyledProperty<IReadOnlyList<HospitalPin>?> PinsProperty =
        AvaloniaProperty.Register<PolandMap, IReadOnlyList<HospitalPin>?>(nameof(Pins));

    public static readonly StyledProperty<string> HoverTextProperty =
        AvaloniaProperty.Register<PolandMap, string>(nameof(HoverText), "");

    private PolandGeometry? _geometry;
    private string? _hoverRegion;
    private HospitalPin? _hoverPin;

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

    static PolandMap()
    {
        AffectsRender<PolandMap>(RegionsProperty, PinsProperty);
    }

    public PolandMap()
    {
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        _geometry ??= PolandGeometry.Load();
        context.FillRectangle(new SolidColorBrush(Color.Parse("#F7F4EE")), new Rect(Bounds.Size));
        if (_geometry.Shapes.Count == 0 || Bounds.Width < 20 || Bounds.Height < 20)
            return;

        var frame = _geometry.Fit(new Rect(12, 12, Bounds.Width - 24, Bounds.Height - 24));
        var shares = (Regions ?? []).ToDictionary(region => region.Name, region => region.Share, StringComparer.CurrentCulture);

        foreach (var shape in _geometry.Shapes)
        {
            var share = shares.GetValueOrDefault(shape.Name);
            var fill = ColorFor(share);
            if (shape.Name == _hoverRegion)
                fill = Color.Parse("#16324F");
            var geometry = shape.ToGeometry(frame);
            context.DrawGeometry(new SolidColorBrush(fill), new Pen(Brushes.White, 1.4), geometry);
        }

        foreach (var pin in Pins ?? [])
        {
            var point = Locate(pin, frame);
            if (point is null)
                continue;
            var radius = pin == _hoverPin ? 6.2 : 4.2 + Math.Min(3.2, Math.Sqrt(Math.Max(pin.Hospitalizations, 0)) / 28);
            var color = (pin.KosShare ?? 0) >= 0.5 ? Color.Parse("#C4474F") : Color.Parse("#0E6E6A");
            if (pin.Quality != "nominatim")
                color = Color.Parse("#8A6A2F");
            context.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 1.3), point.Value, radius, radius);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_geometry is null)
            return;
        var frame = _geometry.Fit(new Rect(12, 12, Bounds.Width - 24, Bounds.Height - 24));
        var position = e.GetPosition(this);
        HospitalPin? pinHit = null;
        foreach (var pin in Pins ?? [])
        {
            var point = Locate(pin, frame);
            if (point is null)
                continue;
            if (Point.Distance(point.Value, position) <= 8)
                pinHit = pin;
        }

        var region = _geometry.Hit(position, frame);
        if (pinHit == _hoverPin && region == _hoverRegion)
            return;

        _hoverPin = pinHit;
        _hoverRegion = region;
        HoverText = pinHit is null
            ? DescribeRegion(region)
            : $"{pinHit.Name} · {pinHit.Voivodeship} · {pinHit.Hospitalizations:N0} hosp. · KOS {(pinHit.KosShare ?? 0).ToString("0.0%", CultureInfo.GetCultureInfo("pl-PL"))}"
              + (pinHit.Quality == "nominatim" ? "" : " · położenie przybliżone");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverPin = null;
        _hoverRegion = null;
        HoverText = "";
        InvalidateVisual();
    }

    private string DescribeRegion(string? name)
    {
        if (name is null)
            return "Najedź na województwo albo ośrodek.";
        var stat = (Regions ?? []).FirstOrDefault(region => region.Name == name);
        if (stat is null)
            return name;
        var culture = CultureInfo.GetCultureInfo("pl-PL");
        return $"{stat.Name}: {stat.Centers} ośrodków KOS, udział zawałów {stat.Share.ToString("0.0%", culture)}";
    }

    private Point? Locate(HospitalPin pin, MapFrame frame)
    {
        if (pin.Latitude is double lat && pin.Longitude is double lon && pin.Quality == "nominatim")
            return frame.Project(lon, lat);

        var shape = _geometry?.Shapes.FirstOrDefault(item => item.Name == pin.Voivodeship);
        if (shape is null)
            return null;
        var hash = pin.Name.GetHashCode(StringComparison.Ordinal);
        var jx = ((hash & 255) - 128) / 128.0 * 0.35;
        var jy = (((hash >> 8) & 255) - 128) / 128.0 * 0.22;
        return frame.Project(shape.CenterLon + jx, shape.CenterLat + jy);
    }

    private static Color ColorFor(double share)
    {
        var t = Math.Clamp(share / 0.7, 0, 1);
        var light = Color.Parse("#D5E4EA");
        var deep = Color.Parse("#0E6E6A");
        return Color.FromRgb(
            (byte)(light.R + (deep.R - light.R) * t),
            (byte)(light.G + (deep.G - light.G) * t),
            (byte)(light.B + (deep.B - light.B) * t));
    }
}

internal sealed class MapFrame
{
    public required double MinX { get; init; }
    public required double MinY { get; init; }
    public required double Scale { get; init; }
    public required double OffsetX { get; init; }
    public required double OffsetY { get; init; }
    public required double Height { get; init; }

    public Point Project(double lon, double lat)
    {
        var (x, y) = PolandGeometry.Plane(lon, lat);
        return new Point(OffsetX + (x - MinX) * Scale, OffsetY + Height - (y - MinY) * Scale);
    }
}

internal sealed class VoivodeshipShape
{
    public required string Name { get; init; }
    public required List<List<Point>> Rings { get; init; }
    public required double CenterLon { get; init; }
    public required double CenterLat { get; init; }

    public Geometry ToGeometry(MapFrame frame)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        foreach (var ring in Rings)
        {
            if (ring.Count == 0)
                continue;
            var first = frame.Project(ring[0].X, ring[0].Y);
            ctx.BeginFigure(first, true);
            for (var i = 1; i < ring.Count; i++)
                ctx.LineTo(frame.Project(ring[i].X, ring[i].Y));
            ctx.EndFigure(true);
        }

        return geometry;
    }

    public bool Contains(double lon, double lat)
    {
        if (Rings.Count == 0)
            return false;
        return Inside(Rings[0], lon, lat);
    }

    private static bool Inside(List<Point> ring, double lon, double lat)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var xi = ring[i].X;
            var yi = ring[i].Y;
            var xj = ring[j].X;
            var yj = ring[j].Y;
            if (((yi > lat) != (yj > lat)) && (lon < (xj - xi) * (lat - yi) / ((yj - yi) + 1e-12) + xi))
                inside = !inside;
        }

        return inside;
    }
}

internal sealed class PolandGeometry
{
    public required List<VoivodeshipShape> Shapes { get; init; }

    public static PolandGeometry Load()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://KosZawal/Assets/wojewodztwa.geojson"));
            using var document = JsonDocument.Parse(stream);
            var shapes = new List<VoivodeshipShape>();
            foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                var rawName = feature.GetProperty("properties").GetProperty("nazwa").GetString() ?? "";
                var name = rawName.Length == 0
                    ? rawName
                    : char.ToUpper(rawName[0], CultureInfo.GetCultureInfo("pl-PL")) + rawName[1..];
                var rings = new List<List<Point>>();
                ReadRings(feature.GetProperty("geometry"), rings);
                if (rings.Count == 0)
                    continue;
                var points = rings[0];
                shapes.Add(new VoivodeshipShape
                {
                    Name = name,
                    Rings = rings,
                    CenterLon = points.Average(point => point.X),
                    CenterLat = points.Average(point => point.Y),
                });
            }

            return new PolandGeometry { Shapes = shapes };
        }
        catch
        {
            return new PolandGeometry { Shapes = [] };
        }
    }

    public MapFrame Fit(Rect target)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var shape in Shapes)
        {
            foreach (var ring in shape.Rings)
            {
                foreach (var point in ring)
                {
                    var (x, y) = Plane(point.X, point.Y);
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        var width = Math.Max(0.0001, maxX - minX);
        var height = Math.Max(0.0001, maxY - minY);
        var scale = Math.Min(target.Width / width, target.Height / height);
        var drawnW = width * scale;
        var drawnH = height * scale;
        return new MapFrame
        {
            MinX = minX,
            MinY = minY,
            Scale = scale,
            OffsetX = target.X + (target.Width - drawnW) / 2,
            OffsetY = target.Y + (target.Height - drawnH) / 2,
            Height = drawnH,
        };
    }

    public string? Hit(Point position, MapFrame frame)
    {
        foreach (var shape in Shapes)
        {
            var geometry = shape.ToGeometry(frame);
            if (geometry.FillContains(position))
                return shape.Name;
        }

        return null;
    }

    public static (double X, double Y) Plane(double lon, double lat)
    {
        var cos = Math.Cos(52 * Math.PI / 180);
        return (lon * cos, lat);
    }

    private static void ReadRings(JsonElement geometry, List<List<Point>> rings)
    {
        var type = geometry.GetProperty("type").GetString();
        var coordinates = geometry.GetProperty("coordinates");
        if (type == "Polygon")
            AddPolygon(coordinates, rings);
        else if (type == "MultiPolygon")
        {
            foreach (var polygon in coordinates.EnumerateArray())
                AddPolygon(polygon, rings);
        }
    }

    private static void AddPolygon(JsonElement polygon, List<List<Point>> rings)
    {
        var first = true;
        foreach (var ring in polygon.EnumerateArray())
        {
            if (!first)
            {
                first = false;
                continue;
            }

            first = false;
            var points = new List<Point>();
            var step = Math.Max(1, ring.GetArrayLength() / 180);
            var index = 0;
            Point? last = null;
            foreach (var pair in ring.EnumerateArray())
            {
                if (index++ % step != 0 && index != ring.GetArrayLength())
                    continue;
                var lon = pair[0].GetDouble();
                var lat = pair[1].GetDouble();
                last = new Point(lon, lat);
                points.Add(last.Value);
            }

            if (points.Count > 2)
                rings.Add(points);
        }
    }
}
