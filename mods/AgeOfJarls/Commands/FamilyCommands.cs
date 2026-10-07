using System;
using System.Collections.Generic;
using System.Globalization;
using AgeOfJarls.Core;
using AgeOfJarls.Family;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;

namespace AgeOfJarls.Commands
{
    /// <summary>Read-only family list plus owner-only cheats, all using the table's event steps.</summary>
    internal sealed class FamilyCommand : AojCommand
    {
        private static readonly List<string> Options = new List<string> { "list", "couple", "court", "conceive", "birth", "child", "age", "separate", "tick" };

        /// <summary>The registered command name.</summary>
        public override string Name => "aoj_family";

        /// <summary>Console usage; list is available without devcommands.</summary>
        public override string Help => "Age of Jarls: families: list | couple <a> <b> | court <a> <b> | conceive <carrier> | birth <carrier> | child [name] | age <name> <days> | separate <name> | tick (all except list are cheats)";

        /// <summary>Verb-specific cheat checks allow the public list command.</summary>
        public override bool IsCheat => false;

        /// <summary>Verb completion.</summary>
        public override List<string> CommandOptionList() => Options;

        /// <summary>Run against the settlement here; never write another machine's ZDO.</summary>
        public override void Run(string[] args, Terminal context)
        {
            JarlTable table = null;
            SettlementData data = null;
            bool save = false;
            try
            {
                string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
                if (!Options.Contains(verb))
                {
                    context?.AddString(Help);
                    return;
                }
                table = ConsoleCommands.SettlementHere(context, out data);
                if (table == null) return;
                double now = WorldClock.Now;
                if (verb == "list")
                {
                    List(data, now, context);
                    return;
                }
                if (context == null || !context.IsCheatsEnabled())
                {
                    context?.AddString("Enable devcommands first; only 'aoj_family list' is not a cheat.");
                    return;
                }
                if (!table.NetView.IsOwner())
                {
                    context.AddString("Only the machine that owns the Jarl's Table can do this; stand closer or try again.");
                    return;
                }
                // Even a failed notification must not leave a completed birth unsaved.
                save = true;
                string actor = TextUtil.SanitizeName(Player.m_localPlayer.GetPlayerName(), 32);
                if (verb == "tick")
                {
                    bool changed = FamilySim.Tick(table, data, now);
                    context.AddString(changed ? "Family tick changed the settlement." : "Family tick: no changes.");
                    return;
                }
                if (verb == "child")
                {
                    Settler child = ChildSpawner.SpawnTestChild(Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.right * 1.5f,
                        args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "");
                    context.AddString(child != null ? "Test child spawned (no parents or home)." : "Child spawn failed; see the log.");
                    return;
                }
                if (verb == "couple" || verb == "court")
                {
                    if (!FindPair(args, out Settler a, out Settler b))
                    {
                        context.AddString("Supply two loaded settlers' names: aoj_family " + verb + " <a> <b>.");
                        return;
                    }
                    string problem = FamilySim.PairProblem(data, a.Uid, b.Uid, now);
                    if (a.HomeId != table.SettlementId || b.HomeId != table.SettlementId) problem = "$aoj_match_not_here";
                    if (problem != null)
                    {
                        context.AddString(ConsoleCommands.Localize("$aoj_msg_match_bad", ConsoleCommands.Localize(problem)));
                        return;
                    }
                    bool changed = verb == "couple" ? FamilySim.Wed(table, data, a.Uid, b.Uid, now, actor) : FamilySim.StartCourtship(data, a.Uid, b.Uid, now);
                    if (changed && verb == "court") Chronicle.Add(table.NetView, now, "$aoj_chr_courting", a.Identity.Name, b.Identity.Name);
                    context.AddString(changed ? "Family event recorded; see the chronicle." : "Family record limit reached.");
                    return;
                }
                int nameCount = args.Length - (verb == "age" ? 2 : 1);
                Settler settler = nameCount > 0 ? Find(string.Join(" ", args, 1, nameCount)) : null;
                if (settler == null)
                {
                    context.AddString("Name a loaded settler.");
                    return;
                }
                if (verb == "age")
                {
                    // Age persists the actual home itself; a refusal must not write the nearby table.
                    save = false;
                    Age(table, data, settler, args[args.Length - 1], now, context);
                    return;
                }
                if (!data.HasSettler(settler.Uid) || settler.HomeId != table.SettlementId)
                {
                    context.AddString("Name a loaded resident of this settlement.");
                    return;
                }
                if (verb == "separate")
                {
                    context.AddString(FamilySim.Separate(table, data, settler.Uid, now, actor) ? "Partners separated; children and any pregnancy remain." : "This settler has no partner.");
                    return;
                }
                Couple couple = data.FindCouple(settler.Uid);
                if (couple == null || !settler.Identity.Female || (verb == "birth" && (couple.Carrier != settler.Uid || couple.DueAt <= 0)))
                {
                    context.AddString("Name a woman in a couple (for birth, the expecting carrier).");
                    return;
                }
                if (verb == "conceive")
                {
                    if (FamilySim.Conceive(data, couple, settler.Uid, now))
                    {
                        FamilySim.AnnounceConception(table, data, couple, now);
                        context.AddString("Conception recorded; see the chronicle.");
                    }
                    else context.AddString("This couple is already expecting.");
                }
                else if (verb == "birth")
                {
                    couple.DueAt = now;
                    context.AddString(FamilySim.Birth(table, data, couple, now) ? "Child born; see the chronicle." : "Birth is pending; check the prefab and blob limits.");
                }
            }
            catch (Exception e)
            {
                Log.Error("Family", $"Family command failed: {e}");
                context?.AddString("Family command failed; see the log.");
            }
            finally
            {
                if (save && table != null && data != null)
                {
                    try { table.Write(data); }
                    catch (Exception e) { Log.Error("Family", $"Family command save failed: {e}"); }
                }
            }
        }

        private static Settler Find(string name)
        {
            foreach (Settler settler in Settler.Loaded)
            {
                if (settler != null && settler.Identity != null && string.Equals(settler.Identity.Name, name, StringComparison.OrdinalIgnoreCase)) return settler;
            }
            return null;
        }

        // Try boundaries between names so renamed settlers containing spaces work like aoj_role.
        private static bool FindPair(string[] args, out Settler a, out Settler b)
        {
            a = null;
            b = null;
            for (int split = 2; split < args.Length; split++)
            {
                a = Find(string.Join(" ", args, 1, split - 1));
                b = Find(string.Join(" ", args, split, args.Length - split));
                if (a != null && b != null) return true;
            }
            return false;
        }

        private static void Age(JarlTable table, SettlementData data, Settler settler, string daysText, double now, Terminal context)
        {
            if (!double.TryParse(daysText, NumberStyles.Float, CultureInfo.InvariantCulture, out double days) || double.IsNaN(days) || double.IsInfinity(days) || days < 0)
            {
                context.AddString("usage: aoj_family age <name> <non-negative days>");
                return;
            }
            ZNetView view = settler.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                context.AddString("Only the machine that owns this settler can change its age.");
                return;
            }
            double born = now - days * WorldClock.DayLength;
            if (born <= 0)
            {
                context.AddString("That age predates the world clock; choose a smaller number of days.");
                return;
            }
            // Both saved birth times must be writable before either one changes.
            JarlTable home = settler.HasHome ? settler.HomeTable : null;
            SettlementData homeData = home?.Data;
            ChildRecord child = homeData?.FindChild(settler.Uid);
            if (settler.HasHome && (home == null || home.NetView == null || !home.NetView.IsValid() || !home.NetView.IsOwner()))
            {
                context.AddString(ConsoleCommands.Localize("$aoj_family_age_home_owner"));
                return;
            }
            if (settler.HasHome && child == null)
            {
                context.AddString(ConsoleCommands.Localize("$aoj_family_age_missing_record"));
                return;
            }
            WorldClock.Set(view.GetZDO(), Keys.ZdoSettlerBorn, born);
            if (child != null)
            {
                child.Born = born;
                home.Write(homeData);
            }
            context.AddString($"{settler.Identity.Name}: age {days:0.##} days.");
        }

        private static void List(SettlementData data, double now, Terminal context)
        {
            context?.AddString($"Families: {data.Couples.Count} couples, {data.Courtships.Count} courtships, {data.PregnancyCount()} pregnancies, {data.Children.Count} children (including adults).");
            foreach (Couple couple in data.Couples)
            {
                string names = FamilyInfo.NameOf(data, couple.A) + " & " + FamilyInfo.NameOf(data, couple.B);
                context?.AddString(names + $"; since day {(int)(couple.Since / WorldClock.DayLength) + 1}; children: {couple.ChildrenBorn}" +
                    (couple.DueAt > 0 ? $"; {FamilyInfo.NameOf(data, couple.Carrier)} expecting, due in {(couple.DueAt - now) / WorldClock.DayLength:0.##} days" : ""));
            }
            foreach (Courtship court in data.Courtships)
            {
                context?.AddString($"Courting: {FamilyInfo.NameOf(data, court.A)} & {FamilyInfo.NameOf(data, court.B)}; {(now - court.StartedAt) / WorldClock.DayLength:0.##} days.");
            }
            foreach (ChildRecord child in data.Children)
            {
                LifeStage stage = FamilyRules.StageAt(child.Born, now, WorldClock.DayLength, FamilyConfig.Live);
                context?.AddString($"{child.Name}: {ConsoleCommands.Localize(FamilyRules.StageToken(stage, child.Female))}, {(now - child.Born) / WorldClock.DayLength:0.##} days; parents: {FamilyInfo.NameOf(data, child.Mother)} & {FamilyInfo.NameOf(data, child.Father)}.");
            }
        }
    }
}
