using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KosZawal;

/// <summary>
/// Klient API Umowy NFZ (https://api.nfz.gov.pl/app-umw-api/) z limitem ~2 req/s.
/// </summary>
public sealed class NfzUmowyClient : IDisposable
{
    public const string ApiBase = "https://api.nfz.gov.pl/app-umw-api/";
    public const string ApiVersion = "1.2";
    public const string SourceUrl = "https://api.nfz.gov.pl/";

    private static readonly Dictionary<string, string> BranchByVoivodeship = new(StringComparer.CurrentCultureIgnoreCase)
    {
        ["Dolnośląskie"] = "01",
        ["Kujawsko-pomorskie"] = "02",
        ["Lubelskie"] = "03",
        ["Lubuskie"] = "04",
        ["Łódzkie"] = "05",
        ["Małopolskie"] = "06",
        ["Mazowieckie"] = "07",
        ["Opolskie"] = "08",
        ["Podkarpackie"] = "09",
        ["Podlaskie"] = "10",
        ["Pomorskie"] = "11",
        ["Śląskie"] = "12",
        ["Świętokrzyskie"] = "13",
        ["Warmińsko-mazurskie"] = "14",
        ["Wielkopolskie"] = "15",
        ["Zachodniopomorskie"] = "16",
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public NfzUmowyClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "KosZawal/1.0 (+https://github.com/signatps/nfz-kos-app)");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public static string? BranchForVoivodeship(string voivodeship) =>
        BranchByVoivodeship.TryGetValue(voivodeship.Trim(), out var code) ? code : null;

    public async Task<FacilityContractBundle> FetchFacilityContractsAsync(
        string voivodeship,
        string providerName,
        int year,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var branch = BranchForVoivodeship(voivodeship);
        progress?.Report("Szukam świadczeniodawcy w API Umowy NFZ…");
        var provider = await FindProviderAsync(providerName, year, branch, cancellationToken)
            .ConfigureAwait(false);
        if (provider is null)
        {
            return FacilityContractBundle.Missing(
                voivodeship,
                providerName,
                year,
                "Nie znaleziono świadczeniodawcy w API Umowy NFZ dla podanej nazwy/roku.");
        }

        progress?.Report($"Pobieram umowy {provider.Name} ({provider.Code})…");
        var agreements = await ListAgreementsAsync(year, provider.Branch, provider.Code, cancellationToken)
            .ConfigureAwait(false);

        var planItems = new List<ContractPlanItem>();
        var index = 0;
        foreach (var agreement in agreements)
        {
            index++;
            progress?.Report($"Pobieram plan umowy {index}/{agreements.Count}: {agreement.Code}…");
            var plans = await ListAgreementPlansAsync(agreement.Id, cancellationToken).ConfigureAwait(false);
            foreach (var plan in plans)
            {
                planItems.Add(plan with { AgreementCode = agreement.Code, ServiceName = agreement.ServiceName });
            }
        }

        var cardiology = AggregateByProduct(planItems.Where(IsCardiologyProduct));
        var kosZawal = AggregateByProduct(planItems.Where(IsKosZawalProduct));

        return new FacilityContractBundle(
            Voivodeship: voivodeship,
            RequestedName: providerName,
            Year: year,
            Status: "ok",
            Message: null,
            FetchedAtUtc: DateTime.UtcNow,
            Source: SourceUrl,
            MetricKindNote: "Kwoty umów i pozycji planu = CONTRACT_VALUE (wartość zakontraktowana). Jednostki = CONTRACT_UNITS. Nie są to potwierdzone wykonania.",
            Provider: provider,
            Agreements: agreements,
            CardiologyItems: cardiology,
            KosZawalItems: kosZawal);
    }

    public async Task<NfzProviderInfo?> FindProviderAsync(
        string providerName,
        int year,
        string? preferredBranch,
        CancellationToken cancellationToken = default)
    {
        var queries = BuildNameQueries(providerName);
        NfzProviderInfo? best = null;
        var bestScore = int.MinValue;

        foreach (var query in queries)
        {
            var url = BuildUrl("providers", new Dictionary<string, string?>
            {
                ["year"] = year.ToString(CultureInfo.InvariantCulture),
                ["name"] = query,
                ["page"] = "1",
                ["limit"] = "25",
            });
            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (document is null)
                continue;

            foreach (var entry in EnumerateEntries(document.RootElement, "entries"))
            {
                if (!entry.TryGetProperty("attributes", out var attr))
                    continue;
                var candidate = new NfzProviderInfo(
                    Code: ReadString(attr, "code"),
                    Branch: ReadString(attr, "branch"),
                    Name: ReadString(attr, "name"),
                    Nip: ReadString(attr, "nip"),
                    Regon: ReadString(attr, "regon"),
                    RegistryNumber: ReadString(attr, "registry-number"),
                    Place: ReadString(attr, "place"),
                    PostCode: ReadString(attr, "post-code"),
                    Street: ReadString(attr, "street"),
                    Phone: ReadString(attr, "phone"));
                if (string.IsNullOrWhiteSpace(candidate.Code) || string.IsNullOrWhiteSpace(candidate.Name))
                    continue;

                var score = ScoreProvider(providerName, preferredBranch, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (bestScore >= 120)
                break;
        }

        return bestScore >= 40 ? best : null;
    }

    public async Task<IReadOnlyList<ContractAgreement>> ListAgreementsAsync(
        int year,
        string branch,
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ContractAgreement>();
        var page = 1;
        while (true)
        {
            var url = BuildUrl("agreements", new Dictionary<string, string?>
            {
                ["year"] = year.ToString(CultureInfo.InvariantCulture),
                ["branch"] = branch,
                ["providerCode"] = providerCode,
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["limit"] = "25",
            });
            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (document is null)
                break;

            var batch = 0;
            foreach (var entry in EnumerateEntries(document.RootElement, "agreements"))
            {
                batch++;
                var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (!entry.TryGetProperty("attributes", out var attr) || string.IsNullOrWhiteSpace(id))
                    continue;
                list.Add(new ContractAgreement(
                    Id: id,
                    Code: ReadString(attr, "code"),
                    ServiceType: ReadString(attr, "service-type"),
                    ServiceName: ReadString(attr, "service-name"),
                    Amount: ReadDecimal(attr, "amount"),
                    UpdatedAt: ReadString(attr, "updated-at"),
                    Year: attr.TryGetProperty("year", out var y) && y.TryGetInt32(out var yi) ? yi : year,
                    Branch: ReadString(attr, "branch"),
                    ProviderCode: ReadString(attr, "provider-code"),
                    ProviderName: ReadString(attr, "provider-name"),
                    ProviderPlace: ReadString(attr, "provider-place"),
                    ProviderNip: ReadString(attr, "provider-nip"),
                    ProviderRegon: ReadString(attr, "provider-regon")));
            }

            if (batch == 0 || !HasNextPage(document.RootElement))
                break;
            page++;
        }

        return list;
    }

    public async Task<IReadOnlyList<ContractPlanItem>> ListAgreementPlansAsync(
        string agreementId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ContractPlanItem>();
        var page = 1;
        while (true)
        {
            var url = $"{ApiBase}agreements/{Uri.EscapeDataString(agreementId)}?page={page}&limit=25&format=json&api-version={ApiVersion}";
            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (document is null)
                break;

            var batch = 0;
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("plans", out var plans)
                && plans.ValueKind == JsonValueKind.Array)
            {
                foreach (var plan in plans.EnumerateArray())
                {
                    batch++;
                    if (!plan.TryGetProperty("attributes", out var attr))
                        continue;
                    list.Add(new ContractPlanItem(
                        PlanId: plan.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                        AgreementCode: "",
                        ServiceName: "",
                        ProductCode: ReadString(attr, "product-code"),
                        ProductName: ReadString(attr, "product-name"),
                        UnitCount: ReadDecimal(attr, "unit-count"),
                        Price: ReadDecimal(attr, "price"),
                        AvgPrice: ReadDecimal(attr, "avg-price"),
                        DateFrom: ReadString(attr, "date-from"),
                        DateTo: ReadString(attr, "date-to")));
                }
            }

            if (batch == 0 || !HasNextPage(document.RootElement))
                break;
            page++;
        }

        return list;
    }

    public static bool IsKosZawalProduct(ContractPlanItem item)
    {
        var name = Normalize(item.ProductName);
        return name.Contains("KOS-ZAWAL", StringComparison.Ordinal)
               || name.Contains("KOS ZAWAL", StringComparison.Ordinal)
               || name.Contains("KOMPLEKSOWA OPIEKA PO ZAWALE", StringComparison.Ordinal);
    }

    public static bool IsCardiologyProduct(ContractPlanItem item)
    {
        var name = Normalize(item.ProductName);
        if (IsKosZawalProduct(item))
            return true;
        // Unikaj fałszywych trafień „KOSZTY …”
        if (name.Contains("KOSZTY", StringComparison.Ordinal) && !name.Contains("KARDIO", StringComparison.Ordinal))
            return false;
        return name.Contains("KARDIO", StringComparison.Ordinal)
               || name.Contains("SERCA", StringComparison.Ordinal)
               || name.Contains("ZAWAL", StringComparison.Ordinal)
               || name.Contains("ZAWAŁ", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ContractPlanItem> AggregateByProduct(IEnumerable<ContractPlanItem> items)
    {
        return items
            .GroupBy(item => item.ProductCode + "\u001f" + item.ProductName, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return first with
                {
                    UnitCount = group.Sum(item => item.UnitCount),
                    Price = group.Sum(item => item.Price),
                    DateFrom = group.Min(item => item.DateFrom) ?? first.DateFrom,
                    DateTo = group.Max(item => item.DateTo) ?? first.DateTo,
                };
            })
            .OrderByDescending(item => item.Price)
            .ToList();
    }

    private static IEnumerable<string> BuildNameQueries(string providerName)
    {
        var cleaned = Regex.Replace(providerName ?? "", @"[""„”]", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        if (cleaned.Length == 0)
            yield break;

        yield return cleaned.Length <= 80 ? cleaned : cleaned[..80];

        var withoutLegal = Regex.Replace(
            cleaned,
            @"\b(SPÓŁKA|SPOLKA|AKCYJNA|Z OGRANICZONĄ|Z OGRANICZONA|ODPOWIEDZIALNOŚCIĄ|ODPOWIEDZIALNOSCIA|SP\.\s*Z\s*O\.?\s*O\.?|S\.A\.|SA|NZOZ|SPZOZ|SAMODZIELNY|PUBLICZNY)\b",
            " ",
            RegexOptions.IgnoreCase);
        withoutLegal = Regex.Replace(withoutLegal, @"\s+", " ").Trim();
        if (withoutLegal.Length >= 8 && !string.Equals(withoutLegal, cleaned, StringComparison.OrdinalIgnoreCase))
            yield return withoutLegal.Length <= 80 ? withoutLegal : withoutLegal[..80];

        var tokens = withoutLegal
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 4)
            .Take(5)
            .ToArray();
        if (tokens.Length >= 2)
            yield return string.Join(' ', tokens);
    }

    private static int ScoreProvider(string requestedName, string? preferredBranch, NfzProviderInfo candidate)
    {
        var req = Normalize(requestedName);
        var name = Normalize(candidate.Name);
        var score = 0;
        if (!string.IsNullOrEmpty(preferredBranch)
            && string.Equals(preferredBranch, candidate.Branch, StringComparison.Ordinal))
            score += 50;

        if (name == req)
            score += 100;
        else if (name.Contains(req, StringComparison.Ordinal) || req.Contains(name, StringComparison.Ordinal))
            score += 70;
        else
        {
            var reqTokens = req.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 4).ToArray();
            var hits = reqTokens.Count(token => name.Contains(token, StringComparison.Ordinal));
            score += hits * 12;
        }

        return score;
    }

    private static string Normalize(string value)
    {
        var upper = (value ?? "").ToUpperInvariant();
        upper = upper
            .Replace('Ą', 'A').Replace('Ć', 'C').Replace('Ę', 'E').Replace('Ł', 'L')
            .Replace('Ń', 'N').Replace('Ó', 'O').Replace('Ś', 'S').Replace('Ź', 'Z').Replace('Ż', 'Z');
        return Regex.Replace(upper, @"[^A-Z0-9]+", " ").Trim();
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == (HttpStatusCode)429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5 + attempt);
                await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = _nextAllowedUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            _nextAllowedUtc = DateTime.UtcNow.AddMilliseconds(550);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string BuildUrl(string path, Dictionary<string, string?> query)
    {
        var sb = new StringBuilder(ApiBase);
        sb.Append(path).Append("?format=json&api-version=").Append(ApiVersion);
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            sb.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }

    private static IEnumerable<JsonElement> EnumerateEntries(JsonElement root, string arrayName)
    {
        if (!root.TryGetProperty("data", out var data))
            yield break;
        if (data.TryGetProperty(arrayName, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
                yield return item;
            yield break;
        }

        if (data.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entries.EnumerateArray())
                yield return item;
        }
    }

    private static bool HasNextPage(JsonElement root) =>
        root.TryGetProperty("links", out var links)
        && links.TryGetProperty("next", out var next)
        && next.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(next.GetString());

    private static string ReadString(JsonElement attr, string name) =>
        attr.TryGetProperty(name, out var el) && el.ValueKind != JsonValueKind.Null
            ? el.GetString() ?? ""
            : "";

    private static decimal ReadDecimal(JsonElement attr, string name)
    {
        if (!attr.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var number))
            return number;
        if (el.ValueKind == JsonValueKind.String
            && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    public void Dispose()
    {
        _gate.Dispose();
        _http.Dispose();
    }
}
