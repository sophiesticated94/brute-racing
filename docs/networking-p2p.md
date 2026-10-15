# Host-authoritative ENet

## Transport i protokół

`INetworkTransport` izoluje ENet od sesji. Gospodarz ma PeerId 1. Odbiorca odczytuje
nadawcę przed pobraniem pakietu. Sygnały połączenia są kolejkowane i dostarczane
po wyjściu z natywnego `Poll`, aby zamknięcie sesji nie usuwało aktywnego obiektu.

| Kanał | Zawartość | Tryb |
|---|---|---|
| 0 | hello, lobby, start, zdarzenia, końcowy snapshot, podium | Reliable |
| 1 | do trzech ostatnich poleceń wejścia, 60 Hz | UnreliableOrdered |
| 2 | stan do ośmiu aut, 30 Hz | UnreliableOrdered |

Pakiet rozpoczyna jednobajtowy typ. Ruch używa jawnego formatu binarnego
little-endian; rzadkie komunikaty i zdarzenia używają JSON. Snapshot ośmiu aut
mieści się w 1200 bajtach. Dekodery ograniczają długość, liczebność i zakresy.
Pętla używa małych buforów i list o ograniczonej historii; obecny kodek nadal
alokuje pakiety i nie jest implementacją całkowicie bez alokacji.

Handshake **wersji 3** zawiera wersję protokołu, SHA-256 zestawu plików JSON bez różnic formatowania
danych, nazwę, kolor i deklarację buildu. Host sprawdza build i sam liczy statystyki.
Nie przyjmuje od klienta pozycji, obrażeń, okrążeń ani wyników. Peer wejścia jest
ustalany przez transport. Duplikaty i stare sekwencje są odrzucane; utrata wejścia
na ponad 150 ms neutralizuje gaz i ogień. Nadmiar komend nie dodaje kroków fizyki.

## Predykcja i interpolacja

Klient symuluje własne auto Jolt natychmiast. Snapshot zawiera numer ostatniej
komendy **wykonanej przez fizykę**, nie tylko odebranej. Klient usuwa potwierdzone
wejścia i odtwarza pozostałe w przybliżonym modelu zawieszenia, sił i skrętu.
Korekta zmienia stan fizyki. Dla błędów poniżej 3,5 m przesunięcie modelu zachowuje
ciągłość i zanika wykładniczo ze stałą 100 ms. Większe błędy i respawn zerują
offset, oczekujące wejścia oraz historię interpolacji. Raport dodaje RTT, jitter,
wiek snapshotu, średnią/maksymalną korektę i liczbę twardych korekt.

To nie jest deterministyczny rollback całego Jolta. Kolizje z innymi autami
i wybuchy mogą wymagać widocznej korekty. Auta zdalne odtwarzają snapshoty
z buforem 100 ms; historia jest ograniczona. Nie synchronizujemy drzewa scen.

Każdy wyścig ma osobny RaceId w starcie, snapshotach i zdarzeniach. Opóźnione
pakiety poprzedniej rundy nie mogą nadpisać rewanżu. Zdarzenia mają monotoniczny
EventId i są deduplikowane. Końcowy snapshot jest niezawodny i poprzedza podium.

## Połączenia

Bezpośredni IP/LAN działa bez backendu. UPnP jest opcjonalną, asynchroniczną próbą
mapowania UDP na godzinę; nie gwarantuje połączenia za CGNAT. Użytkownik może
wybrać port i skonfigurować router. Kod połączenia przenosi IP:Port z sumą kontrolną i ma przyciski kopiowania/wklejania.
Opcjonalny katalog quick jest opisany w `directory.md`; relay/Steam pozostają
osobnymi funkcjami.

Testy `scripts/integration.ps1` uruchamiają oddzielne procesy, porównują wyniki,
zdarzenia i rewanże. Opcje `--latency=50 --loss=0.03` działają w warstwie wysyłania;
opóźniają także zdarzenia niezawodne, a utratę symulują dla ruchu i wejścia.

## Rozwój i oferty quick

Kanał 0 przenosi także reguły balansu, efektywne buildy, stan kampanii, zakupy,
GUID rund/kampanii oraz potwierdzone nagrody. Każda zmiana buildu lub reguł cofa
gotowość. Host liczy statystyki i kontroluje własność w kampanii. Klient nie może
zatwierdzić trafienia, pozycji, okrążenia, zakupu ani nagrody. Protokół kontrolny
ma limit 32 KiB; binarne snapshoty ośmiu aut nadal mieszczą się w 1200 bajtach.

Quick zamraża build zadeklarowany w kolejce. Hello zawiera GUID oferty i biletu;
host sprawdza skład oraz dokładny build z oferty. Bilet ma losowy sekret HTTP,
który nie jest przesyłany pozostałym graczom. Oferta wymaga potwierdzenia ENet
przez wszystkich uczestników. Timeout cofa ofertę, blokuje poprzedniego hosta
na 30 s i szuka kolejnego. Wpis wygasa po 60 s bez heartbeat, oferta po 12 s.
Błąd zawiera identyfikator konkretnej oferty. Katalog nie tuneluje UDP.

Testowe opóźnienie i strata pakietów są iniekcją w transport gry. Nie symulują
awarii routera ani utraty niezawodnych datagramów ENet na poziomie systemu.
