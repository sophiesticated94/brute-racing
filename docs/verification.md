# Weryfikacja — 15.10.2026

Godot 4.7 .NET / Jolt, SDK 10.0.400, Windows, eksport Linux sprawdzony w Ubuntu/WSL.

| Scenariusz | Wynik | Powtórzenie |
|---|---|---|
| Domena, statystyki, DAG, kampania, profil, fitness, kolejka, protokół | 48 testów xUnit — PASS | `scripts/test.ps1` |
| Każdy z pięciu etapów: build, xUnit, impaired/combat, rozłączenia, 8 aut headless | PASS, logi etapów w artifacts/stages | `scripts/verify-stage.ps1 -Stage nazwa` |
| 100 ms RTT / 3% strat zawodnych pakietów, walka | Identyczne wyniki, bramki, czasy i liczniki zdarzeń | `scripts/integration.ps1 -Impaired -Combat` |
| 8 osobnych procesów, 2 rundy, impaired/combat | Zgodne podium i wszystkie zdarzenia — PASS | `scripts/integration.ps1 -Clients 7 -Rounds 2 -Impaired -Combat -Port 29865` |
| Dedicated i rewanż | Ten sam kod sesji, wspólne wyniki — PASS | `scripts/integration.ps1 -Dedicated -Rounds 2` |
| Niedozwolone pozycje, trafienia, wyniki i 5 malformed buildów/klienta | Host ignoruje/odrzuca próby — PASS | wbudowane sondy integracji |
| Rozłączenie klienta i utrata hosta | Przejęcie przez AI / komunikat kończący sesję — PASS | `scripts/connection-tests.ps1` |
| Kampania 3 rundy, 2 procesy, impaired/combat, izolowane profile | Wspólny stan kampanii; po 3 wyścigi i 1 nagrodzie kampanii — PASS | `scripts/integration.ps1 -Campaign -Impaired -Combat -Port 29860` |
| Zapis, restart, deduplikacja nagrody, uszkodzenie i backup | PASS | testy ProfileTests |
| Quick 4 ludzi | Jeden host, wspólna oferta i wyniki; bez botów — PASS | `scripts/quick-integration.ps1 -Players 4` |
| Quick z nieosiągalnym hostem | Powrót do kolejki, kolejny host, pełny wyścig 2 procesów — PASS | `scripts/quick-integration.ps1 -UnreachableHost` |
| Drift boost i EMP z pełnymi buildami | 9 boostów i 71 pól EMP w zakończonym biegu — PASS | `scripts/mechanics-smoke.ps1` |
| Kontener ASP.NET Core 10 | Build i uruchomienie jako non-root, health/protokół/hash — PASS | `docker build -f src/Game.DirectoryApi/Dockerfile ...` |
| Eksport Windows | Pełny wyścig, 8 aut, poziom 5 i archetypy — PASS | eksport `--headless --fixed-fps 60 -- --smoke --bots=7 --balance=open --weapon-level=5 --mine-level=5` |
| Eksport Linux / WSL | Pełny wyścig i podium, 8 aut, poziom 5 — PASS | te same flagi, Linux binary |
| Render 1280×800 na RTX 4050 Laptop, 8 rozbudowanych aut | 59,6 FPS średnio przy limicie 60 | czas rzeczywisty, bez fixed-fps |
| Garaż i HUD | Sprawdzone zrzuty rzeczywistego renderera, polskie nazwy i DAG | `--garage-preview --screenshot=...` / wyścig |

Podczas testu renderowania działały także procesy weryfikacyjne. Najdłuższa klatka
trwała 338 ms, więc średnia nie oznacza braku przycięć. `--fixed-fps` przyspiesza
testy logiki i nie służy do mierzenia płynności. Mocne buildy nadal mogą nie
ukończyć wyścigu przed 30-sekundowym limitem po pierwszej mecie; końcowa
klasyfikacja poprawnie uwzględnia wtedy postęp. W renderowanym biegu wszystkie
8 aut ukończyło, eksport Linux zakończył część stawki według postępu.

Raporty zachowują stare pola oraz dodają RTT, jitter, wiek snapshotu, korekty,
tryb, stan kampanii, nagrody i identyfikator oferty quick. Testy nie dotykają
prawdziwego profilu użytkownika. Lokalne logi/eksporty są poza Git; CI udostępnia
paczki Windows/Linux oraz logi jako artefakty.

## Granice

ENet testowano w osobnych procesach na jednej maszynie. Nie sprawdzono konkretnego
routera UPnP, CGNAT ani publicznego wdrożenia katalogu. Pad ma przypisane sterowanie,
ale nie został sprawdzony fizycznym kontrolerem. Linux przeszedł headless;
renderowanie i dźwięk sprawdzono na Windows. Predykcja jest przybliżonym replay
wejść; kolizje i eksplozje pozostają autorytatywne i mogą powodować twarde korekty.
Nie ma relaya, migracji hosta ani dowodu historycznego zdobycia archiwalnego buildu.

Status pierwszego GitHub Actions zostanie dopisany po publikacji repozytorium.
