# Architektura

## Projekty i dane

SDK .NET 10.0.400 jest przypięty w `global.json`; Godot.NET.Sdk 4.7.0 w kliencie.
Core i klient celują w net8.0 ze względu na runtime Godota. Katalog i testy celują
w net10.0. `Game.Client.sln` służy eksporterowi Godota. Główne rozwiązanie buduje
wszystkie projekty. `data/` jest źródłem prawdy; build kopiuje JSON do klienta
oraz wyjścia/publikacji API. Walidacja startupu obejmuje liczby skończone, limity,
poziomy 1–5, 30 węzłów DAG, ekonomię, kosmetyki, fitness i pięć archetypów.

`Game.Core` nie zależy od Godota. `VehicleStatsCalculator.CalculateBuild` zwraca
auto, broń główną, miny i parametry mechanik. Kolejność to baza → wyposażenie →
suma Add → Multiply → Override → centralne ograniczenia. Efekty są stabilnie
sortowane według priorytetu i ID źródła; ostatni Override wygrywa. Efekt rangi
opisuje jej pełny wynik, nie sumuje się z poprzednimi rangami.

`SkillGraph` waliduje DAG i tworzy porządek topologiczny z rozstrzyganiem ID ordinal.
`BuildValidator` odrzuca uszkodzone deklaracje przed normalizacją. FairCap na
kopii ogranicza broń do 3, następnie odejmuje rangi od końca porządku aż do 15 SP.
OpenMayhem dopuszcza 30 SP / poziom broni 5, Stock 0 SP / 1. Budżet poziomu gracza
również obowiązuje. `LoadoutDeclaration` ma adapter starego `PlayerLoadoutDto`.

## Świat i prowadzenie

`RaceTrack` generuje autorski kanion, bandy, rampę, oznakowanie i śliską strefę.
Kierunkowe checkpointy wymagają rzeczywistego przecięcia płaszczyzny w szerokości
i wysokości bramki. Area3D zachowują geometrię, ale nie monitorują kontaktów.
`ArcadeCar` to RigidBody3D Jolt z czterema promieniami zawieszenia, sprężynami,
tłumieniem, driftem i stabilizacją. `MovementTuning` współdzieli parametry z
przybliżoną predykcją. Kamera zachowuje orientację świata, śledzi prędkość i
lekko kierunek celowania. Model wizualny ma osobny offset korekty.

Warstwy: tor 1, auta 2, auta chronione 4. Ochrona wyłącza kolizje między autami.
Boty i gracze produkują ten sam `PlayerInputCommand`. Boty mają pięć poprawnych
archetypów; reguły lobby normalizują ich buildy. Kampania resetuje je do bazy,
a potem boty kupują rozwój zgodnie z wybraną specjalizacją.

## Sesja i walka

`Main` składa scenę. `SessionLifecycle` jawnie śledzi Menu, Lobby, Countdown,
Racing, Podium, Intermission, CampaignComplete i Disconnected. Dotychczasowy
RacePhase pozostaje bez zmian dla zegara/snapshotów. GameplayInputGate działa
przed odczytem źródła i przed zastosowaniem komendy, z blokadą do zwolnienia
przycisków. Otwarcie panelu i utrata fokusu blokują także kliknięcia między tickami. Części klasy
wydzielają sieć, walkę, mechaniki, wejście, celowanie, prezentację, walidację,
progresję i quick. Ten sam kod obsługuje solo, listen host i serwer headless.
Pierwsza meta uruchamia limit 30 s; pełny finisz kończy rundę wcześniej.

Host rozlicza zasoby, obrażenia, cooldowny, impulsy, respawn i perki. Rakiety
poruszają się w świecie 3D i testują cały odcinek ticku; pocisk przechowuje
niezmienną definicję buildu z chwili wystrzelenia. Laser i karabin używają raycastów.
Miny mają czas uzbrojenia i limit aktywnych sztuk; EMP spowalnia oraz zakłóca
wieżyczkę. Mechaniki DAG obejmują impuls po drifcie, radiatory, odzysk amunicji,
regenerację i awaryjną minę. Potwierdzone zdarzenia tworzą efekty/dźwięk, znaczniki
trafień i liczby rzeczywiście odjętych HP. Assety i dźwięki są autorskie.

## Kampania i profil

`CampaignState` przechowuje autorytatywny XP, poziom, własność wyposażenia, kredyty,
SP i punkty mistrzostw każdego uczestnika. Klient wysyła intencję zakupu/rangi,
a nie dowód własności. Ekonomia pochodzi z JSON: pierwszy zakup po pierwszej
rundzie; XP skaluje się z długością kampanii. Nowa kampania tworzy nowy GUID i
resetuje wszystkie parametry mechaniczne. Rozłączonego gracza przejmuje bot.

`CampaignState.Participants` jest indeksowany trwałymi GUID. Mapa int → GUID jest
adapterem bieżących PeerId i botów, nie trafia do zapisu. CampaignSnapshot ma
schemat 1, revision, hash reguł/danych, rozwój, własność, rundy, identyfikatory
operacji, poufne tokeny i trwałą kolejkę nagród. CampaignRepository zapisuje osobne
pliki przez flush i atomowe zastąpienie z `.bak`. DurableCampaign publikuje kopię
stanu dopiero po zapisie. Nieudany wynik pozostaje oczekujący na retry; nieudany
zakup nie zmienia zatwierdzonego stanu. Main.Persistence łączy te kontrakty z UI,
autoryzacją powrotu i dostarczaniem nagród. Przy wznowieniu odtwarza garaż,
nie stan fizyki przerwanej rundy.

`PlayerProfile` oddziela monety i kosmetyki od rozwoju kampanii. Ma trzy sloty
archiwum oraz oczekujący końcowy build do świadomego zapisania w slocie. `ProfileRules`
deduplikuje nagrody według GUID wyścigu/kampanii. `ProfileRepository` zapisuje
atomowo z flush i kopią, a `ProfileCodec` waliduje wersję oraz SHA-256. Nowszy
schemat jest tylko do odczytu; uszkodzony plik jest zachowany. Suma wykrywa
uszkodzenia, nie jest zabezpieczeniem antycheat.

## Opcjonalny katalog

`Game.DirectoryApi` korzysta z tego samego Core i danych. `MatchQueue` przechowuje
bilety, sekrety, oferty i heartbeat w pamięci pod blokadą. `TimeProvider` umożliwia
testy terminów. Fitness powstaje z końcowych statystyk i perków, z jawnych wag.
Katalog wybiera kandydata na hosta; Godot zestawia prawdziwy ENet i wszyscy
potwierdzają gotowość oferty, zanim host dostanie zgodę na start. Błędy odnoszą
się do GUID oferty, więc stara odpowiedź nie anuluje kolejnej próby.
Nie ma bazy, kont, relaya ani domyślnego publicznego wdrożenia.

Schemat profilu 2 dodaje słownik tokenów kampanii. Dekoder v1 używa oryginalnego
kontraktu do weryfikacji SHA-256 przed migracją. Nagroda jest stosowana na kopii
profilu i potwierdzana hostowi dopiero po zapisie. Smoke domyślnie nie otwiera
repozytoriów profilu/kampanii; testy trwałości podają izolowane ścieżki.

Directory API nie zastępuje aktywnego ClientId bez sekretu: zwraca 409. Middleware
ogranicza IP kubełkami tokenów, body do 32 KiB i JSON do 16 poziomów przed
modyfikacją kolejki. Limity oraz wyjątki pojemności są opisane w directory.md.
