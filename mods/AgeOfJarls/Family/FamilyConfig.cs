using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// The [Family] numbers as plain values, so the rules (<see cref="FamilyRules"/>, the settlement helpers) stay pure
    /// and the unit tests can build one without BepInEx. The field initializers are the config defaults.
    /// </summary>
    internal sealed class FamilyConfig
    {
        /// <summary>Master switch: no courtship, no births; existing children still grow.</summary>
        internal bool Enabled = true;
        internal FamilyPairing Pairing = FamilyPairing.OppositeSex;
        /// <summary>Game days from the start of a courtship to the wedding.</summary>
        internal float CourtshipDays = 2f;
        /// <summary>Per single adult per game day: chance to start courting when a candidate exists.</summary>
        internal float CourtshipChancePerDay = 0.5f;
        /// <summary>Morale both partners need to start courting or to conceive.</summary>
        internal int MinMorale = 40;
        /// <summary>Per couple per game day.</summary>
        internal float PregnancyChancePerDay = 0.3f;
        /// <summary>From conception to birth, in game days.</summary>
        internal float PregnancyDays = 3f;
        /// <summary>Per couple, measured from the last birth.</summary>
        internal float MinDaysBetweenBirths = 3f;
        /// <summary>Born children per couple (dead ones no longer count).</summary>
        internal int MaxChildrenPerCouple = 3;
        /// <summary>Minors in the settlement at most ratio x adults (at least one); blocks conception, never a birth under way.</summary>
        internal float MaxChildrenRatio = 0.5f;
        /// <summary>False: settlers under adult age do not count toward the tier's settler limit.</summary>
        internal bool ChildrenCountTowardLimit;
        /// <summary>Stage lengths in game days; adult after all three.</summary>
        internal float InfantDays = 2f;
        internal float ChildDays = 4f;
        internal float YouthDays = 4f;
        /// <summary>Days the carrier rests after a birth (it also rests the last half day of the pregnancy).</summary>
        internal float RestDays = 1f;
        /// <summary>A wedding extends the settlement feast by this many days.</summary>
        internal float WeddingFeastDays = 1f;
        /// <summary>Grief after losing a partner, a parent or a child: lower morale, no new courtship.</summary>
        internal float WidowDays = 4f;
        /// <summary>Work pace multiplier of youths.</summary>
        internal float YouthWorkPace = 0.7f;

        /// <summary>Game days from birth to adulthood.</summary>
        internal float DaysToAdult => InfantDays + ChildDays + YouthDays;

        private const float LiveRefreshSeconds = 2f;
        private static FamilyConfig s_live;
        private static float s_liveAt = -1f;

        /// <summary>
        /// The live config as a snapshot shared by every settler (stage, scale, hover and morale ask for it every tick
        /// and on every hover), re-read every couple of seconds so a synced change reaches it without allocating per call.
        /// </summary>
        internal static FamilyConfig Live
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (s_live == null || now - s_liveAt >= LiveRefreshSeconds)
                {
                    s_live = FromConfig();
                    s_liveAt = now;
                }
                return s_live;
            }
        }

        /// <summary>A snapshot of the live config; the defaults when the config is not bound yet (tests, early calls).</summary>
        internal static FamilyConfig FromConfig()
        {
            var cfg = new FamilyConfig();
            if (AoJConfig.FamilyEnabled == null)
            {
                return cfg;
            }
            cfg.Enabled = AoJConfig.FamilyEnabled.Value;
            cfg.Pairing = AoJConfig.FamilyPairing.Value;
            cfg.CourtshipDays = AoJConfig.FamilyCourtshipDays.Value;
            cfg.CourtshipChancePerDay = AoJConfig.FamilyCourtshipChancePerDay.Value;
            cfg.MinMorale = AoJConfig.FamilyMinMorale.Value;
            cfg.PregnancyChancePerDay = AoJConfig.FamilyPregnancyChancePerDay.Value;
            cfg.PregnancyDays = AoJConfig.FamilyPregnancyDays.Value;
            cfg.MinDaysBetweenBirths = AoJConfig.FamilyMinDaysBetweenBirths.Value;
            cfg.MaxChildrenPerCouple = AoJConfig.FamilyMaxChildrenPerCouple.Value;
            cfg.MaxChildrenRatio = AoJConfig.FamilyMaxChildrenRatio.Value;
            cfg.ChildrenCountTowardLimit = AoJConfig.FamilyChildrenCountTowardLimit.Value;
            cfg.InfantDays = AoJConfig.FamilyInfantDays.Value;
            cfg.ChildDays = AoJConfig.FamilyChildDays.Value;
            cfg.YouthDays = AoJConfig.FamilyYouthDays.Value;
            cfg.RestDays = AoJConfig.FamilyRestDays.Value;
            cfg.WeddingFeastDays = AoJConfig.FamilyWeddingFeastDays.Value;
            cfg.WidowDays = AoJConfig.FamilyWidowDays.Value;
            cfg.YouthWorkPace = AoJConfig.FamilyYouthWorkPace.Value;
            return cfg;
        }
    }
}
