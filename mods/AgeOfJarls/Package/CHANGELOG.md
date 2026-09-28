# Changelog

## Unreleased

- Work Totems and War Banners are free-standing standards now: a pole with the banner on a crossbar, placed on the
  ground or a floor anywhere (they were wall banners before and could only hang on walls). No workbench needed
  nearby; sturdier (200 health).
- Totem levels: upgrade a totem in its window (level 2: 20 wood, 10 stone, 5 resin; level 3: 10 fine wood,
  4 bronze). Every level adds a worker place and 15% work pace, also while you are away, and the standard grows.
  Levels, costs and bonuses are in `totems.json` (up to 5 levels; the server's copy applies to everyone).
- While placing a totem, a war banner or a Jarl's Table, the zones of the ones of that kind nearby are shown, like
  a workbench's circle.
- `aoj_dump <prefab>` writes a prefab's parts, meshes, colliders and piece flags to the log (for modders).
- Settlers carry without a limit by default: a worker brings its whole take when its bag is nearly full or its
  work is done (`Work/CarryLimit`, 0 = no limit, replaces `WorkerLoad`); a settler doing chores picks up everything
  lying around before one trip to the chests, instead of walking there with every single item.
- `aoj_trace` writes what the settlers decide (activities, chest trips, loot, stuck paths) to the log.
- Smoother settlers: no more standing frozen after a world reload (a seated pose that never cleared), no switching
  course several times a second between work and chores, a settler wedged against furniture slips free.
- Settlers speak up when you are near: a greeting when you come back, what their work lacks ("Place a chest by the
  totem!"), an empty cauldron, nowhere to put their load; captives call for help. Rarely, and never in chorus.
- Fixed placeholders in the new totem texts (a raw "{0}" showed instead of the value).
- Hungry settlers no longer starve next to food: they eat what they carry, and when no cauldron has food they fetch
  it from a settlement chest - enough for everyone when there is a cauldron, the rest goes into it.
- A settler you talk to ([E]) stops and faces you, and carries on where it left off.
- Work Totems may stand outside the settlement, by a forest or a mine: up to `Work/TotemReach` (50 m) past its edge
  they serve the nearest settlement. Its workers walk out, work the zone and carry the take home without turning back
  halfway; if you go out there with them (the settlement unloads), they work on until their bags are full.
- Work is no longer interrupted at every blow: a woodcutter or miner stays with its tree or rock until it is down (it
  let go of it at each swing, and meanwhile the idle stroll could turn it, so the axe hit whatever stood there -
  stones included). Replanting felled trees works again. A worker that runs out of work goes to the chests at once.
- Woodcutters understand their work: a blow is aimed at the target itself (down at a log on the ground - it used to
  pass over it into the ground), a felled tree is finished before the next one falls (the log, its halves, the wood,
  the stump), stumps and small trees are cleared too (never saplings), and a log out of reach (on a roof) is given up
  after a few blows that leave no mark instead of being hit forever. At dusk the day's take goes to the chests first.
- Soldiers stand at their posts and sheltering settlers stay put, instead of drifting towards their home spot and
  back; a settler sits down only after a few seconds of real idleness, not in a short gap between two tasks.
- A place nobody walked lately (a far work zone) is waited for a moment while the game builds its paths, instead of
  being given up at once.
- A starving settler whose settlement has no food at all picks wild berries and mushrooms nearby and eats them -
  never a player's crops.

- `aoj_perf [seconds]` measures what the mod costs per frame on your machine (settler AI with planning, jobs,
  paths and inventory saves, ticks, windows, and the game's own AI for comparison).
- `aoj_despawn [radius]` (cheat) removes test settlers: without a home and following nobody.
- Smoother frames with many settlers: their periodic work (home planning, ticks, gear and post checks) no longer
  lands in the same frame for everyone loaded together; soldiers without a free post stop searching every frame.
- Fixed an error when looking at a settler at the moment it disappears (zone unloaded or removed).

## 0.4.0

- Settlers are knocked out instead of dying (Settlers/PermanentDeath, off by default): they fall for a moment,
  nobody attacks them, they keep all their gear and get up with a little health. With permanent death on, their gear
  waits in a grave anyone can open.
- 13 traits instead of 6 (nimble, cowardly, hawk-eyed, green-thumbed, night owl, hardened, veteran); work speed,
  carry weight and melee damage from traits now really apply.
- Combat roles Spearman (tier 4) and Berserker (tier 5); tier 6 adds a worker place at every totem, tier 7 twice as
  fast healing in bed. The next tier's unlocks are listed with its requirements.
- Catch-up for every job: smelter hands and cooks work through the chests' stock at their stations' pace, farmers
  bring in the crops that ripened and replant them.
- [Shift+E] on a chest in the settlement gives it a kind (wood, ores, food...): settlers put only that kind in it.
- Totem priorities and automatic work: free civilians take free places at the totems by themselves, highest
  priority first (a settlement can switch to manual assignment). Woodcutter totems can replant the trees they fell.
- Captives sit behind bars: beat the guards or break the bars to free them.
- Sieges come from two sides from tier 4 and three from tier 6, with a siege unit that goes for the buildings;
  Builders rebuild what a siege destroyed with materials from the chests.
- Soldiers wear out their weapons (Army/GearWear, half a player's rate); worn-out ones go back to the Armory.
- A settlement's alarm puts a map pin at it for every player; the chronicle names who did what.
- aoj_debug shows the live state of settlers nearby.

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
