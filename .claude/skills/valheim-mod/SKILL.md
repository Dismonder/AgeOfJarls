---
name: valheim-mod
description: Workflow for creating, building, debugging and packaging Valheim BepInEx/Jotunn mods in this repo — scaffolding, searching decompiled game code, reading logs, Thunderstore zip. Use for any Valheim modding task here.
---

# Valheim modding — workflow

Environment (as of 2026-09-27; if the game updated, `tools/decompile.ps1` detects it and re-decompiles):
Valheim 1.0.12 (network 40), Unity 6000.0.75, BepInEx 5.4.23.5 (BepInExPack 5.4.2350), Jotunn 2.30.0.
Game dir: `C:\Program Files (x86)\Steam\steamapps\common\Valheim` (override: `$env:VALHEIM_DIR`).
The user's mods are managed by **Vortex**: never touch folders in `BepInEx\plugins` that contain `__folder_managed_by_vortex`.

## Token economy (mandatory)
- Look up game code in `_ref/decompiled/assembly_valheim/` (one type = one file) with **Grep** using narrow patterns
  (`-n`, small `-C`, `head_limit`). Never read a whole big file (Player.cs is ~7.5k lines): Grep for the line, then Read with `offset`/`limit`.
- If `_ref/` is missing (it is gitignored): `pwsh -NoProfile -File tools/decompile.ps1`. Other assemblies: `pwsh -NoProfile -File tools/decompile.ps1 assembly_utils`,
  Jotunn: `pwsh -NoProfile -File tools/decompile.ps1 -Path "<ValheimDir>\BepInEx\plugins\Jotunn\Jotunn.dll"`.
- Jotunn API docs: Grep `<ValheimDir>\BepInEx\plugins\Jotunn\Jotunn.xml` before going to the web.
- Logs: `pwsh -NoProfile -File tools/log.ps1 [-Mod <Name>] [-Last N]`, never Read the whole LogOutput.log. Full Unity log with extra
  stack traces: `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\Player.log` (Grep only).
- Web (WebFetch/WebSearch) only when local sources do not answer. Docs: https://valheim-modding.github.io/Jotunn/ ,
  wiki https://github.com/Valheim-Modding/Wiki . Examples when really needed:
  `git clone --depth 1 https://github.com/Valheim-Modding/JotunnModExample _ref/JotunnModExample`.
- No subagents / Workflow — do the work yourself.

## Commands (run from the repo root)
| Task | Command |
|---|---|
| New mod | `pwsh -NoProfile -File tools/new-mod.ps1 <Name> [-Jotunn]` → `mods/<Name>/`, added to `Valheim.slnx` |
| Build + deploy to game | `dotnet build mods/<Name>` → copies output to `BepInEx\plugins\<Name>\` |
| Build only | `dotnet build mods/<Name> -p:DeployToGame=false` |
| Build everything | `dotnet build Valheim.slnx` |
| Thunderstore zip | `pwsh -NoProfile -File tools/package.ps1 <Name>` → `dist/<Name>-<ver>.zip` (checks icon 256x256, name, description ≤250) |
| Unit tests | `dotnet test tests/<Name>.Tests` (xunit on net48; game DLLs load from the install; no Unity engine calls, and no generic asserts over types from `assembly_valheim` — .NET Framework cannot load its default interface methods) |
| Translations check | `pwsh -NoProfile -File tools/check-loc.ps1 <Name>` (every `$token` in code exists in every language; exit 1 if not) |
| World backup | `pwsh -NoProfile -File tools/backup-world.ps1 <world>` → `_backups/` (Steam cloud and local saves; refuses while the game runs) |
| Launch game | `Start-Process steam://rungameid/892970` (only when the user wants to test) |
| Is the game running? | `Get-Process valheim -ErrorAction SilentlyContinue` |
| Drive the game in a test | `tools/focus-game.ps1` (focus), `tools/click.ps1 <x> <y>` (menu click, 1456x819 screenshot frame), `tools/sendkey.ps1 KEY[,KEY...]` (scan codes, `MOVE:dx,dy`, `LCLICK`) |

Deploying while the game is running fails (locked DLL, a warning in the build). Ask the user to close the game; do not kill the process on your own.

## How a mod project works
- `Directory.Build.props/.targets` (repo root) set everything: net48, references to the game/BepInEx/Unity DLLs straight
  from the install (Private=false), **publicized `assembly_valheim`** (private members are reachable directly:
  `__instance.m_privateField`, `nameof(Player.PrivateMethod)`), Jotunn when `<UseJotunn>true</UseJotunn>` (+ `#if JOTUNN`).
- `PluginInfo.Guid/Name/Version` are generated from the .csproj (`<Version>` must be `x.y.z`). GUID: `com.<ModAuthor>.<name>`.
- Files in `mods/<Name>/Assets/**` are copied next to the DLL (translations, asset bundles).
- `mods/<Name>/Package/` = manifest.json, icon.png (placeholder, replace it), README.md, CHANGELOG.md for Thunderstore.
- Publishing to Thunderstore = an outward action: only after an explicit OK from the user.

## Valheim map (where to start searching)
- Items/recipes/status effects: `ObjectDB` (hook: `ObjectDB.Awake`, `ObjectDB.CopyOtherDB` postfix), `ItemDrop.ItemData/SharedData`, `Recipe`, `StatusEffect`, `SEMan`.
- Prefabs: `ZNetScene` (`Awake`), `ZNetView`. Building: `Piece`, `PieceTable`, `CraftingStation`, `Player.UpdatePlacementGhost`.
- Player/combat: `Player` → `Humanoid` → `Character`, `Attack`, `Inventory`, `Container`.
- UI: `Hud`, `InventoryGui`, `FejdStartup` (main menu), console commands: `Terminal`. `Localization` lives in `assembly_guiutils`.
- World/network: `ZNet` (`IsServer()`, `IsDedicated()`), `ZDO`, `ZRoutedRpc`, `ZoneSystem`, `EnvMan`, `Game`.
- With Jotunn prefer its managers/events (`PrefabManager.OnVanillaPrefabsAvailable`, `ItemManager`, `PieceManager`,
  `CommandManager`, `SynchronizationManager`) over manual patches of `ObjectDB/ZNetScene`.
- Server-side mods: do not add `[BepInProcess("valheim.exe")]` (the server is `valheim_server.exe`).

## Optional tools — install ONLY when the task needs them
- **UnityExplorer** (Thunderstore, open source): inspect scene/objects at runtime — for UI/prefab debugging.
- **BepInEx.ConfigurationManager**: in-game config UI (F1).
- **ScriptEngine** (BepInEx.Debug): reload a plugin without restarting the game — for long iterations.
- **Unity Editor 6000.0.x + asset bundles**: only for custom models/prefabs. Blender MCP is available for modelling
  (for Valheim 1 Blender unit = 1 m = 1 Unity unit; the stud conventions in the global CLAUDE.md are for Roblox only).
