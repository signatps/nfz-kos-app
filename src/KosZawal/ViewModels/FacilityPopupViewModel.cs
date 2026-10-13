using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KosZawal;

public sealed class FacilityPopupRow
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
}

public partial class FacilityPopupViewModel : ObservableObject
{
    private readonly NfzDatabase _database;
    private readonly NfzUmowyClient _client;
    private readonly RpwdlImporter _rpwdl;
    private readonly HospitalDetail _detail;
    private readonly int _year;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pl-PL");
    private CancellationTokenSource? _cts;

    public FacilityPopupViewModel(
        NfzDatabase database,
        NfzUmowyClient client,
        HospitalDetail detail,
        int year,
        RpwdlImporter? rpwdl = null)
    {
        _database = database;
        _client = client;
        _rpwdl = rpwdl ?? new RpwdlImporter();
        _detail = detail;
        _year = year;
        Title = detail.Name;
        Subtitle = $"{detail.Voivodeship}"
                   + (detail.IsLeader ? " · lider województwa (hospitalizacje zawałowe)" : "")
                   + $" · rok kontraktu/aktywności {year}";
        BuildExecutionRows();
    }

    public ObservableCollection<FacilityPopupRow> AgreementRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> CardiologyRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> KosContractRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> ExecutionRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> StructureSummaryRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> DepartmentRows { get; } = [];

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string statusLine = "Ładowanie danych placówki…";
    [ObservableProperty] private string progress = "";
    [ObservableProperty] private string footer = "Źródła: API Umowy NFZ, RPWDL, raport Power BI zawałów/KOS-zawał.";
    [ObservableProperty] private string providerBlock = "—";
    [ObservableProperty] private string agreementsSummary = "";
    [ObservableProperty] private string cardiologySummary = "";
    [ObservableProperty] private string kosContractSummary = "";
    [ObservableProperty] private string executionSummary = "";
    [ObservableProperty] private string structureSummary = "";
    [ObservableProperty] private string departmentsSummary = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showDepartments;
    [ObservableProperty] private FacilityContractBundle? contracts;
    [ObservableProperty] private CenterHospitalData? hospitalStructure;

    public async Task EnsureLoadedAsync() => await RefreshAsync(force: false);

    public async Task RefreshAsync(bool force)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        Progress = "Przygotowuję dane placówki…";
        StatusLine = Progress;

        try
        {
            await EnsureRpwdlAsync(force, token).ConfigureAwait(true);
            ApplyHospitalStructure(_database.ResolveCenterHospitalData(_detail.Voivodeship, _detail.Name, _year));

            FacilityContractBundle? bundle = null;
            if (!force)
                bundle = _database.TryGetCachedContracts(_detail.Voivodeship, _detail.Name, _year, TimeSpan.FromHours(24));

            if (bundle is null)
            {
                var progress = new Progress<string>(text =>
                {
                    Progress = text;
                    StatusLine = text;
                });
                bundle = await _client.FetchFacilityContractsAsync(
                    _detail.Voivodeship,
                    _detail.Name,
                    _year,
                    progress,
                    token).ConfigureAwait(true);
                _database.SaveContracts(bundle);
            }

            Contracts = bundle;
            ApplyBundle(bundle);
            // Po kontrakcie NIP może poprawić mapowanie RPWDL.
            ApplyHospitalStructure(_database.ResolveCenterHospitalData(_detail.Voivodeship, _detail.Name, _year));
        }
        catch (OperationCanceledException)
        {
            StatusLine = "Pobieranie przerwane.";
        }
        catch (Exception ex)
        {
            StatusLine = "Błąd pobierania: " + ex.Message;
            ProviderBlock = "Nie udało się pobrać części danych.\n" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = "";
        }
    }

    private async Task EnsureRpwdlAsync(bool force, CancellationToken token)
    {
        if (!force && _database.HasRpwdlData && _database.IsRpwdlFresh(TimeSpan.FromDays(7)))
            return;

        Progress = "Pobieram strukturę oddziałów i łóżka z RPWDL…";
        StatusLine = Progress;
        var progress = new Progress<string>(text =>
        {
            Progress = text;
            StatusLine = text;
        });
        await _rpwdl.ImportAsync(_database, progress, token).ConfigureAwait(true);
    }

    private void ApplyHospitalStructure(CenterHospitalData data)
    {
        HospitalStructure = data;
        StructureSummaryRows.Clear();
        DepartmentRows.Clear();

        StructureSummary =
            $"Mapowanie RPWDL: {data.MappingStatus} ({data.MappingBasis}). {data.SourceNote}";

        StructureSummaryRows.Add(new FacilityPopupRow
        {
            Title = $"Łóżka kardiologii: {data.CardiologyBeds.Display(_culture)}",
            Subtitle = MetricSubtitle(data.CardiologyBeds),
        });
        StructureSummaryRows.Add(new FacilityPopupRow
        {
            Title = $"Pacjenci kardiologii: {data.CardiologyPatients.Display(_culture)}",
            Subtitle = MetricSubtitle(data.CardiologyPatients),
        });
        StructureSummaryRows.Add(new FacilityPopupRow
        {
            Title = $"Hospitalizacje (zawał / raport KOS): {data.CardiologyHospitalizations.Display(_culture)}",
            Subtitle = MetricSubtitle(data.CardiologyHospitalizations),
        });
        StructureSummaryRows.Add(new FacilityPopupRow
        {
            Title = $"Rehabilitacja kardiologiczna — łóżka: {data.RehabilitationBeds.Display(_culture)}",
            Subtitle = MetricSubtitle(data.RehabilitationBeds),
        });

        if (!string.IsNullOrWhiteSpace(data.ProviderName))
        {
            StructureSummaryRows.Insert(0, new FacilityPopupRow
            {
                Title = data.ProviderName!,
                Subtitle = $"NIP {data.Nip} · REGON {data.Regon} · księga RPWDL {data.LedgerId}",
            });
        }

        DepartmentsSummary = data.Departments.Count == 0
            ? "Brak oddziałów kardiologicznych w dopasowanym podmiocie RPWDL."
            : $"{data.Departments.Count} komórek (kardiologia / OIOK / rehabilitacja / kardiochirurgia).";

        foreach (var department in data.Departments)
        {
            DepartmentRows.Add(new FacilityPopupRow
            {
                Title = $"{department.SpecialtyCode} · {department.Name}",
                Subtitle =
                    $"{department.SpecialtyName} · {department.Category} · {department.Status} · "
                    + $"łóżka {department.BedsTotal}"
                    + (department.BedsCardiacIcu > 0 ? $" (w tym intensywny nadzór kardiologiczny: {department.BedsCardiacIcu})" : "")
                    + $" · {department.Place}",
            });
        }
    }

    private static string MetricSubtitle(CenterMetric metric)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(metric.MethodologyLabel))
            bits.Add(metric.MethodologyLabel!);
        if (metric.PeriodYear is int year)
            bits.Add($"rok {year}");
        if (!string.IsNullOrWhiteSpace(metric.ReferenceDate))
            bits.Add($"stan rejestru {metric.ReferenceDate}");
        if (!string.IsNullOrWhiteSpace(metric.RetrievedAt))
            bits.Add($"pobrano {metric.RetrievedAt}");
        if (!string.IsNullOrWhiteSpace(metric.SourceUrl))
            bits.Add(metric.SourceUrl!);
        bits.Add($"zakres: {metric.ScopeLevel}");
        bits.Add(metric.IsComplete ? "kompletne" : "częściowe/brak");
        return string.Join(" · ", bits);
    }

    private void ApplyBundle(FacilityContractBundle bundle)
    {
        Footer = $"{bundle.MetricKindNote} Źródło umów: {bundle.Source}. Pobrano: {bundle.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.";

        if (bundle.Provider is null)
        {
            ProviderBlock = bundle.Message ?? "Brak dopasowanego świadczeniodawcy w API Umowy.";
            StatusLine = bundle.Message ?? "Brak kontraktu.";
        }
        else
        {
            var p = bundle.Provider;
            ProviderBlock =
                $"{p.Name}\nKod NFZ: {p.Code} · Oddział: {p.Branch}\n"
                + $"NIP: {p.Nip} · REGON: {p.Regon}\n"
                + $"Rejestr: {p.RegistryNumber}\n"
                + $"Adres: {p.Street}, {p.PostCode} {p.Place}\n"
                + $"Telefon: {p.Phone}";
            StatusLine = $"Dopasowano podmiot NFZ · {bundle.Agreements.Count} umów w {_year} · kardiologia: {bundle.CardiologyItems.Count} pozycji · KOS-zawał w kontrakcie: {bundle.KosZawalItems.Count}";
        }

        AgreementRows.Clear();
        if (bundle.Agreements.Count == 0)
        {
            AgreementsSummary = bundle.Message ?? "Brak umów dla tego roku.";
        }
        else
        {
            AgreementsSummary =
                $"Suma kwot umów (CONTRACT_VALUE, całe umowy — nie tylko kardiologia): {FormatMoney(bundle.AgreementsTotalValue)}. "
                + "Poniżej nagłówki umów; szczegóły zakresów kardiologii są w kolejnej sekcji.";
            foreach (var agreement in bundle.Agreements.OrderByDescending(a => a.Amount))
            {
                AgreementRows.Add(new FacilityPopupRow
                {
                    Title = $"{agreement.Code} · {agreement.ServiceName}",
                    Subtitle = $"CONTRACT_VALUE: {FormatMoney(agreement.Amount)} · aktualizacja NFZ: {agreement.UpdatedAt}",
                });
            }
        }

        CardiologyRows.Clear();
        if (bundle.CardiologyItems.Count == 0)
        {
            CardiologySummary = "Brak pozycji planu zaklasyfikowanych do kardiologii / kardiochirurgii / KOS-zawał w pobranych umowach.";
        }
        else
        {
            CardiologySummary =
                $"Suma wybranych pozycji kardiologicznych (CONTRACT_VALUE): {FormatMoney(bundle.CardiologyContractValue)}. "
                + "Kwoty z planu umowy; to nie jest potwierdzone wykonanie świadczeń.";
            foreach (var item in bundle.CardiologyItems)
            {
                CardiologyRows.Add(new FacilityPopupRow
                {
                    Title = $"{item.ProductCode} · {item.ProductName}",
                    Subtitle =
                        $"CONTRACT_VALUE: {FormatMoney(item.Price)} · CONTRACT_UNITS: {item.UnitCount.ToString("N2", _culture)}"
                        + (string.IsNullOrWhiteSpace(item.AgreementCode) ? "" : $" · umowa {item.AgreementCode}")
                        + FormatPeriod(item),
                });
            }
        }

        KosContractRows.Clear();
        if (bundle.KosZawalItems.Count == 0)
        {
            KosContractSummary = "Brak produktu „Kompleksowa opieka po zawale (KOS-zawał)” w planie umów tego roku — albo szczegóły nie zostały pobrane.";
        }
        else
        {
            KosContractSummary =
                $"Suma pozycji KOS-zawał w kontrakcie (CONTRACT_VALUE): {FormatMoney(bundle.KosZawalContractValue)}.";
            foreach (var item in bundle.KosZawalItems)
            {
                KosContractRows.Add(new FacilityPopupRow
                {
                    Title = $"{item.ProductCode} · {item.ProductName}",
                    Subtitle =
                        $"CONTRACT_VALUE: {FormatMoney(item.Price)} · CONTRACT_UNITS: {item.UnitCount.ToString("N2", _culture)}"
                        + FormatPeriod(item),
                });
            }
        }
    }

    private void BuildExecutionRows()
    {
        ExecutionRows.Clear();
        if (_detail.Years.Count == 0)
        {
            ExecutionSummary = "Brak opublikowanych danych o wykonaniu KOS/KOS-zawał dla tej placówki w lokalnym raporcie zawałów.";
            return;
        }

        ExecutionSummary =
            "Dane z raportu NFZ „zawały / KOS-zawał” (Power BI) — REPORTED_ACTIVITY na poziomie świadczeniodawcy. "
            + "Nie mieszaj ich z wartością zakontraktowaną ani z łóżkami RPWDL.";

        foreach (var year in _detail.Years.OrderByDescending(y => y.Year))
        {
            ExecutionRows.Add(new FacilityPopupRow
            {
                Title = $"{year.Year}: {year.Hospitalizations.ToString("N0", _culture)} hospitalizacji zawałowych"
                        + (year.IsKos ? " · w programie KOS-zawał" : " · poza KOS"),
                Subtitle =
                    $"Udział KOS: {FormatPct(year.KosShare)} · kontrola 14 dni: {FormatPct(year.Control14)} · "
                    + $"rehab 30 dni: {FormatPct(year.Rehab30)} · zakończenie 365 dni: {FormatPct(year.Finish365)}",
            });
        }

        if (_detail.Monthly.Count > 0)
        {
            var last = _detail.Monthly[^1];
            ExecutionRows.Add(new FacilityPopupRow
            {
                Title = $"Ostatni miesiąc w trendzie: {last.Month}",
                Subtitle =
                    $"Hospitalizacje: {last.Hospitalizations.ToString("N0", _culture)} · udział KOS: {last.Share.ToString("0.0%", _culture)}",
            });
        }
    }

    private string FormatMoney(decimal value) => value.ToString("N2", _culture) + " PLN";

    private string FormatPct(double? value) =>
        value is null ? "brak danych" : value.Value.ToString("0.0%", _culture);

    private static string FormatPeriod(ContractPlanItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DateFrom) && string.IsNullOrWhiteSpace(item.DateTo))
            return "";
        var from = item.DateFrom.Length >= 10 ? item.DateFrom[..10] : item.DateFrom;
        var to = item.DateTo.Length >= 10 ? item.DateTo[..10] : item.DateTo;
        return $" · okres {from} – {to}";
    }
}
