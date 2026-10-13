using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KosZawal;

/// <summary>
/// Import oficjalnego eksportu RPWDL 2.0 (podmioty lecznicze, CSV w ZIP).
/// Endpoint produkcyjny potwierdzony: ledgers-zip/initial.
/// </summary>
public sealed class RpwdlImporter
{
    public const string SourceUrl = "https://rpwdl2.ezdrowie.gov.pl/";
    public const string DownloadUrl =
        "https://rpwdl2.ezdrowie.gov.pl/api/rpwdl-public-resource/ledgers-zip/initial"
        + "?activityType=HEALTHCARE_PROVIDER&activeLedgers=true&fileTypeInsideZip=CSV";

    private static readonly HashSet<string> CardiologyCodes = new(StringComparer.Ordinal)
    {
        "4100", "4101", "4106",
    };

    private static readonly HashSet<string> RehabCodes = new(StringComparer.Ordinal)
    {
        "4308", "2302", "2308",
    };

    private static readonly HashSet<string> RelatedCodes = new(StringComparer.Ordinal)
    {
        "4560", "4561",
    };

    private readonly HttpClient _http;

    public RpwdlImporter(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "KosZawal/1.0 (+https://github.com/signatps/nfz-kos-app)");
    }

    public static string? ClassifyCategory(string specialtyCode)
    {
        if (specialtyCode == "4106")
            return "cardiac_intensive_care";
        if (CardiologyCodes.Contains(specialtyCode))
            return "cardiology";
        if (RehabCodes.Contains(specialtyCode))
            return "cardiac_rehabilitation";
        if (RelatedCodes.Contains(specialtyCode))
            return "related";
        return null;
    }

    public static bool IsImportedSpecialty(string specialtyCode) =>
        ClassifyCategory(specialtyCode) is not null;

    public async Task ImportAsync(
        NfzDatabase database,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Pobieram eksport RPWDL (podmioty lecznicze, CSV)…");
        var tempZip = Path.Combine(Path.GetTempPath(), "koszawal-rpwdl-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var file = File.Create(tempZip))
            await using (var stream = await _http.GetStreamAsync(DownloadUrl, cancellationToken).ConfigureAwait(false))
            {
                await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            var hash = await Sha256FileAsync(tempZip, cancellationToken).ConfigureAwait(false);
            progress?.Report("Przetwarzam komórki kardiologiczne z RPWDL…");
            var (providers, cells, referenceDate) = ParseZip(tempZip, progress, cancellationToken);
            progress?.Report($"Zapisuję {cells.Count} komórek / {providers.Count} podmiotów…");
            database.ReplaceRpwdlSnapshot(providers, cells, referenceDate, hash, DownloadUrl);
            progress?.Report($"RPWDL: {cells.Count} komórek kardiologicznych (stan rejestru {referenceDate}).");
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* ignore */ }
        }
    }

    private static (List<RpwdlProviderRow> Providers, List<RpwdlCellRow> Cells, string ReferenceDate) ParseZip(
        string zipPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var info = zip.GetEntry("Info.txt");
        var referenceDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (info is not null)
        {
            using var reader = new StreamReader(info.Open(), Encoding.UTF8);
            var text = reader.ReadToEnd();
            var match = Regex.Match(text, @"\d{4}-\d{2}-\d{2}");
            if (match.Success)
                referenceDate = match.Value;
        }

        var wantedLedgers = new HashSet<string>(StringComparer.Ordinal);
        var cells = new List<RpwdlCellRow>();
        var cellsEntry = zip.GetEntry("komorki.csv")
            ?? throw new InvalidOperationException("Brak komorki.csv w eksporcie RPWDL.");
        using (var stream = cellsEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var header = ReadCsvLine(reader) ?? throw new InvalidOperationException("Pusty komorki.csv.");
            var index = IndexHeader(header);
            string? line;
            var n = 0;
            while ((line = ReadCsvLine(reader)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                n++;
                if (n % 200_000 == 0)
                    progress?.Report($"Skan komórek RPWDL: {n:N0}…");

                var fields = ParseCsvFields(line);
                var code = Get(fields, index, "kodResortVIII");
                var category = ClassifyCategory(code);
                if (category is null)
                    continue;
                var end = Get(fields, index, "Data zakończenia działalności komórki");
                if (!string.IsNullOrEmpty(end) && !string.Equals(end, "NULL", StringComparison.OrdinalIgnoreCase))
                    continue;

                var ledgerId = Get(fields, index, "ID Księgi");
                wantedLedgers.Add(ledgerId);
                cells.Add(new RpwdlCellRow(
                    LedgerId: ledgerId,
                    FacilityId: Get(fields, index, "ID ZOZ"),
                    UnitId: Get(fields, index, "ID Jednostki"),
                    CellId: Get(fields, index, "ID Komórki"),
                    Regon: Get(fields, index, "Regon"),
                    SpecialtyCode: code,
                    SpecialtyName: Get(fields, index, "Specjalność komórki"),
                    Category: category,
                    Name: Get(fields, index, "Nazwa komórki"),
                    Place: Get(fields, index, "Miejscowość"),
                    Street: Get(fields, index, "Ulica"),
                    Building: Get(fields, index, "Budynek"),
                    PostCode: Get(fields, index, "Kod pocztowy"),
                    BedsTotal: ParseInt(Get(fields, index, "Liczba łóżek ogółem")),
                    BedsCardiacIcu: ParseInt(Get(fields, index, "Liczba łóżek intensywnego nadzoru kardiologicznego")),
                    SuspendedFrom: NullIfNullToken(Get(fields, index, "Początek okresu zawieszenia")),
                    SuspendedTo: NullIfNullToken(Get(fields, index, "Koniec okresu zawieszenia"))));
            }
        }

        var providers = new List<RpwdlProviderRow>();
        var providersEntry = zip.GetEntry("podmioty.csv")
            ?? throw new InvalidOperationException("Brak podmioty.csv w eksporcie RPWDL.");
        using (var stream = providersEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var header = ReadCsvLine(reader) ?? throw new InvalidOperationException("Pusty podmioty.csv.");
            var index = IndexHeader(header);
            string? line;
            while ((line = ReadCsvLine(reader)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = ParseCsvFields(line);
                var ledgerId = Get(fields, index, "ID Księgi");
                if (!wantedLedgers.Contains(ledgerId))
                    continue;
                providers.Add(new RpwdlProviderRow(
                    LedgerId: ledgerId,
                    Nip: Get(fields, index, "NIP"),
                    Regon: Get(fields, index, "REGON"),
                    BookNumber: Get(fields, index, "Numer księgi rejestrowej"),
                    Name: FirstNonEmpty(Get(fields, index, "Nazwa"), Get(fields, index, "Nazwa podmiotu")),
                    Place: Get(fields, index, "Miejscowość"),
                    Street: Get(fields, index, "Ulica"),
                    Building: Get(fields, index, "Budynek"),
                    PostCode: Get(fields, index, "Kod pocztowy"),
                    AgencyCode: Get(fields, index, "Kod organu")));
            }
        }

        return (providers, cells, referenceDate);
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Dictionary<string, int> IndexHeader(string headerLine)
    {
        var fields = ParseCsvFields(headerLine);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
            map[fields[i].Trim().Trim('"')] = i;
        return map;
    }

    private static string Get(IReadOnlyList<string> fields, Dictionary<string, int> index, string name) =>
        index.TryGetValue(name, out var i) && i < fields.Count ? NullIfNullToken(fields[i]) : "";

    private static string NullIfNullToken(string value) =>
        string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static int ParseInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        return int.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }

    private static string? ReadCsvLine(StreamReader reader)
    {
        var sb = new StringBuilder();
        var inQuotes = false;
        while (true)
        {
            var ch = reader.Read();
            if (ch < 0)
                return sb.Length == 0 ? null : sb.ToString();
            var c = (char)ch;
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && reader.Peek() == '\n')
                    reader.Read();
                return sb.ToString();
            }

            sb.Append(c);
        }
    }

    private static List<string> ParseCsvFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ';' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        fields.Add(sb.ToString());
        return fields;
    }
}

public sealed record RpwdlProviderRow(
    string LedgerId,
    string Nip,
    string Regon,
    string BookNumber,
    string Name,
    string Place,
    string Street,
    string Building,
    string PostCode,
    string AgencyCode);

public sealed record RpwdlCellRow(
    string LedgerId,
    string FacilityId,
    string UnitId,
    string CellId,
    string Regon,
    string SpecialtyCode,
    string SpecialtyName,
    string Category,
    string Name,
    string Place,
    string Street,
    string Building,
    string PostCode,
    int BedsTotal,
    int BedsCardiacIcu,
    string SuspendedFrom,
    string SuspendedTo);
