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
            Object.Instantiate(prefab, spot, Quaternion.identity).GetComponent<Settler>()?.MakeCaptive();
            foreach (string guard in new[] { "Greydwarf", "Greydwarf" })
            {
                GameObject guardPrefab = ZNetScene.instance.GetPrefab(guard);
                if (guardPrefab != null)
                {
                    Object.Instantiate(guardPrefab, spot + Random.insideUnitSphere * 4f + Vector3.up, Quaternion.identity);
                }
            }
            context.AddString("A captive waits 8 m ahead, guarded. Beat the guards, then press [E] on it.");
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
            context.AddString($"{JarlTable.DisplayName(data)}: tier {data.Tier}, {data.Settlers.Count} settler(s), alarm {(table.AlarmOn ? "ON" : "off")}, fame {table.Fame}, besieged {Sieges.SiegeDirector.IsBesieged(table.SettlementId)}");
            foreach (WorkTotem totem in WorkTotem.Loaded.Where(t => t != null && t.Settlement == table))
            {
                context.AddString($"  totem {totem.Job}: {string.Join(", ", totem.Workers().Select(w => w.DisplayName))} ({totem.Workers().Count}/{totem.Capacity}), radius {totem.Radius:0} m");
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
                                  $"activity {(SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)}{(settler.JobProblem.Length > 0 ? ", problem " + settler.JobProblem : "")}");
            }
        }
    }
}
