using System;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// Pure rules of growing up: what a settler of a given <see cref="LifeStage"/> looks like and may do. No game
    /// state is read here (times and the config come in as parameters) so every machine computes the same answer and
    /// the unit tests cover it.
    /// </summary>
    internal static class FamilyRules
    {
        /// <summary>Overdue pregnancies wait for the server roster prune after activation; absent carriers never deliver.</summary>
        internal static bool CanDeliver(double dueAt, bool carrierPresent, double activatedAt, double now) =>
            carrierPresent && dueAt > 0 && dueAt <= now && (dueAt > activatedAt || now - activatedAt >= 90.0);

        /// <summary>Full spawn health uses the actual age and inherited health traits.</summary>
        internal static float SpawnHealth(float baseHealth, float healthTraits, double born, double now, double dayLength, FamilyConfig cfg)
        {
            LifeStage stage = StageAt(born, now, dayLength, cfg);
            float progress = StageProgress(now - born, dayLength, cfg);
            return baseHealth * (1f + healthTraits) * HealthFactor(ScaleFor(stage, progress));
        }

        /// <summary>Eligible catch-up seconds, optionally weighted by youth pace for an unmeasured rate.</summary>
        internal static double WorkSeconds(double born, double from, double until, JobType job, double dueAt,
            double lastBirth, double dayLength, FamilyConfig cfg, bool fallbackPace)
        {
            if (until <= from) return 0;
            double adultAt = born > 0 ? born + cfg.DaysToAdult * dayLength : double.NegativeInfinity;
            double youthAt = born > 0 ? born + (cfg.InfantDays + cfg.ChildDays) * dayLength : double.NegativeInfinity;
            double seconds = 0;
            for (LifeStage stage = LifeStage.Youth; stage <= LifeStage.Adult; stage++)
            {
                if (!CanWorkAt(stage, job)) continue;
                double start = Math.Max(from, stage == LifeStage.Youth ? youthAt : adultAt);
                double end = stage == LifeStage.Youth ? Math.Min(until, adultAt) : until;
                if (end <= start) continue;
                double restStart = lastBirth > 0 ? lastBirth - 0.5 * dayLength : double.PositiveInfinity;
                double restEnd = lastBirth > 0 ? lastBirth + cfg.RestDays * dayLength : double.NegativeInfinity;
                double pregnancyStart = dueAt > 0 ? dueAt - 0.5 * dayLength : double.PositiveInfinity;
                // Post-birth rest includes the preceding pregnancy rest; merge any overlap with a new pregnancy.
                double resting = Overlap(start, end, restStart, restEnd) + Overlap(start, end, pregnancyStart, end)
                    - Overlap(start, end, Math.Max(restStart, pregnancyStart), restEnd);
                seconds += Math.Max(0, end - start - resting) * (fallbackPace ? PaceFor(stage, cfg.YouthWorkPace) : 1f);
            }
            return seconds;
        }

        private static double Overlap(double from, double until, double start, double end) =>
            Math.Max(0, Math.Min(until, end) - Math.Max(from, start));

        /// <summary>Walk and run speed never drop below this share of an adult's, or infants could not keep up at all.</summary>
        private const float MinSpeedFactor = 0.55f;

        /// <summary>The stage of a settler that is <paramref name="ageSeconds"/> old (thresholds from the config, in game days).</summary>
        internal static LifeStage StageFor(double ageSeconds, double dayLength, FamilyConfig cfg)
        {
            if (ageSeconds < cfg.InfantDays * dayLength)
            {
                return LifeStage.Infant;
            }
            if (ageSeconds < (cfg.InfantDays + cfg.ChildDays) * dayLength)
            {
                return LifeStage.Child;
            }
            if (ageSeconds < cfg.DaysToAdult * dayLength)
            {
                return LifeStage.Youth;
            }
            return LifeStage.Adult;
        }

        /// <summary>The stage of a settler born at <paramref name="born"/>; born &lt;= 0 means it arrived as an adult.</summary>
        internal static LifeStage StageAt(double born, double now, double dayLength, FamilyConfig cfg) =>
            born <= 0.0 ? LifeStage.Adult : StageFor(now - born, dayLength, cfg);

        /// <summary>How far (0..1) the settler is through its current stage; 1 for adults.</summary>
        internal static float StageProgress(double ageSeconds, double dayLength, FamilyConfig cfg)
        {
            double start;
            double length;
            switch (StageFor(ageSeconds, dayLength, cfg))
            {
                case LifeStage.Infant:
                    start = 0.0;
                    length = cfg.InfantDays * dayLength;
                    break;
                case LifeStage.Child:
                    start = cfg.InfantDays * dayLength;
                    length = cfg.ChildDays * dayLength;
                    break;
                case LifeStage.Youth:
                    start = (cfg.InfantDays + cfg.ChildDays) * dayLength;
                    length = cfg.YouthDays * dayLength;
                    break;
                default:
                    return 1f;
            }
            if (length <= 0.0)
            {
                return 1f;
            }
            return Mathf.Clamp01((float)((ageSeconds - start) / length));
        }

        /// <summary>Seconds until the next stage; 0 for adults.</summary>
        internal static double SecondsToNextStage(double ageSeconds, double dayLength, FamilyConfig cfg)
        {
            double threshold;
            switch (StageFor(ageSeconds, dayLength, cfg))
            {
                case LifeStage.Infant:
                    threshold = cfg.InfantDays * dayLength;
                    break;
                case LifeStage.Child:
                    threshold = (cfg.InfantDays + cfg.ChildDays) * dayLength;
                    break;
                case LifeStage.Youth:
                    threshold = cfg.DaysToAdult * dayLength;
                    break;
                default:
                    return 0.0;
            }
            return threshold > ageSeconds ? threshold - ageSeconds : 0.0;
        }

        /// <summary>Model scale: grows continuously from half size at birth to full size when adult.</summary>
        internal static float ScaleFor(LifeStage stage, float progress)
        {
            float t = Mathf.Clamp01(progress);
            switch (stage)
            {
                case LifeStage.Infant:
                    return Mathf.Lerp(0.50f, 0.65f, t);
                case LifeStage.Child:
                    return Mathf.Lerp(0.65f, 0.85f, t);
                case LifeStage.Youth:
                    return Mathf.Lerp(0.85f, 1f, t);
                default:
                    return 1f;
            }
        }

        /// <summary>Walk and run speed multiplier for a model of this scale.</summary>
        internal static float SpeedFactor(float scale) => Mathf.Clamp(scale, MinSpeedFactor, 1f);

        /// <summary>Max health multiplier for a model of this scale.</summary>
        internal static float HealthFactor(float scale) => scale;

        /// <summary>Youths and adults hold jobs; infants and children never do.</summary>
        internal static bool CanWork(LifeStage stage) => stage == LifeStage.Adult || stage == LifeStage.Youth;

        /// <summary>Adults do any job; youths only the light ones (no mining, smelting or building).</summary>
        internal static bool CanWorkAt(LifeStage stage, JobType job)
        {
            switch (stage)
            {
                case LifeStage.Adult:
                    return true;
                case LifeStage.Youth:
                    return job == JobType.Woodcutter || job == JobType.Hauler || job == JobType.Farmer || job == JobType.Cook;
                default:
                    return false;
            }
        }

        /// <summary>Only adults fight, hold a combat role or carry weapons.</summary>
        internal static bool CanFight(LifeStage stage) => stage == LifeStage.Adult;

        /// <summary>Work pace multiplier (Family/YouthWorkPace for youths).</summary>
        internal static float PaceFor(LifeStage stage, float youthPace) => stage == LifeStage.Youth ? youthPace : 1f;

        /// <summary>How much the settler eats compared with an adult (Needs appetite multiplier).</summary>
        internal static float AppetiteFor(LifeStage stage)
        {
            switch (stage)
            {
                case LifeStage.Infant:
                    return 0.3f;
                case LifeStage.Child:
                    return 0.6f;
                case LifeStage.Youth:
                    return 0.9f;
                default:
                    return 1f;
            }
        }

        /// <summary>Infants and children sleep at a parent's bed; youths and adults need their own.</summary>
        internal static bool NeedsOwnBed(LifeStage stage) => stage >= LifeStage.Youth;

        /// <summary>How far from its anchor the settler wanders at home; the config range once it is a youth.</summary>
        internal static float WanderRangeFor(LifeStage stage, float adultRange)
        {
            switch (stage)
            {
                case LifeStage.Infant:
                    return 2f;
                case LifeStage.Child:
                    return 4f;
                default:
                    return adultRange;
            }
        }

        /// <summary>"Ragnarsson" / "Ragnarsdottir" from a parent's first name; "" without one. ASCII suffixes, so the name stays safe to show.</summary>
        internal static string Patronymic(string parentFirstName, bool female)
        {
            if (string.IsNullOrEmpty(parentFirstName))
            {
                return "";
            }
            return parentFirstName + (female ? "sdottir" : "sson");
        }

        /// <summary>The localization token of a stage, in the settler's gender ($aoj_stage_child, $aoj_stage_child_f).</summary>
        internal static string StageToken(LifeStage stage, bool female)
        {
            string name;
            switch (stage)
            {
                case LifeStage.Infant:
                    name = "infant";
                    break;
                case LifeStage.Child:
                    name = "child";
                    break;
                case LifeStage.Youth:
                    name = "youth";
                    break;
                default:
                    name = "adult";
                    break;
            }
            return "$aoj_stage_" + name + (female ? "_f" : "");
        }
    }
}
