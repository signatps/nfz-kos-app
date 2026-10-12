namespace KosZawal;

public static class MethodologyText
{
    public const string Fallback = """
        Definicja zawału serca

        Aplikacja przedstawia porównanie placówek pod kątem leczenia przypadków zawałów mięśnia sercowego. Jako przypadek zawału definiowano hospitalizacje z rozpoznaniem głównym hospitalizacji: I21.0, I21.1, I21.2, I21.3, I21.4, I21.9 (wg klasyfikacji ICD-10), które rozpoczęły się w latach 2018–2025 i zostały sprawozdane w ramach rodzaju świadczeń leczenie szpitalne lub w ramach świadczeń finansowanych z Funduszu Przeciwdziałania COVID-19 (w okresie marzec 2021 – luty 2022). W przypadku hospitalizacji sprawozdanych w ramach programu KOS-zawał, uwzględniano wyłącznie hospitalizacje z rozliczonym produktem JGP oraz wyłącznie takie, dla których data hospitalizacji była zgodna z datą zawału raportowaną komunikatem AP-PKUŚ.

        Hospitalizacje z rozpoznaniem głównym: I21.0, I21.1, I21.2, I21.3 traktowano jako zawał STEMI, a hospitalizacje z rozpoznaniem głównym I21.4 i I21.9 traktowano jako zawał NSTEMI.

        Średni czas pobytu

        Średni czas pobytu obliczono jako różnicę między datą wypisu a datą przyjęcia hospitalizacji, powiększoną o 1 dzień.

        Angioplastyka wieńcowa, CABG

        Angioplastykę wieńcową definiowano jako świadczenie ze sprawozdaną procedurą: 00.40, 00.41, 00.42, 00.43, 00.44, 00.45, 00.46, 00.47, 00.48, 00.66, 00.661, 00.662, 36.0, 36.06, 36.061, 36.062, 36.063, 36.064, 36.065, 36.07, 36.070, 36.071, 36.072, 36.073, 36.074, 36.09, 36.091 (wg klasyfikacji ICD-9).

        Pomostowanie aortalno-wieńcowe definiowano jako świadczenie ze sprawozdaną procedurą: 36.1 wraz z rozszerzeniami (wg klasyfikacji ICD-9).

        Rehabilitacja kardiologiczna

        Rehabilitacja kardiologiczna została zdefiniowana jako świadczenia sprawozdane w ramach zakresów:

        · 05.4308.300.02 – Rehabilitacja kardiologiczna lub kardiologiczna telerehabilitacja hybrydowa w warunkach stacjonarnych,
        · 05.2300.526.02 – Rehabilitacja kardiologiczna lub kardiologiczna telerehabilitacja hybrydowa w ośrodku/oddziale dziennym dla osób o znacznym stopniu niepełnosprawności,
        · 05.2300.026.02 – Rehabilitacja kardiologiczna lub kardiologiczna telerehabilitacja hybrydowa w ośrodku/oddziale dziennym,
        · 05.4308.500.02 – Rehabilitacja kardiologiczna lub kardiologiczna telerehabilitacja hybrydowa w warunkach stacjonarnych dla osób o znacznym stopniu niepełnosprawności,
        · 05.4308.400.02 – Rehabilitacja kardiologiczna lub kardiologiczna telerehabilitacja hybrydowa w warunkach stacjonarnych dla przypadków wymagających

        lub sprawozdane produktami rozliczeniowymi rehabilitacji kardiologicznej w ramach programu KOS-zawał (5.11.02.9000063, 5.11.02.9000064, 5.11.02.9100073).

        Świadczenia w programie KOS-zawał

        Odsetek hospitalizacji sprawozdanych w ramach programu KOS-zawał odnosi się wyłącznie do hospitalizacji niezakończonych zgonem (tj. o trybie wypisu innym niż „9”).

        W zakładce KOS-zawał uwzględniono wyłącznie świadczeniodawców, którzy w analizowanym okresie sprawozdali co najmniej 1 hospitalizację w ramach programu KOS-zawał niezakończoną zgonem pacjenta.

        Wizyta kontrolna koordynująca – świadczenia sprawozdane z kodami produktu: 5.53.01.0005009.

        Specjalistyczna opieka kardiologiczna (AOS) – świadczenia sprawozdane z kodem produktu: 5.52.01.0001507.

        Zakończenie leczenia – świadczenia sprawozdane z kodami produktu: 5.53.01.0005011, 5.52.01.0001508.

        Varia

        O ile nie wskazano inaczej, wskaźniki dotyczące świadczeń udzielonych pacjentowi w ramach leczenia zawału lub po zawale odnoszą się do świadczeń udzielonych u danego świadczeniodawcy. Przy obliczaniu tych wskaźników brano pod uwagę wyłącznie pacjentów, którzy mieli udzielone dane świadczenie lub zdarzenie miało szanse wystąpienia w danym okresie obserwacji (tj. pacjent nie zmarł oraz możliwe było obserwowanie świadczeń tego pacjenta w danych = data końca obserwacji nie wybiega w przyszłość).

        W zakładce Województwa określenie „aktywny pod względem wybranego wskaźnika” odnosi się do świadczeniodawców, którzy w danym roku sprawozdali co najmniej jedno świadczenie związane z wybranym wskaźnikiem. Przykładowo, dla wskaźnika dotyczącego programu KOS-zawał przedstawiono odsetek świadczeniodawców, którzy w danym roku zrealizowali co najmniej 1 świadczenie w ramach programu wśród wszystkich świadczeniodawców, którzy zrealizowali świadczenia związane z zawałem serca.

        Jako koniec okresu obserwacji dla świadczeń udzielonych pacjentom po wystąpieniu zawału serca przyjęto koniec marca 2026 r.

        Przedstawione dane zaprezentowano na podstawie danych sprawozdanych, niekoniecznie rozliczonych.

        Aplikacja nie uwzględnia świadczeń związanych z leczeniem i zapobieganiem COVID-19 w okresie marzec 2020 – luty 2021.

        Dane przygotowano wg stanu bazy Centrali NFZ na dzień 15.06.2026 r.

        """;

    public const string KpiExplanation = """
        Jak liczone są wskaźniki na ekranie startowym (ta sama metoda dla 2024 i 2025)

        1 i 5. Odsetek zawałów objętych KOS w skali kraju
           licznik = Σ (proc_KOS × liczba_hospitalizacji) z tabeli KOS-zawał dla roku i typu zawału
           mianownik = Σ liczba_hospitalizacji z tabeli Placówki dla tego samego roku i typu
           udział = licznik / mianownik
           W tym modelu punkty „odsetek przypadków objętych KOS” i „odsetek w skali całego kraju” to jeden wskaźnik.

        2. Liczba szpitali / świadczeniobiorców
           COUNT placówek z czy_KOS = TAK w tabeli Placówki dla roku i typu.
           To nie jest rejestr umów NFZ — raport Power BI nie publikuje osobnej listy umów.
           Oznacza ośrodki, które sprawozdały co najmniej jedną hospitalizację w programie (zgodnie z metodyką NFZ: niezakończoną zgonem).

        3. Liczba województw z realizatorami
           liczba województw, w których COUNT z punktu 2 jest ≥ 1.

        4. Ośrodki według województwa
           ten sam COUNT z punktu 2, pogrupowany po województwie.

        Udział „w ośrodkach KOS”
           ten sam licznik co w punkcie 1, ale mianownik = Σ liczba_hospitalizacji tylko z tabeli KOS-zawał.

        Lider województwa na mapie
           wśród ośrodków KOS w danym województwie wybierany jest ten z największą liczbą hospitalizacji zawałowych (tabela Placówki / pin mapy) dla wybranego roku i typu.

        Porównanie z komunikatem CeZ/NFZ „40% w 2024”
           Raport CeZ (Tabela 2.1) podaje dla 2024:
           · 37,4% wśród wszystkich przypadków zawału,
           · 40,1% po wyłączeniu zgonów wewnątrzszpitalnych — to jest publiczne „40%”.
           Aplikacja liczy z publicznego Power BI hospitalizacje bez korekty zgonów i bez łączenia epizodów pacjenta, więc dla 2024 (typ łącznie) wychodzi ok. 34,5%. To nie jest błąd arytmetyczny, tylko inna definicja mianownika.
        """;
}
