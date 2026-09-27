# Valheim mods (BepInEx 5 + Jotunn)

- Load the `valheim-mod` skill before any modding work (commands, paths, where to look in the game code).
- Game code: Grep in `_ref/decompiled/assembly_valheim/` (create it with `pwsh -NoProfile -File tools/decompile.ps1`). Do not read whole files.
- Logs: `pwsh -NoProfile -File tools/log.ps1`. Do not touch the Vortex-managed folders in `BepInEx\plugins`.
