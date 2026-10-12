# KOS-zawał

Natywna aplikacja Windows do publicznego raportu NFZ o zawale serca i programie KOS-zawał. Pobiera placówki, KOS-zawał, trendy, województwa i metodykę, zapisuje je w lokalnej bazie SQLite i pozwala wyeksportować dowolny zbiór.

Nie ma instalatora. GitHub Actions składa jeden plik `KosZawal.exe`.

## Co widać na starcie

Dla wybranego roku (domyślnie 2025) i typu zawału:

1. Odsetek hospitalizacji zawałowych objętych KOS-zawał.
2. Liczba szpitali, które sprawozdały hospitalizację w programie.
3. Liczba województw z co najmniej jednym realizatorem.
4. Liczba ośrodków w podziale na województwa.
5. Ten sam odsetek w skali całego kraju. W tym raporcie punkty 1 i 5 są jednym wskaźnikiem: suma (odsetek KOS × liczba hospitalizacji) podzielona przez wszystkie hospitalizacje zawałowe.

Osobno widać udział liczony tylko wśród ośrodków, które program realizują.

Mapa korzysta z kafelków OpenStreetMap (wymaga sieci). Dostępne tryby: wszystkie ośrodki KOS, widok administracyjny (województwa + główne miasta) oraz liderzy województw (ośrodek z największą liczbą hospitalizacji). Złoty punkt oznacza położenie przybliżone.

Komunikat CeZ/NFZ o „40% w 2024” dotyczy udziału wśród przypadków **bez zgonów wewnątrzszpitalnych**. Wskaźnik aplikacji z raportu Power BI liczy wszystkie hospitalizacje i dla 2024 daje ok. 34,5% (typ łącznie).

## Uruchomienie

Potrzebny jest Windows 10 lub 11, 64-bit. Po pierwszym starcie aplikacja sama pobiera raport. Baza ląduje w `%LOCALAPPDATA%\KosZawal\koszawal.db`.

Gotowy plik jest artefaktem workflow **Kompilacja Windows**. Na stronie Actions wybierz ostatnie udane uruchomienie i pobierz `KosZawal-win-x64`.

Źródło danych: [raport NFZ „zawały”](https://app.powerbi.com/view?r=eyJrIjoiNmI3NDZiMDEtMDUwYS00NGU0LTgzZWYtYjM3Y2M3NWRjZGJlIiwidCI6IjJlNzcwYzE2LWMwNzEtNDA1Mi04MzdjLTU0NWJlZTBiMzQwYyIsImMiOjl9).

## Zbudowanie lokalnie

.NET SDK 8.

```bash
dotnet test KosZawal.sln -c Release
dotnet publish src/KosZawal/KosZawal.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist
```

Wynik: `dist/KosZawal.exe`.

Odświeżenie danych z konsoli, bez okna: `KosZawal.exe --sync` nie pokaże konsoli, bo program jest aplikacją okienkową. Do diagnostyki na maszynie z SDK:

```bash
dotnet run --project src/KosZawal/KosZawal.csproj -- --sync
```
