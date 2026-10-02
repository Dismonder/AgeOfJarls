# Age of Jarls — mod do Valheim

**Osady, osadnicy, praca, oblężenia i zarządzanie w co-op.** Ratujesz wikingów z obozów wrogów, osiedlasz ich przy
Stole Jarla, dajesz im pracę przez Totemy Zadań, karmisz z Kotła Osady i razem ze znajomym bronisz osady przed
oblężeniami. Osadnik, który za tobą idzie, jest twoim strażnikiem.

| | |
|---|---|
| **Pobierz (najnowsza wersja)** | **https://aoj-updates.pages.dev** — jeden klik, zip gotowy do rozpakowania |
| Wydania na GitHub | https://github.com/Dismonder/AgeOfJarls/releases/latest |
| Lista zmian | [mods/AgeOfJarls/Package/CHANGELOG.md](mods/AgeOfJarls/Package/CHANGELOG.md) |
| Opis dla graczy (EN) | [mods/AgeOfJarls/Package/README.md](mods/AgeOfJarls/Package/README.md) |
| Dokumentacja projektu (PL) | [docs/AgeOfJarls](docs/AgeOfJarls/README.md) — koncepcja, funkcje, architektura, plan |

Wymagania: Valheim 1.0.x, [BepInEx 5](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/),
[Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/). Gra solo, co-op i serwer dedykowany.
**Wszyscy na serwerze muszą mieć tę samą wersję moda** — gra odrzuca połączenie przy różnicy.

## Instalacja (ręcznie)

1. Zainstaluj BepInEx 5 i Jotunn (najprościej przez r2modman / Thunderstore Mod Manager albo Vortex).
2. Pobierz `AgeOfJarls-<wersja>.zip` ze strony https://aoj-updates.pages.dev lub z wydań na GitHub.
3. Rozpakuj zip. Katalog `plugins/AgeOfJarls` wrzuć do `Valheim/BepInEx/plugins/`
   (powinien powstać `BepInEx/plugins/AgeOfJarls/AgeOfJarls.dll`).
4. Uruchom grę. Przy pierwszym starcie powstaje `BepInEx/config/com.Dismonder.AgeOfJarls.cfg` z opcjami.

Menedżer modów (r2modman): zip jest w formacie Thunderstore, więc „Import local mod” też działa.

## Automatyczne aktualizacje

Od wersji 0.7.0 mod sam sprawdza stronę https://aoj-updates.pages.dev w menu głównym gry. Gdy jest nowsza wersja,
pobiera ją, sprawdza sumę SHA-256 i podmienia pliki na następne uruchomienie gry; w grze pojawia się komunikat
„pobrane — uruchom grę ponownie”. Dzięki temu wszyscy na serwerze mają tę samą wersję. Wyłączenie:
`Updates/AutoUpdate = false` w pliku konfiguracji.

## Jak grać (skrót)

- **Stół Jarla** (młotek → Osada) zakłada osadę; jej promień i obszary widać na mapie (M).
- **Osadnicy**: uwolnij jeńców z obozów wrogów, przyprowadź ich do stołu i przyjmij. Każdy ma imię, cechy, potrzeby.
- **Praca**: Totemy Zadań (drwal, górnik, tragarz, hutnik, budowniczy, rolnik, kucharz); zapasy trafiają do zwykłych
  skrzyń. Na dużej mapie **Z** oznacza obszar osady, np. **magazyn** za portalem.
- **Rozkazy**: **H** patrząc na osadnika (do mnie, czekaj, atak, odwrót, do domu, okno), **G** = koło gry z rozkazami
  dla drużyny. Osadnik, który za tobą idzie, trzyma się za ramieniem, nie włazi w kamerę i broni cię; przechodzi
  z tobą przez portale.
- **Okno osadnika**: ekwipunek (daj/weź), rola bojowa, imię. **F6**: podręcznik w grze.
- **Obrona**: Sztandary Wojenne, Zbrojownia, alarm, oblężenia od 1. szczebla osady.

## Dla modderów

Repozytorium to przestrzeń robocza (BepInEx 5 + Jotunn, .NET Framework 4.8):

```
dotnet build mods/AgeOfJarls            # build + kopia do BepInEx/plugins (gra musi być zamknięta)
dotnet test tests/AgeOfJarls.Tests      # testy formatów zapisu i reguł rang
pwsh -NoProfile -File tools/package.ps1 AgeOfJarls          # zip Thunderstore do dist/
pwsh -NoProfile -File tools/publish-update.ps1 AgeOfJarls   # zip + strona aktualizacji (Cloudflare Pages)
```

Ścieżkę gry nadpisuje `$env:VALHEIM_DIR`. Dekompilacja kodu gry do `_ref/`: `pwsh -NoProfile -File tools/decompile.ps1`
(katalog `_ref` nie trafia do repozytorium). Szczegóły: [CLAUDE.md](CLAUDE.md), [docs/AgeOfJarls/architecture.md](docs/AgeOfJarls/architecture.md).
