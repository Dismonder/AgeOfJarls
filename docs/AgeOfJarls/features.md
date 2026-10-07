# Era Jarlów — katalog funkcji

Priorytety: **MVP** – pierwsza wersja do gry z kolegą (etapy M0–M5) · **v1** – pierwsze wydanie publiczne (M6–M10) ·
**v2** – rozwój po wydaniu · **pomysł** – do rozważenia, bez planu.
Wszystkie liczby to punkt wyjścia do balansu; docelowo siedzą w plikach JSON (zob. [architecture.md](architecture.md)).

## A. Osadnicy

### A1. Tożsamość — MVP
- Imię nordyckie z list (męskie i żeńskie), płeć, wygląd: model, fryzura, broda, kolor skóry i włosów — losowane raz
  i zapisane na stałe.
- Pochodzenie: biom, z którego osadnik został uratowany; wpływa na pulę cech i startowe umiejętności.
- Podpowiedź po najechaniu: imię, zawód, stan, ikony potrzeb. Pełna karta w oknie Stołu Jarla.

### A2. Cechy — MVP: 6 cech · v1: pełna pula
Osadnik ma 1–3 cechy, co najmniej jedną pozytywną.

| Cecha | Efekt | Typowe pochodzenie |
|---|---|---|
| Pracowity | +15% szybkości pracy | Łąki |
| Leniwy | −15% szybkości pracy, szybciej odzyskuje morale | dowolne |
| Silny | +50% udźwigu (gdy serwer ustawi `Work/CarryLimit` albo gra w trybie Realistic; w Chill domyślnie bez limitu), +10% obrażeń wręcz | Góry |
| Zręczny | +20% szybkości chodu | dowolne |
| Oszczędny | −20% zużycia jedzenia | Bagna |
| Żarłok | +30% zużycia jedzenia, +10% zdrowia | dowolne |
| Odważny | nie ucieka przy niskim zdrowiu, +morale oddziału w walce | Równiny |
| Tchórzliwy | ucieka przy 50% zdrowia, −20% skuteczności w walce | dowolne |
| Sokole Oko | +15% zasięgu i celności łuku | Czarny Las |
| Zielona Ręka | +20% plonów, szybsze sadzenie | Łąki |
| Nocny Marek | pracuje w nocy bez kary | Mgliste Ziemie |
| Zahartowany | +20% odporności na obrażenia, wolniej traci morale | Równiny |
| Weteran | startuje z wyższymi umiejętnościami bojowymi | twierdze Zwęglonych |
| Marudny (v2) | obniża morale sąsiadów | dowolne |

W MVP: Pracowity, Leniwy, Silny, Oszczędny, Żarłok, Odważny.

### A3. Umiejętności — v1
- Jedna umiejętność zawodowa (0–100) rośnie z pracą, tak jak umiejętności gracza; przyspiesza pracę i zwiększa urobek.
- Umiejętności bojowe (miecze, łuki, tarcze…) dla drużyny.

### A4. Potrzeby i stany — MVP: głód i sen · v1: reszta
- **Głód** — je z Kotła Osady. **Sen** — nocą w przydzielonym łóżku. **Bezpieczeństwo** — spada po rajdach i śmierci
  innych. **Komfort** — vanillowy poziom komfortu wokół łóżka.
- Stany: praca, bezczynność, jedzenie, sen, ucieczka, walka, ranny, podąża za graczem, w schronieniu (alarm).

### A5. Rany i śmierć — v1
- Ranny osadnik wraca do łóżka i się leczy. Śmierć jest trwała (otwarte pytanie 3), sprzęt zostaje w nagrobku.
- Tryby gry (`General/Mode`, od 0.8.0, synchronizowany z serwera):
  - **Chill** (domyślny): osadnik bez zdrowia jest powalony i po ~15 s sam wstaje (chyba że `Settlers/PermanentDeath`).
  - **Realistic**: powalony leży, dopóki gracz nie pomoże mu wstać ([E]) albo nie podejdzie inny osadnik z tej samej
    osady / idący za tym samym graczem (do 40 m, poza walką). Bez pomocy przez `Settlers/RescueMinutes` (10, zakres
    1-120; liczy się czas świata, także we śnie i gdy nikogo nie ma w pobliżu) umiera, sprzęt zostaje w nagrobku.
    Pracownicy noszą najwyżej `Work/RealisticCarryLimit` (30) przedmiotów na kurs, gdy `Work/CarryLimit` = 0.
- Gracze domyślnie nie ranią osadników (opcja w konfiguracji).

### A6. Rozkazy bezpośrednie — MVP: podążaj i czekaj · v1: szybkie menu
- Interakcja z osadnikiem: podążaj za mną, czekaj tutaj, wróć do pracy, idź do domu.
- Szybkie menu: przydział zawodu, zmiana imienia.

### A7. Rodziny — v1 (0.9.0)
Zautomatyzowany cykl życia mieszkańców: pary, ciąże i dzieci. Pary śpią we dwójkę w łóżku podwójnym.
1. **Pary:** Osadnicy dobierają się sami (szansa w nocy) albo przypisani ręcznie w oknie (zakładka Rodziny, `aoj_family couple`).
2. **Ślub:** Po zaręczynach para „spotyka się pod oknem” na chwilę (emotki, serduszka, `aoj_family court`), po czym pojawia się komunikat o ślubie i osadnicy otrzymują bonus Uczty do morale (x1.15) na 2 dni.
3. **Ciąża i narodziny:** Szansa każdego ranka (`Family/ConceiveChanceBase`). Czas ciąży to 3 dni, w tym czasie osadniczka odpoczywa i nie pracuje. Niemowlę (`aoj_family birth`) rodzi się rano obok matki i zajmuje wolne miejsce w limicie osady; bez miejsc zapada w stan czuwania.
4. **Etapy życia:** Niemowlę (2 dni, leży), dziecko (5 dni, bawi się wokół stołu, siada nocą na krawędzi łóżka rodzica), młodzieniec (7 dni, pracuje jako drwal/rolnik/górnik/tragarz w 50% tempa), dorosły. Wiek ustawia skalę modelu i cechy (wymagany osobny prefab dla dzieci wg rasy).
5. **Żałoba:** Utrata partnera daje status rozpaczy (-20% morale na 3 dni), ponowne wejście w związek resetuje licznik i wymaga upływu okresu samotności.

**Konfiguracja (`Family/`):**

| Opcja | Domyślnie | Opis |
|---|---|---|
| `Enabled` | `true` | Czy w ogóle włączać cykl rodzin w osadach |
| `Pairing` | `Auto` | `Auto` (osadnicy dobierają się sami po nocy), `Manual` (tylko zleceni graczem w GUI) |
| `ConceiveChanceBase` | `5` | Bazowa % szansa każdej płodnej pary na poczęcie każdego ranka |
| `ConceiveChanceBoost` | `3` | Dodatkowy % za wysokie średnie morale pary |
| `MaxChildrenPerCouple` | `3` | Limit dzieci żyjących jednocześnie dla jednej pary |
| `PregnancyDays` | `3` | Czas w dniach (wliczany CatchUp) |
| `MinDaysBetweenBirths` | `5` | Ile dni po narodzinach para nie wejdzie w ciążę |
| `StageInfantDays` | `2` | Dni jako niemowlę |
| `StageChildDays` | `5` | Dni jako dziecko |
| `StageYouthDays` | `7` | Dni jako młodzieniec |
| `YouthWorkPace` | `0.5` | Ułamek normalnego tempa pracy dla młodzieńca |
| `GriefDays` | `3` | Dni żałoby po śmierci członka rodziny |
| `RestDuringPregnancy`| `true` | Czy ciężarne powstrzymują się od pracy na etapie zaawansowanej ciąży |
| `MinDaysToRepair` | `5` | Ile dni od rozstania do następnego ślubu |

## B. Rekrutacja

Wszystko w tej sekcji działa także w **istniejących światach** — nic nie wymaga generowania nowego świata ani
odkrywania nowych stref.

### B1. Obozy jeńców — MVP: Łąki i Czarny Las · v1: pozostałe biomy

| Biom | Źródło | Strażnicy | Jeńcy |
|---|---|---|---|
| Łąki | rozbitkowie: po postawieniu pierwszego Stołu Jarla przypływają łodzią na najbliższy brzeg (bez walki, samouczek) | brak | 1–2, słabsze cechy |
| Czarny Las | obozy Szarokarłów | Szarokarły, szamani | 1–2 |
| Bagna | wioski Draugrów | Draugry | 1–3 |
| Góry | lodowe jaskinie | Kultyści, Ulvy | 1–2 |
| Równiny | wioski Fulingów | Fulingowie, berserkerzy | 2–3, często Zahartowani |
| Mgliste Ziemie | obozy Dvergrów („Mroczne Krasnoludy”) | Dvergrzy | 2–3 |
| Popielne Ziemie | twierdze Zwęglonych | Zwęgleni | 2–4, często Weterani |

Konkretne lokacje vanilla (nazwy prefabów) potwierdzimy w grze na etapie M5.

### B2. Uwolnienie — MVP
1. Klatka stoi w lokacji vanilla — pojawia się w nowych **i** już wygenerowanych światach: dodajemy ją, gdy lokacja
   wczytuje się w grze (raz na lokację, znacznik w zapisie), z szansą ustawianą w konfiguracji (np. 40%), żeby nie
   każdy obóz miał jeńców. W już wyczyszczonych obozach klatka pojawia się razem z nowymi strażnikami — uwolnienie
   zawsze wymaga walki.
2. Otwarcie: brak żywych strażników w promieniu ~20 m albo rozbicie zamka (wytrzymałość zależna od biomu).
3. Uwolniony osadnik idzie za graczem, unika walki i może zginąć po drodze.
4. Przy Stole Jarla: „Przyjmij do klanu” → dostaje łóżko i czeka na przydział. Brak wolnego łóżka → czeka przy stole.

### B3. Inne źródła — v2
- Ochotnicy: przy wysokiej Sławie i morale co kilka dni zjawia się wędrowiec.
- Pomysł: wymiana z handlarzami (Haldor, Hildir).

## C. Stół Jarla — MVP (pełne okno: v1)
- Budowla z młotka, jedna na osadę, minimalny odstęp między osadami 150 m.
- Wyznacza promień osady (okrąg jak przy strażniku), rosnący z poziomem.
- Liczy się jako baza gracza, więc rajdy i oblężenia mogą tu przyjść.
- Zakładki okna:
  1. **Przegląd** — poziom, populacja i limit, średnie morale, zapas jedzenia w dniach, alerty (brak łóżek, narzędzi,
     pełny magazyn), produkcja z ostatniego dnia.
  2. **Osadnicy** — lista i karty; przydział zawodu i totemu, zmiana imienia, wygnanie.
  3. **Praca** — totemy, obsada, wydajność, priorytety.
  4. **Zapasy** — zawartość połączonych magazynów, jedzenie w kotłach, bilans dnia.
  5. **Obrona** — drużyna, sztandary, alarm, historia oblężeń.
  6. **Członkowie** — gracze i szczeble (Jarl ×2, Hersir, Huskarl, Karl, Gość), przyjmowanie graczy przy stole,
     przekazanie tytułu, premia za wspólną osadę.
  7. **Kronika** — dziennik: przybycia, śmierci, oblężenia, raporty z nieobecności, kto co zmienił.
- W MVP: lista osadników, przyjmowanie, przydział, role. Resztę dokładamy w M9.
- Podniesienie poziomu: pokonany boss (klucz globalny) + surowce, w oknie stołu.

## D. Totemy Zadań i logistyka

### D1. Wspólne dla wszystkich totemów — MVP
- Budowla w osadzie albo poza nią (las, kopalnia) — do `Work/TotemReach` (domyślnie 50 m) za jej granicą; służy
  najbliższej osadzie. Promień strefy regulowany (8–30 m) z podglądem okręgu.
- Sloty na pracowników (zależne od poziomu osady), priorytet, godziny pracy (dzień / cała doba).
- Połączone magazyny: automatycznie skrzynie w strefie + ręczne dołączenie; filtr kategorii dla każdej skrzyni.
- Status w podpowiedzi: obsada x/y i ostatni problem („brak siekiery”, „magazyn pełny”, „brak drzew w strefie”).
- Rezerwacje: dwóch pracowników nigdy nie bierze tego samego drzewa, przedmiotu ani miejsca w skrzyni.

### D2. Rodzaje totemów

| Totem | Priorytet | Poziom osady | Wymaga | Co robi |
|---|---|---|---|---|
| Drwala | MVP | 0 | siekiera | ścina drzewa w strefie, rąbie kłody, zbiera i odnosi drewno; v1: „zostaw młode”, sadzenie sadzonek |
| Tragarza | MVP | 0 | — | przenosi przedmioty między magazynami według kategorii, opróżnia skrzynię wejściową, zbiera przedmioty z ziemi |
| Górnika | v1 | 2 | kilof | kopie skały i złoża (z poszanowaniem poziomu narzędzia), odnosi kamień i rudę |
| Hutnika | v1 | 2 | — | dokłada rudę i paliwo do pieców, wypalarek, wiatraka, kołowrotka i rafinerii; odbiera produkty |
| Budowniczego | v1 | 2 | młotek, materiały w magazynie | naprawia uszkodzone budowle w strefie — kluczowy przy oblężeniach |
| Rolnika | v1 | 3 | pole przygotowane kultywatorem przez gracza | zbiera dojrzałe plony, sadzi z nasion z magazynu |
| Kucharza | v1 | 3 | rożen lub ruszt, kocioł | piecze mięso, gotuje potrawy z vanillowych przepisów kotła, uzupełnia Kocioł Osady |
| Zbieracza | v2 | 1 | — | jagody, grzyby, kwiaty, osty |
| Hodowcy | v2 | 3 | — | karmi zwierzęta, pilnuje wielkości stada, ubija nadwyżkę |
| Rybaka | pomysł | — | wędka, przynęta | łowi ryby w strefie wody |

### D3. Kategorie magazynów — MVP: stała lista · v1: edytowalna w JSON
Drewno · Kamień i ruda · Metale · Jedzenie · Materiały (skóry, trofea…) · Sprzęt · Inne.
Kategorię wyznacza typ przedmiotu, a wyjątki lista w JSON.

## E. Wyżywienie, morale, zdrowie

### E1. Kocioł Osady — MVP
- Pojemnik na gotowe jedzenie (potrawy vanilla). Osadnicy jedzą 2 razy dziennie.
- Wartość posiłku = statystyki potrawy (zdrowie + wytrzymałość + eitr) → punkty sytości.
- Różnorodność: 3 różne potrawy w ostatnich posiłkach dają premię do morale (jak 3 sloty jedzenia gracza).
- Kotły w osadzie sumują się; okno stołu pokazuje, na ile dni starczy jedzenia.

### E2. Morale — MVP: jedzenie i łóżko · v1: pełny model
Skala 0–100, start 50:

| Czynnik | Zakres |
|---|---|
| Sytość | −30 … +15 |
| Różnorodność jedzenia | 0 … +10 |
| Łóżko (brak / jest) | −20 … +5 |
| Komfort wokół łóżka (vanilla) | 0 … +10 |
| Bezpieczeństwo (niedawne rajdy i śmierci) | −20 … 0 |
| Cechy | ±10 |
| Uczta (v1: przy stole, miód + potrawy) | +20 przez 2 dni |

Skutki: szybkość pracy ×0,6 … ×1,25; w walce ±15% obrażeń; poniżej 15 przez 2 dni — odmowa pracy
(dezercja tylko, jeśli włączona w konfiguracji).

### E3. Zdrowie — v1
Regeneracja w łóżku, szybsza przy wysokim komforcie. Pomysł: Uzdrowiciel z miodami.

## F. Armia i obrona — v1

### F1. Zawody bojowe

| Zawód | Poziom osady | Sprzęt | Rola |
|---|---|---|---|
| Wojownik | 1 | broń jednoręczna | uniwersalny, patrole |
| Łucznik | 2 | łuk + strzały | mury i wieże, strzela z góry |
| Tarczownik (ciężka piechota) | 3 | tarcza, broń jednoręczna, ciężki pancerz | trzyma wyłomy i bramę |
| Włócznik (v2) | 4 | włócznia | walczy zza tarczowników |
| Berserk (v2) | 5 | broń dwuręczna | szarża, duże obrażenia |

### F2. Zbrojownia
Skrzynia, z której drużyna bierze najlepszą dostępną broń, pancerz i strzały dla swojej roli. Strzały i wytrzymałość
sprzętu się zużywają. Osadnika można też wyposażyć ręcznie.

### F3. Sztandary Wojenne
- Posterunek: pozycja, promień, liczba miejsc, postawa (trzymaj pozycję / patroluj / ścigaj do X m).
- Rodzaje: **Mur** (łucznicy), **Brama/Wyłom** (piechota), **Zbiórka** (punkt zebrania drużyny),
  **Schronienie** (cywile w czasie alarmu).

### F4. Alarm
- Włącza się sam, gdy w promieniu osady startuje rajd lub oblężenie; ręcznie — Rogiem Wojennym przy stole.
- Cywile przerywają pracę i idą do Schronienia, drużyna na posterunki, gracze dostają komunikat.

### F5. Rozkazy w polu
Klawisze przy drużynie: za mną / trzymaj tutaj / atakuj cel / wróć na posterunek.

## G. Oblężenia — v1: podstawowe · v2: rozbudowane
- Rajdy vanilla działają dalej i trafiają w osadę (Stół Jarla liczy się jako baza).
- Oblężenia moda: 2–4 fale z kilku kierunków; skład zależy od biomu i siły osady (populacja, zapasy, poziom);
  jednostki oblężnicze (np. trolle, berserkerzy Fulingów).
- Tylko gdy gracz jest w osadzie — pusta osada nie jest oblegana (tak jak przy rajdach vanilla).
- Częstotliwość i siła w konfiguracji.
- Nagrody: łupy, Sława (więcej ochotników), morale za zwycięstwo. Koszty: ranni i polegli, uszkodzone budowle.
- Wyłomy: zniszczone ściany są zapamiętywane; Budowniczowie naprawiają je po walce (v1) lub w trakcie (v2).

## H. Kooperacja — MVP
- Szczeble Jarl (do 2) / Hersir / Huskarl / Karl / Gość zapisane w osadzie; każdy członek powiększa osadę.
- Gracze zarządzają jednocześnie; zmiany widzą wszyscy od razu.
- Działa na serwerze dedykowanym i przy hostowaniu z gry.
- v1: Kronika pokazuje, kto co zmienił. Pomysł: role graczy „Marszałek” (armia) i „Zarządca” (gospodarka)
  z osobnymi uprawnieniami.

## I. Progresja osady

| Poziom | Nazwa | Wymaganie | Osadnicy | Promień | Odblokowuje |
|---|---|---|---|---|---|
| 0 | Obozowisko | postawienie stołu | 3 | 30 m | Drwal, Tragarz, Kocioł Osady |
| 1 | Zagroda | Eikthyr | 6 | 35 m | Wojownik, Sztandary, Zbieracz (v2) |
| 2 | Osada | Starszy | 10 | 40 m | Górnik, Hutnik, Budowniczy, Łucznik, Zbrojownia |
| 3 | Wieś | Bonemass | 15 | 45 m | Rolnik, Kucharz, Tarczownik, Uczta |
| 4 | Gród | Moder | 20 | 50 m | pełne oblężenia, Włócznik |
| 5 | Twierdza | Yagluth | 25 | 55 m | Berserk, więcej slotów w totemach |
| 6 | Jarlostwo | Królowa | 30 | 60 m | do ustalenia |
| 7 | Era Jarlów | Fader | 30 | 60 m | do ustalenia |

Klucze globalne vanilla: `defeated_eikthyr`, `defeated_gdking`, `defeated_bonemass`, `defeated_dragon`,
`defeated_goblinking`, `defeated_queen`, `defeated_fader`.

## J. Interfejs
- Okna w stylu Valheima (Jotunn `GUIManager`); polski i angielski od pierwszego dnia.
- Podpowiedzi po najechaniu na osadnika, stół, totem, sztandar i klatkę.
- Okręgi promieni przy stawianiu i najechaniu.
- Pinezki na mapie: osada, alarm; opcjonalnie totemy.
- Komunikaty na środku ekranu: przybycie, śmierć, początek oblężenia, raport z nieobecności.
- Podpowiedzi klawiszy, np. zmiana promienia totemu.

## K. Konfiguracja i komendy
- Konfiguracja BepInEx synchronizowana z serwera, zmieniana tylko przez admina: tempo pracy, limity, częstotliwość
  oblężeń, limit nadrabiania, przyjacielski ogień, dezercja, tryb debug.
- Komendy konsoli (admin / cheaty): `aoj_spawn` (osadnik z zawodem i cechami), `aoj_info` (stan osady),
  `aoj_catchup <godziny>` (symulacja nieobecności), `aoj_siege` (start oblężenia), `aoj_morale <wartość>`,
  `aoj_debug` (cele i ścieżki AI), `aoj_reload_defs` (przeładowanie JSON).

## L. Czego mod celowo nie robi (w v1)
- Osadnicy nie wykuwają broni ani pancerzy — sprzęt tworzy gracz.
- Osadnicy nie stawiają nowych budowli, tylko naprawiają istniejące.
- Brak nowych biomów, bossów i waluty.
- Brak oblężeń między klanami graczy (PvP).
- Brak wypraw osadników bez gracza (pomysł na v2+: wyprawy łupieżcze na statkach).
