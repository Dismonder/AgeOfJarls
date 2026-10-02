# Making your mod's settings look good in Mod Menu

Mod Menu needs nothing from your mod: it reads standard BepInEx config entries. No dependency, no API to call.
Everything below is optional and is plain BepInEx - it also helps BepInEx.ConfigurationManager (F1) and r2modman's
config editor.

## Which editor a setting gets

| Your entry | Editor in Mod Menu |
|---|---|
| `bool` | checkbox |
| `enum` (up to 60 values, no `[Flags]`) | dropdown |
| any type with `AcceptableValueList<T>` (up to 60 values) | dropdown |
| number with `AcceptableValueRange<T>` | slider + number field |
| number without a range | number field (a decimal comma is accepted) |
| `string`, `KeyboardShortcut`, `KeyCode`, `Color`, `Vector2/3/4`, `[Flags]` enums, long value lists | text field, checked with BepInEx's own converter |
| a type BepInEx cannot convert to text | read-only text |

```csharp
// Slider from 0 to 10 with a typed field next to it.
Config.Bind("Combat", "DamageMultiplier", 1f,
    new ConfigDescription("Damage dealt by settlers.", new AcceptableValueRange<float>(0f, 10f)));

// Dropdown.
Config.Bind("General", "Mode", "Chill",
    new ConfigDescription("Game mode.", new AcceptableValueList<string>("Chill", "Realistic")));
```

Values typed into a text field that are not in an `AcceptableValueList` are refused (BepInEx would silently replace
them with the first allowed value). Ranges are clamped by BepInEx as usual.

## Description and layout

- The **description** is shown under the setting's name - write it for players.
- Settings are grouped by **section** (`[Section]` in the .cfg). Big mods (more than 40 settings) open with every
  section folded, and the window has a search box over names, keys, sections and descriptions.
- Changes are written through `ConfigEntryBase.BoxedValue`: your `SettingChanged` handlers run as usual. If your
  file has `SaveOnConfigSet = false`, Mod Menu saves it after a change anyway.

## ConfigurationManager tags

Mod Menu honours the usual `ConfigurationManagerAttributes` object in the description's tags (each mod ships its own
copy of the class; only the class name and field names matter):

| Field | Effect in Mod Menu |
|---|---|
| `Browsable = false` | hidden |
| `ReadOnly = true` | shown, cannot be changed |
| `Order` | higher values first inside a section |
| `DispName` | name shown instead of the key |
| `Category` | group shown instead of the section |
| `IsAdvanced = true` | marked "(advanced)" |
| `HideDefaultButton = true` | no *Default* button |

```csharp
Config.Bind("UI", "Scale", 1f, new ConfigDescription("Window scale.", new AcceptableValueRange<float>(0.5f, 2f),
    new ConfigurationManagerAttributes { DispName = "Window scale", Order = 10 }));
```

`CustomDrawer` cannot be shown (it draws with IMGUI); such a setting falls back to its normal editor.

## Several config files

Besides your plugin's `Config`, Mod Menu finds other `ConfigFile` objects your plugin uses - fields of the plugin
class and static fields in your assembly holding a `ConfigFile` or a `ConfigEntry<T>` of another file. Each file
becomes its own group ("file / section"). Types with a static constructor are not read, so Mod Menu never runs your
code early: keep extra files in a field of the plugin, or a static field of a class without field initializers.

## Switching mods off

A switched-off mod is simply not loaded at the next start (its GUID is listed in `BepInEx/config/ModMenu/disabled.txt`);
files are never moved. Mods that hard-depend on it are reported to the player. Mod Menu itself and its dependencies
(Jotunn) cannot be switched off.

## Update check

- Thunderstore installs: the `manifest.json` next to your DLL (and the `Namespace-Name` folder r2modman creates).
- Vortex installs: the Nexus mod id from Vortex's deployment manifest.
- Hand installs: a Thunderstore package with your plugin's name (spaces removed) whose team appears in your GUID or
  that is the only package with that name, then a Nexus mod with exactly your plugin's name.

Matching names and versions (`BepInPlugin` version = package version) give your players correct update notices.

Questions and improvements: https://github.com/Dismonder/ModMenu/issues - pull requests welcome (MIT license).
