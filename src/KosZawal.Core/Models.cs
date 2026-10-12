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
