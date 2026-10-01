# Era Jarlów — plan dalszego rozwoju (po 0.3.0)

> Stan na 2026-09-27. Historia etapów M0–M10 i szczegóły tego, co już zrobione: [roadmap.md](roadmap.md).
> Rozmiary S < M < L to względna wielkość pracy, nie czas.

## Postęp (0.4.0, 2026-09-27)
- **Etap 0 — zrobiony:** repozytorium z historią (tag `v0.3.0`), `tools/backup-world.ps1`, kopia świata `testo`.
- **Etap 1, mój tor — zrobiony:** `aoj_debug`, testy jednostkowe (`tests/AgeOfJarls.Tests`, 31 testów),
  `tools/check-loc.ps1`. Sesje testowe w grze — do zrobienia.
- **Decyzje:** śmierć i utrata sprzętu domyślnie wyłączone (osadnik jest powalony i wstaje; trwała śmierć z
  grobem — opcja `Settlers/PermanentDeath`); reszta według propozycji.
- **Etap 3 — kod gotowy (0.4.0), niesprawdzony w grze:** punkty 1–9 z tabeli poniżej (nadrabianie wszystkich
  zawodów, 13 cech, powalenie zamiast śmierci, krata jeńców, rodzaje skrzyń + priorytety i samoczynny przydział
  pracy + sadzenie drzew, oblężenia z kilku stron + jednostki oblężnicze + odbudowa wyłomów, pinezka alarmu +
  kronika „kto co zrobił”, zużycie broni żołnierzy, Włócznik/Berserk i poziomy 6–7). Zostają: balans (10) po testach
  i wydanie (11, za zgodą).
- **Sprawdzone w grze (pętla autonomiczna, 2026-09-27/28):** start 0.4.0 bez błędów, migracja osady do formatu 5,
  `aoj_debug`, krata jeńca, powalenie (HP 10% → wstaje po 15 s), zbieranie i odkładanie do skrzyń, samoczynny
  przydział do totemu, totemy jako wolnostojące sztandary bez warsztatu, ulepszanie totemów 1→3 (koszt, brakujące
  surowce, poziom maks., rosnący sztandar), strefy przy stawianiu.
- **Etap 2 — rozpoczęty:** `aoj_perf` mierzy koszt moda na klatkę; pierwsze liczby i wnioski w
  [architecture.md](architecture.md) §11. Poprawki: rozłożenie okresowej pracy osadników w czasie, żołnierze bez
  wolnego posterunku nie przeszukują sztandarów co klatkę, liczenie obsady bez alokacji.
- **0.5.0 (2026-09-28, paczka `dist/AgeOfJarls-0.5.0.zip`, nie wysłana na Thunderstore):** wolnostojące totemy i
  sztandary wojenne, poziomy totemów w `totems.json` (protokół definicji 5), totemy poza osadą, zawody sprawdzone i
  poprawione, niezawodność ruchu i zbierania, komendy testowe. Wersja podbita, żeby w co-op stara 0.4.0 u drugiego
  gracza była odrzucona (`VersionStrictness.Minor`).
- **Sprawdzone w grze 2026-09-28 (AI osadników):** praca bez przerw przy każdym zamachu, celowanie w kłodę, pełny
  cykl drwala (drzewo → kłoda → połówki → drewno → pniak, małe drzewa), totem drwala **poza osadą** (praca tam i
  odnoszenie urobku do skrzyń 46 m dalej), jedzenie ze skrzyń i z plecaka (racja do 3 porcji, bez kursów z jedną
  jagodą), osadnik nazywa przedmiot bez skrzyni („Pióra: postaw pustą skrzynię”).
- **Wszystkie zawody sprawdzone w grze 2026-09-28** (komendy `aoj_tier`, `aoj_place`, `aoj_totem assign`): drwal,
  górnik (złoże miedzi, skały), budowniczy (sam wziął młotek ze skrzyni, naprawił podłogę i skrzynię), hutnik
  (mielerz → węgiel, piec → miedź), kucharz (ruszt nad ogniskiem, upieczone mięso do skrzyń/kotła), rolnik (zbiór
  marchwi, plon do skrzyń), tragarz (luźne kamienie i krzemień do skrzyń). Naprawione po drodze: górnik krążył wokół
  dużych skał, kilof celował w ziemię, stacje „nieosiągalne” co 4 s, rolnik trzymał plon w plecaku.
- **Niezawodność sprawdzona w grze 2026-09-28 (drugi przebieg):** łuczniczka bierze ze Zbrojowni łuk, strzały i
  brakującą zbroję; alarm w nocy bez migotania sen↔schronienie; budowniczy odbudowuje wyłom (`aoj_breach`) i naprawia
  dach oraz ścianę domu na palach z ziemi (`aoj_breach damage`); drwal sadzi buk z nasion ze skrzyni
  (`aoj_totem replant on`); osadnik nie porzuca celu przy pierwszym utknięciu; podejście do skrzyni z dostępnej strony.
- **Co-op sprawdzony przez użytkownika 2026-09-30 (serwer, dwóch graczy):** działa; uwagi: alarm za często,
  osadnicy nie bronią się (brak bloków), zbieranie i magazyn nieintuicyjne, interfejs za mały, brak zabierania
  pojedynczych przedmiotów.
- **0.6.0 (2026-10-01, kod bez testu w grze — użytkownik prosił o samo pisanie kodu):** CombatSense (bloki, blok
  idealny z uczonego zamachu, cel = bijący, dobór broni roli, pełny naciąg łuku NPC, odskok łucznika), zabieranie
  pojedynczych przedmiotów z okna osadnika (RPC + routowana dostawa), `UI/Scale`, ChestIndex + etykieta „zawiera”,
  próg i odstęp alarmu. W trace z 10:08 widać działanie uczenia: „hit by Skeleton 0,76 s into its swing”.
- **Do sprawdzenia w grze (0.6.0):** blok i blok idealny wojownika z tarczą (`aoj_give ShieldBronzeBuckler`, `spawn
  Greydwarf 3`, `aoj_trace` → „(blocking)”), łucznik (strzały pełnej mocy, odskok), okno osadnika w skali 1.3,
  zakładka Ekwipunek „Weź”/„Weź 1” (w co-op od osadnika symulowanego przez drugiego gracza), etykieta skrzyni,
  częstość alarmu w co-op. Świat `testo` ma trwający najazd draugrów przy bazie; postać `test` ma 25 HP (użyj `god`).
- **0.7.0 (2026-10-01, kod bez testu w grze — użytkownik grał w trakcie):** zgłoszenie z co-op: „Weź” działa tylko
  u mnie (klient u kolegi-hosta), u kolegi nic nie trafia do plecaka. Log klienta: postfix `ZNet.Awake` moda M182
  Admin Panel rzuca (`StartupGuard` ratuje ładowanie), co może pominąć rejestrację routowanego `AoJ_ReceiveItems` na
  maszynie i przedmioty znikały po drodze. Zrobione: dostawa RPC na osadniku (`AoJ_SettlerDeliver`/`Delivered`),
  potwierdzenie i zwrot do plecaka po 6 s bez odpowiedzi, „Oddaj wszystko” do plecaka gracza, komunikat o odmowie
  i o brakującym przedmiocie u pytającego, przejęcie osadnika bez właściciela, `RoutedRpcs` (rejestracja w
  konstruktorze `ZRoutedRpc`). Szczegóły: [architecture.md](architecture.md). Wersja 0.7.0 — obaj gracze muszą
  mieć tę samą (format `AoJ_SettlerTakeItem` i `TakeBack` się zmienił).
- **Crash hosta (0.6.0, zgłoszony 2026-10-01):** host nacisnął G i wezwał najbliższego osadnika → gra się wyłączyła.
  Przyczyna: osadnik bez właściciela (klient właśnie wyszedł ze strefy, `ReleaseNearbyZDOS` ustawia właściciela 0,
  serwer przydziela nowego dopiero w następnym przebiegu). `ZNetView.InvokeRPC` do właściciela 0 to rozgłoszenie,
  które gra obsługuje od razu lokalnie; nie-właściciel w `RPC_*` przekazywał dalej → ta sama metoda bez końca →
  przepełnienie stosu. W 0.7.0 `OwnerHandles` przejmuje takiego osadnika (`ClaimOwnership` + `OnBecameOwner`), a
  przekazywanie między dwiema maszynami o nieaktualnym właścicielu jest ucinane po 10 na sekundę.
- **Strażnik (0.7.0, kod bez testu w grze):** prośba użytkownika — osadnik przypisany jako ochroniarz, który nie włazi
  w kadr i broni „inteligentnie”. `AI/Escort`: pozycja przy ramieniu (za plecami, z boku), odstępowanie z kadru po
  sekundzie, cele wokół gracza (najpierw ten, co idzie na gracza lub go trafił), smycz `GuardLeash`. Szczegóły w
  [architecture.md](architecture.md).
- **Osadnicy na mapie (0.7.0, kod bez testu w grze):** `UI/SettlerPins` — pinezka gracza z imieniem, złota
  (`UI/SettlerPinColor`), dla wczytanych osadników. Do sprawdzenia: mapa (M) i minimapa pokazują osadników złotym
  kolorem z imieniem, pinezka idzie za osadnikiem, znika po wyjściu ze strefy, filtr „gracze” ukrywa je, po
  wylogowaniu i wejściu do świata nie ma duplikatów.
- **Portale i obszary (0.7.0, 2026-10-01, kod bez testu w grze — użytkownik prosił o kod zamiast testu):**
  moduł `Portals/` (szybszy skok gracza, osadnicy używają portali przez nogę portalową w `PathMover`, powrót do domu
  portalem), obszary osady na mapie (`SettlementZone`, format 6, okno Z na dużej mapie, magazyn = skrzynie osady),
  rysowanie koła osady i stref. Szczegóły: [architecture.md](architecture.md). Zrobione w pierwszym przebiegu testów
  (19:40): nowy build startuje, log „[Update] … is current (the update page lists 0.7.0)”; świat `testo` wczytany.
  **Do sprawdzenia:** Z na mapie otwiera okno, zapis strefy widać jako koło z nazwą u obu graczy; skrzynia w strefie
  „Magazyn” 150 m od stołu za parą portali → tragarz niesie tam przez portal (log „went through the portal '…'”) i
  wraca; `aoj_trace` „portal '…' on the way”; skok gracza przez portal trwa ~0,5 s; osadnik zostawiony 200 m od
  domu przy portalu do osady wraca nim; brak zapętlenia noga portalowa ↔ Blocked (log „gives up the portal” raz na
  minutę, nie co klatkę). Wiadomo z góry: osadnik po skoku w niewczytaną strefę znika do czasu, aż gracz tam dotrze.
- **Automatyczne aktualizacje (0.7.0, 2026-10-01, strona opublikowana, mod bez testu w grze):** użytkownik prosił
  o stronę na Cloudflare, z której mod sam pobiera aktualizacje. Strona https://aoj-updates.pages.dev (manifest +
  zip), mod `Core/AutoUpdate`, publikacja `tools/publish-update.ps1`. Do sprawdzenia: po starcie gry log „is current
  (the update page lists 0.7.0)”; po publikacji 0.7.1 w menu log „installed … next start”, komunikat po wejściu do
  świata, po restarcie nowa wersja i brak `.old` w `plugins/AgeOfJarls`.
- **Osadnik po zniszczonym stole (zgłoszenie 2026-10-01):** „idzie za mną, a nie da się przypisać” — `UpdateHome`
  czyścił dom tylko w 40 m od starego stołu; przy stole nowej osady filtr `!HasHome` go odrzucał. Naprawione: przyjęcie
  podążającego, którego stół nie jest tu wczytany, adopcja domu z listy nowego stołu, usuwanie ze starych list.
  Do sprawdzenia: taki osadnik przy „Przyjmij” trafia na listę i idzie do nowej osady (log „settled in …”).
- **Przegląd „ulepsz gdzie się da” (0.7.0, kod bez testu w grze):** ta sama klasa crasha (RPC do właściciela 0)
  była w `JarlTable`, `WorkTotem`, `WarBanner`, `ChestLabels` → wspólny `Net/OwnerRpc`. Podążający osadnicy
  przechodzą przez portale (`Settlers/PortalFollow`, opcja `Settlers/FollowThroughPortals`). Do sprawdzenia: wejście
  w portal ze strażnikiem → po wyjściu stoi 2 m obok; portal daleki (ładowanie strefy) → pojawia się po dotarciu;
  powalony osadnik zostaje; w co-op strażnik symulowany przez hosta też przechodzi (log hosta: „followed its leader
  through a portal”); zmiana ustawień totemu zaraz po przyjściu do pustej osady nie wiesza gry.
- **Do sprawdzenia w grze (0.7.0, strażnik):** „Do mnie!” → osadnik idzie 2–3 m za plecami z boku, nie staje przed
  kamerą; obrót gracza o 180° → po ~1 s przechodzi za plecy, bez tańczenia przy rozglądaniu; bieg za graczem bez
  zacinania; `spawn Greydwarf 3` 20 m dalej → strażnik nie rusza, póki nie podejdą na 15 m lub nie uderzą gracza;
  wróg uciekający >30 m → strażnik wraca; rozkaz „atakuj” nadal działa; `aoj_trace` pokazuje „guard: …”.
- **Do sprawdzenia w grze (0.7.0, co-op):** rozkaz G/H dla osadnika, od którego drugi gracz właśnie odszedł (poprzednio crash); „Weź”/„Weź 1”/„Oddaj wszystko” z obu maszyn, od osadnika symulowanego
  przez drugiego gracza i przez siebie; log właściciela: „handed … to peer”, brak „did not confirm”; odmowa dla
  gościa bez szczebla Karl pokazuje komunikat u pytającego; pinezka alarmu na mapie u drugiego gracza.
- **0.7.1 (2026-10-01, przegląd kodu 0.7.0 bez gry — użytkownik testuje sam):** przeczytane wszystkie nowe pliki
  0.7.0 i sygnatury gry pod patchami Harmony (`UpdateMap`, `TeleportTo`, `RPC_Damage`, `Follow`, `UpdateTeleport`,
  `AddPin` — zgodne). Błędy znalezione i naprawione: (1) lokalne „Weź”/„Oddaj wszystko” od osadnika symulowanego
  u siebie — `ItemDelivery.Send` czytał tę samą `ZPackage` od końca (pozycja po zapisie), odbiór padał i przedmioty
  wracały do plecaka osadnika bez komunikatu → `SetPos(0)`; (2) `SettlerAI` wołał `HomeRoutine.Stop()` co klatkę
  w drodze do portalu → aktywność Idle↔Returning i zapis ZDO co klatkę → stop tylko przy starcie podróży;
  (3) strażnik ruszał przy każdym obrocie kamery (punkt odjeżdżał > 2,5 m) → przy stojącym graczu rusza tylko
  „w kadrze > 1 s” lub > 5 m od punktu, cel zamrożony; (4) `RPC_Teleport` przekazany dalej był odrzucany (nadawca =
  pośrednik) → format `Request`; (5) `DropStaleRosters` co tick → co 10 s; podwójny log skoku. Wersja 0.7.1 (Jotunn
  `VersionStrictness.Minor`, więc 0.7.0 i 0.7.1 łączą się ze sobą). Strona aktualizacji przygotowana lokalnie
  (`cloud/aoj-updates/site`), **nieopublikowana** — `pwsh tools/publish-update.ps1 AgeOfJarls` publikuje.
  Lista do sprawdzenia w grze: jak dla 0.7.0 powyżej, plus „Weź” od własnego osadnika solo (komunikat „took” i
  przedmiot w plecaku).
- **Do sprawdzenia w grze:** serwer dedykowany. Uwaga: gra działająca w tle między testami
  to upływ czasu świata (doba = 30 min) — osadnicy w tym czasie jedzą zapasy.
- **Świat `testo` po testach:** Totem Tragarza poziom 3 przy osadzie, krata jeńca i 2 greydwarfy (świat ma pasywne
  potwory), postać „test” z odblokowanymi przepisami na brąz (przedmioty z `spawn`).

## 1. Gdzie jesteśmy

| | Stan |
|---|---|
| Wersja | 0.3.0, kompiluje się 0/0, wdrożona u Ciebie, paczka `dist/AgeOfJarls-0.3.0.zip` (niewysłana) |
| Kod | 64 pliki C#, ~11,8 tys. linii; 5 plików definicji JSON; 344 teksty PL/EN |
| **Sprawdzone w grze** | osadnik (wygląd, imię, podążanie, czekanie, walka), Stół Jarla, łóżka, stałe id i zapis po restarcie, grupa „Rozkazy” w kole G, okno stołu z zakładkami, nocny sen |
| **Niesprawdzone w grze** | prawie cała rozgrywka: praca (7 zawodów), Kocioł i głód, morale, nadrabianie, jeńcy i rozbitkowie, armia, alarm, oblężenia, koło rozkazów z daleka (H), wspólna osada 0.3.0 |
| Historia w git | **brak** — repozytorium nie ma ani jednego commita |

Wniosek: kodu nie brakuje, brakuje **pewności, że działa**. Dlatego najpierw stabilizacja, potem braki do 1.0,
a nowe pomysły dopiero po wydaniu.

## 2. Zasady na dalej
- **Najpierw sprawdzić, potem poszerzać.** Nowe funkcje dopiero wtedy, gdy stare przejdą odbiór w grze.
- **Dwa tory naraz.** Ty (z kolegą) grasz sesje testowe i mówisz, co widzisz; ja w tym czasie poprawiam błędy
  i piszę kod, który nie wymaga gry (braki do 1.0, testy jednostkowe, narzędzia).
- **Co-op i istniejące światy** w każdym odbiorze, jak dotąd.
- **Małe wydania.** Każda poprawiona sesja → 0.3.x; commit i tag po każdej.
- **Zapis świata jest święty.** Każda zmiana formatu: wersja + odczyt starej + test migracji.

## 3. Etapy

### Etap 0 — Zabezpieczenie (S) — zaraz
- Pierwszy commit repozytorium i tag `v0.3.0` (**za Twoją zgodą**).
- Kopia świata testowego przed każdą sesją (`%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local`).
- Poprawić nieaktualne dokumenty (status w README, „Najbliższe kroki” w roadmapie) — zrobione razem z tym planem.

**Odbiór:** `git log` pokazuje wersję 0.3.0; jest kopia świata `testo` sprzed testów.

### Etap 1 — Stabilizacja 0.3.x (L) — najważniejszy
Sesje w grze w kolejności zależności. W każdej: scenariusz → lista błędów → poprawki → 0.3.x.

| Sesja | Kto | Co sprawdzamy | Odbiór |
|---|---|---|---|
| 1. Podstawy | Ty solo | wczytanie świata z osadą 0.2 (migracja szczebli), zakładka Członkowie, koło H z 30 m, Drwal + Tragarz przez dzień, Kocioł, sen, sortowanie do skrzyń | drwale zapełniają skrzynię drewnem, jedzą z kotła, nocą śpią, nie blokują się na drzwiach |
| 2. Gospodarka | Ty solo | poziomy 2–3 (`setkey defeated_…` w devcommands), Górnik, Hutnik, Budowniczy, Rolnik, Kucharz, uczta, morale, `aoj_catchup 24` | osada na poziomie 3 sama wydobywa, przetapia i gotuje; nadrabianie ±20% tempa na żywo |
| 3. Rekrutacja | Ty solo | rozbitkowie (nowy świat), obóz z jeńcem w nowym i w wyczyszczonym obszarze | jeniec po walce idzie za graczem i daje się przyjąć |
| 4. Armia | Ty solo | role, Zbrojownia, sztandary, alarm, `aoj_siege`, wyłomy | łucznicy na murze, piechota przy bramie, cywile w schronieniu, ranni w łóżkach |
| 5. Co-op | Ty + kolega | współjarl, podwojony limit, szczeble, rozkazy obu graczy, wyjście jednego gracza w trakcie pracy osadników (zmiana właściciela), skrzynie otwarte przez gracza | nic się nie dubluje ani nie znika; obaj widzą to samo |
| 6. Serwer | Ty + kolega | Valheim Dedicated Server z BepInEx i Jotunnem: synchronizacja konfiguracji i definicji, restart serwera | po restarcie osada, szczeble i łóżka nietknięte |

Równolegle (mój tor, bez gry):
- **`aoj_debug`** (M): nad osadnikiem jego stan, zadanie, cel, ścieżka i rezerwacje — skraca diagnozę błędów AI
  z Twoich opisów.
- **Testy jednostkowe** (M): mały projekt testów dla czystej logiki — zapis i migracja `SettlementData`, reguły
  szczebli, sortowanie do skrzyń, wzory głodu i morale. Chroni zapis świata przy każdej zmianie.
- **`tools/check-loc.ps1`** (S): sprawdzanie, czy każdy tekst z kodu ma tłumaczenie PL i EN (dziś robię to ręcznie).

**Odbiór etapu:** wszystkie 6 sesji przechodzi bez błędów blokujących; znane drobne błędy spisane.

### Etap 2 — Wydajność i zgodność (M)
- **Profilowanie** przy 30 / 60 / 120 osadnikach (premia za członków pozwala na 120 przy 4 graczach na poziomie
  6–7): klatki, czas AI i zadań na klatkę, ruch sieciowy (zapisy ekwipunku i stanu). Budżety z
  [architecture.md](architecture.md) §11. Decyzja o twardym limicie, jeśli 120 okaże się za dużo.
- **Zgodność** z Twoimi modami z Vortexa (np. Equipment and Quick Slots) i popularnymi (Epic Loot — przedmioty
  do sortowania przez `storage.json`, mody skrzyń i budowania). Znany konflikt: Companions (zawieszony start świata —
  obejście `GuardWorldStartup` już jest).
- **Aktualizacje gry:** po każdej łatce Valheima `tools/decompile.ps1` + build + krótki test; lista patchy Harmony
  do przejrzenia.

**Odbiór:** 60 osadników w jednej osadzie bez odczuwalnego spadku klatek u obu graczy; brak błędów w logu z modami
z Vortexa.

### Etap 3 — Braki do wersji 1.0 (L)
Rzeczy oznaczone w [features.md](features.md) jako MVP/v1, których jeszcze nie ma:

| # | Co | Rozmiar | Uwagi |
|---|---|---|---|
| 1 | Nadrabianie dla wszystkich zawodów | M | dziś tylko Drwal i Górnik; dojdą Rolnik (plony), Hutnik (ruda → sztaby z zapasów), Kucharz (surowe → gotowe) |
| 2 | Pełna pula cech (14 zamiast 6) | M | Zręczny, Tchórzliwy, Sokole Oko, Zielona Ręka, Nocny Marek, Zahartowany, Weteran, z działającymi efektami |
| 3 | Nagrobek ze sprzętem po śmierci | S | dziś sprzęt wypada na ziemię; pytanie 3 z README |
| 4 | Klatka jeńców + rozbijanie zamka | M | model z części vanilla (Jotunn Kitbash); potwierdzić obozy we wszystkich biomach (pytanie 1) |
| 5 | Totemy: priorytety, ręczne łączenie skrzyń, filtr kategorii na skrzyni | M | Drwal: „zostaw młode” i sadzenie sadzonek |
| 6 | Oblężenia v1 | L | kilka kierunków naraz, jednostki oblężnicze (trolle, berserkerzy Fulingów), Budowniczowie odbudowują wyłomy po walce z materiałów z magazynu |
| 7 | Interfejs | S–M | podpowiedzi klawiszy (Jotunn KeyHints), pinezka alarmu, opcjonalne pinezki totemów, kronika „kto co zmienił” dla wszystkich akcji |
| 8 | Zbrojownia: zużycie strzał i wytrzymałości | S | sprawdzić, co dziś robi gra z bronią NPC |
| 9 | Treść poziomów 4–7 | M | `tiers.json` obiecuje Włócznika (4) i Berserka (5), których nie ma w kodzie; poziomy 6–7 nic nie odblokowują |
| 10 | Balans w JSON | M | tempo pracy, sytość, oblężenia, nadrabianie, premie członków — na liczbach z etapów 1–2 |
| 11 | Wydanie | S | ikona i grafika, opis PL/EN, zrzuty ekranu; **publikacja w Thunderstore tylko za Twoją zgodą** |

**Odbiór 1.0:** scenariusze z [roadmap.md](roadmap.md) M3–M8 przechodzą w co-op na świecie z wcześniejszą
rozgrywką; paczka przechodzi `tools/package.ps1`.

### Etap 4 — v2, po wydaniu (backlog)
Kolejność ustalimy po reakcjach graczy. Z katalogu funkcji:
- **Zawody:** Zbieracz (poziom 1), Hodowca (3); pomysły: Rybak, Uzdrowiciel z miodami.
- **Sława z efektem:** dziś Sława tylko się wyświetla — przy wysokiej Sławie i morale co kilka dni przychodzi ochotnik.
- **Sztandary:** postawy (trzymaj pozycję / patroluj / ścigaj do X m).
- **Oblężenia v2:** naprawy w trakcie walki, większe fale z kilku biomów.
- **Przyczółki:** kilka osad jednej grupy w różnych biomach (pytanie 6).
- **Opcje:** dezercja przy niskim morale (domyślnie wyłączona) i cecha Marudny; nadrabianie „odwzorowane”
  (po powrocie brakuje ściętych drzew) jako alternatywa dla abstrakcji.
- **Pomysły większe:** handel z Haldorem i Hildir, wyprawy łupieżcze osadników na statkach.
- **Oprawa:** własne modele totemów, kotła, klatki i sztandarów (Blender MCP jest gotowy); kolejne języki.

## 4. Kolejność w skrócie

```
Etap 0  zabezpieczenie ─► Etap 1  6 sesji testowych ─► 0.3.x ─► Etap 2  wydajność i zgodność
                           └─ równolegle: aoj_debug, testy jednostkowe, check-loc
Etap 3  braki do 1.0 (1–10) ─► wydanie 1.0 (za zgodą) ─► Etap 4  v2 wg reakcji graczy
```

## 5. Decyzje dla Ciebie
| # | Pytanie | Moja propozycja |
|---|---|---|
| 1 | Pierwszy commit repozytorium teraz? | tak — 12 tys. linii bez historii to największe ryzyko |
| 2 | Śmierć osadnika trwała, sprzęt w nagrobku? | tak |
| 3 | Włócznik i Berserk w 1.0 czy w v2? | w 1.0, jako treść poziomów 4–5, bo `tiers.json` już je obiecuje |
| 4 | Co odblokowują poziomy 6–7? | +1 miejsce przy każdym totemie i szybsze leczenie w łóżkach |
| 5 | Limit osadników przy premii za członków | zdecydować po profilowaniu (etap 2); do tego czasu domyślnie jak jest |
| 6 | Dezercja przy niskim morale | opcja, domyślnie wyłączona (v2) |
| 7 | Kiedy wydanie w Thunderstore | po etapach 1–3, nazwa `AgeOfJarls` |

## 6. Ryzyka (aktualne)
| Ryzyko | Wpływ | Odpowiedź |
|---|---|---|
| Szerokość niesprawdzona w grze — błędy wyjdą naraz | wysoki | etap 1 przed nowymi funkcjami; `aoj_debug`; małe wydania 0.3.x |
| Wydajność przy 60–120 osadnikach | wysoki | etap 2, limit w konfiguracji |
| Brak historii zmian | wysoki | etap 0 |
| Uszkodzenie zapisu świata przy zmianie formatu | wysoki | testy jednostkowe migracji, kopia świata przed sesją |
| Konflikty z innymi modami | średni | etap 2, zgodność z Vortexem |
| Łatka Valheima zmienia metody, które patchujemy | średni | `decompile.ps1` wykrywa nową wersję; lista patchy |
| Rozrost zakresu | średni | backlog v2 zamknięty do wydania 1.0 |
