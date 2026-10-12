using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;
using Mapsui;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using Color = Mapsui.Styles.Color;

namespace KosZawal.Controls;

internal static class VoivodeshipGeo
{
    private static IReadOnlyList<VoivodeshipPolygon>? _polygons;
    private static string? _crownSource;
    private static IFeature? _maskFeature;

    public static IReadOnlyList<VoivodeshipPolygon> Polygons => _polygons ??= Load();

    public static IFeature NeighborMaskFeature() => _maskFeature ??= BuildNeighborMask();

    private static IFeature BuildNeighborMask()
    {
        var factory = GeometryFactory.Default;
        var style = new VectorStyle
        {
            Fill = new Brush(new Color(243, 240, 232)),
            Outline = null,
        };

        try
        {
            var mask = CutPolandFromWorld(factory);
            var feature = new GeometryFeature(mask);
            feature.Styles.Add(style);
            return feature;
        }
        catch
        {
            var feature = new GeometryFeature(RectangularCollarMask(factory));
            feature.Styles.Add(style);
            return feature;
        }
    }

    private static Geometry CutPolandFromWorld(GeometryFactory factory)
    {
        var world = WorldPolygon(factory);
        // Union+Difference pada na uproszczonych granicach („side location conflict”).
        // Odejmowanie województw po kolei przez OverlayNG jest stabilne.
        Geometry mask = world;
        foreach (var polygon in Polygons)
        {
            var piece = GeometryFixer.Fix(polygon.Geometry);
            if (piece.IsEmpty)
                continue;
            piece = piece.Buffer(0);
            if (piece.IsEmpty)
                continue;
            mask = OverlayNGRobust.Overlay(mask, piece, SpatialFunction.Difference);
        }

        return mask.IsEmpty ? RectangularCollarMask(factory) : mask;
    }

    private static Geometry WorldPolygon(GeometryFactory factory)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(-20, 30);
        var (maxX, maxY) = SphericalMercator.FromLonLat(50, 70);
        return factory.CreatePolygon(
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]);
    }

    /// <summary>Fallback: zasłania obszar poza prostokątem Polski.</summary>
    private static Geometry RectangularCollarMask(GeometryFactory factory)
    {
        var extent = PolandExtent();
        var world = WorldPolygon(factory);
        var polandBox = factory.CreatePolygon(
        [
            new Coordinate(extent.MinX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MinY),
        ]);
        try
        {
            return OverlayNGRobust.Overlay(world, polandBox, SpatialFunction.Difference);
        }
        catch
        {
            return world;
        }
    }

    public static IEnumerable<IFeature> BorderFeatures(IReadOnlyList<VoivodeshipStat>? regions, bool fillByShare)
    {
        var shares = (regions ?? [])
            .ToDictionary(region => region.Name, region => region.Share, StringComparer.CurrentCultureIgnoreCase);

        foreach (var polygon in Polygons)
        {
            shares.TryGetValue(polygon.Name, out var share);
            var fill = fillByShare
                ? ColorFor(share)
                : new Color(14, 110, 106, 36);
            var feature = new GeometryFeature(polygon.Geometry)
            {
                ["name"] = polygon.Name,
                ["share"] = share,
                ["kind"] = "region",
            };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(fill),
                Outline = new Pen(new Color(255, 255, 255, 230), fillByShare ? 1.6 : 2.0),
            });
            yield return feature;
        }
    }

    public static IEnumerable<IFeature> CityFeatures(bool capitalsOnly)
    {
        foreach (var city in PolandCities.All.Where(city => !capitalsOnly || city.IsCapital))
        {
            var (x, y) = SphericalMercator.FromLonLat(city.Longitude, city.Latitude);
            var feature = new GeometryFeature(new Point(x, y))
            {
                ["name"] = city.Name,
                ["woj"] = city.Voivodeship,
                ["capital"] = city.IsCapital,
                ["kind"] = "city",
            };
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = city.IsCapital ? 0.5 : 0.35,
                Fill = new Brush(city.IsCapital ? new Color(196, 71, 79) : new Color(22, 50, 79)),
                Outline = new Pen(Color.White, 1.4),
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = city.Name,
                ForeColor = new Color(18, 36, 51),
                BackColor = new Brush(new Color(243, 240, 232, 210)),
                Font = new Font { FontFamily = "Inter", Size = city.IsCapital ? 13 : 11 },
                Offset = new Offset(0, city.IsCapital ? -18 : -14),
                CollisionDetection = true,
            });
            yield return feature;
        }
    }

    public static IEnumerable<IFeature> HospitalFeatures(IReadOnlyList<HospitalPin>? pins, bool leadersOnly)
    {
        var list = pins ?? [];
        var leaderKeys = list
            .GroupBy(pin => pin.Voivodeship, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(pin => pin.Hospitalizations).ThenBy(pin => pin.Name).First())
            .Select(pin => Key(pin.Voivodeship, pin.Name))
            .ToHashSet(StringComparer.Ordinal);

        var ordered = list
            .OrderBy(pin => leaderKeys.Contains(Key(pin.Voivodeship, pin.Name)) ? 1 : 0)
            .ThenBy(pin => pin.Hospitalizations);

        foreach (var pin in ordered)
        {
            var isLeader = leaderKeys.Contains(Key(pin.Voivodeship, pin.Name));
            if (leadersOnly && !isLeader)
                continue;
            if (!TryLocate(pin, out var lon, out var lat))
                continue;

            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            var feature = new GeometryFeature(new Point(x, y))
            {
                ["name"] = pin.Name,
                ["woj"] = pin.Voivodeship,
                ["hosp"] = pin.Hospitalizations,
                ["share"] = pin.KosShare ?? 0,
                ["quality"] = pin.Quality,
                ["leader"] = isLeader,
                ["kind"] = "hospital",
            };

            if (isLeader)
            {
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolScale = 0.55,
                    Offset = new Offset(0, -10),
                    Fill = new Brush
                    {
                        Image = new Image { Source = CrownSource() },
                    },
                });
                feature.Styles.Add(new LabelStyle
                {
                    Text = ShortName(pin.Name),
                    ForeColor = new Color(18, 36, 51),
                    BackColor = new Brush(new Color(243, 240, 232, 235)),
                    Font = new Font { FontFamily = "Inter", Size = 11 },
                    Offset = new Offset(0, -28),
                    CollisionDetection = true,
                });
            }
            else
            {
                var color = pin.Quality == "nominatim"
                    ? (pin.KosShare ?? 0) >= 0.5 ? new Color(196, 71, 79) : new Color(14, 110, 106)
                    : new Color(138, 106, 47);
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolScale = 0.42 + Math.Min(0.28, Math.Sqrt(Math.Max(pin.Hospitalizations, 0)) / 90),
                    Fill = new Brush(color),
                    Outline = new Pen(Color.White, 1.4),
                });
            }

            yield return feature;
        }
    }

    public static MRect PolandExtent()
    {
        var (minX, minY) = SphericalMercator.FromLonLat(14.05, 48.95);
        var (maxX, maxY) = SphericalMercator.FromLonLat(24.25, 55.05);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static string Key(string woj, string name) => woj + "\u001f" + name;

    private static string ShortName(string name) =>
        name.Length <= 36 ? name : name[..33] + "…";

    private static string CrownSource()
    {
        if (_crownSource is not null)
            return _crownSource;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KosZawal");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "crown.png");
        if (!File.Exists(path))
        {
            using var stream = AssetLoader.Open(new Uri("avares://KosZawal/Assets/crown.png"));
            using var file = File.Create(path);
            stream.CopyTo(file);
        }

        _crownSource = new Uri(path).AbsoluteUri;
        return _crownSource;
    }

    private static bool TryLocate(HospitalPin pin, out double lon, out double lat)
    {
        if (pin.Latitude is double pinLat && pin.Longitude is double pinLon && pin.Quality == "nominatim")
        {
            lon = pinLon;
            lat = pinLat;
            return true;
        }

        var shape = Polygons.FirstOrDefault(item =>
            string.Equals(item.Name, pin.Voivodeship, StringComparison.CurrentCultureIgnoreCase));
        if (shape is null)
        {
            lon = 0;
            lat = 0;
            return false;
        }

        var hash = pin.Name.GetHashCode(StringComparison.Ordinal);
        lon = shape.CenterLon + ((hash & 255) - 128) / 128.0 * 0.35;
        lat = shape.CenterLat + (((hash >> 8) & 255) - 128) / 128.0 * 0.22;
        return true;
    }

    private static Color ColorFor(double share)
    {
        var t = Math.Clamp(share / 0.7, 0, 1);
        return new Color(
            (int)(213 + (14 - 213) * t),
            (int)(228 + (110 - 228) * t),
            (int)(234 + (106 - 234) * t),
            150);
    }

    private static IReadOnlyList<VoivodeshipPolygon> Load()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://KosZawal/Assets/wojewodztwa.geojson"));
            using var document = JsonDocument.Parse(stream);
            var list = new List<VoivodeshipPolygon>();
            var factory = GeometryFactory.Default;
            foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
            {
                var rawName = feature.GetProperty("properties").GetProperty("nazwa").GetString() ?? "";
                var name = rawName.Length == 0
                    ? rawName
                    : char.ToUpper(rawName[0], CultureInfo.GetCultureInfo("pl-PL")) + rawName[1..];
                var geometry = ReadGeometry(feature.GetProperty("geometry"), factory);
                if (geometry is null || geometry.IsEmpty)
                    continue;
                var centroid = geometry.Centroid;
                var (lon, lat) = SphericalMercator.ToLonLat(centroid.X, centroid.Y);
                list.Add(new VoivodeshipPolygon(name, geometry, lon, lat));
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static Geometry? ReadGeometry(JsonElement geometry, GeometryFactory factory)
    {
        var type = geometry.GetProperty("type").GetString();
        var coordinates = geometry.GetProperty("coordinates");
        if (type == "Polygon")
            return factory.CreatePolygon(ReadRing(coordinates[0]), ReadHoles(coordinates));
        if (type == "MultiPolygon")
        {
            var polygons = new List<Polygon>();
            foreach (var polygon in coordinates.EnumerateArray())
            {
                var poly = factory.CreatePolygon(ReadRing(polygon[0]), ReadHoles(polygon));
                if (!poly.IsEmpty)
                    polygons.Add(poly);
            }

            return polygons.Count == 0 ? null : factory.CreateMultiPolygon(polygons.ToArray());
        }

        return null;
    }

    private static LinearRing ReadRing(JsonElement ring)
    {
        var coords = new List<Coordinate>();
        var step = Math.Max(1, ring.GetArrayLength() / 240);
        var index = 0;
        foreach (var pair in ring.EnumerateArray())
        {
            if (index++ % step != 0 && index != ring.GetArrayLength())
                continue;
            var lon = pair[0].GetDouble();
            var lat = pair[1].GetDouble();
            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            coords.Add(new Coordinate(x, y));
        }

        if (coords.Count > 0 && (Math.Abs(coords[0].X - coords[^1].X) > 1e-6 || Math.Abs(coords[0].Y - coords[^1].Y) > 1e-6))
            coords.Add(new Coordinate(coords[0].X, coords[0].Y));
        return GeometryFactory.Default.CreateLinearRing(coords.ToArray());
    }

    private static LinearRing[] ReadHoles(JsonElement polygon)
    {
        if (polygon.GetArrayLength() <= 1)
            return [];
        var holes = new List<LinearRing>();
        for (var i = 1; i < polygon.GetArrayLength(); i++)
            holes.Add(ReadRing(polygon[i]));
        return holes.ToArray();
    }
}

internal sealed record VoivodeshipPolygon(string Name, Geometry Geometry, double CenterLon, double CenterLat);
