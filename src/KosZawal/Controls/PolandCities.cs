namespace KosZawal.Controls;

/// <summary>Stolice i główne miasta województw (współrzędne WGS84).</summary>
public static class PolandCities
{
    public static IReadOnlyList<AdminCity> All { get; } =
    [
        new("Dolnośląskie", "Wrocław", 51.1079, 17.0385, true),
        new("Kujawsko-pomorskie", "Bydgoszcz", 53.1235, 18.0084, true),
        new("Kujawsko-pomorskie", "Toruń", 53.0138, 18.5981, true),
        new("Lubelskie", "Lublin", 51.2465, 22.5684, true),
        new("Lubuskie", "Gorzów Wielkopolski", 52.7325, 15.2369, true),
        new("Lubuskie", "Zielona Góra", 51.9356, 15.5062, true),
        new("Łódzkie", "Łódź", 51.7592, 19.4560, true),
        new("Małopolskie", "Kraków", 50.0647, 19.9450, true),
        new("Mazowieckie", "Warszawa", 52.2297, 21.0122, true),
        new("Opolskie", "Opole", 50.6751, 17.9213, true),
        new("Podkarpackie", "Rzeszów", 50.0413, 21.9990, true),
        new("Podlaskie", "Białystok", 53.1325, 23.1688, true),
        new("Pomorskie", "Gdańsk", 54.3520, 18.6466, true),
        new("Pomorskie", "Gdynia", 54.5189, 18.5305, false),
        new("Pomorskie", "Sopot", 54.4418, 18.5601, false),
        new("Śląskie", "Katowice", 50.2649, 19.0238, true),
        new("Świętokrzyskie", "Kielce", 50.8661, 20.6286, true),
        new("Warmińsko-mazurskie", "Olsztyn", 53.7784, 20.4801, true),
        new("Wielkopolskie", "Poznań", 52.4064, 16.9252, true),
        new("Zachodniopomorskie", "Szczecin", 53.4285, 14.5528, true),
        new("Małopolskie", "Tarnów", 50.0121, 20.9858, false),
        new("Małopolskie", "Nowy Sącz", 49.6175, 20.7153, false),
        new("Dolnośląskie", "Wałbrzych", 50.7840, 16.2844, false),
        new("Dolnośląskie", "Legnica", 51.2070, 16.1619, false),
        new("Śląskie", "Częstochowa", 50.8118, 19.1203, false),
        new("Śląskie", "Bielsko-Biała", 49.8224, 19.0444, false),
        new("Śląskie", "Gliwice", 50.2945, 18.6714, false),
        new("Mazowieckie", "Radom", 51.4027, 21.1471, false),
        new("Mazowieckie", "Płock", 52.5463, 19.7060, false),
        new("Wielkopolskie", "Kalisz", 51.7600, 18.0900, false),
        new("Łódzkie", "Piotrków Trybunalski", 51.4050, 19.7030, false),
        new("Lubelskie", "Zamość", 50.7231, 23.2520, false),
        new("Podkarpackie", "Przemyśl", 49.7840, 22.7673, false),
        new("Podlaskie", "Suwałki", 54.1110, 22.9300, false),
        new("Warmińsko-mazurskie", "Elbląg", 54.1560, 19.4044, false),
        new("Zachodniopomorskie", "Koszalin", 54.1943, 16.1715, false),
        new("Kujawsko-pomorskie", "Włocławek", 52.6483, 19.0677, false),
        new("Opolskie", "Kędzierzyn-Koźle", 50.3498, 18.2261, false),
        new("Świętokrzyskie", "Ostrowiec Świętokrzyski", 50.9294, 21.3852, false),
        new("Lubuskie", "Nowa Sól", 51.8033, 15.7170, false),
    ];
}

public sealed record AdminCity(string Voivodeship, string Name, double Latitude, double Longitude, bool IsCapital);
