# Era Jarlów — architektura techniczna

Nazwy klas, metod i pól gry sprawdzone w zdekompilowanym `assembly_valheim` (Valheim 1.0.12, `_ref/decompiled/`).
Rzeczy wymagające potwierdzenia w grze są tak oznaczone.

## 1. Zasady
1. **Kompozycja zamiast patchy.** Własne komponenty na własnych prefabach; Harmony tylko tam, gdzie gra nie daje
   punktu zaczepienia. Mniej patchy = mniej awarii po aktualizacjach gry.
2. **Decyduje właściciel obiektu.** Stan zmienia tylko właściciel ZDO; inni gracze wysyłają prośby przez RPC.
3. **Stan w ZDO, nie w pamięci.** Osada i AI muszą się wznowić po przejęciu obiektu przez innego gracza, relogu
   i restarcie serwera.
4. **Dane zamiast kodu.** Cechy, zawody, imiona, poziomy i oblężenia w JSON.
5. **Integracja przez komponenty vanilla.** Pracujemy na `TreeBase`, `MineRock5`, `Pickable`, `Container`,
   `Smelter`… — treści innych modów zbudowane na tych komponentach działają automatycznie.
6. **Każda funkcja ma komendę debug.**
7. **Zero zależności od generowania świata.** Mod musi działać w istniejących światach: treści w świecie (klatki,
   rozbitkowie) wstawiamy w trakcie gry — przy wczytaniu lokacji (`LocationProxy.SpawnLocation()`) albo wokół stołu —
   a nie przez nowe lokacje generatora (`ZoneManager` dodałby je tylko do niezbadanych stref).

## 2. Struktura projektu

```
mods/AgeOfJarls/
  AgeOfJarls.csproj            UseJotunn=true
  Plugin.cs                    start: konfiguracja, Harmony, rejestracja w Jotunn, ładowanie definicji
  Core/
    AoJConfig.cs               wpisy konfiguracji (BindConfig: synced = admin-only z serwera, lokalne osobno)
    Keys.cs                    nazwy prefabów, klucze ZDO, nazwy RPC — jedno źródło prawdy
    ModPaths.cs                Assets obok DLL (tylko odczyt) i BepInEx/config/AgeOfJarls (edytowalne)
    TranslationLoader.cs       Assets/Localization/<Język>/*.json → Jotunn
    Defs/                      DefTypes (TraitDef, TierDef, NamesDef…), DefsValidator, DefsRegistry (Newtonsoft.Json)
    Log.cs                     logi z tagiem modułu, Debug zależny od konfiguracji
  Settlement/
    JarlTable.cs               komponent stołu: Interactable, Hoverable, właściciel danych osady
    SettlementData.cs          model: członkowie, osadnicy (ZDOID), poziom, łóżka, ustawienia
    SettlementRegistry.cs      indeks wczytanych osad, wyszukiwanie po pozycji
    SettlementSim.cs           tick na żywo + nadrabianie (CatchUp)
    Permissions.cs             prawa szczebli (Karl: życie, Huskarl: wojsko, Hersir: zarząd, Jarl: rządy)
  Settlers/
    Settler.cs                 komponent NPC: tożsamość, cechy, umiejętności, potrzeby, przydział
    SettlerPrefab.cs           budowa prefabu z modelu gracza
    Appearance.cs, Names.cs, Traits.cs, Needs.cs, Morale.cs
  AI/
    SettlerAI.cs               : MonsterAI — override UpdateAI (mózg)
    Brain.cs                   wybór zachowania według priorytetów
    Behaviours/                Follow, Idle, Eat, Sleep, Flee, Shelter, Guard, Work
    Jobs/                      JobBase + WoodcutterJob, HaulerJob, MinerJob, SmelterJob, FarmerJob, CookJob, BuilderJob
    Nav.cs                     ruch, wykrywanie zablokowania, drzwi
    Reservations.cs            rezerwacje celów (drzewa, przedmioty, miejsca w skrzyniach)
  Logistics/
    WorkTotem.cs               komponent totemu
    WorkZone.cs                skan strefy, indeks zasobów (cache)
    StorageLink.cs             połączone skrzynie, kategorie, wolne miejsce
    ItemCategories.cs
  Military/
    CombatRole.cs, Armory.cs, WarBanner.cs, Alarm.cs
    Sieges/                    SiegeDirector.cs, SiegeEvent.cs, SiegeScaling.cs
  World/
    CaptiveCamps.cs            klatki w lokacjach (nowe i istniejące światy)
    PrisonerCage.cs            klatka: strażnicy, zamek, uwolnienie
  UI/
    JarlTableWindow.cs (+ zakładki), TotemPanel.cs, SettlerCard.cs, Hover.cs, MapPins.cs, Notifications.cs
  Net/
    Rpc.cs                     rejestracja RPC, walidacja uprawnień
    Serialization.cs           ZPackage <-> modele, wersjonowanie
    DefsSync.cs                definicje serwer → klient (Jotunn AddInitialSynchronization) + rozsyłka po przeładowaniu
  Commands/
    ConsoleCommands.cs         komendy aoj_* (Jotunn CommandManager); nie „Debug/”, bo przestrzeń nazw Debug
                               przesłoniłaby UnityEngine.Debug
  Assets/
    Defs/{traits,tiers,names}.json          domyślne definicje, kopiowane do BepInEx/config/AgeOfJarls przy 1. starcie
    Localization/{English,Polish}/aoj.json  nie „Translations/”, żeby automatyczny loader Jotunna nie wczytał ich drugi raz
```

## 3. Prefaby

| Prefab | Z czego | Komponenty moda |
|---|---|---|
| `AoJ_Settler` | klon `Player` (model, animacje, `VisEquipment`); `Player`, `PlayerController`, `Skills`, `Talker` usunięte, `Humanoid` z przepisanymi polami serializowanymi; `ZNetView.m_persistent = true`; warstwa `player` → `character` (inaczej `Player.m_interactMask` nie widzi osadnika) | `MonsterAI` (od M1c `SettlerAI`), `Settler` |
| `AoJ_JarlTable` | klon `piece_table_oak` (zapasowo `piece_table`); dziecko z `CircleProjector` sklonowane z `guard_stone` (`PrivateArea.m_areaMarker`); dziecko z `EffectArea` PlayerBase sklonowane z `piece_workbench` | `JarlTable` (Hoverable) |
| `AoJ_Totem_*` | kitbash: słup / totem | `WorkTotem`, `CircleProjector` |
| `AoJ_Cauldron` | kitbash na bazie `piece_cauldron` | `Container`, `SettlementCauldron` |
| `AoJ_Armory` | kitbash: stojak na broń + skrzynia | `Container`, `Armory` |
| `AoJ_WarBanner` | kitbash: sztandar | `WarBanner`, `CircleProjector` |
| `AoJ_Cage` | kitbash: bale i kraty | `PrisonerCage`, `WearNTear` (zamek) |

Parametry `MonsterAI` osadnika (zasięg wzroku i słuchu, zachowanie w walce) kopiujemy z Dvergra — humanoidalnego NPC,
który już sprawnie walczy bronią.

## 4. Dane i zapis

### 4.1 Gdzie leży stan

| Co | Gdzie | Jak |
|---|---|---|
| Osada: członkowie i role, lista osadników, poziom, ustawienia, liczniki, czas ostatniej symulacji | ZDO Stołu Jarla | blob `aoj_settlement` (ZPackage → `byte[]`, `ZDO.Set(int, byte[])` / `GetByteArray`) + proste pola do szybkiego odczytu (`aoj_tier`, `aoj_pop`) |
| Osadnik: tożsamość (imię, płeć, pochodzenie, cechy, fryzura, broda, kolory) | ZDO osadnika | blob `aoj_settler_identity` (wersjonowany); imię też w `ZDOVars.s_overrideHoverName` (czyta je `Character.GetHoverName`); model i kolory dodatkowo w polach `VisEquipment` |
| Osadnik: ekwipunek | ZDO osadnika | `aoj_settler_inventory` (format `Inventory.Save`) + `aoj_settler_inventory_rev`; zapis tylko u właściciela, zbiorczo co ≤ 1 s |
| Osadnik: rozkaz „podążaj” | ZDO osadnika | `aoj_settler_follow` (ID gracza, 0 = czeka); punkt czekania = vanillowy punkt patrolu `BaseAI` |
| Osadnik: umiejętności, potrzeby, przydział, stan AI | ZDO osadnika | pola stanu (`aoj_state`, `aoj_job`, `aoj_target`) — od M1c |
| Ekwipunek osadnika | ZDO osadnika | `Inventory.Save(ZPackage)` / `Load`, tak jak w `Container`. Gra **nie** zapisuje ekwipunku NPC — `Humanoid.Start()` przy każdym starcie woła `GiveDefaultItems()` |
| Totem: typ, promień, sloty, filtry, skrzynie, pula zasobów | ZDO totemu | blob `aoj_totem` |
| Klatka: jeńcy, stan zamka | ZDO klatki | proste pola |
| „Obóz już dodany” dla lokacji | ZDO lokacji (`LocationProxy`) | `aoj_camp` |

### 4.2 Wersjonowanie i bezpieczeństwo
- Każdy blob zaczyna się numerem wersji; czytnik obsługuje wszystkie starsze wersje (migracje).
- Odczyt defensywny: uszkodzony blob nie wysypuje gry — obiekt wraca do bezpiecznego stanu, a błąd trafia do logu.
- Po usunięciu moda gra pomija obiekty o nieznanym prefabie (`ZNetScene` loguje „Missing prefab”), ale ich ZDO
  zostają w zapisie, więc po ponownej instalacji wszystko wraca. Przedmioty i tak leżą w skrzyniach vanilla.
  v1: komenda `aoj_cleanup` do trwałego sprzątnięcia przed odinstalowaniem.

### 4.3 Klucze
Wszystkie w `Core/Keys.cs` jako `"aoj_…".GetStableHashCode()` — jedno źródło prawdy, zero literówek.

## 5. Sieć i autorytet
- Obiekt symuluje jego **właściciel** (`ZNetView.IsOwner()`) — zwykle najbliższy gracz, na serwerze dedykowanym
  któryś z klientów. AI osadników liczy się więc na komputerze jednego z graczy i przy zmianie właściciela wznawia się
  ze stanu w ZDO.
- **Zmiany w osadzie:** klient → RPC na `ZNetView` stołu → właściciel sprawdza uprawnienia → zmienia blob →
  ZDO rozchodzi się do wszystkich. Okno odświeża się przy zmianie rewizji danych ZDO.
- **Sprawy globalne** (lista osad, oblężenia, definicje): `ZRoutedRpc` lub Jotunn `NetworkManager`, autorytetem jest
  serwer. Osady na serwerze znajdujemy przez `ZDOMan.GetAllZDOsWithPrefabIterative`.
- Mod wymagany u wszystkich, łącznie z serwerem (`NetworkCompatibility(EveryoneMustHaveMod, Minor)` — już
  w szablonie): serwer potrzebuje prefabów i definicji oblężeń.
- Konfiguracja: wpisy `IsAdminOnly` synchronizowane przez Jotunn `SynchronizationManager`. Definicje JSON: serwer przy
  połączeniu wysyła klientom swój zestaw (hash + treść), a klient używa go zamiast lokalnego.

## 6. Symulacja: na żywo i nadrabianie
Gra symuluje tylko obiekty w aktywnym obszarze wokół graczy (strefy 64 × 64 m, `ZNetScene.InActiveArea`).
Gdy wszyscy odejdą, osada **zamiera** — dlatego dwa tryby.

**Na żywo** (gracz w pobliżu): osadnicy fizycznie chodzą, rąbią i noszą. `SettlementSim` co ~2 s aktualizuje potrzeby
i morale, a raz na dzień gry (1200 s, `EnvMan.m_dayLengthSec`) rozlicza posiłki.

**Nadrabianie** (po powrocie): właściciel stołu po wczytaniu strefy liczy `Δt = ZNet.GetTime() − lastSimTime`
(ten sam wzorzec co `Smelter.GetDeltaTime()` z `ZDOVars.s_accTime`) i rozlicza nieobecność analitycznie:

```
okno = min(Δt, maxCatchUp)                         // np. 2 dni gry
dla każdego totemu:
    moc    = Σ po pracownikach(tempo_bazowe × umiejętność × morale × narzędzie × cechy)
    urobek = min(moc × okno,
                 pula_zasobów_strefy,             // skan z ostatniej wizyty + odrost
                 wolne_miejsce_w_magazynach,
                 jedzenie_na_okno)                 // bez jedzenia praca staje
    dodaj urobek do magazynów; zużyj jedzenie i narzędzia; zmniejsz pulę
zapisz raport z nieobecności (Kronika + komunikat)
lastSimTime = teraz                                // w tym samym zapisie co wynik
```

- Tempo bazowe pochodzi z pomiarów w trybie na żywo, więc oba tryby dają podobne wyniki.
- Pula zasobów: skan strefy przy ostatniej wizycie (np. 40 drzew × plon) plus powolny odrost. W MVP świat się nie
  zmienia (abstrakcja); v2: opcja odwzorowania — po powrocie znika odpowiednia liczba drzew.
- Wynik i znacznik czasu zapisywane razem — relog nie dubluje surowców.

## 7. AI osadnika

### 7.1 Budowa
- `SettlerAI : MonsterAI` nadpisuje `UpdateAI(float dt)` — w grze to `public virtual` w `BaseAI`, nadpisana
  w `MonsterAI`. Mózg osadnika działa bez Harmony. Wchodzi w M3 (pierwsza praca); do tego czasu wystarcza
  vanillowe `MonsterAI` (bezczynność, podążanie, punkt patrolu, walka, ucieczka, regeneracja).
- Świat traktuje każdą postać niebędącą graczem jak wroga: `Character.Awake` i kod ruchu skalują ją modyfikatorami
  świata („rozmiar/szybkość wrogów”, poziom świata). `Settler` cofa to lokalnie na każdym kliencie.
- Osadnicy są **oswojeni** (`Character.SetTamed(true)`, ZDO `s_tamed`): bez tego `MonsterAI` przegania ich z obszarów
  „bez potworów” (np. krąg startowy) przed rozkazami podążania i czekania (test 27.09). Skutki uboczne vanilla:
  `IdleMovement` krąży wokół bieżącej pozycji → `Settler.AnchorIfIdle` ustawia punkt patrolu; `AvoidFire` nie działa
  dla oswojonych → **SettlerAI (M3) musi sam omijać ogień**; `RaiseSkill` ostrzega bez `Tameable` → nadpisane pustą
  metodą w `SettlerCharacter`. Fryzurę i brodę `Humanoid` przekazuje do `VisEquipment` tylko graczom, więc osadnik
  ustawia je bezpośrednio.
- Ruch, pathfinding, animacje i walkę daje vanilla; my wybieramy cel i zachowanie.
- Frakcja `Character.Faction.Players`: potwory atakują osadników, a osadnicy walczą z potworami. Bez `Tameable` —
  podążanie przez `MonsterAI.SetFollowTarget`, posterunki przez `BaseAI.SetPatrolPoint` / `ResetPatrolPoint`.

### 7.2 Mózg — priorytety od najwyższego
1. Zagrożenie: wróg blisko → walcz (drużyna, Odważni) albo uciekaj (cywile).
2. Alarm → Schronienie (cywile) lub posterunek (drużyna).
3. Rozkaz gracza: podążaj, czekaj.
4. Potrzeba krytyczna: głód → Kocioł, noc → łóżko, ranny → łóżko.
5. Praca: zadanie z totemu.
6. Bezczynność: kręci się przy domu lub stole.

Decyzje zapadają co 0,5–1 s, rozłożone między osadników, a nie co klatkę.

### 7.3 Zadania jako maszyny stanów
Przykład — Drwal:

```
ZnajdźDrzewo   (TreeBase w strefie, rezerwacja)
→ IdźDo
→ Rąb          (Damage z HitData: obrażenia „chop” siekiery, poziom narzędzia)
→ drzewo pada → TreeLog: rąb kłodę
→ ZbierzDrewno (ItemDrop w pobliżu, rezerwacja → ekwipunek)
→ pełny ekwipunek lub koniec drewna → ZanieśDoMagazynu (StorageLink: kategoria Drewno, wolne miejsce)
→ ZnajdźDrzewo
```

Każdy krok zapisuje w ZDO typ zadania, krok i cel (ZDOID), więc zadanie trwa dalej po zmianie właściciela lub relogu.

### 7.4 Znane problemy i odpowiedzi
- **Drzwi.** AI vanilla nie otwiera drzwi. `Nav` wykrywa `Door` na drodze, otwiera je RPC drzwi (`Door.RPC_UseDoor`)
  i zamyka po przejściu.
- **Zablokowanie.** Brak postępu przez N s → porzucenie celu i zwolnienie rezerwacji; przy uporczywym — teleport
  do najbliższego wolnego punktu.
- **Rezerwacje.** W pamięci właściciela totemu, z czasem wygaśnięcia — znikają same, gdy pracownik zniknie.
- **Skanowanie.** `Physics.OverlapSphere` z maską warstw, wynik trzymany kilka sekund w cache; żadnego
  `FindObjectsOfType` w pętli.

## 8. Punkty zaczepienia w grze

| System | Klasy vanilla | Co robimy |
|---|---|---|
| Prefaby, budowle, przedmioty | `ZNetScene`, `ObjectDB`, `Piece`, `PieceTable` | Jotunn `PrefabManager`, `PieceManager`, `ItemManager`, `KitbashManager` |
| NPC | `Humanoid`, `BaseAI`, `MonsterAI`, `VisEquipment` | klon `Player` → `AoJ_Settler`; Jotunn `CreatureManager` |
| Drewno | `TreeBase`, `TreeLog`, `ItemDrop` | obrażenia przez `HitData`, zbieranie dropów |
| Górnictwo | `MineRock`, `MineRock5`, `Destructible` | jw., z poziomem kilofa (`m_minToolTier`) |
| Rolnictwo | `Pickable`, `Plant` | zbiór, sadzenie prefabów roślin na uprawnej ziemi |
| Przetwórstwo | `Smelter` (piec, wypalarka, piec hutniczy, wiatrak, kołowrotek, rafineria), `CookingStation` | RPC dodawania surowca i paliwa, odbiór produktów |
| Naprawy | `WearNTear.Repair()` | Budowniczy |
| Magazyny | `Container`, `Inventory` | zmiany tylko u właściciela ZDO skrzyni |
| Łóżka, komfort, dach | `Bed`, `SE_Rested.CalculateComfortLevel(bool, Vector3)`, `Cover` | morale |
| Dzień i noc | `EnvMan.IsNight()`, `EnvMan.m_dayLengthSec` (1200) | sen, godziny pracy |
| Rajdy | `RandEventSystem`, `RandomEvent`, `SpawnSystem.SpawnData` | własne zdarzenia oblężeń |
| Baza gracza | `EffectArea` typu `PlayerBase` | stół podnosi „wartość bazy” |
| Lokacje | `LocationProxy.SpawnLocation()`; Jotunn `ZoneManager` tylko do odczytu definicji lokacji vanilla | klatki w obozach wstawiane przy wczytaniu lokacji — działa w istniejących światach |
| Promienie | `CircleProjector.m_radius` | okręgi stołu, totemów i sztandarów |
| Interfejs | `Hud`, `InventoryGui`, `Minimap`, `MessageHud` | Jotunn `GUIManager`, `MinimapManager`, `KeyHintManager` |
| Konsola | `Terminal` | Jotunn `CommandManager` |
| Tłumaczenia | `Localization` | Jotunn `LocalizationManager` |
| JSON | `Newtonsoft.Json.dll` (w katalogu gry) | loader definicji |

## 9. Oblężenia technicznie
- Jotunn nie ma menedżera zdarzeń, więc własne `RandomEvent` dopisujemy do `RandEventSystem.m_events`
  (postfix na `Awake`).
- Przydatne pola `RandomEvent`: `m_nearBaseOnly` (wymaga „wartości bazy” ≥ 3 przy graczu), `m_pauseIfNoPlayerInArea`,
  `m_eventRange` (domyślnie 96 m), `m_spawn` (lista `SpawnSystem.SpawnData`), `m_requiredGlobalKeys`, `m_duration`,
  `m_startMessage` / `m_endMessage`, `m_forceMusic`, `m_forceEnvironment`.
- Gra losuje zdarzenie co `m_eventIntervalMin` (1) × 60 s × `Game.m_eventRate`, z szansą `m_eventChance` (25%).
- Fale i skalowanie: `SiegeDirector` u właściciela stołu dobiera skład według siły osady i biomu; punkty wejścia leżą
  poza promieniem osady.
- Wyłomy: zniszczenie `WearNTear` w promieniu osady → zapis pozycji → zadania dla Budowniczych.

## 10. Interfejs
- Jotunn `GUIManager`: drewniane panele, przyciski i przewijane listy w stylu gry; blokada sterowania postacią przy
  otwartym oknie.
- Okno stołu otwiera `JarlTable.Interact`; czyta blob osady, a akcje wysyła przez RPC.
- Tłumaczenia: `Assets/Localization/Polish/aoj.json` i `English/aoj.json`; każdy tekst to token `$aoj_…`.

## 11. Budżet wydajności

| Element | Domyślnie |
|---|---|
| Osadnicy na osadę | maks. 30 |
| Decyzje mózgu | co 0,5–1 s, rozłożone w czasie |
| Skan strefy totemu | co 5–10 s, cache |
| Tick symulacji osady | co 2 s |
| Blob osady | < 8 KB |
| Odświeżanie okna | tylko przy zmianie rewizji ZDO |

**Pomiar (`aoj_perf`, 2026-09-28, 1 gracz, 60 kl./s).** 23 osadników (2 w domu, 21 bez domu): kod moda
0,025 ms na klatkę (0,1%), AI osadnika w modzie średnio 2,8 µs na aktualizację (20 Hz), AI gry (MonsterAI)
dla tych samych osadników 8,7 µs i skoki do 5,3 ms. Plan domowy (skan skrzyń, przydział pracy) ~100 µs co 3 s,
zapis ekwipunku ~140 µs. Wniosek: koszt dużej osady to przede wszystkim AI gry, animacja i fizyka postaci;
w kodzie moda ważne jest tylko, żeby okresowa praca nie wypadała w tej samej klatce u wszystkich osadników
(zegary planu, ticku, sprzętu i posterunków startują w losowej fazie). Pomiar przy 60/120 osadnikach w domu
wymaga osady na wyższym poziomie.

## 12. Zgodność z innymi modami
- Twoje mody z Vortexa:
  - **AutoStore** — sam zbiera przedmioty do skrzyń; może konkurować z Tragarzami, do sprawdzenia.
  - **CraftingStorageLink** — synergia: rzemiosło z magazynów zapełnianych przez osadników.
  - **PlantEverything, FarmGrid, HoeRadius** — rolnictwo idzie przez `Plant` / `Pickable`, więc powinno działać.
  - **chest_label** — synergia z kategoriami magazynów.
  - **EquipmentAndQuickSlots** — zmienia ekwipunek gracza; sprawdzić, czy nie przeszkadza przy wymianie sprzętu
    z osadnikami.
- Duże mody zmieniające AI lub przedmioty: best-effort, bez gwarancji.

## 13. Testowanie
- Każdy etap kończy się scenariuszem w grze ([roadmap.md](roadmap.md)) i czystym logiem
  (`tools/log.ps1 -Mod AgeOfJarls`).
- Kolejność: gra solo → host + klient (z kolegą) → serwer dedykowany uruchomiony lokalnie + klient.
- Komenda debug do każdej funkcji: spawn, nadrabianie, oblężenie, morale, wizualizacja AI.
- Czysta logika (morale, nadrabianie, serializacja, skalowanie oblężeń) w klasach bez zależności od Unity — można ją
  później objąć testami jednostkowymi.

## 14. Konwencje
- Prefaby `AoJ_*`, tokeny `$aoj_*`, klucze ZDO `aoj_*`, RPC `AoJ_*`, komendy `aoj_*`.
- Logi przez `Core.Log` z tagiem modułu (`[Defs]`, `[Net]`, `[AI]`…); `Log.Debug` tylko przy `General.DebugLogging`.
- Patche Harmony: ciało w `try/catch` — wyjątek moda nie może przerwać metody gry. Obecne: `ZNet.Awake`
  (definicje), `Character.RPC_Damage` (przyjacielski ogień), `Player.UpdatePlacementGhost` (odstęp między stołami,
  co klatkę w trybie budowy → najpierw tanie warunki), `Piece.CanBeRemoved` (stół rozbiera tylko Jarl).
- Tożsamość nadawcy RPC: identyfikator peera → gracz, którego ZDO postaci należy do tego peera. Nigdy nie ufamy
  ID gracza przysłanemu w paczce.
- Postfiksy na `ZNet.Awake` mają `[HarmonyPriority(Priority.High)]`. W logu użytkownika M182AdminPanel rzuca wyjątek
  we własnym postfiksie na `ZNet.Awake` (podwójna rejestracja RPC), a to może pominąć postfiksy, które wykonują się
  po nim.
- `StartupGuard` (finalizer na `ZNet.Awake`): wyjątek z **postfiksa** innego moda jest logowany i tłumiony, bo inaczej
  Unity nie wywoła `ZNet.Start`, a świat nigdy się nie wczyta (przypadek z 27.09: M182 Admin Panel + Companion
  rejestrują te same RPC). Wyjątki z kodu vanilla i z prefiksów są przepuszczane. Wyłącznik: `General.GuardWorldStartup`.
- Rzeczy, które wie na pewno tylko serwer (czy ZDO istnieje gdziekolwiek w świecie), robi `SettlementServer`
  (komponent na obiekcie `ZNet`, działa tylko przy `IsServer()`); zmiany wysyła RPC do właściciela obiektu.
- Zapis do ZDO tylko u właściciela. Uwaga: `Character.SetMaxHealth` pisze do ZDO bez sprawdzania właściciela,
  `VisEquipment.Set*` sprawdza sam. Wartości lokalne (np. `MonsterAI.m_fleeIfLowHealth`) ustawia każdy klient
  identycznie, bo właściciel może się zmienić.
- Kod po angielsku, dokumentacja po polsku, gra po polsku i angielsku.
