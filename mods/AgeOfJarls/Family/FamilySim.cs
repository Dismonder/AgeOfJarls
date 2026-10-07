using System;
using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Family
{
    /// <summary>Family events are evaluated and saved by the table owner, including overdue births.</summary>
    internal static class FamilySim
    {
        private static float s_nextErrorAt;

        /// <summary>Ten-second owner tick; true means all event marks must be written with the roster.</summary>
        internal static bool Tick(JarlTable table, SettlementData data, double now)
        {
            if (!Owns(table) || data == null)
            {
                return false;
            }
            bool changed = false;
            try
            {
                FamilyConfig cfg = FamilyConfig.Live;
                double day = WorldClock.DayLength;
                changed = data.PruneMoods(now);
                var residents = new Dictionary<long, Settler>();
                foreach (Settler s in Settler.Loaded)
                {
                    if (s != null && s.HomeId == table.SettlementId && s.Identity != null && !s.IsCaptive && data.HasSettler(s.Uid))
                    {
                        residents[s.Uid] = s;
                    }
                }
                if (cfg.Enabled)
                {
                    for (int i = data.Couples.Count - 1; i >= 0; i--)
                    {
                        Couple couple = data.Couples[i];
                        if (couple.DueAt > 0 && couple.DueAt <= now)
                        {
                            changed |= Birth(table, data, couple, now);
                        }
                    }
                }
                foreach (ChildRecord child in data.Children)
                {
                    LifeStage stage = FamilyRules.StageAt(child.Born, now, day, cfg);
                    if ((int)stage <= child.LastStage)
                    {
                        continue;
                    }
                    child.LastStage = (int)stage;
                    changed = true;
                    if (stage == LifeStage.Youth || stage == LifeStage.Adult)
                    {
                        Chronicle.Add(table.NetView, now, stage == LifeStage.Youth ? "$aoj_chr_youth" : "$aoj_chr_adult", child.Name);
                        if (stage == LifeStage.Adult)
                        {
                            MessageAll(table, "$aoj_msg_came_of_age", child.Name);
                        }
                    }
                }
                for (int i = data.Courtships.Count - 1; i >= 0; i--)
                {
                    Courtship court = data.Courtships[i];
                    if (!data.HasSettler(court.A) || !data.HasSettler(court.B))
                    {
                        data.Courtships.RemoveAt(i);
                        changed = true;
                    }
                    else if (cfg.Enabled && now - court.StartedAt >= cfg.CourtshipDays * day)
                    {
                        // NoNewCouples forbids new courtships, not weddings already promised.
                        changed |= Wed(table, data, court.A, court.B, now, null);
                    }
                }
                var ordered = new List<long>(residents.Keys);
                ordered.Sort();
                if (cfg.Enabled && !data.HasFamilyFlag(FamilyFlags.NoNewCouples) && data.Courtships.Count < SettlementData.MaxCourtships)
                {
                    foreach (long uid in ordered)
                    {
                        Settler a = residents[uid];
                        if (!SingleAdult(data, a, now, day, cfg) || UnityEngine.Random.value >= cfg.CourtshipChancePerDay * 10 / day)
                        {
                            continue;
                        }
                        Settler nearest = null;
                        float distance = float.MaxValue;
                        foreach (long other in ordered)
                        {
                            Settler b = residents[other];
                            if (uid == other || !SingleAdult(data, b, now, day, cfg) ||
                                PairProblem(data, uid, other, now, day, cfg, a.Identity.Female, b.Identity.Female) != null)
                            {
                                continue;
                            }
                            float d = (a.transform.position - b.transform.position).sqrMagnitude;
                            // Sorted uids also break equal-distance ties consistently.
                            if (d < distance)
                            {
                                distance = d;
                                nearest = b;
                            }
                        }
                        if (nearest != null && StartCourtship(data, uid, nearest.Uid, now))
                        {
                            changed = true;
                            Chronicle.Add(table.NetView, now, "$aoj_chr_courting", Name(data, uid), Name(data, nearest.Uid));
                            break;
                        }
                    }
                }
                if (cfg.Enabled && !data.HasFamilyFlag(FamilyFlags.NoBirths))
                {
                    int minors = data.MinorCount(now, day, cfg);
                    int pregnancies = data.PregnancyCount();
                    int limit = Math.Max(1, (int)Math.Ceiling(data.AdultCount(now, day, cfg) * cfg.MaxChildrenRatio));
                    foreach (Couple couple in data.Couples)
                    {
                        if (minors + pregnancies >= limit)
                        {
                            break;
                        }
                        if (couple.DueAt != 0 || couple.ChildrenBorn >= cfg.MaxChildrenPerCouple ||
                            (couple.LastBirth > 0 && now - couple.LastBirth < cfg.MinDaysBetweenBirths * day) ||
                            !residents.TryGetValue(couple.A, out Settler a) || !residents.TryGetValue(couple.B, out Settler b) ||
                            Needs.Morale(a.Zdo) < cfg.MinMorale || Needs.Morale(b.Zdo) < cfg.MinMorale ||
                            Needs.IsHungry(a.Zdo) || Needs.IsHungry(b.Zdo))
                        {
                            continue;
                        }
                        bool femaleA = a.Identity.Female;
                        bool femaleB = b.Identity.Female;
                        if ((!femaleA && !femaleB) || (cfg.Pairing == FamilyPairing.OppositeSex && femaleA == femaleB))
                        {
                            continue;
                        }
                        long carrier = femaleA && femaleB ? (UnityEngine.Random.value < 0.5f ? couple.A : couple.B) : femaleA ? couple.A : couple.B;
                        if (UnityEngine.Random.value < cfg.PregnancyChancePerDay * 10 / day && Conceive(data, couple, carrier, now))
                        {
                            pregnancies++;
                            changed = true;
                            AnnounceConception(table, data, couple, now);
                        }
                    }
                }
                for (int i = data.Couples.Count - 1; i >= 0; i--)
                {
                    Couple couple = data.Couples[i];
                    bool survivor = couple.DueAt > 0 && couple.Carrier != 0 && data.HasSettler(couple.Carrier) &&
                        ((couple.A == 0 && couple.B == couple.Carrier) || (couple.B == 0 && couple.A == couple.Carrier));
                    if (!survivor && (!data.HasSettler(couple.A) || !data.HasSettler(couple.B)))
                    {
                        data.Couples.RemoveAt(i);
                        changed = true;
                    }
                }
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup >= s_nextErrorAt)
                {
                    s_nextErrorAt = Time.realtimeSinceStartup + 30f;
                    Log.Error("Family", $"Family tick failed: {e}");
                }
                // Persist any event marks already made before a notification or effect failed.
                return true;
            }
            return changed;
        }

        /// <summary>Finish a pregnancy; missing prefabs or hard blob limits leave it pending for retry.</summary>
        internal static bool Birth(JarlTable table, SettlementData data, Couple couple, double now)
        {
            if (!Owns(table) || couple == null || !data.Couples.Contains(couple) || couple.DueAt <= 0 ||
                !FamilyRules.CanDeliver(couple.DueAt, data.HasSettler(couple.Carrier), table.ActivatedAt, now) ||
                data.Children.Count >= SettlementData.MaxChildren || data.Settlers.Count >= SettlementData.MaxRoster)
            {
                return false;
            }
            double born = couple.DueAt;
            Settler child = ChildSpawner.SpawnChild(table, data, couple, born, out ChildRecord record);
            if (child == null || record == null)
            {
                return false;
            }
            // An overdue newborn must still announce its promotion in step 4.
            record.LastStage = (int)LifeStage.Infant;
            data.Children.Add(record);
            data.TryAddSettler(record.Uid, record.Name, int.MaxValue);
            couple.ChildrenBorn++;
            couple.LastBirth = born;
            couple.DueAt = 0;
            couple.Carrier = 0;
            double until = now + 2 * WorldClock.DayLength;
            if (data.HasSettler(record.Mother)) data.AddMood(record.Mother, MoodKind.NewParent, until);
            if (data.HasSettler(record.Father)) data.AddMood(record.Father, MoodKind.NewParent, until);
            if (couple.A == 0 || couple.B == 0) data.Couples.Remove(couple);
            string mother = Name(data, record.Mother);
            string father = record.Father != 0 ? Name(data, record.Father) : mother;
            Chronicle.Add(table.NetView, born, record.Female ? "$aoj_chr_born_daughter" : "$aoj_chr_born_son", record.Name, mother, father);
            MessageAll(table, "$aoj_msg_born", record.Name, mother);
            return true;
        }

        /// <summary>Mark conception; the caller owns the blob and announces the event before writing it.</summary>
        internal static bool Conceive(SettlementData data, Couple couple, long carrierUid, double now)
        {
            if (couple == null || !data.Couples.Contains(couple) || couple.DueAt != 0 || carrierUid == 0 || !couple.Has(carrierUid))
            {
                return false;
            }
            couple.SetPregnancy(carrierUid, now, now + FamilyConfig.Live.PregnancyDays * WorldClock.DayLength);
            return true;
        }

        /// <summary>Publish a conception on the table owner; shared by the tick and console.</summary>
        internal static void AnnounceConception(JarlTable table, SettlementData data, Couple couple, double now)
        {
            string carrier = Name(data, couple.Carrier);
            Chronicle.Add(table.NetView, now, "$aoj_chr_expecting", carrier);
            MessageAll(table, "$aoj_msg_expecting", carrier);
        }

        /// <summary>Natural or arranged wedding; removes the promise and extends, rather than replaces, a feast.</summary>
        internal static bool Wed(JarlTable table, SettlementData data, long a, long b, double now, string actorNameOrNull)
        {
            if (!Owns(table) || a == b || !data.HasSettler(a) || !data.HasSettler(b) ||
                data.FindCouple(a) != null || data.FindCouple(b) != null || data.Couples.Count >= SettlementData.MaxCouples)
            {
                return false;
            }
            data.Courtships.RemoveAll(c => c.Has(a) || c.Has(b));
            data.Couples.Add(data.NewCouple(a, b, now));
            data.AddMood(a, MoodKind.InLove, now + 2 * WorldClock.DayLength);
            data.AddMood(b, MoodKind.InLove, now + 2 * WorldClock.DayLength);
            WorldClock.Set(table.NetView.GetZDO(), Keys.ZdoSettlementFeastUntil,
                Math.Max(table.FeastUntil, now + FamilyConfig.Live.WeddingFeastDays * WorldClock.DayLength));
            Chronicle.Add(table.NetView, now, actorNameOrNull == null ? "$aoj_chr_wedding" : "$aoj_chr_wedding_by", Name(data, a), Name(data, b), actorNameOrNull ?? "");
            MessageAll(table, "$aoj_msg_wedding", Name(data, a), Name(data, b));
            return true;
        }

        /// <summary>Start one promise after eligibility was checked by the caller.</summary>
        internal static bool StartCourtship(SettlementData data, long a, long b, double now)
        {
            if (a == b || !data.HasSettler(a) || !data.HasSettler(b) || data.FindCouple(a) != null || data.FindCouple(b) != null ||
                data.FindCourtship(a) != null || data.FindCourtship(b) != null || data.Courtships.Count >= SettlementData.MaxCourtships)
            {
                return false;
            }
            data.Courtships.Add(new Courtship { A = a, B = b, StartedAt = now });
            return true;
        }

        /// <summary>Separate partners while keeping an underway pregnancy on its carrier's record.</summary>
        internal static bool Separate(JarlTable table, SettlementData data, long uid, double now, string actorName)
        {
            Couple couple = data.FindCouple(uid);
            if (!Owns(table) || !data.HasSettler(uid) || couple == null || couple.A == 0 || couple.B == 0)
            {
                return false;
            }
            string a = Name(data, couple.A);
            string b = Name(data, couple.B);
            SeparateRecord(data, uid);
            Chronicle.Add(table.NetView, now, "$aoj_chr_separated_by", a, b, actorName);
            return true;
        }

        /// <summary>Pure separation: retain the expecting carrier, without removing parent references or children.</summary>
        internal static bool SeparateRecord(SettlementData data, long uid)
        {
            Couple couple = data.FindCouple(uid);
            if (!data.HasSettler(uid) || couple == null || couple.A == 0 || couple.B == 0) return false;
            if (couple.DueAt > 0 && couple.Has(couple.Carrier))
            {
                if (couple.A == couple.Carrier) couple.B = 0;
                else couple.A = 0;
            }
            else data.Couples.Remove(couple);
            return true;
        }

        /// <summary>Eligibility for arranged pairs, using loaded identities for sex and home.</summary>
        internal static string PairProblem(SettlementData data, long a, long b, double now)
        {
            Settler sa = Settler.FindByUid(a);
            Settler sb = Settler.FindByUid(b);
            if (sa == null || sb == null || sa.Identity == null || sb.Identity == null || sa.IsCaptive || sb.IsCaptive ||
                sa.HomeId == 0 || sa.HomeId != sb.HomeId)
            {
                return "$aoj_match_not_here";
            }
            FamilyConfig cfg = FamilyConfig.Live;
            string problem = PairProblem(data, a, b, now, WorldClock.DayLength, cfg, sa.Identity.Female, sb.Identity.Female);
            if (problem != null) return problem;
            if (FamilyRules.StageAt(sa.Born, now, WorldClock.DayLength, cfg) != LifeStage.Adult ||
                FamilyRules.StageAt(sb.Born, now, WorldClock.DayLength, cfg) != LifeStage.Adult) return "$aoj_match_not_adult";
            return Needs.Morale(sa.Zdo) < cfg.MinMorale || Needs.Morale(sb.Zdo) < cfg.MinMorale ? "$aoj_match_morale" : null;
        }

        /// <summary>Pure roster eligibility; the caller supplies sex because the roster does not store it.</summary>
        internal static string PairProblem(SettlementData data, long a, long b, double now, double dayLength, FamilyConfig cfg, bool femaleA, bool femaleB)
        {
            if (data == null || a == b || !data.HasSettler(a) || !data.HasSettler(b)) return "$aoj_match_not_here";
            if (data.IsMinor(a, now, dayLength, cfg) || data.IsMinor(b, now, dayLength, cfg)) return "$aoj_match_not_adult";
            if (data.FindCouple(a) != null || data.FindCouple(b) != null || data.FindCourtship(a) != null || data.FindCourtship(b) != null) return "$aoj_match_taken";
            if (data.AreRelated(a, b)) return "$aoj_match_related";
            if (cfg.Pairing == FamilyPairing.OppositeSex && femaleA == femaleB) return "$aoj_match_same_sex";
            if (data.HasMood(a, MoodKind.Grief, now) || data.HasMood(b, MoodKind.Grief, now)) return "$aoj_match_grieving";
            return null;
        }

        private static bool SingleAdult(SettlementData data, Settler s, double now, double day, FamilyConfig cfg) =>
            !data.IsMinor(s.Uid, now, day, cfg) && FamilyRules.StageAt(s.Born, now, day, cfg) == LifeStage.Adult &&
            data.FindCouple(s.Uid) == null && data.FindCourtship(s.Uid) == null && !data.HasMood(s.Uid, MoodKind.Grief, now) && Needs.Morale(s.Zdo) >= cfg.MinMorale;

        private static bool Owns(JarlTable table) => table != null && table.NetView != null && table.NetView.IsValid() && table.NetView.IsOwner();

        private static string Name(SettlementData data, long uid) => data.FindSettler(uid)?.Name ?? "";

        // Vanilla MessageAll broadcasts world-wide and formats on the sender. Keep these local and translate on each recipient.
        private static void MessageAll(JarlTable table, string token, string first, string second = "")
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if ((player.transform.position - table.transform.position).sqrMagnitude > 80f * 80f) continue;
                ZNetView view = player.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) table.NotifyFamily(view.GetZDO().GetOwner(), token, first, second);
            }
        }
    }
}
