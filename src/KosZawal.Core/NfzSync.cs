namespace KosZawal;

public sealed class NfzSync
{
    private readonly PowerBiClient _client;

    public NfzSync(PowerBiClient? client = null)
    {
        _client = client ?? new PowerBiClient();
    }

    public async Task RunAsync(
        NfzDatabase database,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SyncProgress("Łączenie", "Otwieram publiczny raport NFZ…"));
        var session = await _client.OpenAsync(cancellationToken);
        using var transaction = database.BeginSync();

        var skippedNotes = new List<(string Id, string Note)>();
        foreach (var dataset in ReportCatalog.Datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SyncProgress(dataset.Title, $"Sprawdzam kolumny: {dataset.Title}"));
            var columns = await _client.ReadableColumnsAsync(session, dataset, cancellationToken);
            var skipped = dataset.Columns.Select(column => column.Source).Except(columns).ToArray();
            if (skipped.Length > 0)
                skippedNotes.Add((dataset.Id, string.Join(" | ", skipped)));

            await _client.DownloadAsync(
                session,
                dataset,
                columns,
                page =>
                {
                    database.InsertPage(dataset, columns, page, transaction);
                    return Task.CompletedTask;
                },
                progress,
                cancellationToken);
        }

        progress?.Report(new SyncProgress("Indeksy", "Buduję indeksy lokalnej bazy…"));
        database.CreateIndexes(transaction);
        transaction.Commit();

        foreach (var (id, note) in skippedNotes)
            database.SetMeta("skipped:" + id, note);

        database.SetMeta("source_url", ReportCatalog.ReportUrl);
        database.SetMeta("refreshed_at", session.RefreshedAt);
        database.SetMeta("synced_at", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"));
        database.SetMeta("methodology", session.Methodology ?? MethodologyText.Fallback);
        database.SetMeta("model_id", session.ModelId.ToString());
        progress?.Report(new SyncProgress("Gotowe", "Lokalna baza jest aktualna."));
    }
}
