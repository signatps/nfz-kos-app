using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KosZawal;

public sealed class PowerBiClient
{
    private static readonly Regex ClusterRegex = new(
        "resolvedClusterUri\\s*=\\s*'(?<uri>https://[^']+)'",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public PowerBiClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(4),
        };
    }

    public async Task<ReportSession> OpenAsync(CancellationToken cancellationToken)
    {
        using var page = await SendAsync(HttpMethod.Get, ReportCatalog.ReportUrl, null, null, cancellationToken);
        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var match = ClusterRegex.Match(html);
        if (!match.Success)
            throw new InvalidOperationException("Nie udało się odczytać adresu klastra Power BI.");

        var cluster = new Uri(match.Groups["uri"].Value);
        var host = cluster.Host.Replace("-redirect", "-api", StringComparison.Ordinal);
        var api = $"{cluster.Scheme}://{host}";
        var resourceKey = DecodeResourceKey(ReportCatalog.ReportUrl);

        var headers = new Dictionary<string, string>
        {
            ["X-PowerBI-ResourceKey"] = resourceKey,
            ["ActivityId"] = Guid.NewGuid().ToString(),
            ["RequestId"] = Guid.NewGuid().ToString(),
        };

        using var explorationResponse = await SendAsync(
            HttpMethod.Get,
            $"{api}/public/reports/{resourceKey}/modelsAndExploration?preferReadOnlySession=true",
            headers,
            null,
            cancellationToken);
        using var exploration = JsonDocument.Parse(await explorationResponse.Content.ReadAsStringAsync(cancellationToken));
        var model = exploration.RootElement.GetProperty("models")[0];
        var report = exploration.RootElement.GetProperty("exploration").GetProperty("report");

        var methodology = ExtractMethodology(exploration.RootElement);
        var refreshed = model.TryGetProperty("lastRefreshTime", out var refreshElement)
            ? refreshElement.GetString()
            : null;

        return new ReportSession(
            api,
            resourceKey,
            model.GetProperty("id").GetInt64(),
            model.GetProperty("dbName").GetString() ?? "",
            report.GetProperty("objectId").GetString() ?? "",
            refreshed,
            methodology);
    }

    public async Task<IReadOnlyList<string>> ReadableColumnsAsync(
        ReportSession session,
        DatasetDefinition dataset,
        CancellationToken cancellationToken)
    {
        var wanted = dataset.Columns.Select(column => column.Source).ToList();
        if (await TryQueryAsync(session, dataset.Entity, wanted, 2, null, cancellationToken) is not null)
            return wanted;

        var readable = new List<string>();
        foreach (var column in wanted)
        {
            var probe = readable.Append(column).ToList();
            if (await TryQueryAsync(session, dataset.Entity, probe, 2, null, cancellationToken) is not null)
                readable.Add(column);
        }

        if (readable.Count == 0)
            throw new InvalidOperationException($"Raport NFZ nie oddał kolumn tabeli {dataset.Title}.");

        return readable;
    }

    public async Task DownloadAsync(
        ReportSession session,
        DatasetDefinition dataset,
        IReadOnlyList<string> columns,
        Func<IReadOnlyList<object?[]>, Task> consumePage,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        JsonElement? restart = null;
        var page = 0;
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await TryQueryAsync(session, dataset.Entity, columns, 20000, restart, cancellationToken)
                ?? throw new InvalidOperationException($"Puste odpowiedź dla tabeli {dataset.Title}.");

            using (document)
            {
                var dataSource = document.RootElement
                    .GetProperty("results")[0]
                    .GetProperty("result")
                    .GetProperty("data")
                    .GetProperty("dsr")
                    .GetProperty("DS")[0];

                var rows = DsrParser.Parse(dataSource);
                if (restart is not null && rows.Count > 0)
                    rows.RemoveAt(0);

                total += rows.Count;
                page++;
                progress?.Report(new SyncProgress(
                    dataset.Title,
                    $"{dataset.Title}: {total:N0} wierszy"));

                if (rows.Count > 0)
                    await consumePage(rows);

                var complete = DsrParser.IsComplete(dataSource);
                restart = DsrParser.RestartTokens(dataSource);
                if (complete || rows.Count == 0 || restart is null)
                    break;
            }
        }
    }

    private async Task<JsonDocument?> TryQueryAsync(
        ReportSession session,
        string entity,
        IReadOnlyList<string> columns,
        int window,
        JsonElement? restart,
        CancellationToken cancellationToken)
    {
        var payload = BuildQuery(session, entity, columns, window, restart);
        using var response = await SendAsync(
            HttpMethod.Post,
            $"{session.ApiRoot}/public/reports/querydata?synchronous=true",
            SessionHeaders(session),
            payload,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = JsonDocument.Parse(body);
        var result = document.RootElement.GetProperty("results")[0];
        if (!result.TryGetProperty("result", out var ok)
            || !ok.TryGetProperty("data", out var data)
            || !data.TryGetProperty("dsr", out _))
        {
            document.Dispose();
            return null;
        }

        return document;
    }

    private static string BuildQuery(
        ReportSession session,
        string entity,
        IReadOnlyList<string> columns,
        int window,
        JsonElement? restart)
    {
        var select = new List<object>();
        for (var i = 0; i < columns.Count; i++)
        {
            select.Add(new Dictionary<string, object>
            {
                ["Column"] = new Dictionary<string, object>
                {
                    ["Expression"] = new Dictionary<string, object>
                    {
                        ["SourceRef"] = new Dictionary<string, object> { ["Source"] = "t" },
                    },
                    ["Property"] = columns[i],
                },
                ["Name"] = "c" + i,
            });
        }

        var windowObject = new Dictionary<string, object> { ["Count"] = window };
        if (restart is { } tokens)
            windowObject["RestartTokens"] = tokens;

        var payload = new Dictionary<string, object>
        {
            ["version"] = "1.0.0",
            ["queries"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["Query"] = new Dictionary<string, object>
                    {
                        ["Commands"] = new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["SemanticQueryDataShapeCommand"] = new Dictionary<string, object>
                                {
                                    ["Query"] = new Dictionary<string, object>
                                    {
                                        ["Version"] = 2,
                                        ["From"] = new object[]
                                        {
                                            new Dictionary<string, object>
                                            {
                                                ["Name"] = "t",
                                                ["Entity"] = entity,
                                                ["Type"] = 0,
                                            },
                                        },
                                        ["Select"] = select,
                                    },
                                    ["Binding"] = new Dictionary<string, object>
                                    {
                                        ["Primary"] = new Dictionary<string, object>
                                        {
                                            ["Groupings"] = new object[]
                                            {
                                                new Dictionary<string, object>
                                                {
                                                    ["Projections"] = Enumerable.Range(0, columns.Count).ToArray(),
                                                },
                                            },
                                        },
                                        ["DataReduction"] = new Dictionary<string, object>
                                        {
                                            ["DataVolume"] = 3,
                                            ["Primary"] = new Dictionary<string, object>
                                            {
                                                ["Window"] = windowObject,
                                            },
                                        },
                                        ["Version"] = 1,
                                    },
                                    ["ExecutionMetricsKind"] = 1,
                                },
                            },
                        },
                    },
                    ["QueryId"] = "",
                    ["ApplicationContext"] = new Dictionary<string, object>
                    {
                        ["DatasetId"] = session.DatasetId,
                        ["Sources"] = new object[]
                        {
                            new Dictionary<string, object> { ["ReportId"] = session.ReportId },
                        },
                    },
                },
            },
            ["cancelQueries"] = Array.Empty<object>(),
            ["modelId"] = session.ModelId,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static Dictionary<string, string> SessionHeaders(ReportSession session) => new()
    {
        ["X-PowerBI-ResourceKey"] = session.ResourceKey,
        ["ActivityId"] = Guid.NewGuid().ToString(),
        ["RequestId"] = Guid.NewGuid().ToString(),
    };

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        Dictionary<string, string>? headers,
        string? json,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "KosZawal/1.0");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                    request.Headers.TryAddWithoutValidation(key, value);
            }

            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is 429 or 500 or 502 or 503 or 504 && attempt < 4)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = errorBody.Length > 700 ? errorBody[..700] : errorBody;
                response.Dispose();
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
            }

            return response;
        }

        throw new InvalidOperationException("Serwer NFZ nie odpowiedział.");
    }

    private static string DecodeResourceKey(string url)
    {
        var token = url.Split("r=", 2)[1];
        var padding = token.Length % 4 == 0 ? "" : new string('=', 4 - token.Length % 4);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(token + padding));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("k").GetString()
            ?? throw new InvalidOperationException("Brak klucza raportu.");
    }

    private static string? ExtractMethodology(JsonElement root)
    {
        if (!root.TryGetProperty("exploration", out var exploration)
            || !exploration.TryGetProperty("explorationContent", out var content)
            || !content.TryGetProperty("explorationDocument", out var documentElement))
            return null;

        var documentText = documentElement.GetString();
        if (string.IsNullOrWhiteSpace(documentText))
            return null;

        using var document = JsonDocument.Parse(documentText);
        if (!document.RootElement.TryGetProperty("pages", out var pages)
            || !pages.TryGetProperty("pages", out var pageList))
            return null;

        foreach (var page in pageList.EnumerateArray())
        {
            if (!page.TryGetProperty("content", out var pageContent))
                continue;
            if (!pageContent.TryGetProperty("displayName", out var name) || name.GetString() != "Metodyka")
                continue;

            var paragraphs = new List<string>();
            CollectParagraphs(pageContent, paragraphs);
            var text = string.Join("\n\n", paragraphs.Where(paragraph => paragraph.Length > 0));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    private static void CollectParagraphs(JsonElement element, List<string> paragraphs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("paragraphs", out var paragraphList) && paragraphList.ValueKind == JsonValueKind.Array)
            {
                foreach (var paragraph in paragraphList.EnumerateArray())
                {
                    if (!paragraph.TryGetProperty("textRuns", out var runs))
                        continue;
                    var builder = new StringBuilder();
                    foreach (var run in runs.EnumerateArray())
                    {
                        if (run.TryGetProperty("value", out var value))
                            builder.Append(value.GetString());
                    }

                    var text = builder.ToString().Trim();
                    if (text.Length > 0)
                        paragraphs.Add(text);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name != "paragraphs")
                    CollectParagraphs(property.Value, paragraphs);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectParagraphs(item, paragraphs);
        }
    }
}

public sealed record ReportSession(
    string ApiRoot,
    string ResourceKey,
    long ModelId,
    string DatasetId,
    string ReportId,
    string? RefreshedAt,
    string? Methodology);
