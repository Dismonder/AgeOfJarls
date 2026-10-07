using System.Collections.Generic;
using AgeOfJarls.Settlement;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// What the settlement blob says about one settler's family, as a view the settler keeps (Settler.Family) and
    /// rebuilds only when the table's data revision changes. Read on every machine; the Jarl's Table owner alone writes
    /// the records. <see cref="Empty"/> stands in until a valid snapshot is available for this home.
    /// </summary>
    internal sealed class FamilyInfo
    {
        /// <summary>Shared "nothing known": never mutate it.</summary>
        internal static readonly FamilyInfo Empty = new FamilyInfo();

        /// <summary>Uid and roster name of the partner; 0 / "" when single.</summary>
        internal long PartnerUid;
        internal string PartnerName = "";
        /// <summary>Uid and roster name of the settler it is courting; 0 / "" when not courting.</summary>
        internal long CourtingUid;
        internal string CourtingName = "";
        /// <summary>This settler carries a child.</summary>
        internal bool IsExpecting;
        /// <summary>World time the child is due (carrier or partner); 0 when none is on the way.</summary>
        internal double DueAt;
        /// <summary>The partner carries a child.</summary>
        internal bool PartnerExpecting;
        /// <summary>Children born to this settler (grown-up ones included); records of the blob, do not modify.</summary>
        internal readonly List<ChildRecord> Children = new List<ChildRecord>();
        /// <summary>World time of the latest birth this settler gave (as the mother); 0 = never. For the rest after a birth.</summary>
        internal double LastBirthAsMother;
        /// <summary>The settler's own child record when it was born in the settlement; null for settlers that arrived.</summary>
        internal ChildRecord Own;

        private readonly List<Mood> _moods = new List<Mood>();

        /// <summary>The mood is on at <paramref name="now"/> (world time).</summary>
        internal bool HasMood(MoodKind kind, double now)
        {
            foreach (Mood mood in _moods)
            {
                if (mood.Kind == kind && mood.Until > now)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Children of this settler that are not adults yet.</summary>
        internal int MinorChildren(double now, double dayLength, FamilyConfig cfg)
        {
            int count = 0;
            foreach (ChildRecord child in Children)
            {
                if (FamilyRules.StageAt(child.Born, now, dayLength, cfg) != LifeStage.Adult)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Builds the view of <paramref name="uid"/> from the blob; <see cref="Empty"/> for no data or uid 0.</summary>
        internal static FamilyInfo Build(SettlementData data, long uid)
        {
            if (data == null || uid == 0L)
            {
                return Empty;
            }
            var info = new FamilyInfo();
            Couple couple = data.FindCouple(uid);
            if (couple != null)
            {
                info.PartnerUid = couple.Other(uid);
                info.PartnerName = NameOf(data, info.PartnerUid);
                info.DueAt = couple.DueAt;
                info.IsExpecting = couple.IsExpecting && couple.Carrier == uid;
                info.PartnerExpecting = couple.IsExpecting && couple.Carrier != uid && couple.Carrier != 0L;
            }
            Courtship courtship = data.FindCourtship(uid);
            if (courtship != null)
            {
                info.CourtingUid = courtship.Other(uid);
                info.CourtingName = NameOf(data, info.CourtingUid);
            }
            foreach (ChildRecord child in data.Children)
            {
                if (child.Uid == uid)
                {
                    info.Own = child;
                    continue;
                }
                if (child.Mother == uid || child.Father == uid)
                {
                    info.Children.Add(child);
                    if (child.Mother == uid && child.Born > info.LastBirthAsMother)
                    {
                        info.LastBirthAsMother = child.Born;
                    }
                }
            }
            foreach (Mood mood in data.Moods)
            {
                if (mood.Uid == uid)
                {
                    info._moods.Add(mood);
                }
            }
            return info;
        }

        /// <summary>The roster name of a settler; "" when it is not listed (dead, dismissed) or uid 0.</summary>
        internal static string NameOf(SettlementData data, long uid)
        {
            RosterEntry entry = uid != 0L ? data.FindSettler(uid) : null;
            return entry != null ? entry.Name : "";
        }
    }
}
