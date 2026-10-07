using System.Collections.Generic;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// Family records of a settlement, saved in its blob (Settlement.SettlementData, format 7) and written only by
    /// the Jarl's Table owner. Plain data (long/double/int/bool/string) so the unit tests round-trip them without Unity.
    /// Settlers are named by their stable uid, never by ZDOID. A &lt; B ordering is not required: compare pairs with
    /// SettlementData.SameUids.
    /// </summary>
    internal sealed class Couple
    {
        internal long A;
        internal long B;
        /// <summary>World time of the wedding.</summary>
        internal double Since;
        /// <summary>Children born to this couple that are still alive (a child's death takes one off).</summary>
        internal int ChildrenBorn;
        /// <summary>World time of the last birth; 0 = never.</summary>
        internal double LastBirth;
        /// <summary>World time the child is due; 0 = not expecting.</summary>
        internal double DueAt;
        /// <summary>Uid of the partner carrying the child; 0 = none.</summary>
        internal long Carrier;
        internal double ConceivedAt;
        /// <summary>Biological parents fixed at conception, independent of the current partnership.</summary>
        internal long ConceptionMother;
        internal long ConceptionFather;

        /// <summary>Records the pregnancy and its immutable ancestry together before partnership changes.</summary>
        internal void SetPregnancy(long carrier, double conceivedAt, double dueAt)
        {
            ConceptionMother = carrier;
            ConceptionFather = Other(carrier);
            Carrier = carrier;
            ConceivedAt = conceivedAt;
            DueAt = dueAt;
        }

        internal bool IsExpecting => DueAt > 0.0;

        internal bool Has(long uid) => A == uid || B == uid;

        /// <summary>The other partner; 0 when <paramref name="uid"/> is not one of the two.</summary>
        internal long Other(long uid) => A == uid ? B : B == uid ? A : 0L;
    }

    /// <summary>Two settlers courting; becomes a <see cref="Couple"/> after Family/CourtshipDays.</summary>
    internal sealed class Courtship
    {
        internal long A;
        internal long B;
        internal double StartedAt;

        internal bool Has(long uid) => A == uid || B == uid;

        internal long Other(long uid) => A == uid ? B : B == uid ? A : 0L;
    }

    /// <summary>
    /// A settler born in the settlement. Kept after it grows up (the windows list parents and children from it);
    /// whether it is a minor is always derived from its stage, never from the record's presence.
    /// </summary>
    internal sealed class ChildRecord
    {
        internal long Uid;
        /// <summary>Biological parent uids; 0 = unknown. Presence is derived from roster membership.</summary>
        internal long Mother;
        internal long Father;
        /// <summary>World time of the birth.</summary>
        internal double Born;
        /// <summary>The last <see cref="LifeStage"/> the table chronicled, so "came of age" is told once.</summary>
        internal int LastStage;
        internal bool Female;
        /// <summary>First name (the patronymic is derived from the parents).</summary>
        internal string Name = "";
    }

    /// <summary>Values are saved: only append.</summary>
    internal enum MoodKind
    {
        /// <summary>Just wed.</summary>
        InLove = 1,
        /// <summary>A child was just born.</summary>
        NewParent = 2,
        /// <summary>Lost a partner, a parent or a child; no new courtship meanwhile.</summary>
        Grief = 3,
    }

    /// <summary>A temporary morale term of one settler, kept in the settlement blob.</summary>
    internal sealed class Mood
    {
        internal long Uid;
        internal MoodKind Kind;
        /// <summary>World time the mood ends.</summary>
        internal double Until;
    }

    /// <summary>Bits of SettlementData.FamilyFlags (Manage toggles).</summary>
    internal static class FamilyFlags
    {
        internal const byte NoNewCouples = 1;
        internal const byte NoBirths = 2;
    }

    /// <summary>What SettlementData.FamilyCleanup undid when a settler left the roster, for moods and the chronicle.</summary>
    internal sealed class FamilyCleanupResult
    {
        /// <summary>The partner of a dissolved couple (at most one).</summary>
        internal readonly List<long> WidowedUids = new List<long>();
        /// <summary>Minor children of the lost settler (biological ancestry is retained).</summary>
        internal readonly List<long> OrphanedUids = new List<long>();
        /// <summary>Parents of the lost settler, when it was a child of the settlement.</summary>
        internal readonly List<long> BereavedUids = new List<long>();
        /// <summary>A pregnancy the lost settler carried was cancelled.</summary>
        internal bool PregnancyCancelled;
        /// <summary>Any record changed (the blob needs a write).</summary>
        internal bool Changed;
    }
}
