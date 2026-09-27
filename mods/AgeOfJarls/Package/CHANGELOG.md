# Changelog

## 0.3.0

- Shared settlements: every member (Karl and up) beyond the first adds the tier's settler limit again (two players =
  twice the settlers), +15% radius (at most 80 m) and one more worker place at every Work Totem (configurable).
- Two Jarls with equal rights (Settlement/MaxJarls); only the senior one may demote the other; the title can still be
  handed over.
- New ranks for bigger groups: Karl (brings in their settlers, everyday orders, feasts) and Huscarl (alarm, combat roles,
  war banners, battle orders) below the Hersir (work, totems, dismissals, tier, name, Karl/Huscarl ranks).
- Members tab in the Jarl's Table: ranks, promoting, demoting, leaving, admitting players standing at the table.
- Sieges bring more attackers for every extra player at home (Sieges/PerPlayer).
- Whoever a settler follows may send it home, also far from its table.
- Console: `aoj_rank <rank> <player>` replaces `aoj_hersir`.
- Fix: chronicle entries showed raw tokens (e.g. the tier's name).
- Command wheel key (H, configurable): orders for the settler you look at from up to 40 m (come to me!, stay!, attack,
  fall back, go home, details) or for your squad with the creature in sight as the target; G works from afar too.
- Settlements saved by 0.2 are read and converted (the Jarl and Hersirs keep their ranks); 0.2 cannot join 0.3 games.

## 0.2.0

- Work Totems for seven jobs (woodcutter, hauler, miner, smelter, builder, farmer, cook) with zones, hours,
  reservations, job experience and problems shown on the totem.
- Settlement Cauldron, hunger, morale and feasts; wounded settlers heal in bed.
- Catch-up of the work done while nobody was at the settlement.
- Castaways and captives in enemy camps (also in existing worlds).
- Combat roles, Armory, War Banners, automatic alarm, sieges with fame.
- Command wheel in the game's radial menu; tabbed windows for the table, settlers and totems; the chronicle.
- Settlers sleep in their beds, sort loot into chests, open doors.
- Fix: settlements no longer lose their settlers after a world reload (stable ids instead of ZDOIDs).
- Polish: captives sit tied up and take no orders until freed; idle settlers sometimes sit down at home.
- Polish: settlers learn to fight (up to +50% damage) and morale affects their blows (-15%..+15%).
- Polish: full morale model (comfort around the bed, grief after a loss or a siege).
- Polish: hand food to a settler to feed it; any item can be given; tools stay in the bag.
- Polish: the alarm reacts only to enemies that noticed someone (no false alarms with passive mobs).
- Polish: siege waves and camp guards are editable in `raids.json`; destroyed buildings are noted in the chronicle.
- Polish: the farmer replants reliably; lighter per-frame work for all jobs.

## 0.1.0

- First version: settlers, the Jarl's Table, roles.
