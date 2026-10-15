# Architektura grywalnego rdzenia

## Granice

`Game.Core` celuje w net8.0, bez Godota, aby działać w runtime desktopowym silnika.
SDK rozwiązania jest przypięty do .NET 10.0.400; testy celują w net10.0. Dodatkowy
`Game.Client.sln` jest wymagany przez eksporter C# Godota. W przyszłości
`Game.DirectoryApi` może niezależnie celować w net10.0.

`GameCatalog` ładuje dane i odrzuca nieprawidłowe definicje. `BuildValidator`
akceptuje tylko istniejący samochód, jedną z trzech broni głównych na poziomie 1
i pusty przydział umiejętności. `VehicleStatsCalculator` jest jedynym miejscem
wyprowadzania statystyk; klient nigdy nie deklaruje ich wartości.

## Świat i fizyka

`RaceTrack` tworzy zamknięty tor z segmentów, widoczną siatkę, kolizje, rampę,
strefę obniżonej przyczepności i 12 kierunkowych obszarów checkpointów. Objętości
Area3D pozostają w scenie; zaliczenie wymaga przecięcia płaszczyzny bramki we
właściwym kierunku, szerokości i wysokości. Test geometrii działa w `Game.Core`,
bez wczesnych kontaktów zderzaka i bez zaliczania teleportacji.
`ArcadeCar` jest RigidBody3D z czterema promieniami zawieszenia. Siły działają
w `_IntegrateForces`; ciąg, tłumienie boczne i skręt zależą od danych.
Stabilizacja oraz automatyczne odwrócenie pomagają po wybuchach.

Warstwy: 1 — tor, 2 — auta, 4 — auta chronione. Chronione auta nadal zaliczają
bramki, ale nie zderzają się z innymi pojazdami. Efekty wizualne i markery broni
są dziećmi osobnego korzenia modelu, który można przesunąć podczas korekty ruchu.

## Sesja

`Main` składa scenę i zarządza stanami Lobby → Countdown → Racing → Podium.
Świat i kod sesji są identyczne dla hosta grającego oraz headless. Host wywołuje
czystą logikę `RaceProgress`, `RaceClock` i `RaceRanking` z wejściami fizyki.
Pierwszy finisz rozpoczyna 30-sekundowe zamknięcie mety; ukończenie przez wszystkich
kończy wyścig wcześniej. Po wyścigu host może uruchomić nowy z tym samym składem.

Boty produkują `PlayerInputCommand`, podobnie jak klawiatura i pad. Nie teleportują
się do przodu w rankingu. Jeśli utkwią, wracają do ostatniej zaliczonej bramki.
Utrata klienta zamienia kierowcę w bota. Nie ma migracji hosta.

## Walka i prezentacja

Host sprawdza cooldowny, amunicję, energię, przegrzanie, raycasty, pociski, miny,
obrażenia, impulsy i respawn. Rakiety korzystają z ciągłego testu odcinka ruchu
w świecie fizyki, dzięki czemu nie przelatują przez cienkie przeszkody między tickami.
Laser trafia aktualny świat hosta; nie ma historycznego cofania trafień.

Zatwierdzone zdarzenia generują smugi, eksplozje, iskry i syntetyzowane dźwięki.
Niski stan zdrowia emituje dym. Modele i środowisko powstają z autorskiej geometrii;
gra nie korzysta z pobranych assetów ani nazw czy map referencyjnych gier.
