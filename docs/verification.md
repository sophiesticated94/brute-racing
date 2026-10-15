# Weryfikacja pierwszego rdzenia — 15.10.2026

Sprawdzone lokalnie na Windows z Godot 4.7 .NET i SDK 10.0.400:

| Scenariusz | Wynik | Powtórzenie |
|---|---|---|
| Domena, walidacja, checkpointy 3D, ranking, zegar, protokół, fuzzing, predykcja | 22 testy zaliczone | `scripts/test.ps1` |
| 8 aut, wszystkie bronie, pełny wyścig | 288 zaliczonych bramek, wszystkie auta ukończyły 3 okrążenia | headless `--fixed-fps 60 -- --smoke --bots=7` |
| Host–klient, 100 ms RTT, 3% utraty pakietów ruchu | Wspólne podium, czasy, bramki i identyczne liczniki wszystkich zdarzeń | `scripts/integration.ps1 -Impaired -Combat` |
| Maksimum sesji: host i 7 klientów | 8 osobnych procesów, wspólne podium i zgodne zdarzenia | `scripts/integration.ps1 -Clients 7` |
| Sfałszowane snapshoty, obrażenia i wyniki wysłane przez klienta | Host ignoruje próby; klient otrzymuje właściwe podium | automatycznie w testach integracyjnych |
| Headless bez lokalnego gracza, 2 klientów, rewanż | 2 rundy, zgodne wyniki w 3 procesach | `scripts/integration.ps1 -Dedicated -Clients 2 -Rounds 2` |
| Utrata klienta podczas wyścigu | Auto przejmuje bot, wyścig dochodzi do podium | `scripts/connection-tests.ps1` |
| Utrata hosta | Komunikat i bezpieczne zamknięcie połączenia klienta | `scripts/connection-tests.ps1` |
| Eksport Windows bez edytora | Pełny wyścig z 8 autami | `artifacts/windows/MiniCarMayhem.exe --headless --fixed-fps 60 -- --smoke --bots=7` |
| Eksport Linux na Ubuntu / WSL | Pełny wyścig z 8 autami; bez osobnego .NET i ICU | `artifacts/linux/MiniCarMayhem.x86_64 --headless --fixed-fps 60 -- --smoke --bots=7` |

W biegu sieciowym z utrudnieniami średnia korekta lokalnego auta wyniosła około
**0,20 m**. Liczniki zdarzeń hosta i klienta były identyczne, włącznie ze
zniszczeniami i respawnami. Raporty zachowują także największą korektę, która
obejmuje celowe teleportacje podczas respawnu.

## Renderowanie

Pełny, renderowany wyścig 8 aut na NVIDIA RTX 4050 Laptop, 1280×800, OpenGL
Compatibility, przy limicie 60 FPS: **59,9 FPS średnio**. Najdłuższa klatka trwała
111,7 ms, więc średnia nie oznacza braku pojedynczych przycięć. Test używał czasu
rzeczywistego, bez `--fixed-fps`. Menu, HUD i rozgrywkę sprawdzono na zrzutach
z rzeczywistego renderera.

## Granice weryfikacji

Pad ma przypisane sterowanie, ale nie sprawdzono go fizycznym kontrolerem.
Połączenia testowano na jednej maszynie z rzeczywistymi procesami ENet;
konkretnego routera UPnP ani gry przez publiczny Internet nie sprawdzano.
Linux przeszedł test headless; renderowanie i dźwięk sprawdzono na Windows.
Workflow GitHub Actions jest przygotowany, lecz nie został wysłany ani wykonany
na GitHubie. Raporty i logi lokalnych uruchomień są w ignorowanym `artifacts/tests/`.
