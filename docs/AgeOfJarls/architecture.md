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
- Ruch, pathfinding, animacje i walkę daje vanilla; my wybieramy cel i zachowanie. Obronę dokładamy sami
  (`AI/CombatSense`, `AI/CombatPatches`, 0.6.0): co 0,1 s osadnik patrzy, który wróg w zasięgu ciosu zamachnął się w
  jego stronę, obraca się do niego i podnosi tarczę (albo broń białą, jak gracz) — `Character.m_blocking` ustawiane u
  właściciela, resztę (licznik bloku idealnego, animacja, flaga w ZDO) robi `Humanoid.UpdateBlock`. Moment podniesienia
  bloku liczony z zamachu wroga: czas od `InAttack()` do trafienia, uczony per prefab z trafień (`Character.RPC_Damage`
  prefix), nieznany wróg blokowany od początku zamachu. Gdy osadnik chce blokować, `MonsterAI.DoAttack` (prefix) nie
  zaczyna zamachu. Wróg bijący z boku staje się celem. Dobór broni własny (`Humanoid.EquipBestWeapon` prefix: broń
  roli, łuk z daleka, biała w zwarciu), naciąg łuku NPC = pełny (`GetAttackDrawPercentage` postfix), pola AI broni
  graczy (`m_aiAttackRange/Interval`) ustawiane przy założeniu. Łucznik bez broni białej odskakuje od wroga w zwarciu.
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
- **Zablokowanie.** Brak postępu przez 3 s (albo 6 s krążenia przy punkcie trasy) to „utknięcie”, ale nie porażka:
  pierwsze → nowa ścieżka i dalej, drugie w tym samym miejscu → przejście przez meble (łóżko, skrzynia, ława), trzecie
  → krok na następny punkt trasy (≤ 2 m, na navmeshu). Dopiero gdy nic z tego nie działa, `PathMover` zgłasza
  `Blocked`, a zadanie porzuca cel i zwalnia rezerwację (wcześniej porzucało go przy pierwszym utknięciu).
- **Podejście do obiektu.** Skrzynia, stacja, piec, uszkodzony element: strona najbliższa osadnikowi bywa zamurowana
  (skrzynia między stołem a ławą, wewnętrzna strona ściany domu). `WorkScanner.FindApproach` sprawdza 8 stron obiektu,
  przy jego podstawie i na gruncie pod nim (dom na palach, dach), i wybiera najkrótszą z pełną ścieżką i obiektem w
  zasięgu; bez takiej (navmesh jeszcze się buduje) idzie do najbliższego punktu i szuka znów co 2 s.
- **Cele nieosiągalne.** Skrzynia, stacja, uszkodzony element, plon, wyłom bez drogi — odłożone na minutę
  (`JobBase.MarkUnreachable`), następne wybierane są inne; nigdy ten sam cel w kółko.
- **Powrót do domu z histerezą.** Powrót zaczyna się dalej niż 12 m od kotwicy domu i trwa do 8 m — osadnik pchany na
  granicy (kłoda) przełączał się co klatkę między powrotem a spacerem vanilla.
- **Mapa skrzyń (`Settlement/ChestIndex`).** Jeden opis na skrzynię (stosy, przedmioty, rodzaje, wolne sloty),
  przebudowywany, gdy zmieni się `Container.m_lastRevision` (gra sama co 1 s wczytuje skrzynię z ZDO na każdej
  maszynie, właściciel zapisuje przy zmianie). `SettlementStorage.BestChestFor` i `Rank` czytają opis, nie inwentarz;
  `StoreInto` po każdym przedmiocie unieważnia opis (`Touch`). Na każdej maszynie ten sam obraz, więc osadnicy obu
  graczy sortują tak samo. Etykieta skrzyni bez przypisania pokazuje, za co osadnicy ją mają („zawiera: drewno”).
- **Oddawanie przedmiotów (`Net/ItemDelivery`).** Okno osadnika prosi RPC `AoJ_SettlerTakeItem` (id pytającego
  peera + ciało: nazwa, jakość, założone, ilość) albo `AoJ_SettlerTakeBack` (cały plecak); właściciel osadnika
  zdejmuje/wyjmuje i wysyła paczkę RPC **na osadniku** `AoJ_SettlerDeliver(id, paczka)` do peera pytającego (lokalnie
  od razu); odbiorca wkłada do plecaka gracza, reszta ląduje u stóp, i odpowiada `AoJ_SettlerDelivered(id)`.
  Właściciel trzyma paczkę do potwierdzenia: bez odpowiedzi w 6 s (`Plugin.Update` → `ItemDelivery.Update`) przedmioty
  wracają do plecaka osadnika (`Settler.TakeBackParcel`), a gdy osadnik zniknął z tej maszyny — na ziemię, gdzie stał.
  Tożsamość pytającego to nadawca z sieci; z pola w paczce tylko wtedy, gdy prośbę przekazał serwer (maszyna, do której
  trafiła, straciła własność w międzyczasie) lub pole wskazuje serwer (`Settler.Requester`). Odmowa (szczebel) i
  „już tego nie ma” wracają do pytającego RPC `AoJ_SettlerNotify(token, argument)` — tylko tokeny `$aoj_*`.
- **Strażnik (`AI/Escort`).** Osadnik podążający za graczem: prefix `BaseAI.Follow` zastępuje vanilla (prosto na
  gracza, stop 3 m — często w kadrze) pozycją „przy ramieniu”: 2,5 m za graczem, 2 m w bok (strona z `Uid`), względem
  `transform.forward` gracza. Histereza: gdy gracz idzie (prędkość z różnicy pozycji > 0,5 m/s), rusza, gdy punkt
  odjedzie 2 m, i idzie za żywym punktem; gdy gracz stoi, obroty kamery go nie ruszają — idzie tylko, gdy jest w
  kadrze (stożek 60° przed graczem, bliżej niż 4 m) dłużej niż 1 s albo oddalił się od punktu > 5 m, i wtedy cel
  wyprawy jest zamrożony (nie krąży za obracającym się graczem). `MoveTo(dist 0)` kończy sam (0,5/1 m lub brak
  ścieżki). Cele: co 0,5 s przed vanilla (`HoldVanillaTargeting` trzyma
  `m_updateTargetTimer`, więc vanilla nie szuka sam) — wróg najbliżej **gracza** w `Commands/GuardRange`, premia dla
  tego, który celuje w gracza (`MonsterAI.GetTargetCreature`) i dla tego, który trafił gracza (prefix
  `Character.RPC_Damage` na maszynie gracza → `Escort.Defend` u strażników symulowanych tam); cel dalej niż
  `Commands/GuardLeash` od gracza jest porzucany. Rozkazy gracza (`_orderedTarget`, odwrót) mają pierwszeństwo —
  wtedy strażnik nie wybiera celów.
- **Osadnicy na mapie (`UI/SettlerPins`).** Postfix `Minimap.UpdateMap` co 0,5 s: pinezka `PinType.Player` z imieniem
  dla każdego z `Settler.Loaded` (tylko wczytani na tej maszynie — pozycji dalszych nikt tu nie zna), pozycja
  aktualizowana z `m_pinUpdateRequired`, usuwana, gdy osadnik zniknie; nowa instancja `Minimap` = nowa sesja → słownik
  czyszczony. Gra w `UpdatePins` maluje każdą pinezkę na biało przy każdym przerysowaniu, więc postfix `UpdatePins`
  nadaje naszym kolor `UI/SettlerPinColor` (ikona i tekst). Filtr ikon mapy „gracze” ukrywa je razem z graczami.
- **Portale (`Settlers/PortalFollow`).** Postfix `Player.TeleportTo` na maszynie gracza (gra przenosi gracza dopiero
  po 2 s): podążający osadnicy w 20 m (nie powaleni, nie jeńcy) dostają `Settler.RequestTeleport(cel)` — właściciel
  (tu, albo przez RPC `AoJ_SettlerTeleport(id pytającego + ciało: cel)` w formacie `Request`/`Requester` jak prośby
  o przedmioty, przyjmowany tylko od maszyny gracza, za którym osadnik idzie) ustawia
  `transform`, `m_body` i `ZDO.SetPosition` na punkt 2 m od wyjścia portalu (wysokość z `GetGroundHeight`, jeśli strefa
  wczytana). Gdy strefa nie jest wczytana u właściciela, instancja znika, ZDO niesie nową pozycję i osadnik pojawia
  się, gdy gracz tam dotrze.
- **Właściciel obiektu dla kawałków (`Net/OwnerRpc`).** `JarlTable.RPC_Action`, `WorkTotem.RPC_Config`,
  `WarBanner.RPC_SetKind`, `ChestLabels.RPC_SetKind` używają tego samego schematu co osadnik: obiekt bez właściciela
  jest przejmowany, przekazanie dalej ograniczone do 10/s na ZDOID.
- **Aktualizacje (`Core/AutoUpdate`, `tools/publish-update.ps1`).** Strona Cloudflare Pages `aoj-updates`
  (https://aoj-updates.pages.dev, konto dismonder@gmail.com, wrangler zalogowany OAuth) z `manifest.json` (version,
  url, sha256, notes), zipem Thunderstore i `index.html` z szablonu `cloud/aoj-updates/template.html`;
  `_headers` wyłącza cache manifestu. Mod w `FejdStartup.Start` (menu główne: start i powrót ze świata, nie częściej
  niż co 10 min) pobiera manifest przez `UnityWebRequest`, porównuje `System.Version`, pobiera zip (≤ 50 MB),
  sprawdza SHA-256, rozpakowuje wpisy `plugins/AgeOfJarls/` (`System.IO.Compression` z Mono gry): plik zapisywany obok
  jako `.new`, zajęty cel (DLL) przemianowany na `.old` (Windows pozwala przemianować załadowaną DLL), `.old` kasowane
  przy następnym starcie. Komunikat `$aoj_msg_update_ready` po wejściu do świata (`Player.OnSpawned`). Opcje
  `Updates/AutoUpdate`, `Updates/Url`. Publikacja: `pwsh tools/publish-update.ps1 AgeOfJarls` (pakuje, generuje stronę,
  `wrangler pages deploy`). Vortex: folder `plugins/AgeOfJarls` nie jest zarządzany przez Vortex, więc podmiana trzyma.
- **Dom po zniszczonym stole.** `Settler.UpdateHome`: gdy stół domu nie jest wczytany, a inny wczytany stół ma
  osadnika na liście → adopcja (`AdoptListedHome` zwraca bool); gdy stół domu jest wczytany, a inny wczytany stół
  wciąż go listuje → `RequestRemoveSettler` (raport właściciela). `JarlTable` przyjmuje podążających, których stół nie
  jest tu wczytany (`FindById == null`), a tych ze stołem na miejscu wymienia w komunikacie
  `$aoj_msg_followers_have_home`.
- **Moduł portali (`Portals/`).** `PortalPatches`: prefix `Player.UpdateTeleport` przyspiesza licznik gry (faza
  ciemności 2 s → `Portals/PlayerTeleportSeconds`, czekanie 6 s po dalekim skoku → `DistantLoadSeconds`); warunki gry
  (strefa wczytana, podłoga) zostają. `PortalRoutes`: wczytane `TeleportWorld` (skan co 5 s), wyjście jak u gracza
  (`GetConnectionZDOID` → ZDO celu, 1 m przed nim), `FindRoute` (cel w promieniu) i `FindShortcut` (droga przez portal
  krótsza o ≥ 60 m). **Noga portalowa w `PathMover.MoveTo`**: cel dalej niż 120 m → co 5 s szukanie skrótu; jest →
  marsz do wejścia (`MoveDirect`), przy portalu `Settler.JumpTo(exit)` i ścieżka od nowa po drugiej stronie;
  `Blocked` → rezygnacja na 60 s. Każde zadanie korzysta (skrzynie, drzewa, stacje, łóżko), więc magazyn za portalem
  jest osiągany i opuszczany przez portal. `AI/PortalTravel` dodatkowo prowadzi do domu osadnika, którego stół nie
  jest tu wczytany (wtedy `HomeRoutine` nie działa). Ograniczenie gry: osadnicy działają tylko w strefach wczytanych
  wokół graczy.
- **Obszary osady (`SettlementZone`, format 6).** Lista stref w `SettlementData.Zones` (id, nazwa, rodzaj
  warehouse/other, środek, promień 5–80 m, maks. 16, ≤ 400 m od stołu); akcje `SetZone`/`RemoveZone` (Hersir+,
  właściciel stołu nadaje id `Keys.NewId`). `JarlTable.Contains` = promień stołu lub strefa; `FindContaining` z tego
  korzysta (etykiety skrzyń). `SettlementStorage.CollectChests(center, radius, data, chests)` zbiera skrzynie też ze
  stref (bez duplikatów) — HomeRoutine, JobBase, SettlementSim, okno stołu. Mapa: `UI/ZoneWindow` — postfix
  `Minimap.UpdateMap` w trybie dużej mapy, klawisz `Commands/ZoneKey` (Z) nad mapą → `ScreenToWorldPoint(pointer)`;
  strefa pod kursorem → edycja, inaczej nowa w najbliższej wczytanej osadzie z prawem Manage. `UI/SettlementPins`
  rysuje koło osady i strefy (pinezki `EventArea` z `m_worldSize`, niebieskie/zielone, z nazwą).
- **RPC na osadniku zamiast routowanych globalnie.** Odbiór paczki rejestruje `Settler.Awake` na każdej maszynie, więc
  nie zależy od postfixu `ZNet.Awake`. Routowane RPC bez obiektu (`AoJ_AlarmPin`) rejestruje `Net/RoutedRpcs` w
  postfixie **konstruktora `ZRoutedRpc`** i ponownie (idempotentnie, `m_functions.ContainsKey`) po `ZNet.Awake`:
  rzucający postfix innego moda (M182 Admin Panel, patrz `StartupGuard`) przerywa postfixy po nim i maszyna bez
  rejestracji po cichu ignoruje, co inni do niej wysyłają.
- **Osadnik bez właściciela.** Każda prośba do właściciela (`Settler.OwnerHandles`, `ClaimIfOwnerless`) najpierw
  przejmuje osadnika, którego nikt nie symuluje (`HasOwner()` false — właściciel właśnie wyszedł ze strefy), i od razu
  wczytuje plecak z ZDO (`OnBecameOwner`). Inaczej `InvokeRPC` do właściciela 0 to rozgłoszenie do wszystkich, które
  każda maszyna przekazałaby dalej (zdublowana obsługa).
- **Alarm bez fałszywych.** Automatyczny alarm po ≥ `Sieges/AlarmMinThreats` zaalarmowanych wrogach w osadzie przez
  4 s i nie wcześniej niż `AlarmCooldownSeconds` po poprzednim; oblężenie uruchamia od razu.
- **Skala okien.** `UI/Scale` to `localScale` panelu Jotunn (przycięte do rozmiaru canvasu), więc układ i teksty
  rosną razem.
- **Rezerwacje.** W pamięci właściciela totemu, z czasem wygaśnięcia — znikają same, gdy pracownik zniknie.
- **Zamach to praca, nie walka.** `SettlerAI.IsCalm` nie patrzy na `InAttack`: własny zamach siekierą czy kilofem nie
  zatrzymuje zadania (wcześniej każdy cios zwalniał cel). W trakcie zamachu `HarvestJob` trzyma osadnika w miejscu,
  zwróconego do celu — inaczej vanilla `IdleMovement` obracała go w połowie zamachu i cios trafiał w co popadnie.
- **Klatki bez polecenia ruchu.** Vanilla najpierw sama wybiera ruch (`IdleMovement` wokół punktu patrolu = domu,
  bieg, gdy dalej niż 2× zasięg), zachowanie osadnika go nadpisuje. Każdy stan „na miejscu” (przy skrzyni, na
  posterunku, w schronieniu, przy łupie) woła `StopMoving`, inaczej osadnik dryfuje do domu i z powrotem.
- **Totemy poza osadą.** `WorkTotem.Settlement` = osada, w której stoi, albo ta z najbliższą krawędzią w zasięgu
  `Work/TotemReach`. Dla pracownika takiego totemu „dom” sięga do dalszej krawędzi strefy (`HomeRoutine.HomeReach`),
  więc droga tam i z powrotem nie jest „oddaleniem się”. Gdy stół nie jest wczytany (gracz przy dalekim totemie),
  osadnik pracuje dalej (`WorkAway`), a z pełnym plecakiem wraca.
- **Navmesh na żądanie.** `Pathfinding` buduje kafle 32 m dopiero po pierwszym zapytaniu o ścieżkę; `PathMover` czeka
  do 4 s (pytając co 1 s), zanim uzna cel za nieosiągalny — inaczej daleka strefa traciła wszystkie cele naraz.
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
