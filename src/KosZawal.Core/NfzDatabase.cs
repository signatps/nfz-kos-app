using System.Globalization;
using Microsoft.Data.Sqlite;

namespace KosZawal;

public sealed class NfzDatabase : IDisposable
{
    private readonly string _path;
    private readonly SqliteConnection _connection;

    public NfzDatabase(string path)
    {
        _path = path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        EnsureSchema();
    }

    public string Path => _path;

    public bool HasData
    {
        get
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM placowki;";
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }
    }

    public string? Meta(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string? value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO meta(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public SqliteTransaction BeginSync()
    {
        var transaction = _connection.BeginTransaction();
        Execute("DELETE FROM placowki; DELETE FROM kos; DELETE FROM trendy; DELETE FROM wojewodztwa;", transaction);
        return transaction;
    }

    public void InsertPage(
        DatasetDefinition dataset,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<object?[]> rows,
        SqliteTransaction transaction)
    {
        var used = dataset.Columns.Where(column => sourceColumns.Contains(column.Source)).ToList();
        var indexes = used.Select(column => sourceColumns.ToList().IndexOf(column.Source)).ToArray();
        var sqlColumns = string.Join(", ", used.Select(column => column.Sql));
        var parameters = string.Join(", ", used.Select((_, index) => $"$p{index}"));
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {dataset.Id} ({sqlColumns}) VALUES ({parameters});";
        foreach (var row in rows)
        {
            command.Parameters.Clear();
            for (var i = 0; i < used.Count; i++)
            {
                var value = indexes[i] >= 0 && indexes[i] < row.Length ? row[indexes[i]] : null;
                command.Parameters.AddWithValue($"$p{i}", ToDb(value, used[i].Kind));
            }

            command.ExecuteNonQuery();
        }
    }

    public void ApplyGeocodes(IEnumerable<GeoHit> hits)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO geokody(wojewodztwo, swiadczeniodawca, lat, lon, jakosc)
            VALUES($woj, $name, $lat, $lon, $quality)
            ON CONFLICT(wojewodztwo, swiadczeniodawca) DO UPDATE SET
                lat = excluded.lat,
                lon = excluded.lon,
                jakosc = excluded.jakosc;
            """;
        foreach (var hit in hits)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$woj", hit.Voivodeship);
            command.Parameters.AddWithValue("$name", hit.Name);
            command.Parameters.AddWithValue("$lat", hit.Latitude);
            command.Parameters.AddWithValue("$lon", hit.Longitude);
            command.Parameters.AddWithValue("$quality", hit.Quality);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<int> Years()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT rok FROM placowki WHERE rok IS NOT NULL ORDER BY rok DESC;";
        var years = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            years.Add(reader.GetInt32(0));
        return years;
    }

    public IReadOnlyList<string> Voivodeships()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT wojewodztwo FROM placowki WHERE wojewodztwo IS NOT NULL ORDER BY wojewodztwo;";
        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    public KpiSnapshot Compute(int year, string infarctType)
    {
        var hospitalizations = ScalarDouble("""
            SELECT COALESCE(SUM(liczba_hospitalizacji), 0)
            FROM placowki
            WHERE rok = $year AND typ_zawalu = $type;
            """, year, infarctType);

        var centersByVoivodeship = ReadPairs("""
            SELECT wojewodztwo, COUNT(*)
            FROM placowki
            WHERE rok = $year AND typ_zawalu = $type AND czy_kos = 'TAK'
            GROUP BY wojewodztwo;
            """, year, infarctType);

        var kosByVoivodeship = ReadTriples("""
            SELECT wojewodztwo,
                   COALESCE(SUM(proc_kos * liczba_hospitalizacji), 0),
                   COALESCE(SUM(liczba_hospitalizacji), 0)
            FROM kos
            WHERE rok = $year AND typ_zawalu = $type
            GROUP BY wojewodztwo;
            """, year, infarctType);

        var allByVoivodeship = ReadPairs("""
            SELECT wojewodztwo, COALESCE(SUM(liczba_hospitalizacji), 0)
            FROM placowki
            WHERE rok = $year AND typ_zawalu = $type
            GROUP BY wojewodztwo;
            """, year, infarctType);

        var names = allByVoivodeship.Keys
            .Concat(centersByVoivodeship.Keys)
            .Concat(kosByVoivodeship.Keys)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        var stats = new List<VoivodeshipStat>();
        foreach (var name in names)
        {
            var all = allByVoivodeship.GetValueOrDefault(name);
            kosByVoivodeship.TryGetValue(name, out var kos);
            var centers = (int)Math.Round(centersByVoivodeship.GetValueOrDefault(name));
            stats.Add(new VoivodeshipStat(
                name,
                centers,
                all,
                kos.Cases,
                all > 0 ? kos.Cases / all : 0));
        }

        stats = stats.OrderByDescending(stat => stat.Centers).ThenBy(stat => stat.Name).ToList();
        var kosCases = stats.Sum(stat => stat.KosHospitalizations);
        var kosDenominator = kosByVoivodeship.Values.Sum(item => item.Denominator);

        var monthly = new List<MonthPoint>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT substr(data, 1, 7),
                       COALESCE(SUM(wartosc), 0),
                       COALESCE(SUM(proc_kos * wartosc), 0)
                FROM trendy
                WHERE typ_zawalu = $type
                  AND atrybut = $attr
                  AND data LIKE $prefix
                  AND swiadczeniodawca NOT LIKE '%OGÓŁEM%'
                GROUP BY substr(data, 1, 7)
                ORDER BY 1;
                """;
            command.Parameters.AddWithValue("$type", infarctType);
            command.Parameters.AddWithValue("$attr", ReportCatalog.HospitalizationsAttribute);
            command.Parameters.AddWithValue("$prefix", year + "-%");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hosp = reader.GetDouble(1);
                var cases = reader.GetDouble(2);
                monthly.Add(new MonthPoint(reader.GetString(0), hosp, cases, hosp > 0 ? cases / hosp : 0));
            }
        }

        var years = Years().OrderBy(value => value).Select(value =>
        {
            var hosp = ScalarDouble("""
                SELECT COALESCE(SUM(liczba_hospitalizacji), 0) FROM placowki
                WHERE rok = $year AND typ_zawalu = $type;
                """, value, infarctType);
            var cases = ScalarDouble("""
                SELECT COALESCE(SUM(proc_kos * liczba_hospitalizacji), 0) FROM kos
                WHERE rok = $year AND typ_zawalu = $type;
                """, value, infarctType);
            var centers = (int)ScalarDouble("""
                SELECT COUNT(*) FROM placowki
                WHERE rok = $year AND typ_zawalu = $type AND czy_kos = 'TAK';
                """, value, infarctType);
            return new YearPoint(value, hosp, cases, hosp > 0 ? cases / hosp : 0, centers);
        }).ToList();

        return new KpiSnapshot
        {
            Year = year,
            InfarctType = infarctType,
            Hospitalizations = hospitalizations,
            KosHospitalizations = kosCases,
            NationalShare = hospitalizations > 0 ? kosCases / hospitalizations : 0,
            ShareAmongKosCenters = kosDenominator > 0 ? kosCases / kosDenominator : 0,
            Centers = stats.Sum(stat => stat.Centers),
            VoivodeshipsWithCenters = stats.Count(stat => stat.Centers > 0),
            ByVoivodeship = stats,
            Monthly = monthly,
            Years = years,
        };
    }

    public TablePage Query(
        string datasetId,
        int? year,
        string? infarctType,
        string? voivodeship,
        string? search,
        bool aggregateOnly,
        int limit)
    {
        var dataset = ReportCatalog.ById(datasetId);
        var filters = new List<string>();
        var parameters = new List<(string, object)>();

        if (year is int selectedYear && dataset.Columns.Any(column => column.Sql == "rok"))
        {
            filters.Add("rok = $year");
            parameters.Add(("$year", selectedYear));
        }
        else if (year is int trendYear && dataset.Id == "trendy")
        {
            filters.Add("data LIKE $prefix");
            parameters.Add(("$prefix", trendYear + "-%"));
        }

        if (!string.IsNullOrWhiteSpace(infarctType))
        {
            filters.Add("typ_zawalu = $type");
            parameters.Add(("$type", infarctType));
        }

        if (!string.IsNullOrWhiteSpace(voivodeship))
        {
            filters.Add("wojewodztwo = $woj");
            parameters.Add(("$woj", voivodeship));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("swiadczeniodawca LIKE $search");
            parameters.Add(("$search", "%" + search.Trim() + "%"));
        }

        if (aggregateOnly && dataset.Id == "trendy")
            filters.Add("swiadczeniodawca LIKE '%OGÓŁEM%'");

        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);
        var order = dataset.Id switch
        {
            "trendy" => "ORDER BY data, wojewodztwo, swiadczeniodawca, atrybut",
            "wojewodztwa" => "ORDER BY wojewodztwo, kategoria",
            _ => "ORDER BY wojewodztwo, swiadczeniodawca",
        };

        long total;
        using (var count = _connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM {dataset.Id} {where};";
            foreach (var (name, value) in parameters)
                count.Parameters.AddWithValue(name, value);
            total = Convert.ToInt64(count.ExecuteScalar());
        }

        var columns = dataset.Columns.ToList();
        var rows = new List<object?[]>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT {string.Join(", ", columns.Select(column => column.Sql))}
                FROM {dataset.Id}
                {where}
                {order}
                LIMIT $limit;
                """;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new object?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }

        return new TablePage(
            columns.Select(column => column.Header).ToArray(),
            columns.Select(column => column.Sql).ToArray(),
            columns.Select(column => column.Kind).ToArray(),
            rows,
            total);
    }

    public IEnumerable<object?[]> ExportRows(
        string datasetId,
        int? year,
        string? infarctType,
        string? voivodeship,
        string? search,
        bool aggregateOnly)
    {
        var pageSize = 5000;
        var offset = 0;
        while (true)
        {
            var page = QueryOffset(datasetId, year, infarctType, voivodeship, search, aggregateOnly, pageSize, offset);
            if (page.Count == 0)
                yield break;
            foreach (var row in page)
                yield return row;
            if (page.Count < pageSize)
                yield break;
            offset += pageSize;
        }
    }

    public IReadOnlyList<HospitalPin> Hospitals(int year, string infarctType)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT p.wojewodztwo,
                   p.swiadczeniodawca,
                   p.liczba_hospitalizacji,
                   k.proc_kos,
                   g.lat,
                   g.lon,
                   COALESCE(g.jakosc, '')
            FROM placowki p
            LEFT JOIN kos k
              ON k.rok = p.rok
             AND k.wojewodztwo = p.wojewodztwo
             AND k.swiadczeniodawca = p.swiadczeniodawca
             AND k.typ_zawalu = p.typ_zawalu
            LEFT JOIN geokody g
              ON g.wojewodztwo = p.wojewodztwo
             AND g.swiadczeniodawca = p.swiadczeniodawca
            WHERE p.rok = $year
              AND p.typ_zawalu = $type
              AND p.czy_kos = 'TAK'
            ORDER BY p.wojewodztwo, p.liczba_hospitalizacji DESC;
            """;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$type", infarctType);
        var pins = new List<HospitalPin>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            pins.Add(new HospitalPin(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6)));
        }

        return pins;
    }

    public HospitalDetail? HospitalDetail(string voivodeship, string name, string infarctType, int leaderYear)
    {
        var years = new List<HospitalYearSeries>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT p.rok,
                       COALESCE(p.liczba_hospitalizacji, 0),
                       COALESCE(p.czy_kos, 'NIE'),
                       k.proc_kos,
                       k.kontrola_14,
                       k.rehab_30,
                       k.zakon_365
                FROM placowki p
                LEFT JOIN kos k
                  ON k.rok = p.rok
                 AND k.wojewodztwo = p.wojewodztwo
                 AND k.swiadczeniodawca = p.swiadczeniodawca
                 AND k.typ_zawalu = p.typ_zawalu
                WHERE p.wojewodztwo = $woj
                  AND p.swiadczeniodawca = $name
                  AND p.typ_zawalu = $type
                ORDER BY p.rok;
                """;
            command.Parameters.AddWithValue("$woj", voivodeship);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$type", infarctType);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                years.Add(new HospitalYearSeries(
                    reader.GetInt32(0),
                    reader.GetDouble(1),
                    string.Equals(reader.GetString(2), "TAK", StringComparison.OrdinalIgnoreCase),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6)));
            }
        }

        if (years.Count == 0)
            return null;

        var monthly = new List<MonthPoint>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT data,
                       COALESCE(wartosc, 0),
                       COALESCE(proc_kos, 0)
                FROM trendy
                WHERE wojewodztwo = $woj
                  AND swiadczeniodawca = $name
                  AND typ_zawalu = $type
                  AND atrybut = $attr
                ORDER BY data;
                """;
            command.Parameters.AddWithValue("$woj", voivodeship);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$type", infarctType);
            command.Parameters.AddWithValue("$attr", ReportCatalog.HospitalizationsAttribute);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hosp = reader.GetDouble(1);
                var share = reader.GetDouble(2);
                monthly.Add(new MonthPoint(reader.GetString(0), hosp, hosp * share, share));
            }
        }

        string quality = "";
        double? lat = null;
        double? lon = null;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT lat, lon, COALESCE(jakosc, '')
                FROM geokody
                WHERE wojewodztwo = $woj AND swiadczeniodawca = $name
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$woj", voivodeship);
            command.Parameters.AddWithValue("$name", name);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                lat = reader.IsDBNull(0) ? null : reader.GetDouble(0);
                lon = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                quality = reader.IsDBNull(2) ? "" : reader.GetString(2);
            }
        }

        var leaders = Hospitals(leaderYear, infarctType)
            .GroupBy(pin => pin.Voivodeship, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(pin => pin.Hospitalizations).ThenBy(pin => pin.Name).First())
            .ToList();
        var isLeader = leaders.Any(pin =>
            string.Equals(pin.Voivodeship, voivodeship, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(pin.Name, name, StringComparison.Ordinal));

        return new HospitalDetail(voivodeship, name, infarctType, isLeader, quality, lat, lon, years, monthly);
    }

    public FacilityContractBundle? TryGetCachedContracts(string voivodeship, string name, int year, TimeSpan maxAge)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT fetched_at, status, message, source, metric_note,
                   provider_code, provider_branch, provider_name, provider_nip, provider_regon,
                   provider_registry, provider_place, provider_post, provider_street, provider_phone
            FROM umowy_cache
            WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$woj", voivodeship);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$year", year);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var fetchedAt = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (DateTime.UtcNow - fetchedAt.ToUniversalTime() > maxAge)
            return null;

        var status = reader.GetString(1);
        var message = reader.IsDBNull(2) ? null : reader.GetString(2);
        var source = reader.GetString(3);
        var metricNote = reader.GetString(4);
        NfzProviderInfo? provider = null;
        if (!reader.IsDBNull(5) && !string.IsNullOrWhiteSpace(reader.GetString(5)))
        {
            provider = new NfzProviderInfo(
                reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                reader.IsDBNull(7) ? "" : reader.GetString(7),
                reader.IsDBNull(8) ? "" : reader.GetString(8),
                reader.IsDBNull(9) ? "" : reader.GetString(9),
                reader.IsDBNull(10) ? "" : reader.GetString(10),
                reader.IsDBNull(11) ? "" : reader.GetString(11),
                reader.IsDBNull(12) ? "" : reader.GetString(12),
                reader.IsDBNull(13) ? "" : reader.GetString(13),
                reader.IsDBNull(14) ? "" : reader.GetString(14));
        }

        reader.Close();
        var agreements = ReadCachedAgreements(voivodeship, name, year);
        var items = ReadCachedPlanItems(voivodeship, name, year);
        return new FacilityContractBundle(
            voivodeship,
            name,
            year,
            status,
            message,
            fetchedAt.ToUniversalTime(),
            source,
            metricNote,
            provider,
            agreements,
            items.Where(NfzUmowyClient.IsCardiologyProduct).OrderByDescending(i => i.Price).ToList(),
            items.Where(NfzUmowyClient.IsKosZawalProduct).OrderByDescending(i => i.Price).ToList());
    }

    public void SaveContracts(FacilityContractBundle bundle)
    {
        using var transaction = _connection.BeginTransaction();
        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM umowy_cache WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year;
                DELETE FROM umowy_pozycje WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year;
                DELETE FROM umowy_naglowki WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year;
                """;
            delete.Parameters.AddWithValue("$woj", bundle.Voivodeship);
            delete.Parameters.AddWithValue("$name", bundle.RequestedName);
            delete.Parameters.AddWithValue("$year", bundle.Year);
            delete.ExecuteNonQuery();
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO umowy_cache(
                    wojewodztwo, swiadczeniodawca, rok, fetched_at, status, message, source, metric_note,
                    provider_code, provider_branch, provider_name, provider_nip, provider_regon,
                    provider_registry, provider_place, provider_post, provider_street, provider_phone)
                VALUES(
                    $woj, $name, $year, $fetched, $status, $message, $source, $metric,
                    $pcode, $pbranch, $pname, $pnip, $pregon, $preg, $pplace, $ppost, $pstreet, $pphone);
                """;
            insert.Parameters.AddWithValue("$woj", bundle.Voivodeship);
            insert.Parameters.AddWithValue("$name", bundle.RequestedName);
            insert.Parameters.AddWithValue("$year", bundle.Year);
            insert.Parameters.AddWithValue("$fetched", bundle.FetchedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$status", bundle.Status);
            insert.Parameters.AddWithValue("$message", (object?)bundle.Message ?? DBNull.Value);
            insert.Parameters.AddWithValue("$source", bundle.Source);
            insert.Parameters.AddWithValue("$metric", bundle.MetricKindNote);
            insert.Parameters.AddWithValue("$pcode", (object?)bundle.Provider?.Code ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pbranch", (object?)bundle.Provider?.Branch ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pname", (object?)bundle.Provider?.Name ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pnip", (object?)bundle.Provider?.Nip ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pregon", (object?)bundle.Provider?.Regon ?? DBNull.Value);
            insert.Parameters.AddWithValue("$preg", (object?)bundle.Provider?.RegistryNumber ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pplace", (object?)bundle.Provider?.Place ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ppost", (object?)bundle.Provider?.PostCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pstreet", (object?)bundle.Provider?.Street ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pphone", (object?)bundle.Provider?.Phone ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO umowy_naglowki(
                    wojewodztwo, swiadczeniodawca, rok, agreement_id, code, service_type, service_name,
                    amount, updated_at, branch, provider_code)
                VALUES($woj, $name, $year, $id, $code, $stype, $sname, $amount, $updated, $branch, $pcode);
                """;
            foreach (var agreement in bundle.Agreements)
            {
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$woj", bundle.Voivodeship);
                insert.Parameters.AddWithValue("$name", bundle.RequestedName);
                insert.Parameters.AddWithValue("$year", bundle.Year);
                insert.Parameters.AddWithValue("$id", agreement.Id);
                insert.Parameters.AddWithValue("$code", agreement.Code);
                insert.Parameters.AddWithValue("$stype", agreement.ServiceType);
                insert.Parameters.AddWithValue("$sname", agreement.ServiceName);
                insert.Parameters.AddWithValue("$amount", agreement.Amount.ToString(CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$updated", agreement.UpdatedAt);
                insert.Parameters.AddWithValue("$branch", agreement.Branch);
                insert.Parameters.AddWithValue("$pcode", agreement.ProviderCode);
                insert.ExecuteNonQuery();
            }
        }

        var allItems = bundle.CardiologyItems
            .Concat(bundle.KosZawalItems)
            .GroupBy(item => item.PlanId + "\u001f" + item.ProductCode + "\u001f" + item.ProductName)
            .Select(group => group.First())
            .ToList();
        // Zachowaj też pozycje zagregowane bez PlanId unikalnego — zapisujemy listę wynikową.
        if (allItems.Count == 0)
            allItems = bundle.CardiologyItems.Concat(bundle.KosZawalItems).ToList();

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO umowy_pozycje(
                    wojewodztwo, swiadczeniodawca, rok, plan_id, agreement_code, service_name,
                    product_code, product_name, unit_count, price, avg_price, date_from, date_to, kind)
                VALUES($woj, $name, $year, $pid, $acode, $sname, $pcode, $pname, $units, $price, $avg, $from, $to, $kind);
                """;
            foreach (var item in bundle.CardiologyItems.Concat(bundle.KosZawalItems)
                         .GroupBy(i => i.ProductCode + "\u001f" + i.ProductName + "\u001f" + i.AgreementCode)
                         .Select(g => g.First()))
            {
                var kind = NfzUmowyClient.IsKosZawalProduct(item)
                    ? "kos-zawal"
                    : NfzUmowyClient.IsCardiologyProduct(item) ? "kardiologia" : "inne";
                insert.Parameters.Clear();
                insert.Parameters.AddWithValue("$woj", bundle.Voivodeship);
                insert.Parameters.AddWithValue("$name", bundle.RequestedName);
                insert.Parameters.AddWithValue("$year", bundle.Year);
                insert.Parameters.AddWithValue("$pid", item.PlanId);
                insert.Parameters.AddWithValue("$acode", item.AgreementCode);
                insert.Parameters.AddWithValue("$sname", item.ServiceName);
                insert.Parameters.AddWithValue("$pcode", item.ProductCode);
                insert.Parameters.AddWithValue("$pname", item.ProductName);
                insert.Parameters.AddWithValue("$units", item.UnitCount.ToString(CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$price", item.Price.ToString(CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$avg", item.AvgPrice.ToString(CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$from", item.DateFrom);
                insert.Parameters.AddWithValue("$to", item.DateTo);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private IReadOnlyList<ContractAgreement> ReadCachedAgreements(string voivodeship, string name, int year)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT agreement_id, code, service_type, service_name, amount, updated_at, branch, provider_code
            FROM umowy_naglowki
            WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year
            ORDER BY CAST(amount AS REAL) DESC;
            """;
        command.Parameters.AddWithValue("$woj", voivodeship);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$year", year);
        var list = new List<ContractAgreement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ContractAgreement(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                year,
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                reader.IsDBNull(7) ? "" : reader.GetString(7),
                name,
                "",
                "",
                ""));
        }

        return list;
    }

    private IReadOnlyList<ContractPlanItem> ReadCachedPlanItems(string voivodeship, string name, int year)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT plan_id, agreement_code, service_name, product_code, product_name,
                   unit_count, price, avg_price, date_from, date_to
            FROM umowy_pozycje
            WHERE wojewodztwo = $woj AND swiadczeniodawca = $name AND rok = $year;
            """;
        command.Parameters.AddWithValue("$woj", voivodeship);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$year", year);
        var list = new List<ContractPlanItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ContractPlanItem(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? "" : reader.GetString(8),
                reader.IsDBNull(9) ? "" : reader.GetString(9)));
        }

        return list;
    }

    public void Dispose() => _connection.Dispose();

    private List<object?[]> QueryOffset(
        string datasetId,
        int? year,
        string? infarctType,
        string? voivodeship,
        string? search,
        bool aggregateOnly,
        int limit,
        int offset)
    {
        var dataset = ReportCatalog.ById(datasetId);
        var filters = new List<string>();
        using var command = _connection.CreateCommand();
        if (year is int selectedYear && dataset.Columns.Any(column => column.Sql == "rok"))
        {
            filters.Add("rok = $year");
            command.Parameters.AddWithValue("$year", selectedYear);
        }
        else if (year is int trendYear && dataset.Id == "trendy")
        {
            filters.Add("data LIKE $prefix");
            command.Parameters.AddWithValue("$prefix", trendYear + "-%");
        }

        if (!string.IsNullOrWhiteSpace(infarctType))
        {
            filters.Add("typ_zawalu = $type");
            command.Parameters.AddWithValue("$type", infarctType);
        }

        if (!string.IsNullOrWhiteSpace(voivodeship))
        {
            filters.Add("wojewodztwo = $woj");
            command.Parameters.AddWithValue("$woj", voivodeship);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("swiadczeniodawca LIKE $search");
            command.Parameters.AddWithValue("$search", "%" + search.Trim() + "%");
        }

        if (aggregateOnly && dataset.Id == "trendy")
            filters.Add("swiadczeniodawca LIKE '%OGÓŁEM%'");

        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);
        var columns = dataset.Columns.ToList();
        command.CommandText = $"""
            SELECT {string.Join(", ", columns.Select(column => column.Sql))}
            FROM {dataset.Id}
            {where}
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var rows = new List<object?[]>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY,
                value TEXT
            );
            CREATE TABLE IF NOT EXISTS placowki (
                rok INTEGER,
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                typ_zawalu TEXT,
                liczba_hospitalizacji REAL,
                sredni_czas REAL,
                transp_z REAL,
                transp_do REAL,
                pci_7 REAL,
                cabg_60 REAL,
                rehab_30 REAL,
                rehab_60 REAL,
                rehab_60_dowolny REAL,
                czy_kos TEXT
            );
            CREATE TABLE IF NOT EXISTS kos (
                rok INTEGER,
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                typ_zawalu TEXT,
                liczba_hospitalizacji REAL,
                proc_kos REAL,
                kontrola_14 REAL,
                rehab_14 REAL,
                rehab_30 REAL,
                rehab_30_dowolny REAL,
                zakon_365 REAL,
                sok_365 REAL
            );
            CREATE TABLE IF NOT EXISTS trendy (
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                typ_zawalu TEXT,
                proc_kos REAL,
                atrybut TEXT,
                wartosc REAL,
                data TEXT
            );
            CREATE TABLE IF NOT EXISTS wojewodztwa (
                rok INTEGER,
                wojewodztwo TEXT,
                typ_zawalu TEXT,
                kategoria TEXT,
                odsetek_swiadczeniodawcow REAL,
                odsetek_swiadczen REAL
            );
            CREATE TABLE IF NOT EXISTS geokody (
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                lat REAL,
                lon REAL,
                jakosc TEXT,
                PRIMARY KEY (wojewodztwo, swiadczeniodawca)
            );
            CREATE TABLE IF NOT EXISTS umowy_cache (
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                rok INTEGER,
                fetched_at TEXT,
                status TEXT,
                message TEXT,
                source TEXT,
                metric_note TEXT,
                provider_code TEXT,
                provider_branch TEXT,
                provider_name TEXT,
                provider_nip TEXT,
                provider_regon TEXT,
                provider_registry TEXT,
                provider_place TEXT,
                provider_post TEXT,
                provider_street TEXT,
                provider_phone TEXT,
                PRIMARY KEY (wojewodztwo, swiadczeniodawca, rok)
            );
            CREATE TABLE IF NOT EXISTS umowy_naglowki (
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                rok INTEGER,
                agreement_id TEXT,
                code TEXT,
                service_type TEXT,
                service_name TEXT,
                amount TEXT,
                updated_at TEXT,
                branch TEXT,
                provider_code TEXT
            );
            CREATE TABLE IF NOT EXISTS umowy_pozycje (
                wojewodztwo TEXT,
                swiadczeniodawca TEXT,
                rok INTEGER,
                plan_id TEXT,
                agreement_code TEXT,
                service_name TEXT,
                product_code TEXT,
                product_name TEXT,
                unit_count TEXT,
                price TEXT,
                avg_price TEXT,
                date_from TEXT,
                date_to TEXT,
                kind TEXT
            );
            """);
    }

    public void CreateIndexes(SqliteTransaction? transaction = null)
    {
        Execute("""
            CREATE INDEX IF NOT EXISTS ix_plac_filter ON placowki(rok, typ_zawalu, wojewodztwo);
            CREATE INDEX IF NOT EXISTS ix_kos_filter ON kos(rok, typ_zawalu, wojewodztwo);
            CREATE INDEX IF NOT EXISTS ix_trendy_filter ON trendy(typ_zawalu, atrybut, data);
            CREATE INDEX IF NOT EXISTS ix_woj_filter ON wojewodztwa(rok, typ_zawalu, kategoria);
            """, transaction);
    }

    private double ScalarDouble(string sql, int year, string infarctType)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$type", infarctType);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private Dictionary<string, double> ReadPairs(string sql, int year, string infarctType)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$type", infarctType);
        var map = new Dictionary<string, double>(StringComparer.CurrentCulture);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(0)] = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
        return map;
    }

    private Dictionary<string, (double Cases, double Denominator)> ReadTriples(string sql, int year, string infarctType)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$type", infarctType);
        var map = new Dictionary<string, (double, double)>(StringComparer.CurrentCulture);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(0)] = (
                reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                reader.IsDBNull(2) ? 0 : reader.GetDouble(2));
        return map;
    }

    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object ToDb(object? value, ColumnKind kind)
    {
        if (value is null)
            return DBNull.Value;

        return kind switch
        {
            ColumnKind.Date => ToDate(value),
            ColumnKind.Integer or ColumnKind.Number or ColumnKind.Percent => (object?)ToNumber(value) ?? DBNull.Value,
            _ => value.ToString()?.Trim() ?? (object)DBNull.Value,
        };
    }

    public static double? ToNumber(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case double number:
                return number;
            case float number:
                return number;
            case int number:
                return number;
            case long number:
                return number;
            case decimal number:
                return (double)number;
            case string text:
                var trimmed = text.Trim().Replace("%", "").Replace(" ", "");
                if (trimmed.Length == 0)
                    return null;
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                if (double.TryParse(trimmed, NumberStyles.Float, new CultureInfo("pl-PL"), out parsed))
                    return parsed;
                return null;
            default:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
    }

    private static string ToDate(object? value)
    {
        var number = ToNumber(value);
        if (number is > 10_000_000_000)
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds((long)number.Value).UtcDateTime;
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (number is > 1_000_000_000 and < 10_000_000_000)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds((long)number.Value).UtcDateTime;
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (value is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return value?.ToString() ?? "";
    }
}
