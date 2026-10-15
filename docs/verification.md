# Weryfikacja Fazy 0 — 15.10.2026

Godot 4.7 .NET / Jolt, SDK 10.0.400, Windows / RTX 4050 Laptop. Linux headless
sprawdzony w Ubuntu/WSL. CI pozostaje odłożone na życzenie użytkownika.

| Scenariusz | Wynik lokalny | Powtórzenie |
|---|---|---|
| Domena, DAG, kampania, profil, fitness, protokół, zapis i autoryzacja | 73 xUnit — PASS | `scripts/test.ps1` |
| Build, impaired/combat, rozłączenia i 8 aut headless | PASS | `scripts/verify-stage.ps1 -Stage phase0-final` |
| 8 procesów, 2 rundy, 100 ms RTT / 3% strat zawodnych | Wspólne podium, bramki, czasy i zdarzenia — PASS | `scripts/integration.ps1 -Clients 7 -Rounds 2 -Impaired -Combat` |
| Dedicated, 2 rundy | PASS | `scripts/integration.ps1 -Dedicated -Rounds 2` |
| Sfałszowane pozycje, trafienia, podium i buildy | Odrzucone — PASS | sondy integracji |
| Rozłączenie i utrata hosta | AI / zakończenie sesji, także timeout — PASS | `scripts/connection-tests.ps1` |
| Kampania 3 rundy, impaired/combat, izolowane profile | 3 wyścigi i jedna nagroda kampanii na profil — PASS | `scripts/integration.ps1 -Campaign -Impaired -Combat` |
| Przerwanie drugiej rundy i restart hosta | Zachowany rozwój/zakupy/GUID, nowe PeerId, powtórka rundy — PASS | `scripts/campaign-resume-tests.ps1 -RewardCrash` |
| Brak/błędny token i zajęte miejsce | Odrzucone bez zmiany revision — PASS | ten sam test procesowy |
| Nieobecny klient podczas finału, restart ukończonej kampanii | Zaległe nagrody zapisane i odebrane jednokrotnie — PASS | ten sam test procesowy |
| Błędy zapisu, checksum, backup, drugi restart, migracja v1 | PASS | `CampaignPersistenceTests`, `ProfileTests` |
| Mysz/pad: garaż, dialog, pauza, fokus i zwolnienie przycisku | Natywne zdarzenia Godota — PASS | `--smoke --input-smoke --bots=0` |
| 12 sesji, stare timery, pełne sprzątanie | PASS | ten sam input smoke |
| Cudzy ClientId, oferta, HTTP body/chunked/JSON i limity NAT | 409/413/429 bez nieuprawnionej zmiany kolejki — PASS | `scripts/directory-security-tests.ps1` |
| Quick: nieosiągalny host | Kolejna oferta i wspólny wyścig — PASS | `scripts/quick-integration.ps1 -UnreachableHost` |
| Quick: anulowanie, stare odpowiedzi, zgubiony sekret | Cztery cykle oraz oczekiwanie na wygaśnięcie — PASS | `scripts/quick-cancel-tests.ps1 -Orphan` |
| Drift boost i EMP, 8 pełnych buildów | PASS | `scripts/mechanics-smoke.ps1` |
| Kontener API i zmienne limitów | Health v4 oraz 429 po skonfigurowanym progu — PASS | Dockerfile + `RequestLimits__Capacity` |
| Eksporty Windows i Linux | Po 8 aut i ukończony wyścig — PASS | `scripts/export.ps1`, eksport `--headless --fixed-fps 60 -- --smoke --bots=7 --balance=open --weapon-level=5 --mine-level=5` |
| Renderer 1280×800, 8 aut, broń/miny poz. 5 | 59,83 FPS, p95 17,716 ms, p99 19,209 ms | rzeczywiste renderowanie, limit 60, bez fixed-fps |

Baseline miał 59,84 FPS, p95 17,532 ms i p99 18,884 ms. Porównanie i granice
pomiaru są w [phase0-stability.md](phase0-stability.md) oraz
[phase0-results.json](phase0-results.json). Zrzut prawdziwego renderera
sprawdzono wizualnie. Średnia około 60 FPS nie wyklucza pojedynczych przycięć.

Ścieżki lokalnych logów i raportów: [phase0-checks.json](phase0-checks.json).
Raporty zachowują znaczenie starych pól; runtimeMetrics, SessionState i generacja
są osobnymi, dodatkowymi polami. Smoke używa pamięci, a testy trwałości jawnie
izolowanych katalogów. Profile, sekrety, silnik, cache i eksporty są poza Git.

## Granice

Testy sieciowe używają osobnych procesów na jednym komputerze. Nie sprawdzono
publicznego routera UPnP/CGNAT ani wdrożonego katalogu. Pad sprawdzono przez
natywne InputEventJoypad, bez fizycznego kontrolera. Linux przeszedł headless;
renderowanie i dźwięk sprawdzano na Windows. Monitor aktywnych ciał Jolta zwraca
0 i jest oznaczony jako niedostępny. Czas fizyki silnika nie jest odizolowanym
pomiarem Jolta. Alokacje zarządzane dotyczą całego procesu.

Predykcja nadal odtwarza wejście w przybliżeniu; kolizje i eksplozje pozostają
autorytatywne i mogą powodować twarde korekty. Nie ma relaya, migracji hosta,
dowodu historycznego zdobycia buildu ani publicznej alphy.

Repozytorium: [sophiesticated94/brute-racing](https://github.com/sophiesticated94/brute-racing).
Workflow pozostaje w repozytorium, lecz CI nie stanowi odbioru tej fazy i nie
było sprawdzane podczas realizacji tego planu.
