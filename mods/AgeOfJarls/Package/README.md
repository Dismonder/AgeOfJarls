# Age of Jarls (Era Jarlów)

Found a settlement, rescue settlers and let them live, work and fight for you — in single player, on a dedicated
server and in co-op. Works in new **and existing** worlds.

## Features

- **Settlers** — every settler has a name, looks, an origin and up to three of 13 traits (diligent, strong,
  hawk-eyed, night owl, veteran...). Order them with [E] (window) or the **command wheel** (radial, like the emote
  wheel): press **H** while looking at a settler — up to 40 m away — to call it (*come to me!*), stop it, send it to
  attack, fall back, go home or open its window; press H looking elsewhere for your squad's orders, with the creature
  in your sight as the attack target. The game's wheel (G) has the same squad orders under *Commands*. By default a
  settler is **knocked out, not killed**: it gets up with its gear (permanent death is an option). Its window's
  Equipment tab takes single items back (**Take** / **Take 1**), worn gear included.
- **They fight like you do** — a settler raises its shield (or weapon) against a blow about to land, times it for a
  perfect block once it has learned the creature's wind-up, turns on the enemy striking it, fights with the weapon of
  its role (bow from afar, melee up close) and backs off when caught with only a bow in hand.
- **Jarl's Table** — founds a settlement (radius 30–60 m, tiers 0–7 unlocked by bosses). Its window has tabs for
  the settlement, the members, the settlers (orders, dismissal), work, storage, defence and the chronicle.
- **Built together, bigger together** — every player who joins (as a member) adds the tier's settler limit again (two
  players = twice the settlers), more radius and more worker places at the totems. Up to two **Jarls** rule with
  equal rights; below them **Hersirs** run the settlement, **Huscarls** lead the troop and **Karls** live there and
  bring their settlers; guests only look. Sieges grow with the number of players at home.
- **Life at home** — settlers get a free bed (they sleep in it at night), eat from the **Settlement Cauldron**, carry
  what they find into **sorted chests** (same item → same kind → an empty chest, never mixing; [Shift+E] on a chest
  sets what it is for), open and close doors, and heal in bed when wounded. Morale (food, bed, variety, feasts) sets
  their work pace.
- **Work Totems** — Woodcutter, Hauler, Miner, Smelter, Builder, Farmer and Cook: standards you plant anywhere in
  the settlement, no workbench needed. Set the zone (8–30 m), the hours and a priority: free settlers take free places
  by themselves (or assign them by hand). Upgrade a totem (3 levels) for more places and faster work. Woodcutters can
  replant. Tools come from you (axe, pickaxe, hammer).
- **While you are away** — when you come back, the settlement credits the work of your absence (up to 2 days): wood,
  stone, bars from the smelters, cooked food, the harvest.
- **Recruitment** — castaways reach the shore after your first table; captives wait behind bars in enemy camps (also
  in old worlds): beat the guards or break the bars and set them free.
- **Troop and sieges** — combat roles (warrior, archer, shieldbearer, spearman, berserker), War Banners (posts) and an
  Armory the troop takes and returns gear to. From tier 1 settlements are sometimes besieged while you are home —
  later from several sides, with siege units that go for the walls; Builders put back what was destroyed. The alarm
  sounds by itself and puts a pin on everyone's map.

## Installation

Install with r2modman / Thunderstore Mod Manager, or copy `plugins/AgeOfJarls` to `BepInEx/plugins`.
Everyone on a server needs the mod (the server too). Requires BepInEx and Jotunn.

## Configuration

`BepInEx/config/*AgeOfJarls*.cfg` (synced from the server): settler health and death, loot, work speed, hunger,
catch-up, captive camps, sieges and the alarm (`Sieges/AlarmMinThreats`, `AlarmCooldownSeconds`), settler blocking,
gear wear, the members' bonuses and the number of Jarls. Local: the command wheel key (`Commands/WheelKey`, default H)
and reach (`Commands/LookRange`), and the size of the mod's windows (`UI/Scale`, default 1.3). Balance data in `BepInEx/config/AgeOfJarls/*.json`
(traits, tiers, names, storage kinds, siege waves, totem levels).

## Console (devcommands)

`aoj_info`, `aoj_settlers`, `aoj_settlement`, `aoj_totem [upgrade]`, `aoj_debug`, `aoj_perf [seconds]`, `aoj_dump <prefab>`, `aoj_rank <rank> <player>`,
`aoj_jarl <player>`, `aoj_spawn`, `aoj_despawn [radius]`, `aoj_catchup <hours>`, `aoj_siege`, `aoj_morale <0-100>`,
`aoj_captive`, `aoj_reload_defs`.
