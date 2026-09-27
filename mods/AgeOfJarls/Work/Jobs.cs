using System.Collections.Generic;
using AgeOfJarls.Core.Defs;

namespace AgeOfJarls.Work
{
    /// <summary>Work a settler can be given at a Work Totem. Values are saved in ZDOs: only append.</summary>
    public enum JobType
    {
        None = 0,
        Woodcutter = 1,
        Hauler = 2,
        Miner = 3,
        Smelter = 4,
        Builder = 5,
        Farmer = 6,
        Cook = 7,
    }

    internal enum ToolKind
    {
        None,
        Axe,
        Pickaxe,
        Hammer,
    }

    internal static class JobInfo
    {
        internal static readonly JobType[] All =
        {
            JobType.Woodcutter, JobType.Hauler, JobType.Miner, JobType.Smelter, JobType.Builder, JobType.Farmer, JobType.Cook,
        };

        internal static string Token(JobType job) => "$aoj_job_" + job.ToString().ToLowerInvariant();

        /// <summary>The id used in tiers.json "unlocks".</summary>
        internal static string UnlockId(JobType job) => job.ToString().ToLowerInvariant();

        /// <summary>A job no tier lists is available from the start; otherwise from the first tier that lists it.</summary>
        internal static bool IsUnlocked(JobType job, int tier)
        {
            string id = UnlockId(job);
            List<TierDef> tiers = DefsRegistry.Current.Tiers;
            bool listed = false;
            foreach (TierDef def in tiers)
            {
                if (!def.Unlocks.Contains(id))
                {
                    continue;
                }
                listed = true;
                if (def.Level <= tier)
                {
                    return true;
                }
            }
            return !listed;
        }

        internal static ToolKind RequiredTool(JobType job)
        {
            switch (job)
            {
                case JobType.Woodcutter:
                    return ToolKind.Axe;
                case JobType.Miner:
                    return ToolKind.Pickaxe;
                case JobType.Builder:
                    return ToolKind.Hammer;
                default:
                    return ToolKind.None;
            }
        }

        internal static string ToolToken(ToolKind tool) => "$aoj_tool_" + tool.ToString().ToLowerInvariant();

        internal static bool Fits(ItemDrop.ItemData item, ToolKind tool)
        {
            if (item == null)
            {
                return false;
            }
            ItemDrop.ItemData.SharedData shared = item.m_shared;
            switch (tool)
            {
                case ToolKind.Axe:
                    return item.IsWeapon() && (shared.m_skillType == Skills.SkillType.Axes || shared.m_damages.m_chop > 0f);
                case ToolKind.Pickaxe:
                    return item.IsWeapon() && (shared.m_skillType == Skills.SkillType.Pickaxes || shared.m_damages.m_pickaxe > 0f);
                case ToolKind.Hammer:
                    return shared.m_name == "$item_hammer";
                default:
                    return false;
            }
        }

        /// <summary>The best tool of that kind carried: highest tool tier, then most damage of its kind.</summary>
        internal static ItemDrop.ItemData BestTool(Inventory inventory, ToolKind tool)
        {
            ItemDrop.ItemData best = null;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!Fits(item, tool))
                {
                    continue;
                }
                if (best == null || item.m_shared.m_toolTier > best.m_shared.m_toolTier ||
                    (item.m_shared.m_toolTier == best.m_shared.m_toolTier && Power(item, tool) > Power(best, tool)))
                {
                    best = item;
                }
            }
            return best;
        }

        private static float Power(ItemDrop.ItemData item, ToolKind tool)
        {
            HitData.DamageTypes damage = item.GetDamage();
            return tool == ToolKind.Pickaxe ? damage.m_pickaxe : damage.m_chop;
        }
    }
}
