# Mod Menu

Adds a **Mods** button to Valheim's main menu.

- **Switch mods on and off** with a checkbox. Nothing is renamed or moved, so Vortex, r2modman and Thunderstore installs stay intact: a small BepInEx patcher (`BepInEx/patchers/ModMenu`) simply does not load the mods you switched off. Changes apply after a restart; the *Restart game* button does it for you.
- **Edit every mod's settings** in a proper window: toggles, dropdowns, sliders and text fields, saved at once, with a *Default* button per setting.
- **Profiles**: save which mods are off (e.g. "co-op" and "solo") and switch between them in one click.
- **Update check** against Nexus Mods (mods installed through Vortex) and Thunderstore. No account or API key needed. Nothing is downloaded; the *Page* button opens the mod's page.

Mod Menu and Jotunn cannot be switched off from the menu (you would lose the menu). Client-side only: other players and the server do not need it.

If the game does not start after switching a mod off, delete `BepInEx/config/ModMenu/disabled.txt` and every mod loads again.

Source code and issues: https://github.com/Dismonder/AgeOfJarls/tree/master/mods/ModMenu
