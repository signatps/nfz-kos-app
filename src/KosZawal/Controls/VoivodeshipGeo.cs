using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;
using Mapsui;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using Color = Mapsui.Styles.Color;

namespace KosZawal.Controls;

internal static class VoivodeshipGeo
{
    private static IReadOnlyList<VoivodeshipPolygon>? _polygons;
    private static IFeature? _maskFeature;

    public static IReadOnlyList<VoivodeshipPolygon> Polygons => _polygons ??= Load();

    /// <summary>
    /// Prosta maska poza prostokątem Polski — bez OverlayNG/Difference na skomplikowanych granicach,
    /// które na Windows potrafiły wywalić proces bez komunikatu (Skia/native).
    /// </summary>
    public static IFeature NeighborMaskFeature() => _maskFeature ??= BuildNeighborMask();

    private static IFeature BuildNeighborMask()
    {
        var factory = GeometryFactory.Default;
        var extent = PolandExtent();
        // Cztery prostokąty wokół Polski zamiast jednej dziurawej geometrii.
        // Wąski kołnierz (~600 km) — szeroka maska (4e6 m) rozciągała extent warstwy
        // daleko na północ i przy nieudanym ZoomToBox potrafiła „uciekać” w kierunku Arktyki.
        const double pad = 600_000;
        var pads = new[]
        {
            Rect(factory, extent.MinX - pad, extent.MinY - pad, extent.MaxX + pad, extent.MinY),
            Rect(factory, extent.MinX - pad, extent.MaxY, extent.MaxX + pad, extent.MaxY + pad),
            Rect(factory, extent.MinX - pad, extent.MinY, extent.MinX, extent.MaxY),
            Rect(factory, extent.MaxX, extent.MinY, extent.MaxX + pad, extent.MaxY),
        };
        var mask = factory.CreateMultiPolygon(pads);
        var feature = new GeometryFeature(mask);
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(new Color(243, 240, 232)),
            Outline = null,
        });
        return feature;
    }

    private static Polygon Rect(GeometryFactory factory, double minX, double minY, double maxX, double maxY) =>
        factory.CreatePolygon(
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]);

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
                Font = new Font { FontFamily = "Segoe UI", Size = city.IsCapital ? 13 : 11 },
                Offset = new Offset(0, city.IsCapital ? -18 : -14),
                CollisionDetection = false,
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
                // Korona jako trójkąt + etykieta — bez PNG/Image (to wywalało Skia w single-file).
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Triangle,
                    SymbolScale = 0.85,
                    Fill = new Brush(new Color(212, 160, 48)),
                    Outline = new Pen(Color.White, 2),
                });
                feature.Styles.Add(new LabelStyle
                {
                    Text = "Lider · " + ShortName(pin.Name),
                    ForeColor = new Color(18, 36, 51),
                    BackColor = new Brush(new Color(243, 240, 232, 235)),
                    Font = new Font { FontFamily = "Segoe UI", Size = 11 },
                    Offset = new Offset(0, -22),
                    CollisionDetection = false,
                });
            }
            else
            {
                var color = pin.Quality == "nominatim"
                    ? (pin.KosShare ?? 0) >= 0.5 ? new Color(196, 71, 79) : new Color(14, 110, 106)
                    : new Color(138, 106, 47);
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Ellipse,
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

    private static bool TryLocate(HospitalPin pin, out double lon, out double lat)
    {
        // Używaj zapisanych współrzędnych niezależnie od jakości (nominatim/miasto/adres).
        if (pin.Latitude is double pinLat && pin.Longitude is double pinLon
            && pinLat is >= 48.5 and <= 55.5 && pinLon is >= 13.5 and <= 24.5)
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
