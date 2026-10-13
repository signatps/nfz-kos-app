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
    private readonly HospitalDetail _detail;
    private readonly int _year;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pl-PL");
    private CancellationTokenSource? _cts;

    public FacilityPopupViewModel(NfzDatabase database, NfzUmowyClient client, HospitalDetail detail, int year)
    {
        _database = database;
        _client = client;
        _detail = detail;
        _year = year;
        Title = detail.Name;
        Subtitle = $"{detail.Voivodeship}"
                   + (detail.IsLeader ? " · lider województwa (hospitalizacje zawałowe)" : "")
                   + $" · rok kontraktu {year}";
        BuildExecutionRows();
    }

    public ObservableCollection<FacilityPopupRow> AgreementRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> CardiologyRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> KosContractRows { get; } = [];
    public ObservableCollection<FacilityPopupRow> ExecutionRows { get; } = [];

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string subtitle = "";
    [ObservableProperty] private string statusLine = "Ładowanie kontraktu…";
    [ObservableProperty] private string progress = "";
    [ObservableProperty] private string footer = "Źródło kontraktów: API Umowy NFZ. Wykonanie: raport Power BI zawałów/KOS-zawał.";
    [ObservableProperty] private string providerBlock = "—";
    [ObservableProperty] private string agreementsSummary = "";
    [ObservableProperty] private string cardiologySummary = "";
    [ObservableProperty] private string kosContractSummary = "";
    [ObservableProperty] private string executionSummary = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private FacilityContractBundle? contracts;

    public async Task EnsureLoadedAsync() => await RefreshAsync(force: false);

    public async Task RefreshAsync(bool force)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        Progress = force ? "Odświeżam dane z API Umowy NFZ…" : "Przygotowuję dane kontraktu…";
        StatusLine = Progress;

        try
        {
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
        }
        catch (OperationCanceledException)
        {
            StatusLine = "Pobieranie przerwane.";
        }
        catch (Exception ex)
        {
            StatusLine = "Błąd pobierania kontraktu: " + ex.Message;
            ProviderBlock = "Nie udało się pobrać danych z API Umowy NFZ.\n" + ex.Message;
            AgreementsSummary = "Brak danych kontraktowych.";
            CardiologySummary = "Brak danych.";
            KosContractSummary = "Brak danych.";
            AgreementRows.Clear();
            CardiologyRows.Clear();
            KosContractRows.Clear();
        }
        finally
        {
            IsBusy = false;
            Progress = "";
        }
    }

    private void ApplyBundle(FacilityContractBundle bundle)
    {
        Footer = $"{bundle.MetricKindNote} Źródło: {bundle.Source}. Pobrano: {bundle.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.";

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
            + "Nie mieszaj ich z wartością zakontraktowaną z API Umowy.";

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
