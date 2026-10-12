namespace KosZawal;

public static class ReportCatalog
{
    public const string ReportUrl =
        "https://app.powerbi.com/view?r=eyJrIjoiNmI3NDZiMDEtMDUwYS00NGU0LTgzZWYtYjM3Y2M3NWRjZGJlIiwidCI6IjJlNzcwYzE2LWMwNzEtNDA1Mi04MzdjLTU0NWJlZTBiMzQwYyIsImMiOjl9";

    public const string InfarctAll = "Łącznie (STEMI i NSTEMI)";
    public const string InfarctStemi = "STEMI";
    public const string InfarctNstemi = "NSTEMI";

    public static readonly string[] InfarctTypes = [InfarctAll, InfarctStemi, InfarctNstemi];

    public const string HospitalizationsAttribute = "Liczba hospitalizacji";

    public static readonly IReadOnlyList<DatasetColumn> PlacowkiColumns =
    [
        new("Rok", "rok", "Rok", ColumnKind.Integer),
        new("Województwo", "wojewodztwo", "Województwo", ColumnKind.Text),
        new("Nazwa świadczeniodawcy", "swiadczeniodawca", "Świadczeniodawca", ColumnKind.Text),
        new("Typ zawału", "typ_zawalu", "Typ zawału", ColumnKind.Text),
        new("Liczba hospitalizacji", "liczba_hospitalizacji", "Liczba hospitalizacji", ColumnKind.Number),
        new("Średni czas pobytu", "sredni_czas", "Średni czas pobytu (dni)", ColumnKind.Number),
        new("% hospitalizacji dla pacjentów transportowanych z innego szpitala", "transp_z", "% transportowanych z innego szpitala", ColumnKind.Percent),
        new("% hospitalizacji dla pacjentów transportowanych do innego szpitala", "transp_do", "% transportowanych do innego szpitala", ColumnKind.Percent),
        new("% hospitalizacji z angioplastyką wieńcową w ciągu 7 dni od daty przyjęcia", "pci_7", "% angioplastyki w 7 dni", ColumnKind.Percent),
        new("% hospitalizacji z pomostowaniem aortalno-wieńcowym w ciągu 60 dni od daty przyjęcia", "cabg_60", "% CABG w 60 dni", ColumnKind.Percent),
        new("% hospitalizacji z rehabilitacją kardiologiczną podczas hosp. lub do 30 dni od daty wypisu", "rehab_30", "% rehabilitacji do 30 dni", ColumnKind.Percent),
        new("% hospitalizacji z rehabilitacją kardiologiczną podczas hosp. lub do 60 dni od daty wypisu", "rehab_60", "% rehabilitacji do 60 dni", ColumnKind.Percent),
        new("% hospitalizacji z rehabilitacją kardiologiczną (u jakiegokolwiek świadczeniodawcy) podczas hosp. lub do 60 dni od daty wypisu", "rehab_60_dowolny", "% rehabilitacji do 60 dni (dowolny ośrodek)", ColumnKind.Percent),
        new("czy_KOS", "czy_kos", "Czy KOS-zawał", ColumnKind.Text),
    ];

    public static readonly IReadOnlyList<DatasetColumn> KosColumns =
    [
        new("Rok_hosp", "rok", "Rok", ColumnKind.Integer),
        new("Woj", "wojewodztwo", "Województwo", ColumnKind.Text),
        new("NSWU", "swiadczeniodawca", "Świadczeniodawca", ColumnKind.Text),
        new("zawal", "typ_zawalu", "Typ zawału", ColumnKind.Text),
        new("n_IKSG", "liczba_hospitalizacji", "Liczba hospitalizacji", ColumnKind.Number),
        new("proc_KOS", "proc_kos", "% hospitalizacji w KOS-zawał", ColumnKind.Percent),
        new("kontrola_14dniwyp", "kontrola_14", "% wizyty kontrolnej w 14 dni", ColumnKind.Percent),
        new("rehab_14dniwyp", "rehab_14", "% rehabilitacji w 14 dni", ColumnKind.Percent),
        new("rehab_30dniwyp", "rehab_30", "% rehabilitacji w 30 dni", ColumnKind.Percent),
        new("rehabilitacja kardiologiczna (u jakiegokolwiek świadczeniodawcy) podczas hosp. lub do 30 dni od daty wypisu", "rehab_30_dowolny", "% rehabilitacji w 30 dni (dowolny ośrodek)", ColumnKind.Percent),
        new("zakon_365dniprz", "zakon_365", "% zakończenia leczenia w 365 dni", ColumnKind.Percent),
        new("SOK_365dniprz", "sok_365", "% specjalistycznej opieki w 365 dni", ColumnKind.Percent),
    ];

    public static readonly IReadOnlyList<DatasetColumn> TrendyColumns =
    [
        new("Woj", "wojewodztwo", "Województwo", ColumnKind.Text),
        new("NSWU", "swiadczeniodawca", "Świadczeniodawca", ColumnKind.Text),
        new("zawal", "typ_zawalu", "Typ zawału", ColumnKind.Text),
        new("proc_KOS", "proc_kos", "% w KOS w miesiącu", ColumnKind.Percent),
        new("Atrybut", "atrybut", "Statystyka", ColumnKind.Text),
        new("Wartość", "wartosc", "Wartość", ColumnKind.Number),
        new("Data", "data", "Miesiąc", ColumnKind.Date),
    ];

    public static readonly IReadOnlyList<DatasetColumn> WojewodztwaColumns =
    [
        new("Rok_hosp", "rok", "Rok", ColumnKind.Integer),
        new("Województwo", "wojewodztwo", "Województwo", ColumnKind.Text),
        new("zawal", "typ_zawalu", "Typ zawału", ColumnKind.Text),
        new("kat", "kategoria", "Kategoria", ColumnKind.Text),
        new("Odsetek świadczeniodawców", "odsetek_swiadczeniodawcow", "Odsetek świadczeniodawców", ColumnKind.Percent),
        new("Odsetek świadczeń", "odsetek_swiadczen", "Odsetek hospitalizacji", ColumnKind.Percent),
    ];

    public static readonly IReadOnlyList<DatasetDefinition> Datasets =
    [
        new("placowki", "Placówki", "Placówki", PlacowkiColumns),
        new("kos", "KOS-zawał", "Kos-zawał", KosColumns),
        new("trendy", "Trendy", "Trendy", TrendyColumns),
        new("wojewodztwa", "Województwa", "Województwa", WojewodztwaColumns),
    ];

    public static DatasetDefinition ById(string id) =>
        Datasets.First(dataset => dataset.Id == id);
}

public enum ColumnKind
{
    Text,
    Integer,
    Number,
    Percent,
    Date,
}

public sealed record DatasetColumn(string Source, string Sql, string Header, ColumnKind Kind);

public sealed record DatasetDefinition(
    string Id,
    string Title,
    string Entity,
    IReadOnlyList<DatasetColumn> Columns);
