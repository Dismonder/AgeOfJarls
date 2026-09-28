using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Net;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace AgeOfJarls.Commands
{
    internal static class ConsoleCommands
    {
        internal static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new VersionCommand());
            CommandManager.Instance.AddConsoleCommand(new DefsCommand());
            CommandManager.Instance.AddConsoleCommand(new ReloadDefsCommand());
            CommandManager.Instance.AddConsoleCommand(new SpawnSettlerCommand());
            CommandManager.Instance.AddConsoleCommand(new ListSettlersCommand());
            CommandManager.Instance.AddConsoleCommand(new SettlementInfoCommand());
            CommandManager.Instance.AddConsoleCommand(new RankCommand());
            CommandManager.Instance.AddConsoleCommand(new JarlCommand());
            CommandManager.Instance.AddConsoleCommand(new RenameCommand());
            CommandManager.Instance.AddConsoleCommand(new CatchUpCommand());
            CommandManager.Instance.AddConsoleCommand(new SiegeCommand());
            CommandManager.Instance.AddConsoleCommand(new MoraleCommand());
            CommandManager.Instance.AddConsoleCommand(new CaptiveCommand());
            CommandManager.Instance.AddConsoleCommand(new InfoCommand());
            CommandManager.Instance.AddConsoleCommand(new DebugCommand());
            CommandManager.Instance.AddConsoleCommand(new PerfCommand());
            CommandManager.Instance.AddConsoleCommand(new DespawnCommand());
            CommandManager.Instance.AddConsoleCommand(new DumpCommand());
            CommandManager.Instance.AddConsoleCommand(new TotemCommand());
            CommandManager.Instance.AddConsoleCommand(new TraceCommand());
            CommandManager.Instance.AddConsoleCommand(new GiveCommand());
        }

        internal static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text, words) : text;

        /// <summary>The settlement the local player stands in, or null (with a console hint).</summary>
        internal static JarlTable SettlementHere(Terminal context, out SettlementData data)
        {
            Player me = Player.m_localPlayer;
            JarlTable table = me != null ? JarlTable.FindContaining(me.transform.position) : null;
            data = table != null ? table.Data : null;
            if (data == null)
            {
                context?.AddString("Stand inside your settlement first.");
                return null;
            }
            return table;
        }

        /// <summary>Another loaded (i.e. nearby) player with that name, ignoring case.</summary>
        internal static Player NearbyPlayer(string name) =>
            Player.GetAllPlayers().FirstOrDefault(p => p != Player.m_localPlayer && string.Equals(p.GetPlayerName(), name, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Routes the context-less overload to the console, so commands only implement Run(args, context).</summary>
    internal abstract class AojCommand : ConsoleCommand
    {
        public sealed override void Run(string[] args) => Run(args, global::Console.instance);

        public abstract override void Run(string[] args, Terminal context);
    }

    internal sealed class VersionCommand : AojCommand
    {
        public override string Name => "aoj_version";

        public override string Help => "Age of Jarls: mod version and active definitions (compare the hash between players)";

        public override void Run(string[] args, Terminal context)
        {
            context?.AddString($"{PluginInfo.Name} {PluginInfo.Version} | definitions: {DefsRegistry.Describe()}");
        }
    }

    internal sealed class DefsCommand : AojCommand
    {
        private static readonly List<string> Options = new List<string> { "traits", "tiers", "names" };

        public override string Name => "aoj_defs";

        public override string Help => "Age of Jarls: list active definitions [traits|tiers|names]";

        public override List<string> CommandOptionList() => Options;

        public override void Run(string[] args, Terminal context)
        {
            if (context == null)
            {
                return;
            }

            DefsBundle defs = DefsRegistry.Current;
            context.AddString(DefsRegistry.Describe());
            switch (args.Length > 0 ? args[0].ToLowerInvariant() : "")
            {
                case "traits":
                    foreach (TraitDef trait in defs.Traits)
                    {
                        string modifiers = string.Join(" ", trait.Modifiers.Select(m => $"{m.Key}{m.Value:+0.##;-0.##}"));
                        context.AddString($"  {trait.Id} \"{ConsoleCommands.Localize("$aoj_trait_" + trait.Id)}\" {(trait.Positive ? "+" : "-")} weight={trait.Weight} {modifiers}");
                    }
                    break;
                case "tiers":
                    foreach (TierDef tier in defs.Tiers)
                    {
                        string key = tier.RequiredGlobalKey.Length == 0 ? "-" : tier.RequiredGlobalKey;
                        context.AddString($"  {tier.Level} \"{ConsoleCommands.Localize("$aoj_tier_" + tier.Level)}\" key={key} settlers={tier.MaxSettlers} radius={tier.Radius}m unlocks={string.Join(",", tier.Unlocks)}");
                    }
                    break;
                case "names":
                    context.AddString($"  male ({defs.Names.Male.Count}): {string.Join(", ", defs.Names.Male)}");
                    context.AddString($"  female ({defs.Names.Female.Count}): {string.Join(", ", defs.Names.Female)}");
                    break;
                default:
                    context.AddString("  usage: aoj_defs traits|tiers|names");
                    break;
            }
        }
    }

    internal sealed class ReloadDefsCommand : AojCommand
    {
        public override string Name => "aoj_reload_defs";

        public override string Help => "Age of Jarls: reload definitions from BepInEx/config/AgeOfJarls (host or single player)";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                context?.AddString("Only the host can reload definitions; clients always use the server's set.");
                return;
            }
            if (!DefsRegistry.LoadLocal())
            {
                context?.AddString("Reload failed, previous definitions kept (see the BepInEx log).");
                return;
            }

            int peers = DefsSync.BroadcastToPeers();
            context?.AddString($"Reloaded: {DefsRegistry.Describe()} | sent to {peers} connected player(s)");
        }
    }

    internal sealed class SpawnSettlerCommand : AojCommand
    {
        private const int MaxCount = 10;
        private const float Distance = 3f;
        private const float Spacing = 1.5f;

        public override string Name => "aoj_spawn";

        public override string Help => $"Age of Jarls: spawn settlers in front of you [count 1-{MaxCount}] [weapon prefab, e.g. SwordBronze]";

        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Keys.SettlerPrefab) : null;
            if (player == null || prefab == null)
            {
                context?.AddString(player == null ? "Load into a world first." : $"{Keys.SettlerPrefab} is not registered, see the BepInEx log.");
                return;
            }

            int count = args.Length > 0 && int.TryParse(args[0], out int parsed) ? Mathf.Clamp(parsed, 1, MaxCount) : 1;
            GameObject weapon = null;
            if (args.Length > 1)
            {
                weapon = ObjectDB.instance.GetItemPrefab(args[1]);
                if (weapon == null || weapon.GetComponent<ItemDrop>() == null)
                {
                    context?.AddString($"Unknown item '{args[1]}'.");
                    return;
                }
            }

            Transform origin = player.transform;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = origin.position + origin.forward * Distance + origin.right * ((i - (count - 1) / 2f) * Spacing) + Vector3.up * 0.5f;
                GameObject settler = Object.Instantiate(prefab, position, Quaternion.LookRotation(-origin.forward));
                if (weapon != null)
                {
                    Equip(settler.GetComponent<Humanoid>(), weapon);
                }
            }
            context?.AddString($"Spawned {count} settler(s){(weapon != null ? $" with {weapon.name}" : "")}.");
        }

        private static void Equip(Humanoid humanoid, GameObject itemPrefab)
        {
            ItemDrop.ItemData item = itemPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = itemPrefab;
            if (humanoid.GetInventory().AddItem(item))
            {
                humanoid.EquipItem(item);
            }
        }
    }

    internal sealed class ListSettlersCommand : AojCommand
    {
        public override string Name => "aoj_settlers";

        public override string Help => "Age of Jarls: list settlers loaded around you";

        public override void Run(string[] args, Terminal context)
        {
            if (context == null)
            {
                return;
            }

            Vector3 center = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            int count = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                Settler settler = character.GetComponent<Settler>();
                if (settler == null)
                {
                    continue;
                }

                count++;
                SettlerIdentity identity = settler.Identity;
                string who = identity == null
                    ? "identity pending"
                    : $"{(identity.Female ? "F" : "M")}, {identity.Origin}, {string.Join(", ", identity.Traits.Select(t => ConsoleCommands.Localize(Settler.TraitToken(t, identity.Female))))}";
                string owner = character.GetComponent<ZNetView>().IsOwner() ? "owner: me" : "owner: other player";
                context.AddString($"  {character.GetHoverName()} [{who}] hp {character.GetHealth():0}/{character.GetMaxHealth():0}, {Vector3.Distance(center, character.transform.position):0} m, {owner}");
            }
            context.AddString($"{count} settler(s) loaded");
        }
    }

    internal sealed class SettlementInfoCommand : AojCommand
    {
        private const float SearchRadius = 50f;

        public override string Name => "aoj_settlement";

        public override string Help => "Age of Jarls: show the settlement you stand in (or the nearest Jarl's Table within 50 m)";

        public override void Run(string[] args, Terminal context)
        {
            Player player = Player.m_localPlayer;
            if (context == null || player == null)
            {
                return;
            }

            Vector3 position = player.transform.position;
            JarlTable table = JarlTable.FindContaining(position);
            if (table == null)
            {
                table = JarlTable.FindNearest(position, SearchRadius);
            }
            SettlementData data = table != null ? table.Data : null;
            if (data == null)
            {
                context.AddString("No settlement here.");
                return;
            }

            context.AddString($"{JarlTable.DisplayName(data)} | tier {data.Tier} | radius {JarlTable.RadiusOf(data):0} m (tier {JarlTable.RadiusFor(data.Tier):0} m) | " +
                              $"table {Vector3.Distance(position, table.transform.position):0} m away");
            foreach (SettlementMember member in data.Members)
            {
                context.AddString($"  {member.Role}: {member.Name} ({member.PlayerId})");
            }
            context.AddString($"  you: {data.RoleOf(player.GetPlayerID())} | members {data.Members.Count}, bonus for {JarlTable.ExtraMembers(data)} extra");
            context.AddString($"  id {table.SettlementId:X16} | settlers {data.Settlers.Count}/{JarlTable.CapacityOf(data)} (tier {JarlTable.CapacityFor(data.Tier)}), " +
                              $"with a bed {data.SettlersWithBed} (*): {JarlTable.BuildRosterText(data)}");
        }
    }

    internal sealed class RankCommand : AojCommand
    {
        private static readonly List<string> Ranks = new List<string> { "guest", "karl", "huskarl", "hersir", "jarl" };

        public override string Name => "aoj_rank";

        public override string Help => "Age of Jarls: give a member, or a player nearby, a rank in your settlement " +
                                       "(guest = off the list) <guest|karl|huskarl|hersir|jarl> <player name>";

        public override List<string> CommandOptionList() => Ranks;

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            if (context == null || me == null)
            {
                return;
            }
            if (args.Length < 2 || !System.Enum.TryParse(args[0], true, out SettlementRole rank) || !System.Enum.IsDefined(typeof(SettlementRole), rank))
            {
                context.AddString("usage: aoj_rank <guest|karl|huskarl|hersir|jarl> <player name>");
                return;
            }

            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            string name = string.Join(" ", args.Skip(1));
            // A member by the name on the list (they may be far away or offline), else a player standing nearby.
            SettlementMember member = data.Members.Find(m => string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase));
            Player nearby = member == null ? ConsoleCommands.NearbyPlayer(name) : null;
            if (member == null && nearby == null)
            {
                context.AddString($"No member or player nearby named '{name}'.");
                return;
            }
            long id = member?.PlayerId ?? nearby.GetPlayerID();
            string shown = member?.Name ?? nearby.GetPlayerName();

            // Checked here for instant feedback; the table owner checks again before applying.
            string problem = table.RankProblem(me, id, rank);
            if (problem != null)
            {
                context.AddString(ConsoleCommands.Localize(problem, AoJConfig.MaxJarls.Value.ToString()));
                return;
            }
            table.RequestSetRank(id, shown, rank);
            context.AddString(rank == SettlementRole.Guest
                ? ConsoleCommands.Localize("$aoj_msg_rank_removed", shown)
                : ConsoleCommands.Localize("$aoj_msg_rank_set", shown, ConsoleCommands.Localize(Permissions.Token(rank))));
        }
    }

    internal sealed class JarlCommand : AojCommand
    {
        public override string Name => "aoj_jarl";

        public override string Help => "Age of Jarls: hand your Jarl title to a nearby player, you stay a Hersir (Jarl only) <player name>";

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            if (context == null || me == null)
            {
                return;
            }
            if (args.Length == 0)
            {
                context.AddString("usage: aoj_jarl <player name>");
                return;
            }

            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            if (data.RoleOf(me.GetPlayerID()) != SettlementRole.Jarl)
            {
                context.AddString(ConsoleCommands.Localize("$aoj_msg_only_jarl"));
                return;
            }

            Player target = ConsoleCommands.NearbyPlayer(string.Join(" ", args));
            if (target == null)
            {
                context.AddString("No player with that name nearby.");
                return;
            }
            table.RequestHandOver(target.GetPlayerID(), target.GetPlayerName());
            context.AddString(ConsoleCommands.Localize("$aoj_msg_new_jarl", target.GetPlayerName()));
        }
    }

    internal sealed class RenameCommand : AojCommand
    {
        public override string Name => "aoj_rename";

        public override string Help => "Age of Jarls: rename the settlement you stand in, no name = default name (Jarl or Hersir) [name]";

        public override void Run(string[] args, Terminal context)
        {
            Player me = Player.m_localPlayer;
            if (context == null || me == null)
            {
                return;
            }

            JarlTable table = ConsoleCommands.SettlementHere(context, out SettlementData data);
            if (table == null)
            {
                return;
            }
            if (!data.RoleOf(me.GetPlayerID()).Allows(SettlementRight.Manage))
            {
                context.AddString(Permissions.Denied(SettlementRight.Manage));
                return;
            }

            string name = string.Join(" ", args);
            table.RequestRename(name);
            context.AddString(ConsoleCommands.Localize("$aoj_msg_renamed", name.Length > 0 ? name : "-"));
        }
    }
}
