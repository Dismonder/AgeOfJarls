# Age of Jarls (Era Jarlów)

Found a settlement, rescue settlers and let them live, work and fight for you — in single player, on a dedicated
server and in co-op. Works in new **and existing** worlds.

## Features

- **Settlers** — every settler has a name, looks, an origin and traits. Order them with [E] (window) or the
  **command wheel** (radial, like the emote wheel): press **H** while looking at a settler — up to 40 m away — to
  call it (*come to me!*), stop it, send it to attack, fall back, go home or open its window; press H looking
  elsewhere for your squad's orders, with the creature in your sight as the attack target. The game's wheel (G) has
  the same squad orders under *Commands* and gives a settler's orders when your crosshair is on it.
- **Jarl's Table** — founds a settlement (radius 30–60 m, tiers 0–7 unlocked by bosses). Its window has tabs for
  the settlement, the members, the settlers (orders, dismissal), work, storage, defence and the chronicle.
- **Built together, bigger together** — every player who joins (as a member) adds the tier's settler limit again (two
  players = twice the settlers), more radius and more worker places at the totems. Up to two **Jarls** rule with
  equal rights; below them **Hersirs** run the settlement, **Huscarls** lead the troop and **Karls** live there and
  bring their settlers; guests only look. Sieges grow with the number of players at home.
- **Life at home** — settlers get a free bed (they sleep in it at night), eat from the **Settlement Cauldron**, carry
  what they find into **sorted chests** (same item → same kind → an empty chest, never mixing), open and close doors,
  and heal in bed when wounded. Morale (food, bed, variety, feasts) sets their work pace.
- **Work Totems** — Woodcutter, Hauler, Miner, Smelter, Builder, Farmer and Cook. Assign workers, set the zone
  (8–30 m) and the hours. Tools come from you (axe, pickaxe, hammer).
- **While you are away** — when you come back, the settlement credits the work of your absence (up to 2 days).
- **Recruitment** — castaways reach the shore after your first table; captives wait in enemy camps (also in old
  worlds): beat the guards and set them free.
- **Troop and sieges** — give settlers a combat role (warrior, archer, shieldbearer), build War Banners (posts) and an
  Armory. From tier 1 settlements are sometimes besieged while you are home; the alarm sounds by itself.

## Installation

Install with r2modman / Thunderstore Mod Manager, or copy `plugins/AgeOfJarls` to `BepInEx/plugins`.
Everyone on a server needs the mod (the server too). Requires BepInEx and Jotunn.

## Configuration

`BepInEx/config/*AgeOfJarls*.cfg` (synced from the server): settler health, loot, work speed, hunger, catch-up,
captive camps, sieges, the members' bonuses and the number of Jarls. Local: the command wheel key (`Commands/WheelKey`, default H) and reach (`Commands/LookRange`).
Balance data in `BepInEx/config/AgeOfJarls/*.json` (traits, tiers, names, storage kinds).

## Console (devcommands)

`aoj_info`, `aoj_settlers`, `aoj_settlement`, `aoj_rank <rank> <player>`, `aoj_jarl <player>`, `aoj_spawn`,
`aoj_catchup <hours>`, `aoj_siege`, `aoj_morale <0-100>`, `aoj_captive`, `aoj_reload_defs`.
