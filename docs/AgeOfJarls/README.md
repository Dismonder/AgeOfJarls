# Era Jarlów (Age of Jarls) — koncepcja

> Koncepcja 0.1 · 2026-09-27 · **stan: mod w wersji 0.3.0** — kod etapów M0–M10 gotowy, większość rozgrywki
> jeszcze niesprawdzona w grze; dalsze kroki w [plan-rozwoju.md](plan-rozwoju.md).
> Cel techniczny: Valheim 1.0.12 · BepInEx 5 · Jotunn 2.30 · gra solo, co-op i serwer dedykowany.

**W jednym zdaniu:** ratujesz wikingów z obozów wrogów, osiedlasz ich przy Stole Jarla, dajesz im pracę przez Totemy
Zadań, karmisz z Kotła Osady i razem ze znajomym prowadzisz ich w obronie przed oblężeniami.

## Dokumenty

| Plik | Zawartość |
|---|---|
| [README.md](README.md) | koncepcja, filary, pętla rozgrywki, hierarchie, słowniczek, decyzje, otwarte pytania |
| [features.md](features.md) | katalog funkcji z priorytetami, zawody, cechy, progresja, czego mod celowo nie robi |
| [architecture.md](architecture.md) | architektura: moduły, prefaby, dane i zapis, sieć, symulacja offline, AI, punkty zaczepienia w grze |
| [roadmap.md](roadmap.md) | etapy M0–M10 z kryteriami odbioru, ryzyka, historia prac |
| [plan-rozwoju.md](plan-rozwoju.md) | plan po 0.3.0: stabilizacja, wydajność, braki do 1.0, backlog v2, decyzje |

## Fantazja gracza

Na początku jesteś sam z kilofem. Kilku bossów później jesteś jarlem: rano sprawdzasz przy stole, czy jedzenia
w kotłach starczy na trzy dni, wysyłasz drwali na północny zagajnik, dajesz łucznikom nowe łuki i ruszasz na wyprawę
po kolejnych jeńców. Gdy wracasz, magazyny są pełne, a na murach stoi warta.

## Filary

1. **Od kilofa do sztandaru.** Powtarzalną pracę (rąbanie, kopanie, noszenie, dokładanie do pieców) przejmują
   osadnicy. Gracz planuje, eksploruje, walczy i decyduje.
2. **Żywi ludzie, nie maszyny.** Każdy osadnik ma imię, wygląd, cechy, umiejętności i potrzeby. Morale ma realne skutki.
3. **Vanilla-first.** Mod stoi na tym, co już jest w grze: prawdziwe drzewa, skrzynie, łóżka, jedzenie, narzędzia
   i rajdy, a bossowie wyznaczają progresję. Drwal rąbie siekierą, którą mu dasz; łucznik strzela z łuku, który wykujesz.
4. **Co-op od pierwszego dnia.** Każda funkcja działa w grze wieloosobowej i na serwerze dedykowanym; kilku graczy
   zarządza jedną osadą.
5. **Mało przycisków, dużo decyzji.** Kilka czytelnych narzędzi (stół, totemy, sztandary) z rozsądnymi ustawieniami
   domyślnymi zamiast mikrozarządzania.
6. **Działa na istniejących światach i jest dla nich bezpieczny.** Wymóg twardy: mod instalujesz do świata, w którym
   już grasz — nic nie zależy od generowania nowego świata. Zapasy leżą w zwykłych skrzyniach, a dane w zapisie świata
   są wersjonowane — usunięcie moda nie niszczy świata ani przedmiotów.

## Pętla rozgrywki

```
WYPRAWA      eksploruj biom → znajdź obóz z jeńcami → pokonaj strażników → uwolnij osadników
   ↓
OSADA        przyjmij ich przy Stole Jarla → przydziel łóżka → przypisz do Totemów Zadań
   ↓
PRACA        osadnicy wydobywają, uprawiają, przetapiają i odnoszą surowce do magazynów
   ↓
UTRZYMANIE   jedzenie w Kotle Osady + narzędzia i sprzęt → morale → wydajność
   ↓
ZAGROŻENIE   bogatsza osada przyciąga oblężenia → drużyna broni murów i wyłomów
   ↓
ROZWÓJ       nadwyżki → lepszy sprzęt → kolejny boss → wyższy poziom osady → nowe zawody i jeńcy
   ↺         z powrotem do WYPRAWY, w następnym biomie
```

Dwa rytmy:
- **dzień gry** (20 minut): jedzenie, przydziały, obrona, raport z nieobecności;
- **biom i boss**: nowy poziom osady, nowe zawody, nowe obozy jeńców, silniejsze oblężenia.

## Hierarchie

### Społeczna — kto rządzi

```
Jarl ................ założyciel i współjarl (do 2, równe prawa; starszy sam degraduje drugiego); przekazanie tytułu
└─ Hersir ........... zarządca: praca i totemy, wypędzanie, poziom i nazwa, szczeble Karl/Huskarl
   └─ Huskarl ....... dowódca: alarm, role bojowe, sztandary, rozkazy bojowe
      └─ Karl ....... członek: przyjmuje swoich osadników, codzienne rozkazy, uczty
         └─ Osadnicy  NPC, każdy z jednym zawodem
            ├─ robotnicy: Drwal, Tragarz, Górnik, Hutnik, Budowniczy, Rolnik, Kucharz, Zbieracz
            └─ Hird (drużyna): Wojownik, Łucznik, Tarczownik; później Włócznik i Berserk
Gość ................ gracz bez uprawnień: widzi osadę, nic nie zmienia
```

Każdy członek (Karl i wyżej) ponad pierwszego powiększa osadę: limit mieszkańców, promień, miejsca przy totemach
(konfigurowalne); oblężenia rosną z liczbą graczy w domu.

### Budowle — co czym steruje

```
Stół Jarla ................ serce osady: promień, rejestr osadników, okno zarządzania
├─ Łóżka (vanilla) ........ miejsca do spania = limit mieszkańców
├─ Kocioł Osady ........... wspólne zapasy jedzenia, źródło morale
├─ Totemy Zadań ........... strefy pracy: promień, sloty, filtry, połączone magazyny
│  └─ Magazyny (vanilla) .. skrzynie z kategoriami: skąd brać narzędzia, dokąd odnosić urobek
├─ Zbrojownia ............. skrzynia ze sprzętem dla drużyny
└─ Sztandary Wojenne ...... posterunki: mury (łucznicy), brama i wyłomy (piechota), zbiórka, schronienie
Klatki jeńców (w świecie) . źródło nowych osadników, poza osadą
```

### Kod — moduły (szczegóły w [architecture.md](architecture.md))

```
AgeOfJarls
├─ Core ........ start, konfiguracja, definicje JSON, klucze, logi
├─ Settlement .. Stół Jarla, dane osady, uprawnienia, symulacja i nadrabianie
├─ Settlers .... osadnik: tożsamość, wygląd, cechy, umiejętności, potrzeby, morale
├─ AI .......... mózg osadnika, zachowania, zadania zawodów, nawigacja, rezerwacje
├─ Logistics ... Totemy Zadań, strefy pracy, magazyny, kategorie przedmiotów
├─ Military .... role bojowe, zbrojownia, sztandary, alarm, oblężenia
├─ World ....... obozy jeńców, klatki, uwalnianie
├─ UI .......... okno Stołu Jarla, panele, podpowiedzi, mapa, komunikaty
├─ Net ......... RPC, serializacja, synchronizacja definicji
└─ Debug ....... komendy aoj_*
```

## Słowniczek

| Polski | English | W kodzie |
|---|---|---|
| Era Jarlów | Age of Jarls | `AgeOfJarls`; prefaby `AoJ_*`, tokeny `$aoj_*`, klucze ZDO `aoj_*` |
| Stół Jarla | Jarl's Table | `AoJ_JarlTable`, `JarlTable` |
| Osada | Settlement | `SettlementData` |
| Osadnik | Settler | `AoJ_Settler`, `Settler` |
| Zawód | Profession | `ProfessionDef` |
| Cecha | Trait | `TraitDef` |
| Totem Zadań | Work Totem | `AoJ_Totem_*`, `WorkTotem` |
| Strefa pracy | Work Zone | `WorkZone` |
| Magazyn | Linked storage | `StorageLink` |
| Kocioł Osady | Settlement Cauldron | `AoJ_Cauldron`, `SettlementCauldron` |
| Zbrojownia | Armory | `AoJ_Armory`, `Armory` |
| Sztandar Wojenny | War Banner | `AoJ_WarBanner`, `WarBanner` |
| Klatka jeńców | Prisoner Cage | `AoJ_Cage`, `PrisonerCage` |
| Oblężenie | Siege | `SiegeDirector`, `SiegeEvent` |
| Sława | Renown | `Renown` |
| Kronika | Chronicle | `Chronicle` |
| Nadrabianie | Catch-up (offline simulation) | `SettlementSim.CatchUp` |

## Kluczowe decyzje (propozycje)

| # | Decyzja | Uzasadnienie |
|---|---|---|
| D1 | Osadnik = model gracza + `Humanoid` + własne AI dziedziczące po `MonsterAI` | wygląda jak wiking, nosi prawdziwy sprzęt, walczy vanillową AI; mózg bez patchowania gry |
| D2 | Symulacja dwutrybowa: na żywo przy graczach + **nadrabianie** po powrocie | Valheim symuluje tylko okolice graczy; bez tego osada zamiera, gdy ruszasz na wyprawę |
| D3 | Stan w ZDO (zapis świata); zmienia go tylko właściciel obiektu, reszta prosi przez RPC | tak działa cała gra: synchronizacja i zapis „za darmo”, brak rozjazdów w co-op |
| D4 | Magazyny, łóżka, jedzenie i sprzęt to obiekty vanilla | mniej kodu, zgodność z innymi modami, bezpieczne odinstalowanie |
| D5 | Definicje (cechy, zawody, imiona, poziomy, oblężenia) w JSON | balans bez rekompilacji; serwer narzuca swoje definicje klientom |
| D6 | Nowe budowle w MVP złożone z części vanilla (Jotunn `KitbashManager`) | szybko i spójnie z grą; własne modele (Blender) dopiero przy polerce |
| D7 | Kolejność: najpierw największe ryzyko (NPC), potem pionowy wycinek, potem szerokość | jeśli NPC się nie uda, wiemy to na starcie, a nie po miesiącu |
| D8 | Zero zależności od generowania świata: wszystko, co mod dodaje do świata, pojawia się w trakcie gry | wymóg działania na istniejących światach; nowe lokacje z generatora trafiłyby tylko do niezbadanych stref |

## Otwarte pytania

1. **„Mroczne Krasnoludy”** — Dvergrzy z Mglistych Ziem czy Szarokarły z Czarnego Lasu?
   *Propozycja:* jedno i drugie — Szarokarły jako wczesne obozy, Dvergrzy jako późne.
2. **Nadrabianie offline** — abstrakcja (surowce przybywają, świat się nie zmienia) czy odwzorowanie (po powrocie
   znikają ścięte drzewa)? *Propozycja:* abstrakcja w MVP, odwzorowanie jako opcja później.
3. **Śmierć osadnika** — trwała? *Propozycja:* tak (stawka w oblężeniach), sprzęt zostaje w nagrobku.
4. **Dezercja przy niskim morale?** *Propozycja:* domyślnie wyłączona, tylko spadek wydajności; opcja w konfiguracji.
5. **Limit osadników.** *Propozycja:* 30 na osadę (wydajność), konfigurowalny.
6. **Ile osad na świat?** *Propozycja:* w MVP jedna na grupę graczy; kolejne (np. przyczółki w innych biomach) w v2.
7. **Nazwa w Thunderstore.** *Propozycja:* `AgeOfJarls` (wyświetlana: Age of Jarls / Era Jarlów).
