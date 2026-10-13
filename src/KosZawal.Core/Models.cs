namespace KosZawal;

public sealed record SyncProgress(string Stage, string Detail);

public sealed class KpiSnapshot
{
    public int Year { get; init; }
    public string InfarctType { get; init; } = "";
    public double Hospitalizations { get; init; }
    public double KosHospitalizations { get; init; }
    public double NationalShare { get; init; }
    public double ShareAmongKosCenters { get; init; }
    public int Centers { get; init; }
    public int VoivodeshipsWithCenters { get; init; }
    public IReadOnlyList<VoivodeshipStat> ByVoivodeship { get; init; } = [];
    public IReadOnlyList<MonthPoint> Monthly { get; init; } = [];
    public IReadOnlyList<YearPoint> Years { get; init; } = [];
}

public sealed record VoivodeshipStat(
    string Name,
    int Centers,
    double Hospitalizations,
    double KosHospitalizations,
    double Share);

public sealed record MonthPoint(string Month, double Hospitalizations, double KosHospitalizations, double Share);

public sealed record YearPoint(int Year, double Hospitalizations, double KosHospitalizations, double Share, int Centers);

public sealed record TablePage(
    IReadOnlyList<string> Headers,
    IReadOnlyList<string> SqlColumns,
    IReadOnlyList<ColumnKind> Kinds,
    IReadOnlyList<object?[]> Rows,
    long Total);

public sealed record HospitalPin(
    string Voivodeship,
    string Name,
    double Hospitalizations,
    double? KosShare,
    double? Latitude,
    double? Longitude,
    string Quality);

public sealed record HospitalYearSeries(
    int Year,
    double Hospitalizations,
    bool IsKos,
    double? KosShare,
    double? Control14,
    double? Rehab30,
    double? Finish365);

public sealed record HospitalDetail(
    string Voivodeship,
    string Name,
    string InfarctType,
    bool IsLeader,
    string Quality,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<HospitalYearSeries> Years,
    IReadOnlyList<MonthPoint> Monthly);

public sealed record GeoHit(string Voivodeship, string Name, double Latitude, double Longitude, string Quality);

public sealed record NfzProviderInfo(
    string Code,
    string Branch,
    string Name,
    string Nip,
    string Regon,
    string RegistryNumber,
    string Place,
    string PostCode,
    string Street,
    string Phone);

public sealed record ContractAgreement(
    string Id,
    string Code,
    string ServiceType,
    string ServiceName,
    decimal Amount,
    string UpdatedAt,
    int Year,
    string Branch,
    string ProviderCode,
    string ProviderName,
    string ProviderPlace,
    string ProviderNip,
    string ProviderRegon);

public sealed record ContractPlanItem(
    string PlanId,
    string AgreementCode,
    string ServiceName,
    string ProductCode,
    string ProductName,
    decimal UnitCount,
    decimal Price,
    decimal AvgPrice,
    string DateFrom,
    string DateTo);

public sealed record FacilityContractBundle(
    string Voivodeship,
    string RequestedName,
    int Year,
    string Status,
    string? Message,
    DateTime FetchedAtUtc,
    string Source,
    string MetricKindNote,
    NfzProviderInfo? Provider,
    IReadOnlyList<ContractAgreement> Agreements,
    IReadOnlyList<ContractPlanItem> CardiologyItems,
    IReadOnlyList<ContractPlanItem> KosZawalItems)
{
    public decimal CardiologyContractValue => CardiologyItems.Sum(item => item.Price);
    public decimal KosZawalContractValue => KosZawalItems.Sum(item => item.Price);
    public decimal AgreementsTotalValue => Agreements.Sum(item => item.Amount);
    public bool HasProvider => Provider is not null;
    public bool HasCardiology => CardiologyItems.Count > 0;
    public bool HasKosZawalContract => KosZawalItems.Count > 0;

    public static FacilityContractBundle Missing(string voivodeship, string name, int year, string message) =>
        new(voivodeship, name, year, "missing", message, DateTime.UtcNow, NfzUmowyClient.SourceUrl,
            "Brak danych kontraktowych z API Umowy NFZ.", null, [], [], []);
}
