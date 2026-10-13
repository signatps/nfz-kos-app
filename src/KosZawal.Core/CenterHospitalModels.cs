namespace KosZawal;

public enum MetricAvailability
{
    Available,
    NotPublished,
    Suppressed,
    NotImported,
    NotApplicable,
    Error,
}

public enum MappingStatus
{
    Confirmed,
    Ambiguous,
    Unmatched,
}

public sealed record CenterMetric(
    double? Value,
    MetricAvailability Availability,
    string Metric,
    int? PeriodYear,
    string? ReferenceDate,
    string? RetrievedAt,
    string ScopeLevel,
    string? Specialty,
    bool IsComplete,
    string? SourceId,
    string? SourceUrl,
    string? MethodologyLabel)
{
    public static CenterMetric Missing(string metric, MetricAvailability availability, string? methodology = null) =>
        new(null, availability, metric, null, null, null, "provider", null, false, null, null, methodology);

    public string Display(System.Globalization.CultureInfo culture) =>
        Availability switch
        {
            MetricAvailability.Available when Value is not null => Value.Value.ToString("N0", culture),
            MetricAvailability.NotImported => "nie zaimportowano",
            MetricAvailability.NotPublished => "brak publikacji",
            MetricAvailability.Suppressed => "ukryte (<próg)",
            MetricAvailability.NotApplicable => "nie dotyczy",
            MetricAvailability.Error => "błąd źródła",
            _ => "brak danych",
        };
}

public sealed record DepartmentInfo(
    string CellId,
    string Name,
    string SpecialtyCode,
    string SpecialtyName,
    string Category,
    string Status,
    int BedsTotal,
    int BedsCardiacIcu,
    string Place,
    string Address);

public sealed record CenterHospitalData(
    MappingStatus MappingStatus,
    string MappingBasis,
    string? ProviderName,
    string? Nip,
    string? Regon,
    string? LedgerId,
    CenterMetric CardiologyBeds,
    CenterMetric CardiologyPatients,
    CenterMetric CardiologyHospitalizations,
    CenterMetric RehabilitationBeds,
    IReadOnlyList<DepartmentInfo> Departments,
    string SourceNote);
