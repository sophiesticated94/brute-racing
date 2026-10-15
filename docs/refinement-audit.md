# Audyt rozwoju — 15.10.2026

## Stan wyjściowy

Jolt jest autorytetem fizyki hosta. Klient prowadzi własne RigidBody3D, usuwa
potwierdzone komendy i odtwarza pozostałe w przybliżonym modelu Game.Core.
Stałe tłumienia i sterowania były powielone; korekta zachowywała jedynie
przesunięcie modelu, bez jawnego resetu historii przy respawnie. Interpolacja
pozostałych aut ma bufor 100 ms. Nie jest to rollback całego świata Jolt.

ENet przenosi binarne wejścia i snapshoty oraz niezawodne zdarzenia JSON.
Peer nadawcy pochodzi z transportu. Host odrzuca pozycje i obrażenia od klienta,
waliduje prosty build poziomu 1 i sam pobiera statystyki z katalogu. Hash danych
chroni zgodność wersji; nie potwierdza historii zdobycia lokalnego wyposażenia.

## Wpięcie kolejnych systemów

1. Wspólne MovementTuning, kontrolowane wygaszanie reprezentacji wizualnej,
   diagnostyka RTT/jitter i osobna prezentacja walki. Main zostaje podzielony
   na części sesji, sieci, symulacji i prezentacji bez przepisywania sceny.
2. Każdy poziom broni jest pełną definicją JSON. Pocisk przechowuje obliczone
   parametry z chwili wystrzału. Jedyny kalkulator domenowy obsługuje wyposażenie
   i późniejsze efekty DAG; Jolt nie liczy ekonomii ani modyfikatorów.
3. Graf ładowany z JSON, walidacja i normalizacja FairCap w Game.Core.
   Klient przesyła deklarację, host publikuje efektywny build przed gotowością.
4. Host prowadzi osobną, resetowaną kampanię; profil lokalny przechowuje monety
   kosmetyczne i archiwalne buildy do quick. Zapis atomowy, backup i trwałe
   identyfikatory przyznanych nagród zapobiegają ich podwajaniu.
5. Opcjonalny katalog ASP.NET Core działa w pamięci, bez bazy. Oblicza fitness
   i kojarzy graczy, lecz rozgrywka nadal używa ENet. Kampania po IP nie wymaga API.

## Zgodność

Dotychczasowe flagi CLI i skrypty pozostają działające. Smoke używa stanu
w pamięci; --rounds zachowuje znaczenie rewanżów. Nowe tryby otrzymują osobne
argumenty. Po każdym etapie zapisujemy wynik pełnego zestawu kontroli.

## Etap P0 — wynik
Build bez błędów; 24 testy xUnit; impaired/combat PASS; przejęcie AI i utrata hosta PASS; smoke 8 aut PASS. Render sprawdzony lokalnie. Raporty: artifacts/p0-*.

## Etap uzbrojenia — wynik
27 testów xUnit, pełny zestaw verify-stage weapons PASS. Dodatkowy smoke poziomu 5: salwy i 10 pól EMP. Utrata hosta sprawdzana na logu UTF-8, niezależnie od kodowania konsoli.

## Etap DAG i walidacji — wynik
32 testy xUnit; verify-stage skills PASS, w tym pięć odrzuconych sfałszowanych deklaracji na klienta ENet. FairCap testowany jako kopia z poprawnymi zależnościami.

## Etap 4 — kampania i profil

38 testów domeny/profilu oraz pełna weryfikacja `verify-stage.ps1 -Stage progression`: build, ENet impaired/combat, rozłączenia i 8 aut headless — PASS. Dodatkowy `integration.ps1 -Campaign -Impaired -Combat` ukończył 3 rundy w oddzielnych procesach z tym samym stanem kampanii i nagrodami; każdy profil ma 3 wyścigi i 1 kampanię. Profile testowe są izolowane w artifacts. Sprawdzono render garażu (pan/zoom DAG, panel statystyk, archiwum i kosmetyka).
