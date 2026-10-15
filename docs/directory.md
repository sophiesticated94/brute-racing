# Katalog quick

Katalog jest opcjonalny. Podstawowa gra, kampania i połączenie IP nie potrzebują
serwera. Nie ma wspólnego publicznego adresu wdrożonego przez ten projekt.

## Lokalnie

```powershell
./scripts/directory.ps1 -Port 5080
# w dwóch terminalach, różne porty hostowania:
./scripts/run.ps1 -Mode Quick -DirectoryUrl http://127.0.0.1:5080 -Port 28747
./scripts/run.ps1 -Mode Quick -DirectoryUrl http://127.0.0.1:5080 -Port 28748
```

Adres można wpisać w menu lub podać przez `MCM_DIRECTORY_URL` / `--directory`.
CLI: `--mode=quick --preset=1 --port=28747`. `--no-host` wyklucza klienta z
kandydatów na hosta. Quick nie tworzy botów; co najmniej dwóch ludzi musi czekać.

## Kontener

Z korzenia repozytorium:

```powershell
docker build -f src/Game.DirectoryApi/Dockerfile -t brute-racing-directory .
docker run --rm -p 5080:8080 brute-racing-directory
```

Obraz publikuje API w Release, działa jako użytkownik bez uprawnień root i nie
wymaga woluminu ani bazy. Restart usuwa kolejkę; klienci mogą wyszukać ponownie.
Publiczne wdrożenie powinno udostępnić HTTPS (np. reverse proxy) i monitoring. Nie włączono automatycznego zaufania do nagłówków forwarded;
API wybiera adres hosta na podstawie bezpośredniego połączenia. Jeśli reverse
proxy ukrywa adres nadawcy, skonfiguruj zaufane proxy przed takim wdrożeniem.
Przy lokalnym wywołaniu dopuszczony jest jawny adres LAN do testów.

## Kontrakt

| Metoda / ścieżka | Znaczenie |
|---|---|
| GET `/health` | protokół 4 i SHA-256 danych |
| GET `/address` | obserwowany adres nadawcy |
| POST `/queue` | deklaracja buildu, ID gracza, nazwa, port, hash i możliwość hostowania |
| GET `/queue/{id}` | heartbeat i stan kolejki/oferty |
| POST `/queue/{id}/ready?offer={guid}` | klient potwierdza ENet dla konkretnej oferty |
| POST `/queue/{id}/fail?offer={guid}` | ponowne zestawienie grupy |
| DELETE `/queue/{id}` | anulowanie |

Operacje biletu wymagają nagłówka `X-Queue-Secret`. Katalog sam waliduje pełny
build do 30 SP i poziomu 5 oraz liczy fitness. Różnica fitnessu jest dzielona
przez większą wartość; musi mieścić się w tolerancji obu graczy. Kolejność:
czas wejścia, następnie GUID. Tolerancja 10%, +5 punktów procentowych co 10 s,
maksymalnie 25%. Cel 4, minimum 2 po 15 s najstarszego wpisu, maksimum 8.
Oferty nie uznaje się za gotową przed potwierdzeniem wszystkich połączeń ENet.

Wagi są w `data/matchmaking/fitness.json`; zmiana danych wymaga tej samej wersji
u klientów i katalogu. TTL biletu 60 s, oferty 12 s; nieosiągalny kandydat nie
hostuje przez 30 s. Poprzednia porażka nie może anulować nowej oferty.
NAT/CGNAT bez dostępnego portu UDP nadal wymaga relaya, którego nie ma w zakresie.

Test pełnego przepływu: `scripts/quick-integration.ps1 -UnreachableHost`.
Uruchamia lokalny API i dwa prawdziwe procesy Godota, najpierw oferując celowo
nieosiągalny port, a potem sprawdza wspólną grę i identyczne wyniki.

## Konflikty i limity (Faza 0)

Aktywny ClientId zwraca **409** i `Retry-After: 60`, bez usuwania biletu ani oferty.
Zastąpienie wymaga DELETE z dotychczasowym sekretem lub wygaśnięcia. Klient quick
czeka na potwierdzenie anulowania przed kolejnym enqueue. Jeśli po restarcie nie
ma sekretu, pokazuje oczekiwanie i ponawia po wygaśnięciu starego wpisu.

Limit ogólny per IP: 120 żądań początkowo, odnawianie 20/s. Enqueue ma dodatkowo
8 żądań i odnawianie 1/s. Osiem osób za jednym NAT-em może normalnie wejść i
wysyłać heartbeat. 429 zawiera Retry-After. Kolejka ma 1024 wpisy; pełna zwraca
429 z Retry-After 5 s. Body, także chunked, ma 32 KiB, głębokość JSON 16;
przekroczenie zwraca 413 przed zmianą kolejki.

Konfiguracja przez sekcję RequestLimits w konfiguracji ASP.NET lub zmienne:
`RequestLimits__Capacity`, `RequestLimits__RefillPerSecond`,
`RequestLimits__EnqueueCapacity`, `RequestLimits__EnqueueRefillPerSecond`.
Nie ufamy X-Forwarded-For bez jawnie skonfigurowanego zaufanego proxy.

Powtórzenie kontroli HTTP: `scripts/directory-security-tests.ps1`.
Kontrola klienta, anulowania i odzyskania po utracie sekretu:
`scripts/quick-cancel-tests.ps1 -Orphan`. Wszystkie dane testowe są w artifacts.
