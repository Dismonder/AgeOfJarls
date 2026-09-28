using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.Commands
{
    /// <summary><c>aoj_catchup &lt;game hours&gt;</c>: credits the work of an absence right away (cheat).</summary>
    internal sealed class CatchUpCommand : AojCommand
    {
        public override string Name => "aoj_catchup";

        public override string Help => "Age of Jarls: credit your settlement the work of an absence of <game hours> (cheat)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            if (!table.NetView.IsOwner())
            {
                context.AddString("Only the machine that owns the Jarl's Table can do this; stand closer or try again.");
                return;
            }
            float hours = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Max(0.1f, parsed)
                : 24f;
            SettlementSim.CatchUp(table, data, hours * WorldClock.DayLength / 24.0);
            context.AddString($"Caught up {hours:0.#} game hour(s); see the chronicle.");
        }
    }

    /// <summary><c>aoj_siege</c>: starts a siege of the settlement you stand in (cheat).</summary>
    internal sealed class SiegeCommand : AojCommand
    {
        public override string Name => "aoj_siege";

        public override string Help => "Age of Jarls: besiege the settlement you stand in now (cheat)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            if (!table.NetView.IsOwner())
            {
                context.AddString("Only the machine that owns the Jarl's Table can start it.");
                return;
            }
            context.AddString(Sieges.SiegeDirector.Start(table, data, table.SettlementId) ? "The siege begins." : "No siege: already besieged or no dry ground around.");
        }
    }

    /// <summary><c>aoj_morale &lt;0-100&gt;</c>: sets the morale (and fills the bellies) of the settlers you simulate (cheat).</summary>
    internal sealed class MoraleCommand : AojCommand
    {
        public override string Name => "aoj_morale";

        public override string Help => "Age of Jarls: set the morale of nearby settlers and feed them <0-100> (cheat)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            float value = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Clamp(parsed, 0f, 100f)
                : 80f;
            int count = 0;
            foreach (Settler settler in Settler.Loaded.Where(s => s != null && s.Zdo != null && s.GetComponent<ZNetView>().IsOwner()))
            {
                settler.Zdo.Set(Keys.ZdoSettlerMorale, value);
                settler.Zdo.Set(Keys.ZdoSettlerSatiety, Mathf.Max(value, 60f));
                count++;
            }
            context.AddString($"Morale {value:0} for {count} settler(s) simulated by this machine.");
        }
    }

    /// <summary><c>aoj_captive</c>: a captive with two guards right here, to try freeing one (cheat).</summary>
    internal sealed class CaptiveCommand : AojCommand
    {
        public override string Name => "aoj_captive";

        public override string Help => "Age of Jarls: spawn a captive settler and two guards 8 m ahead (cheat)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Keys.SettlerPrefab) : null;
            if (me == null || prefab == null)
            {
                return;
            }
            Vector3 spot = me.transform.position + me.transform.forward * 8f + Vector3.up;
            GameObject captive = Object.Instantiate(prefab, spot, Quaternion.identity);
            captive.GetComponent<Settler>()?.MakeCaptive(caged: Recruitment.CaptiveCage.Place(spot, Heightmap.Biome.Meadows));
            foreach (string guard in new[] { "Greydwarf", "Greydwarf" })
            {
                GameObject guardPrefab = ZNetScene.instance.GetPrefab(guard);
                if (guardPrefab != null)
                {
                    Object.Instantiate(guardPrefab, spot + Random.insideUnitSphere * 4f + Vector3.up, Quaternion.identity);
                }
            }
            context.AddString("A captive waits 8 m ahead behind bars, guarded. Beat the guards or break the bars, then press [E] on it.");
        }
    }

    /// <summary><c>aoj_debug</c>: live state over every settler nearby (read only, so not a cheat).</summary>
    internal sealed class DebugCommand : AojCommand
    {
        public override string Name => "aoj_debug";

        public override string Help => "Age of Jarls: toggle live settler state over their heads (ZDO, and AI for the settlers you simulate)";

        public override void Run(string[] args, Terminal context)
        {
            bool on = UI.DebugOverlay.Toggle();
            context?.AddString(on
                ? "Settler debug ON: state over settlers within 40 m. [mine] = your machine runs its AI. aoj_debug again to hide."
                : "Settler debug OFF.");
        }
    }

    /// <summary><c>aoj_perf [seconds]</c>: what the mod costs per frame on this machine (read only, so not a cheat).</summary>
    internal sealed class PerfCommand : AojCommand
    {
        private const float DefaultSeconds = 10f;
        private const float MaxSeconds = 120f;

        public override string Name => "aoj_perf";

        public override string Help => $"Age of Jarls: measure the mod's cost per frame on this machine for [seconds, default {DefaultSeconds:0}]";

        public override void Run(string[] args, Terminal context)
        {
            float seconds = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Clamp(parsed, 1f, MaxSeconds)
                : DefaultSeconds;
            context?.AddString(Perf.Begin(seconds)
                ? $"Measuring for {seconds:0} s; the report comes here and to the BepInEx log."
                : "A measurement is already running.");
        }
    }

    /// <summary><c>aoj_despawn [radius]</c>: removes test settlers - homeless and not following anyone (cheat).</summary>
    internal sealed class DespawnCommand : AojCommand
    {
        private const float DefaultRadius = 10f;
        private const float MaxRadius = 50f;

        public override string Name => "aoj_despawn";

        public override string Help => $"Age of Jarls: remove settlers without a home that follow nobody, within [radius m, default {DefaultRadius:0}] (cheat)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNetScene.instance == null)
            {
                return;
            }
            float radius = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Clamp(parsed, 1f, MaxRadius)
                : DefaultRadius;
            int removed = 0;
            foreach (Settler settler in Settler.Loaded.ToArray())
            {
                ZNetView view = settler != null ? settler.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid() || settler.HasHome || settler.FollowedPlayerId != 0L ||
                    Vector3.Distance(settler.transform.position, me.transform.position) > radius)
                {
                    continue;
                }
                // Only the owner may destroy the ZDO for everyone.
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(settler.gameObject);
                removed++;
            }
            context?.AddString($"Removed {removed} settler(s) without a home within {radius:0} m.");
        }
    }

    /// <summary>
    /// <c>aoj_totem [upgrade]</c>: the nearest Work Totem - level, pace, places and the next level's cost; "upgrade"
    /// buys that level exactly like the button in its window (same rights, same payment), so not a cheat.
    /// </summary>
    internal sealed class TotemCommand : AojCommand
    {
        private const float Range = 10f;
        private static readonly List<string> Options = new List<string> { "upgrade", "release", "assign", "priority", "radius", "replant" };

        public override string Name => "aoj_totem";

        public override string Help => $"Age of Jarls: the nearest totem within {Range:0} m (level, places, next upgrade); " +
                                       "'upgrade' buys its next level, 'release' frees its workers, 'assign <settler>' gives one the job, " +
                                       $"'priority low|normal|high' and 'radius <{WorkTotem.MinRadius:0}-{WorkTotem.MaxRadius:0}>' set them (like its window)";

        public override List<string> CommandOptionList() => Options;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            WorkTotem totem = me == null ? null : WorkTotem.Loaded
                .Where(t => t != null && Vector3.Distance(t.transform.position, me.transform.position) <= Range)
                .OrderBy(t => Vector3.Distance(t.transform.position, me.transform.position))
                .FirstOrDefault();
            if (totem == null)
            {
                context?.AddString($"No totem within {Range:0} m.");
                return;
            }
            if (args.Length > 0 && args[0].Equals("release", System.StringComparison.OrdinalIgnoreCase))
            {
                List<Settler> workers = totem.Workers();
                workers.ForEach(w => w.RequestSetJob(0L));
                context?.AddString($"{totem.Job}: released {workers.Count} worker(s); free settlers take free places by themselves.");
                return;
            }
            if (args.Length > 1 && args[0].Equals("assign", System.StringComparison.OrdinalIgnoreCase))
            {
                // Like choosing the job in the settler's window: its owner checks the rights.
                string name = string.Join(" ", args.Skip(1));
                Settler settler = Settler.Loaded.FirstOrDefault(s => s != null && s.Identity != null &&
                                                                     s.Identity.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
                if (settler == null)
                {
                    context?.AddString($"No loaded settler called {name}.");
                    return;
                }
                settler.RequestSetJob(totem.Id);
                context?.AddString($"{settler.DisplayName} now works as {totem.Job} here.");
                return;
            }
            if (args.Length > 1 && args[0].Equals("priority", System.StringComparison.OrdinalIgnoreCase))
            {
                int priority = args[1].ToLowerInvariant() == "high" ? WorkTotem.HighPriority
                    : args[1].ToLowerInvariant() == "low" ? WorkTotem.LowPriority
                    : WorkTotem.NormalPriority;
                totem.RequestPriority(priority);
                context?.AddString($"{totem.Job}: priority {ConsoleCommands.Localize(WorkTotem.PriorityToken(priority))}.");
                return;
            }
            if (args.Length > 1 && args[0].Equals("replant", System.StringComparison.OrdinalIgnoreCase))
            {
                bool replant = args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase);
                totem.RequestReplant(replant);
                context?.AddString($"{totem.Job}: replanting {(replant ? "on" : "off")}{(totem.Job == JobType.Woodcutter ? "" : " (only woodcutters replant)")}.");
                return;
            }
            if (args.Length > 1 && args[0].Equals("radius", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float radius))
                {
                    context?.AddString($"usage: aoj_totem radius <{WorkTotem.MinRadius:0}-{WorkTotem.MaxRadius:0}>");
                    return;
                }
                radius = Mathf.Clamp(radius, WorkTotem.MinRadius, WorkTotem.MaxRadius);
                // The owner checks the rights, as for the window's slider.
                totem.RequestRadius(radius);
                context?.AddString($"{totem.Job}: radius {radius:0} m.");
                return;
            }
            if (args.Length > 0 && args[0].Equals("upgrade", System.StringComparison.OrdinalIgnoreCase))
            {
                // Read before: the owner (this machine, alone) raises the level while the request is sent.
                int target = totem.Level + 1;
                string problem = totem.TryUpgrade(me);
                context?.AddString(problem == null ? $"{totem.Job}: upgrade to level {target} sent." : ConsoleCommands.Localize(problem));
                return;
            }
            int level = totem.Level;
            string next = level < WorkTotem.MaxLevel
                ? ", next level: " + ConsoleCommands.Localize(WorkTotem.CostText(WorkTotem.UpgradeCost(level + 1)))
                : ", highest level";
            context?.AddString($"{totem.Job}: level {level}/{WorkTotem.MaxLevel}, pace x{totem.PaceBonus:0.00}, workers {totem.WorkerCount()}/{totem.Capacity}, " +
                               $"radius {totem.Radius:0} m, priority {ConsoleCommands.Localize(WorkTotem.PriorityToken(totem.Priority))}{next}");
        }
    }

    /// <summary><c>aoj_tier &lt;level&gt;</c>: the tier of the settlement you stand in, without costs or boss (cheat, for testing).</summary>
    internal sealed class TierCommand : AojCommand
    {
        public override string Name => "aoj_tier";

        public override string Help => "Age of Jarls: set the tier of the settlement you stand in, without costs or boss (cheat, for testing): aoj_tier <level>";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            JarlTable table = me != null ? JarlTable.FindContaining(me.transform.position) : null;
            if (table == null)
            {
                context?.AddString("Stand inside your settlement first.");
                return;
            }
            if (args.Length == 0 || !int.TryParse(args[0], out int tier))
            {
                context?.AddString("usage: aoj_tier <level>");
                return;
            }
            string problem = table.CheatSetTier(tier);
            context?.AddString(problem ?? $"{JarlTable.DisplayName(table.Data)}: tier {table.Data?.Tier}, radius {table.Radius:0} m, " +
                                          $"{table.Capacity} settlers.");
        }
    }

    /// <summary>
    /// <c>aoj_place &lt;piece&gt; [distance]</c>: a building piece on the ground in front of you, placed the way the hammer
    /// does, without its cost (cheat, for setting up tests: a furnace, a cooking station, a totem).
    /// </summary>
    internal sealed class PlaceCommand : AojCommand
    {
        private const float DefaultDistance = 3f;

        public override string Name => "aoj_place";

        public override string Help => $"Age of Jarls: place a building piece on the ground in front of you, free (cheat, for tests): aoj_place <piece prefab> [distance m, default {DefaultDistance:0}]";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            GameObject prefab = args.Length > 0 && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(args[0]) : null;
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (me == null || piece == null || ZoneSystem.instance == null)
            {
                context?.AddString(args.Length == 0 ? "usage: aoj_place <piece prefab> [distance]" : $"'{args[0]}' is no building piece.");
                return;
            }
            float distance = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float d) ? Mathf.Clamp(d, 1f, 20f) : DefaultDistance;
            Vector3 forward = me.transform.forward;
            forward.y = 0f;
            Vector3 spot = me.transform.position + forward.normalized * distance;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            // Facing the player, as one usually builds.
            Quaternion rotation = Quaternion.LookRotation(-forward.normalized, Vector3.up);
            me.PlacePiece(piece, spot, rotation, doAttack: false);
            context?.AddString($"Placed {prefab.name} at {spot.x:0}, {spot.z:0}.");
        }
    }

    /// <summary><c>aoj_fill &lt;item&gt; [amount]</c>: items into the nearest chest within reach (cheat, for testing haulers).</summary>
    internal sealed class FillCommand : AojCommand
    {
        private const float Range = 5f;

        public override string Name => "aoj_fill";

        public override string Help => $"Age of Jarls: put items into the nearest chest within {Range:0} m (cheat, for tests): aoj_fill <item prefab> [amount]";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            GameObject prefab = args.Length > 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(args[0]) : null;
            if (me == null || prefab == null || prefab.GetComponent<ItemDrop>() == null)
            {
                context?.AddString(args.Length == 0 ? "usage: aoj_fill <item prefab> [amount]" : $"Unknown item '{args[0]}'.");
                return;
            }
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(me.transform.position, Range, pieces);
            Container chest = pieces.Select(p => p.GetComponent<Container>())
                .Where(c => c != null && c.m_nview != null && c.m_nview.IsValid())
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position))
                .FirstOrDefault();
            if (chest == null)
            {
                context?.AddString($"No chest within {Range:0} m.");
                return;
            }
            if (!ChestAccess.Acquire(chest, ask: true))
            {
                context?.AddString("The chest belongs to another machine: asked for it, try again in a moment.");
                return;
            }
            int amount = args.Length > 1 && int.TryParse(args[1], out int n) ? Mathf.Clamp(n, 1, 999) : 1;
            chest.Load();
            // Checked first: the game drops at the player's feet what a full inventory cannot take.
            if (!chest.GetInventory().CanAddItem(prefab, amount))
            {
                context?.AddString($"The chest at {chest.transform.position.x:0},{chest.transform.position.z:0} has no room for {amount}x {prefab.name}.");
                return;
            }
            ItemDrop.ItemData added = chest.GetInventory().AddItem(prefab.name, amount, 1, 0, 0L, "", false);
            context?.AddString(added != null ? $"Put {amount}x {prefab.name} into the chest at {chest.transform.position.x:0},{chest.transform.position.z:0}." : "The chest has no room for it.");
        }
    }

    /// <summary><c>aoj_role &lt;settler&gt; &lt;role&gt;</c>: a settler's combat role, as in its window (its owner checks the rights).</summary>
    internal sealed class RoleCommand : AojCommand
    {
        public override string Name => "aoj_role";

        public override string Help => "Age of Jarls: give a settler a combat role, as in its window: aoj_role <settler> <none|warrior|archer|shieldbearer|spearman|berserker>";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length < 2 || !System.Enum.TryParse(args[args.Length - 1], true, out Army.CombatRole role))
            {
                context?.AddString("usage: aoj_role <settler> <none|warrior|archer|shieldbearer|spearman|berserker>");
                return;
            }
            string name = string.Join(" ", args.Take(args.Length - 1));
            Settler settler = Settler.Loaded.FirstOrDefault(s => s != null && s.Identity != null &&
                                                                 s.Identity.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
            if (settler == null)
            {
                context?.AddString($"No loaded settler called {name}.");
                return;
            }
            settler.RequestSetRole(role);
            context?.AddString($"{settler.DisplayName}: {role}.");
        }
    }

    /// <summary><c>aoj_alarm on|off</c>: the alarm of the settlement you stand in, as the table's window sounds it.</summary>
    internal sealed class AlarmCommand : AojCommand
    {
        public override string Name => "aoj_alarm";

        public override string Help => "Age of Jarls: sound or end the alarm of the settlement you stand in, as its window does: aoj_alarm on|off";

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            JarlTable table = me != null ? JarlTable.FindContaining(me.transform.position) : null;
            if (table == null || args.Length == 0)
            {
                context?.AddString(table == null ? "Stand inside your settlement first." : "usage: aoj_alarm on|off");
                return;
            }
            bool on = args[0].Equals("on", System.StringComparison.OrdinalIgnoreCase);
            table.RequestAlarm(on);
            context?.AddString($"Alarm {(on ? "sounded" : "ended")}.");
        }
    }

    /// <summary><c>aoj_breach</c>: the nearest building breaks as in a siege, listed for the Builders (cheat, for testing).</summary>
    internal sealed class BreachCommand : AojCommand
    {
        private const float Range = 6f;

        public override string Name => "aoj_breach";

        public override string Help => $"Age of Jarls: break the nearest building piece within {Range:0} m as a siege would, for the Builders to put back; " +
                                       "'damage [name part]' only halves its health, for them to repair (cheat, for tests)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            JarlTable table = me != null ? JarlTable.FindContaining(me.transform.position) : null;
            if (table == null)
            {
                context?.AddString("Stand inside your settlement first.");
                return;
            }
            bool damage = args.Length > 0 && args[0].Equals("damage", System.StringComparison.OrdinalIgnoreCase);
            string part = damage && args.Length > 1 ? args[1] : "";
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(me.transform.position, Range, pieces);
            Piece piece = pieces
                .Where(p => p != null && p.GetComponent<WearNTear>() != null && Breaches.IsRebuildable(Utils.GetPrefabName(p.gameObject)) &&
                            Utils.GetPrefabName(p.gameObject).IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => Vector3.Distance(p.transform.position, me.transform.position))
                .FirstOrDefault();
            if (piece == null)
            {
                context?.AddString($"No building a Builder puts back within {Range:0} m.");
                return;
            }
            string name = Utils.GetPrefabName(piece.gameObject);
            if (damage)
            {
                WearNTear wear = piece.GetComponent<WearNTear>();
                if (!wear.m_nview.IsValid() || !wear.m_nview.IsOwner())
                {
                    context?.AddString($"{name} belongs to another machine: stand closer to it.");
                    return;
                }
                wear.ApplyDamage(wear.m_health * 0.5f);
                Vector3 at = piece.transform.position;
                context?.AddString($"{name} at {at.x:0},{at.y:0},{at.z:0} damaged to {wear.GetHealthPercentage():P0}; a Builder repairs it.");
                return;
            }
            string problem = table.CheatBreach(piece);
            context?.AddString(problem ?? $"{name} broken; a Builder with materials in the chests puts it back.");
        }
    }

    /// <summary><c>aoj_give &lt;item&gt; [amount]</c>: an item for the nearest settler, handed over like a player does (cheat).</summary>
    internal sealed class GiveCommand : AojCommand
    {
        private const float Range = 10f;

        public override string Name => "aoj_give";

        public override string Help => "Age of Jarls: give the nearest settler (or the one named) an item, as if handed over (cheat): aoj_give <item> [amount] [name]";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            GameObject prefab = args.Length > 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(args[0]) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (me == null || drop == null)
            {
                context?.AddString(args.Length == 0 ? "usage: aoj_give <item prefab> [amount]" : $"Unknown item '{args[0]}'.");
                return;
            }
            string name = args.Length > 2 ? args[2] : null;
            Settler settler = Settler.Loaded
                .Where(s => s != null && s.Identity != null && (name != null
                    ? s.Identity.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)
                    : Vector3.Distance(s.transform.position, me.transform.position) <= Range))
                .OrderBy(s => Vector3.Distance(s.transform.position, me.transform.position))
                .FirstOrDefault();
            if (settler == null)
            {
                context?.AddString(name != null ? $"No loaded settler called {name}." : $"No settler within {Range:0} m.");
                return;
            }
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Mathf.Clamp(args.Length > 1 && int.TryParse(args[1], out int amount) ? amount : 1, 1, item.m_shared.m_maxStackSize);
            context?.AddString(settler.Give(item)
                ? $"Gave {settler.DisplayName} {item.m_stack}x {prefab.name}."
                : $"{settler.DisplayName} could not take it.");
        }
    }

    /// <summary><c>aoj_trace</c>: settler decisions into the BepInEx log, for judging behaviour over minutes (read only).</summary>
    internal sealed class TraceCommand : AojCommand
    {
        public override string Name => "aoj_trace";

        public override string Help => "Age of Jarls: toggle a timeline of settler decisions (activities, chest trips, loot, stuck paths) in the BepInEx log";

        public override void Run(string[] args, Terminal context)
        {
            AI.AiTrace.On = !AI.AiTrace.On;
            context?.AddString(AI.AiTrace.On
                ? "Settler trace ON: decisions of the settlers this machine simulates go to the BepInEx log as [Trace]. aoj_trace again to stop."
                : "Settler trace OFF.");
        }
    }

    /// <summary><c>aoj_info</c>: the settlement you stand in, its work, food and defence at a glance.</summary>
    internal sealed class InfoCommand : AojCommand
    {
        public override string Name => "aoj_info";

        public override string Help => "Age of Jarls: status of the settlement you stand in (work, food, defence)";

        public override void Run(string[] args, Terminal context)
        {
            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            context.AddString($"{JarlTable.DisplayName(data)}: tier {data.Tier}, {data.Settlers.Count} settler(s), alarm {(table.AlarmOn ? "ON" : "off")}, fame {table.Fame}, besieged {table.UnderSiege}, breaches {table.BreachList.Count}");
            foreach (WorkTotem totem in WorkTotem.Loaded.Where(t => t != null && t.Settlement == table))
            {
                Vector3 at = totem.transform.position;
                context.AddString($"  totem {totem.Job} @{at.x:0},{at.z:0}: {string.Join(", ", totem.Workers().Select(w => w.DisplayName))} ({totem.Workers().Count}/{totem.Capacity}), radius {totem.Radius:0} m");
            }
            foreach (RosterEntry entry in data.Settlers)
            {
                Settler settler = Settler.FindByUid(entry.Uid);
                if (settler?.Zdo == null)
                {
                    context.AddString($"  {entry.Name}: far away");
                    continue;
                }
                ZDO zdo = settler.Zdo;
                context.AddString($"  {settler.DisplayName}: satiety {Needs.Satiety(zdo):0}, morale {Needs.Morale(zdo):0}, job {(settler.JobTotem?.Job.ToString() ?? "-")}, role {settler.Role}, " +
                                  $"activity {(SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)}{(settler.JobProblem.Length > 0 ? ", problem: " + ConsoleCommands.Localize(settler.JobProblem) : "")}");
            }
        }
    }
}
