# Faza 0 — audyt i stabilizacja

Punkt bazowy: b4c349b, 48 testów xUnit zaliczonych. Nowy zakres z doc/plan.txt.
CI jest odłożone na życzenie użytkownika; kontrole lokalne pozostają obowiązkowe.

## Pomiar bazowy

Rzeczywiste renderowanie 8 aut z bronią poziomu 5: 59,84 FPS; klatki po 3 s rozgrzewki:
p95 17,532 ms, p99 18,884 ms, maksimum 67,588 ms. Legacy worstFrameMs obejmuje
rozruch i wynosi 99,11 ms. Monitor fizyki Godota i pomiar pętli sesji są osobnymi
metrykami; monitor Godota jest próbkowany raz na klatkę i nie mierzy osobno Jolta.
Alokacje zarządzane: około 1,28 MB/s (cały proces, także wątki silnika C#).
Szczyt 2482 węzły, 12 pocisków/min, 171 dzieci efektów. Jolt raportował 0 aktywnych
ciał w monitorze silnika; tę wartość traktujemy jako niedostępną, nie brak aut.
Pełne liczby: phase0-baseline.json. Test używał --max-fps 60, bez --fixed-fps.

## Potwierdzone problemy

- ReadInput zmienia desiredAim zanim komenda zostanie zneutralizowana przez UI.
  Neutral zachowuje AimYaw i Handbrake; brak blokady do zwolnienia przycisku.
- Stany sesji są pośrednio kodowane jako RacePhase.Lobby, flagi oraz widoczność UI.
  Timery nie wszystkie uwzględniają generację sesji.
- CampaignState nie ma zapisu. PeerId jest kluczem rozwoju, brakuje odtwarzania
  uczestnika i trwałej kolejki niedostarczonych nagród.
- Enqueue usuwa bilety o tym samym ClientId bez sprawdzenia sekretu.

## Ustalone zachowanie

Wznowienie wraca do garażu z ostatnim zatwierdzonym rozwojem. Przerwana runda
zaczyna się od początku. Uczestnik wraca przez ten sam profil i token kampanii.
Solo, LAN i stara linia poleceń nadal działają bez katalogu. Fazy 1–6 nie należą
do tego odbioru.

Baseline procesu ENet/combat i smoke zaliczone. Test rozłączenia ujawnił brak
PLAYER_AI_TAKEOVER w 15 s po twardym zakończeniu klienta: timeout natywnego ENet
nie gwarantuje czasu wymaganego przez test. Dodano niezależny watchdog hosta
(10 s bez pakietu) i idempotentne przejęcie miejsca.

## Zrealizowane zmiany

- GameplayInputGate blokuje źródło przed ReadInput/BotInput, a następnie filtruje
  komendę. Menu, lobby, podium, pauza, garaż, dialog i utrata fokusu zatrzymują
  jazdę, drift, broń i celowanie. Kąt zatrzymanej wieżyczki jest normalizowany
  do zakresu protokołu. Kliknięcie zamykające UI wymaga zwolnienia przycisku.
- SessionLifecycle ma osiem jawnych stanów, zachowując stary RacePhase. Wyjście
  czyści historię, zdarzenia, miny/pociski, efekty, UI i raporty rund. Generacja
  zabezpiecza transport, timery i UPnP. Quick szereguje anulowanie i enqueue,
  sprawdza generację po await i ponawia po konflikcie aktywnego ClientId.
- Kampania ma snapshot schematu 1, GUID uczestników, revision, hash reguł,
  rozwój, wyposażenie, operacje i trwałą kolejkę nagród. PeerId jest adapterem
  połączenia. Zapis osobnych kampanii jest atomowy z flush i .bak; błędy
  zatrzymują start. Klient otrzymuje ACK operacji dopiero po zapisie hosta.
- Profil schematu 2 przechowuje prywatne tokeny; migracja najpierw sprawdza
  oryginalną sumę schematu 1. Powrót wymaga jednorazowego dowodu HMAC. Aktywne
  miejsce, błędny token i sam GUID są odrzucane. Sekretów nie ma w widokach,
  raportach ani logach. Wynik i nagrody zapisują się przed dostarczeniem;
  klient potwierdza odbiór dopiero po zapisie profilu.
- Wznowienie z menu / --resume-campaign wraca do garażu z zapisanymi regułami
  i długością. Przerwana runda zaczyna się od początku z nowym GUID. Ukończoną
  kampanię można otworzyć w celu odbioru zaległych nagród. Nowa kampania
  zachowuje stary zapis i resetuje rozwój.
- Handshake ma wersję 4. Wejście ma RaceId; stare pakiety są pomijane przed
  dekodowaniem komend. Directory zwraca 409 bez zastępowania biletu/oferty,
  ma limity IP: początkowo 120 żądań i odnawianie 20/s; enqueue 8 i 1/s.
  Body ma 32 KiB, JSON 16 poziomów, a kolejka 1024 wpisy.

## Regresje znalezione podczas odbioru

Osiem procesów i rewanż ujawniły kąt wieżyczki przekraczający pi podczas blokady.
Stary pakiet z podium mógł wtedy rozłączyć klienta przy odliczaniu następnej
rundy. Testy chronią normalizację kąta i pomijanie starego RaceId przed walidacją.
Timeout utraty hosta respektuje teraz także --exit-on-disconnect. Rozłączenie
jest idempotentne; opóźniony reject nie próbuje usuwać już zamkniętego peer-a.

Odzyskanie backupu usuwa uszkodzony plik spod aktywnej nazwy, zachowując go
jako .corrupt-GUID. Kolejny zapis nie zastępuje dobrej kopii uszkodzonymi danymi.
Profil potrafi też odtworzyć backup, gdy po ponownym restarcie nie ma jeszcze
pliku głównego. Polling plików w testach dopuszcza zastąpienie pliku podczas
odczytu i ponawia chwilowy brak nazwy.

## Wynik odbioru lokalnego

73 xUnit — PASS. Build, impaired/combat, rozłączenia, 8 aut headless,
8 osobnych procesów i 2 rundy, dedicated i rewanż, 3-rundowa kampania,
restart przerwanej rundy, zakupy, nowy PeerId, odrzucenie zajętego miejsca,
braku/błędnego tokenu oraz zaległe nagrody po restarcie ukończonej kampanii — PASS.
Mysz/pad przez natywne zdarzenia Godota, 12 cykli sesji, stare timery i odpowiedzi
HTTP, quick z nieosiągalnym hostem, zgubiony sekret biletu, HTTP 409/413/429,
chunked body i 8 klientów NAT — PASS. Kontener sprawdził także konfigurację
limitu przez zmienne środowiskowe. Eksporty Windows i Linux/Ubuntu ukończyły
wyścigi 8 aut. CI pozostało odłożone.

Pełne nazwy lokalnych artefaktów: phase0-checks.json. Porównanie pomiarów:
phase0-results.json. Lokalne profile, logi, silnik i eksporty są ignorowane przez Git.

| Pomiar, izolowany renderer | Przed | Po Fazie 0 |
|---|---:|---:|
| Średni FPS | 59.842 | 59.832 |
| Klatka p95, ms | 17.532 | 17.716 |
| Klatka p99, ms | 18.884 | 19.209 |
| Klatka max po rozgrzewce, ms | 67.588 | 78.300 |
| Tick sesji p95, ms | 1.101 | 1.106 |
| Fizyka silnika p95, ms | 22.241 | 24.462 |
| Alokacje zarządzane, MB/s | 1.281 | 1.326 |

Warunki: RTX 4050 Laptop, 1280×800, 8 aut, broń/miny poziomu 5, limit 60 FPS,
bez fixed-fps i równoległych testów. Stare worstFrameMs nadal obejmuje rozruch;
nowe percentyle pomijają pierwsze 3 s. Średnia około 60 FPS nie oznacza braku
pojedynczych przycięć. Zrzut prawdziwego renderera sprawdzony wizualnie.
Fizyczny kontroler, publiczny router/UPnP i render Linux nie były sprawdzane.
Nowa zawartość i publiczna alpha pozostają poza Fazą 0.
