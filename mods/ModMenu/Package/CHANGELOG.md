# Changelog

## 0.2.0
- Works with far more mods: settings from every config file a mod uses (not only its main one), the ConfigurationManager Category, IsAdvanced and HideDefaultButton tags, and mods that turn auto-save off.
- Big mods: sections fold (folded by default above 40 settings) and a settings search box.
- Long lists of allowed values are typed in and checked instead of a huge dropdown; invalid values are refused.
- Update check: parallel Thunderstore requests and batched Nexus queries (fast with 100+ mods); hand-installed Nexus mods are found by name.
- Ctrl+M opens the menu even when another mod reworks the main menu (General/OpenKey).
- Restart also works outside Steam; the disabling patcher handles loaders that scan several plugin folders.

## 0.1.0
- Mods button in the main menu with the list of every installed mod (loaded, switched off, failed).
- Switch mods on/off without touching their files (preloader patcher; safe with Vortex and r2modman), restart button.
- Settings editor for every loaded mod, with a Default button per setting.
- Profiles of switched-off mods.
- Update check against Nexus Mods (Vortex installs) and Thunderstore, no account needed.
