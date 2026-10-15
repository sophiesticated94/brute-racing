# Mini Car Mayhem 3D

Autorska wyścigowa gra bojowa: Godot **4.7 .NET**, **Jolt**, C#. Jeden tor 3D,
samochód Rook w ośmiu kolorach, karabin, laser, rakiety, miny i do siedmiu rywali.
Menu → lobby → odliczanie → trzy okrążenia → podium → rewanż.

## Uruchomienie na Windows

Wymagany **PowerShell 7**, SDK .NET **10.0.400** i możliwość pobierania pakietów NuGet.
Silnik i szablony eksportu są lokalne, w ignorowanym `.tools/`.

```powershell
./scripts/setup.ps1                  # pobiera oficjalny Godot 4.7 .NET
./scripts/run.ps1                    # menu
./scripts/run.ps1 -Solo              # gracz + 3 boty
./scripts/local-play.ps1 -Clients 1  # dwa okna: host i klient
```

Gotowe eksporty powstają w `artifacts/windows/` i `artifacts/linux/`.
Uruchom `MiniCarMayhem.exe` albo `MiniCarMayhem.x86_64` z całym folderem eksportu;
katalog z bibliotekami .NET jest częścią gry. Na Linux nadaj plikowi wykonywalnemu
uprawnienia `chmod +x MiniCarMayhem.x86_64`. Nie jest potrzebny osobny backend.
Eksport zawiera runtime .NET i używa stałego formatowania liczb, dzięki czemu nie
wymaga instalowania ICU na Linux.

## Sterowanie

| Akcja | Klawiatura / mysz | Pad |
|---|---|---|
| Gaz / hamowanie i wsteczny | W / S, góra / dół | RT / LT |
| Skręt względem auta | A / D, lewo / prawo | Lewa gałka |
| Celowanie wieżyczką | Kursor na torze | Prawa gałka |
| Broń główna | LPM | RB |
| Mina | PPM | LB |
| Drift | Spacja | A |
| Menu opuszczenia wyścigu | Esc | — |
| Wyciszenie | M | — |
| Zrzut ekranu do danych użytkownika | F12 | — |

Gaz i wsteczny są zależne od kierunku auta, kamera zachowuje orientację świata.
Esc otwiera menu, ale **nie zatrzymuje wspólnej symulacji**. Ochrona po respawnie
trwa 2 sekundy, uzupełnia się uzbrojenie. Laser regeneruje energię; przegrzany
musi ostygnąć poniżej 30%. Karabin i rakiety mają ograniczoną amunicję.

## Multiplayer

Host wybiera liczbę botów, broń i kolor, następnie tworzy pokój. Klient podaje IP
i port (domyślnie **28747/UDP**). W lobby można zmienić broń i lakier; zmiana cofa
gotowość. Host startuje po zgłoszeniu gotowości przez wszystkich. Dołączenie
człowieka do pełnego lobby zastępuje bota. Minimum wyścigu sieciowego: 2 auta,
maksimum: 8. Podczas wyścigu nie można dołączać. Rozłączone auto przejmuje AI.

W LAN użyj adresu lokalnego gospodarza. Internet wymaga osiągalnego portu UDP
(przekierowanie routera / opcjonalne UPnP oraz reguła zapory). Gra nie udaje, że
katalog kodów pokoi rozwiązuje CGNAT: relay i Steam są kolejnymi etapami.

```powershell
./scripts/run.ps1 -HostGame -Bots 3
./scripts/run.ps1 -Join 192.168.1.10 -Port 28747
./scripts/run.ps1 -HostGame -Headless
```

Headless ze zwykłym `-HostGame` również ma lokalne miejsce gracza; do automatycznego
hosta bez lokalnego kierowcy służą argumenty silnika `-- --host --dedicated --autostart`.

## Budowanie i sprawdzanie

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/integration.ps1 -Impaired -Combat
./scripts/integration.ps1 -Dedicated -Rounds 2 -Port 29848
./scripts/connection-tests.ps1
./scripts/setup.ps1 -ExportTemplates  # pobiera ok. 1,2 GB
./scripts/export.ps1
```

Test integracyjny uruchamia prawdziwe procesy Godota. Sprawdza wspólne podium,
czasy, zaliczone bramki i liczbę zdarzeń. Klienci próbują także podszyć się pod
hosta przez sfałszowane snapshoty, zdarzenia i podium; host je ignoruje.
`connection-tests.ps1` sprawdza przejęcie rozłączonego gracza przez AI i utratę hosta.
`-Impaired` dodaje po 50 ms opóźnienia
w obu kierunkach i odrzuca 3% pakietów zawodnych. Pakiety niezawodne pozostają
obsługiwane przez ENet. Raporty JSON i logi trafiają do `artifacts/tests/`.

Szybki test solo, bez czasu rzeczywistego:

```powershell
$godot = (Get-ChildItem .tools/godot -Recurse -Filter '*console.exe' | Select-Object -First 1).FullName
& $godot --headless --path src/Game.Client --fixed-fps 60 -- --smoke --bots=7
```

`--fixed-fps` przyspiesza symulację i **nie mierzy płynności renderowania**.
Raport rozdziela czas symulacji od rzeczywistej częstotliwości klatek.
Diagnostyka: `--timeout=210`, `--no-weapons`, `--weapon=laser`, `--rounds=2`,
`--results=C:/absolute/report.json`, `--screenshot=C:/absolute/frame.png`.

## Architektura i zakres

- `src/Game.Core`: niezależne od silnika statystyki, definicje, walidacja buildów,
  checkpointy, ranking, zegar wyścigu, protokół i przybliżony model predykcji.
- `src/Game.Client`: fizyka Jolt, autorytatywna sesja, transport ENet, boty,
  proceduralne modele, tor, polskie UI i syntetyzowane dźwięki.
- `data`: źródłowe JSON-y kopiowane do projektu podczas budowania.
- `tests/Game.Core.Tests`: testy xUnit. `.github/workflows/ci.yml`: build, testy,
  wyścigi procesów i eksporty.

Implementowany jest **pierwszy grywalny rdzeń**, nie cały docelowy produkt.
Garaż, progresja, DAG umiejętności, poziomy broni 2–5, zapis profili, katalog pokoi
i chmura pozostają poza tym odbiorem. Oryginalna wizja: `doc/plan.txt` oraz
`doc/architecture.txt`. Szczegóły obecnego kodu: `docs/architecture.md` i
`docs/networking-p2p.md`. Wyniki i granice sprawdzenia: `docs/verification.md`.
