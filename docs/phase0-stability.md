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
