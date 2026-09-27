# Era Jarlów — plan prac

## Zasady
- **Najpierw ryzyko.** Najtrudniejsza i najmniej pewna rzecz — ludzki NPC — idzie na początek.
- **Pionowy wycinek.** Po M4 działa mała, ale pełna pętla: osadnik → praca → jedzenie → nadrabianie.
- **Każdy etap grywalny.** Po każdym etapie mod działa i przechodzi scenariusz odbioru w grze; wtedy commit i tag
  `aoj-mX`.
- **Co-op w każdym etapie** od M2: odbiór obejmuje grę z drugim graczem.
- **Rozmiary** S < M < L to względna wielkość pracy, nie czas.

## Przegląd

| Etap | Nazwa | Rozmiar | Wynik |
|---|---|---|---|
| M0 | Fundament | S | szkielet moda, konfiguracja, tłumaczenia, komendy, definicje JSON |
| M1 | Osadnik (spike) | L | ludzki NPC: wygląd, imię, ruch, walka, podążanie, zapis, widoczny w co-op — **kod gotowy, czeka na test w grze** |
| M2 | Stół Jarla | M | osada, promień, przyjmowanie, łóżka, role, proste okno, poziomy 0–1 |
| M3 | Pierwsza praca | L | Totem Drwala i Tragarza, magazyny, Kocioł Osady, głód, sen, proste morale |
| M4 | Nadrabianie | M | symulacja nieobecności, pula zasobów, raport |
| | *pionowy wycinek* | | *pełna pętla, osadnicy z komendy* |
| M5 | Rekrutacja | M | klatki w obozach (nowe i istniejące światy), uwalnianie, pochodzenie i cechy |
| | **MVP** | | **pierwsza wersja do gry z kolegą** |
| M6 | Gospodarka | L | Górnik, Hutnik, Budowniczy, Rolnik, Kucharz; umiejętności, pełne morale, Uczta, poziomy 2–3 |
| M7 | Armia | L | zawody bojowe, Zbrojownia, Sztandary, alarm, rozkazy, rany i śmierć |
| M8 | Oblężenia | L | fale, skalowanie, wyłomy, nagrody, Sława, obozy w pozostałych biomach, poziomy 4–7 |
| M9 | Pełne okno | M | wszystkie zakładki stołu, Kronika, mapa, komunikaty, panel totemu |
| M10 | Wydanie | M | balans, testy zgodności, README, ikona, paczka Thunderstore |

## Etapy

### M0 — Fundament (S) — kod gotowy, czeka na test w grze
- `tools/new-mod.ps1 AgeOfJarls -Jotunn`, katalogi według [architecture.md](architecture.md) §2.
- `AoJConfig`, `Keys`, `Log`, `ModPaths`.
- Tłumaczenia PL/EN przez `LocalizationManager`.
- `DefsRegistry` + `DefsValidator` (Newtonsoft.Json) i domyślne `traits.json`, `names.json`, `tiers.json`, kopiowane do
  `BepInEx/config/AgeOfJarls/` przy pierwszym starcie.
- `DefsSync`: w co-op definicje serwera trafiają do klienta przy logowaniu; hash w `aoj_version` do porównania.
- Komendy `aoj_version`, `aoj_defs`, `aoj_reload_defs`.

**Odbiór (na istniejącym świecie):** mod ładuje się bez błędów; komendy działają; zmiana w JSON + `aoj_reload_defs`
zmienia dane w grze; w co-op obaj gracze widzą ten sam hash w `aoj_version`.

### M1 — Osadnik (L, największe ryzyko)
- **M1a — kod gotowy, czeka na test w grze:** prefab `AoJ_Settler` z modelu gracza (Humanoid + vanillowy
  MonsterAI, warstwa `character`); tożsamość (imię, płeć, wygląd, cechy, pochodzenie) losowana przez właściciela
  i zapisana w ZDO; imię przez `ZDOVars.s_overrideHoverName`; efekty cech: zdrowie i próg ucieczki; konfiguracja
  `[Settlers]`; komendy `aoj_spawn [ile] [broń]`, `aoj_settlers`.
- **M1b — kod gotowy, czeka na test w grze:** ekwipunek w ZDO (zapis zbiorczy co ≤ 1 s, licznik rewizji,
  nowy właściciel wczytuje go przed zapisem, nieczytelnych danych nigdy nie nadpisuje); dawanie przedmiotów
  (użycie 1–8 na osadniku; czego nie uniesie, ląduje u jego stóp); [E] podążaj / czekaj (stan w ZDO, RPC niesie
  docelowy stan, a nie przełącznik); [Shift+E] oddanie ekwipunku; upuszczenie ekwipunku po śmierci; ochrona przed
  przyjacielskim ogniem (`Settlers.FriendlyFire`); RPC przekazywane dalej, gdy właściciel zmienił się w locie.
- **M1c — kod gotowy:** statyczna weryfikacja prefabu (metody `Awake` komponentów `Character`, `Humanoid`,
  `CharacterAnimEvent` i `Attack` — rzutowania na `Player` zabezpieczone); cofnięcie modyfikatorów świata
  dla wrogów (rozmiar/szybkość, poziom świata), które gra nakłada na każdą postać niebędącą graczem;
  regeneracja zdrowia przez vanillowe `BaseAI` z `Settlers.FullHealSeconds`.
- **Test w grze 27.09 (świat `testo`):** działa prefab z modelu gracza, imię i pasek zdrowia, broń z `aoj_spawn`,
  podpowiedź PL, efekt cechy (Żarłoczny → 110 HP), „Chodź za mną”, brak błędów moda w logu. Znalezione i poprawione:
  łysi osadnicy, męskie formy cech u kobiet, krążenie i ignorowanie rozkazów w kręgu startowym (brak oswojenia),
  „Czekaj” kręcące w promieniu 5 m, dryfowanie oswojonych bez punktu patrolu.
- **Drugi test 27.09 (wersja 17:45):** włosy, żeńskie formy cech i spokój w kręgu startowym (oswojenie) działają;
  **walka działa** — gracz zaatakował pasywnego Szarokarła, osadniczki dołączyły i go dobiły (świat `testo` ma
  pasywne potwory, więc same nie zaczynają walk). Znaleziony błąd: pochodnia z zestawu startowego prefabu gracza
  dawana każdemu osadnikowi przy każdym wczytaniu — poprawione, czeka na wdrożenie. „Chodź za mną” i „Czekaj”
  (trzyma miejsce) działają. Do sprawdzenia: dawanie przedmiotów, Stół Jarla, zapis między sesjami.
- **Prośby z testu:** osadnicy mają zbierać łupy → `SettlerAI : MonsterAI` z pierwszym zachowaniem
  `LootCollector` (osadnik idący za graczem zbiera łup — tylko przedmioty nigdy nietrzymane przez gracza,
  `m_pickedUp == false` — i oddaje go graczowi; `Settlers.CollectLoot`, `Settlers.LootRange`); więcej opcji
  zarządzania i UI → **okno osadnika i okno Stołu Jarla przesunięte przed M3** (dotąd M2c-2/M9).
- **Okno osadnika — kod gotowy, niewdrożony:** [E] otwiera okno (Jotunn `GUIManager`, drewniany panel): cechy,
  zdrowie, pochodzenie, osada, rozkaz, broń, tarcza, liczba przedmiotów — wszystko z ZDO, więc gracz, który nie
  symuluje osadnika, widzi to samo; przyciski: Chodź za mną/Czekaj, Idź do domu (punkt patrolu przy stole),
  Oddaj ekwipunek, Zmień imię (oczyszczone, zapis w tożsamości i `s_overrideHoverName`), Zamknij; [Shift+E] = szybkie
  podążanie. „Idź do domu” i zmiana imienia mieszkańca wymagają roli Jarl/Hersir (sprawdza właściciel).
  Następne: okno Stołu Jarla.
- **Decyzja:** `SettlerAI : MonsterAI` (mózg) i `aoj_debug` przechodzą do M3. W M1–M2 vanillowe `MonsterAI` już
  robi wszystko, czego trzeba: bezczynność, podążanie, czekanie w punkcie patrolu, walkę, ucieczkę i leczenie.

**Odbiór:**
1. `aoj_spawn` tworzy osadnika z losowym wyglądem i imieniem; po relogu wygląda i nazywa się tak samo.
2. Zakłada dany mu miecz i pokonuje Szarokarła.
3. Podąża za graczem i czeka na rozkaz.
4. Drugi gracz widzi to samo: imię, wygląd, ruch, walkę.

**Plan B:** jeśli klon modelu gracza zawiedzie — baza z Dvergra (gotowy humanoidalny NPC) z podmienionym wyglądem.

### M2 — Stół Jarla (M)
- **M2a — kod gotowy, niewdrożony:** budowla `AoJ_JarlTable` (młotek → Misc, przy warsztacie; 20 drewna,
  10 kamienia, 4 skóry jelenia) z modelu stołu vanilla + okrąg promienia ze Strażnika + obszar „baza gracza”
  z Warsztatu; osada zakładana przy postawieniu (budowniczy = Jarl); `SettlementData` w ZDO stołu; akcje przez RPC,
  a właściciel sprawdza rolę nadawcy ustaloną przez sieć, nie przez treść paczki; Hersir nadawany i odbierany przez
  Jarla (`aoj_hersir <gracz>`); `aoj_settlement`; podpowiedź z nazwą, rolami, poziomem i promieniem (cache wg
  rewizji ZDO); blokada drugiego stołu bliżej niż `Settlement.MinDistance`; rozebrać stół może tylko Jarl.
- **M2b — kod gotowy, niewdrożony:** lista mieszkańców w danych stołu (format v2, v1 nadal czytany);
  [E] przy stole (Jarl/Hersir) przyjmuje osadników, którzy idą za graczem i są w obrębie osady, do limitu z poziomu
  (`tiers.json` → `maxSettlers`); osadnik sam przyjmuje dom, gdy zobaczy się na liście (bez osobnego RPC, więc działa
  też po zmianie właściciela), i rezygnuje z niego, gdy zniknie z listy albo gdy stół przez 10 s nie istnieje tam,
  gdzie musiałby być wczytany; po śmierci osadnik zgłasza się do stołu (RPC do wszystkich, gdy stół nie jest wczytany
  lokalnie); podpowiedzi stołu i osadnika pokazują osadę.
- **M2b-2 — kod gotowy, niewdrożony:** przydział łóżek co 10 s u właściciela stołu (wolne łóżka vanilla w promieniu,
  nigdy łóżko gracza; przydział zostaje, gdy łóżko jest tylko niewczytane; format danych v3); pętla serwera co 60 s
  (`SettlementServer`, skan ZDO rozłożony na klatki jak łączenie portali) usuwa z list osadników, których ZDO już
  nie istnieje; patche na `ZNet.Awake` z wysokim priorytetem.
- **M2c-1 — kod gotowy, niewdrożony:** rozbudowa osady [Shift+E] przy stole (Jarl/Hersir; wymaga klucza bossa
  i surowców z `tiers.json` → `cost`, płaci gracz, który rozbudowuje; właściciel stołu jeszcze raz sprawdza rolę,
  kolejność poziomów i klucz); podpowiedź pokazuje wymagania następnego poziomu; `aoj_jarl <gracz>` (przekazanie
  tytułu, stary Jarl zostaje Hersirem); `aoj_rename [nazwa]` (bez znaczników i tokenów, max 32 znaki);
  synchronizacja domyślnych plików JSON (nieedytowana kopia dostaje nowe wartości, edytowana zostaje, a obok
  powstaje `*.default.json`).
- **M2c-2 — kod gotowy, niewdrożony:** okno Stołu Jarla ([E]): podsumowanie, lista mieszkańców (* = ma łóżko);
  Jarl/Hersir: przyjmij idących za tobą, rozbuduj, zmień nazwę; Jarl: mianuj/odbierz Hersira najbliższemu graczowi
  do 20 m; Gość widzi tylko podgląd. [Shift+E] = szybkie przyjęcie idących za tobą.
- **Stałe identyfikatory — naprawione i sprawdzone w grze (2026-09-27):** gra nadaje każdemu ZDO nowy ZDOID przy
  każdym wczytaniu świata (`ZDO.Load` → `m_uid.SetID(++ZDOID.m_loadID)`), więc po restarcie osadnicy tracili dom,
  a serwer wypisywał ich z listy. Teraz stół ma `aoj_settlement_id`, osadnik `aoj_settler_uid` (losowe 64 bity,
  nadawane też starym obiektom przez ich właściciela), dom osadnika to id osady, lista osady trzyma UID, łóżka
  pozycję; format danych v4 (v2–v3 odczytane, wpisy listy odrzucone z ostrzeżeniem — trzeba przyjąć ponownie).
  Test: przyjęcie 2 osadników → łóżka 2/2 → zapis i restart gry → osada, dom i łóżka zostały, serwer nic nie usunął.
- **Koło rozkazów — sprawdzone w grze:** grupa „Rozkazy” w kole gry (G), obok emotek: Za mną!, Czekać tutaj,
  Atak! (cel pod celownikiem lub najbliższy w stożku 25°), Odwrót! (8 s bez walki, do gracza), Do domu; koło
  otwarte przy celowaniu w osadnika wydaje rozkazy tylko jemu (+ Szczegóły → okno osadnika); gracz robi pasujący
  gest (`Commands.Gestures`), „Za mną!” łapie wolnych osadników do `Commands.FollowMeRange` m.
- **Koło rozkazów z daleka — kod gotowy, niesprawdzony w grze:** własny klawisz `Commands.WheelKey` (H, przycisk
  Jotunn → `Player.CheckKeyboardRadialPressed`, trzymanie/puszczenie i ponowne wciśnięcie jak G przez
  `RadialConfigHelper.SetItemInteractionControls`). H: osadnik, na którego patrzysz, do `Commands.LookRange` (40 m) —
  celownik gry (`GetHoverCreature`, ściany zasłaniają) albo stożek 6° z linią wzroku; rozkazy: Do mnie!, Zostań!,
  Atak! (najbliższy wróg przy nim, zwierzęta na końcu), Odwrót!, Do domu, Szczegóły (okno otwarte z daleka nie
  zamyka się od razu). H gdzie indziej: rozkazy drużyny, cel ataku = stworzenie na celowniku (stożek 20°, linia
  wzroku). G: rozkazy osadnika przy celowniku na nim z daleka (+ Wstecz do koła gry). Uprawnienia sprawdzane lokalnie
  jak u właściciela (atak: przywódca albo Huskarl+; do domu: przywódca albo Karl+; stół niezaładowany → decyduje
  właściciel).
- **0.3.0 — wspólna osada kilku graczy, kod gotowy, niesprawdzony w grze:** szczeble Gość < Karl < Huskarl < Hersir <
  Jarl (format osady v5, v4 czytany: Hersir 1→3, Jarl 2→4), do `Settlement.MaxJarls` (2) równorzędnych Jarlów, starszy
  (pierwszy na liście) jako jedyny degraduje drugiego; przekazanie tytułu zamienia miejsca na liście. Uprawnienia w
  jednej tabeli `Settlement/Permissions.cs` (Live = Karl: przyjmowanie swoich osadników, codzienne rozkazy, uczty, pinezka;
  Military = Huskarl: alarm, role bojowe, sztandary, rozkazy bojowe; Manage = Hersir: praca, totemy, wypędzanie, poziom,
  nazwa, szczeble Karl/Huskarl; Rule = Jarl: rozbiórka stołu, Hersirowie, współjarl). Reguły szczebli w
  `SettlementData.RankChangeProblem` (te same u pytającego i u właściciela stołu). Skalowanie od liczby członków
  (`JarlTable.ExtraMembers`, do `MaxScaledMembers`): mieszkańcy ×(1 + `MemberBonus`·n), promień +`MemberRadiusBonus`·n
  (maks. 80 m, stół nowej osady musi zmieścić się obok powiększonej), +`TotemSlotsPerMember`·n miejsc przy totemie,
  oblężenie ×(1 + `Sieges.PerPlayer`·(gracze w domu − 1)). Zakładka „Członkowie” w oknie stołu (Wyżej/Niżej/Jarl albo
  Przekaż/Usuń albo Odejdź, przyjmowanie graczy stojących przy stole jako Karl albo współjarl), konsola `aoj_rank`.
  Przy okazji: kronika tłumaczy teraz słowa-tokeny (nazwa poziomu, szczebel).
- **M2d — życie w osadzie, kod gotowy, niewdrożony** (zgłoszenie z testu: „łóżek sobie nie przypisali, tylko
  czekają”; „mają odkładać rzeczy do posegregowanych skrzyń”):
  - przydzielone łóżko widać na łóżku („Łóżko osadnika: Tove”) i w oknie osadnika; gracz nadal może je zająć
    (osadnik dostaje wtedy inne wolne);
  - rytm dnia: osadnik bez rozkazów w dzień krąży przy Stole Jarla (sala osady), nocą przy swoim łóżku, a nie tam,
    gdzie go przyjęto; z dalszej odległości wraca własną ścieżką (z drzwiami); zasięg spaceru w konfiguracji
    `Settlers.WanderRange` (synchronizowanej);
  - nocą idzie spać do łóżka (animacja łóżka gracza, widoczna u drugiego gracza); łóżko za zamkniętymi drzwiami:
    po 20 s prób, z odległości ≤ 10 m i tak się kładzie; wstaje rano, do walki i na rozkaz;
  - łupy: z osadą zatrzymuje je i odnosi do skrzyń osady, bez osady oddaje graczowi jak dotąd; w dzień zbiera też
    łupy leżące w osadzie (tylko te, dla których jest skrzynia);
  - sortowanie (`SettlementStorage`): skrzynia z tym samym przedmiotem → z tym samym rodzajem (drewno, kamień, rudy,
    metale, skóry, surowe mięso, nasiona, kosztowności, trofea, jedzenie, ekwipunek) → pusta skrzynia; nigdy do
    skrzyni z innymi rzeczami; skrzynie „czyste” przed mieszanymi, bliższe przed dalszymi; pusta skrzynia przyjmuje
    jeden rodzaj na raz; tylko publiczne skrzynie-budowle (bez wozów, statków, skrzyń osobistych, Niszczarki);
  - co-op: skrzynię zapisuje tylko jej właściciel — osadnik prosi go o nią RPC (jak otwarcie skrzyni przez gracza;
    otwartej nikt nie dostaje), a ekwipunek osadnika zapisuje się w tej samej klatce, co ruch przedmiotu;
  - własna ścieżka (`PathMover`), bo wspólna ścieżka BaseAI była nadpisywana przez ruch vanilla co klatkę;
  - drzwi: gdy ścieżka kończy się przed celem (albo osadnik utknął), osadnik szuka zamkniętych drzwi przy celu lub
    przy sobie, podchodzi, otwiera je jak gracz (RPC `UseDoor` u właściciela drzwi), czeka aż navmesh się odświeży
    (do 7 s), przechodzi i zamyka je za sobą; drzwi na klucz, nie do zamknięcia i w cudzym zasięgu Strażnika zostają
    zamknięte; nieprzydatne drzwi pomija przez minutę;
  - rodzaje przedmiotów do sortowania w `storage.json` (definicje synchronizowane z serwera, protokół v2) — można
    dopisać przedmioty z innych modów.


**Odbiór:** dwóch graczy zarządza tą samą osadą; po restarcie serwera dane są nietknięte; Gość niczego nie zmieni.

**Przygotowanie:** Valheim Dedicated Server (darmowe narzędzie w Steam) z BepInEx i Jotunnem do testów.

### Stan M3–M10 (2026-09-27, wieczór) — kod napisany, kompiluje się 0/0, NIEPRZETESTOWANY w grze
Na prośbę gracza („dokończ pisanie kodu, zrób wszystkie etapy”) etapy M3–M10 zostały zaimplementowane bez testów
w grze; wersja 0.2.0, paczka `dist/AgeOfJarls-0.2.0.zip` (niewysłana).
- **M3:** `WorkTotem` (budowle `AoJ_Totem_<Zawód>` ze sztandarów vanilla, okrąg strefy 8–30 m, godziny dzień/doba,
  stałe id, sloty z poziomu osady), przydział po stronie osadnika (`aoj_settler_job`), okno totemu, `WorkScanner`
  (skan strefy z cache 5 s), `Reservations`, `HarvestJob` → Drwal (prawdziwe zamachy siekierą: `Humanoid.StartAttack`,
  najpierw kłody, potem drzewa, zbieranie drewna), Tragarz (łupy ze strefy + skrzynia wejściowa ≤ 4 m od totemu),
  Kocioł Osady (`AoJ_Cauldron`: model kotła bez stacji + skrzynka), głód/sytość i morale (`Needs`), jedzenie z kotła,
  jedzenie trafia do kotła przy odkładaniu, narzędzia i zapasy zawodu zostają w torbie, ochrona budowli przed ciosami
  osadników (`StructureGuard`).
- **M4:** `SettlementSim` — zegar osady w ZDO stołu, po powrocie nadrabianie (Drwal → Drewno, Górnik → Kamień) wg
  zmierzonego tempa osadnika lub domyślnego, z limitem puli drzew/skał, wolnego miejsca i jedzenia; karmienie z kotłów;
  raport (kronika + komunikat); `aoj_catchup <godziny>`.
- **M5:** `CaptiveCamps` (postfix `LocationProxy.SpawnLocation`, decyzja raz na lokację w jej ZDO, lista nazw
  w konfiguracji, jeniec + 2 strażnicy z biomu; uwolnienie [E] gdy strażnicy nie żyją) — bez modelu klatki;
  `Castaways` (raz na świat po pierwszym stole, najbliższy brzeg, pinezka); `aoj_captive`.
- **M6:** Górnik (kilof, poziom narzędzia), Hutnik (RPC `AddOre`/`AddFuel` pieców i innych `Smelter`), Budowniczy
  (`WearNTear.Repair`), Rolnik (zbiór `Pickable` + ponowne sadzenie z mapy roślin i nasion ze skrzyń), Kucharz
  (`CookingStation`: dokładanie surowego, zdejmowanie gotowego przed spaleniem); doświadczenie w zawodzie przyspiesza
  pracę; uczta (miód + 5 porcji → +20 morale na 2 dni); blokady zawodów wg `tiers.json`.
- **M7:** role bojowe (Wojownik/Łucznik/Tarczownik, blokady wg poziomu), Zbrojownia (`AoJ_Armory`: żołnierz bierze
  brakujący sprzęt i go zakłada), Sztandar Wojenny (`AoJ_WarBanner`: Zbiórka/Mur/Brama/Schronienie, [E] zmienia),
  posterunki, alarm automatyczny (wróg w promieniu) i ręczny (okno stołu), cywile do Schronienia lub łóżka, ranni
  wracają do łóżka (leczenie 1%/s). Brak: umiejętności bojowe.
- **M8:** `SiegeDirector` — od poziomu 1, co `Sieges.IntervalDays`, tylko z graczem w osadzie; 2–4 fale z jednego
  kierunku, skład wg poziomu osady (Czarny Las → Popielne Ziemie), elita w ostatniej fali; wygrana → Sława, dzień morale,
  kronika; `aoj_siege`. Brak: zapis wyłomów.
- **M9:** okno stołu w 6 zakładkach (Osada z morale/jedzeniem/alertami/ucztą, Mieszkańcy, Praca, Magazyn, Obrona z
  alarmem, Kronika), okno osadnika (praca, rola), okno totemu, pinezka osady dla Jarla i Hersirów, `aoj_info`.
  Brak: podpowiedzi klawiszy.
- **M10:** README / CHANGELOG / manifest 0.2.0, paczka zbudowana; skład fal oblężeń i strażników obozów w
  `raids.json` (definicje, protokół v3). Do zrobienia: testy zgodności z modami z Vortexa, testy w grze. Publikacja
  tylko po wyraźnej zgodzie.
- **Dopracowanie (po „dopracuj całość”):** jeńcy siedzą i nie przyjmują rozkazów (wszystkie RPC + koło); bezczynni
  osadnicy czasem siadają (poza z ZDO na każdym kliencie); umiejętność walki (+50% obrażeń, trafienia u atakującego)
  i morale w walce (±15%); pełne morale (komfort łóżka z `SE_Rested`, żałoba po stracie/oblężeniu); karmienie z ręki
  i dawanie dowolnych przedmiotów (narzędzia nie idą do ręki); alarm tylko od czujnych wrogów i napastników
  oblężenia; cywil bez schronienia trzyma się stołu; wyłomy (zniszczone budowle) w kronice; zamach dopiero po
  obróceniu do celu; rolnik sadzi po zniknięciu zebranej rośliny; mniej pracy na klatkę (cache skrzyni wejściowej,
  stanowisk, koliderów celu, posterunków, bez alokacji przy sadzeniu, przerwa po nieudanym pobraniu ze skrzyń).

### M3 — Pierwsza praca (L)
- `WorkTotem`, `WorkZone`, `StorageLink`, rezerwacje, kategorie magazynów.
- `WoodcutterJob`, `HaulerJob`.
- Kocioł Osady, głód, sen, proste morale.
- Drzwi i wykrywanie zablokowania.

**Odbiór:** dwóch drwali przez dzień gry zapełnia skrzynię drewnem, jedząc z kotła; bez jedzenia zwalniają; nocą
śpią; nie blokują się na drzwiach; Tragarz sortuje drewno i kamień do właściwych skrzyń.

### M4 — Nadrabianie (M)
- `SettlementSim.CatchUp`, pula zasobów strefy, pomiar tempa na żywo, raport z nieobecności, `aoj_catchup`.

**Odbiór:** po dniu gry nieobecności przyrost w skrzyni zgadza się z tempem na żywo (±20%), zużycie jedzenia też;
relog nie dubluje surowców.

### M5 — Rekrutacja (M)
- `PrisonerCage`, `CaptiveCamps`: klatki (ze strażnikami) wstawiane przy wczytaniu lokacji
  (`LocationProxy.SpawnLocation()`) — jedna ścieżka dla nowych i istniejących światów.
- Rozbitkowie z Łąk przypływają do pierwszego Stołu Jarla (też bez nowej lokacji w generatorze).
- Pule jeńców według biomu (Łąki, Czarny Las), cechy według pochodzenia.
- Uwolnienie → podążanie → przyjęcie przy stole.

**Odbiór:** w nowym i w starym świecie obóz w Czarnym Lesie ma klatkę; po pokonaniu strażników jeniec idzie za
graczem i daje się przyjąć do osady.

### M6 — Gospodarka (L)
Górnik, Hutnik, Budowniczy, Rolnik, Kucharz; umiejętności zawodowe; pełne morale z Ucztą; poziomy 2–3.

**Odbiór:** osada na poziomie 3 sama wydobywa, przetapia i gotuje; gracz dostarcza tylko narzędzia i przygotowuje pole.

### M7 — Armia (L)
Zawody bojowe, Zbrojownia, Sztandary, alarm, rozkazy w polu, umiejętności bojowe, rany i śmierć.

**Odbiór:** przy rajdzie vanilla cywile się chowają, łucznicy stoją na murze, piechota trzyma bramę, ranni wracają
do łóżek.

### M8 — Oblężenia (L)
`SiegeDirector`, fale, skalowanie, punkty wejścia, wyłomy, nagrody i Sława, obozy jeńców w pozostałych biomach,
poziomy 4–7.

**Odbiór:** oblężenie w Czarnym Lesie da się wygrać trzema wojownikami za palisadą; bez obrony niszczy część budowli.

### M9 — Pełne okno (M)
Wszystkie zakładki Stołu Jarla, Kronika, pinezki, komunikaty, panel totemu, podpowiedzi klawiszy.

### M10 — Wydanie (M)
Balans w JSON, testy zgodności z modami z Vortexa, README / CHANGELOG / ikona, `tools/package.ps1 AgeOfJarls`.
Publikacja w Thunderstore tylko po Twojej wyraźnej zgodzie.

## Ryzyka

| Ryzyko | Wpływ | Szansa | Odpowiedź |
|---|---|---|---|
| NPC z modelu gracza (animacje, ekwipunek, ragdoll) sprawia problemy | wysoki | średnia | M1 na starcie; plan B: baza z Dvergra |
| AI grzęźnie w bazie (drzwi, schody, ciasne przejścia) | wysoki | wysoka | obsługa drzwi, wykrywanie zablokowania, teleport awaryjny, strefy pracy na otwartym terenie |
| Wydajność przy 30 NPC | średni | średnia | budżety z architecture.md §11, limit w konfiguracji, profilowanie od M3 |
| Rozjazdy w co-op przy zmianie właściciela obiektu | wysoki | średnia | stan w ZDO, zmiany tylko u właściciela, testy z dwoma graczami od M2 |
| Nadrabianie za hojne lub za skąpe | średni | średnia | tempo mierzone na żywo, limity, konfiguracja |
| Aktualizacja gry psuje mod | średni | średnia | mało patchy; `tools/decompile.ps1` wykrywa nową wersję gry |
| Rozrost zakresu | wysoki | wysoka | twarde granice etapów, lista „czego nie robimy”, pomysły odkładane do v2 |
| Uszkodzenie zapisu świata | wysoki | niska | wersjonowane bloby, odczyt defensywny, kopia świata przed testami |

## Najbliższe kroki
Etapy M0–M10 mają gotowy kod (wersja 0.3.0). Dalszy plan — stabilizacja w grze, wydajność, braki do 1.0 i backlog
v2 — jest w [plan-rozwoju.md](plan-rozwoju.md).
