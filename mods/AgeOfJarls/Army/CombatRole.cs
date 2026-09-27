namespace AgeOfJarls.Army
{
    /// <summary>A settler's place in the settlement's troop. Values are saved in ZDOs: only append.</summary>
    public enum CombatRole
    {
        None = 0,
        Warrior = 1,
        Archer = 2,
        Shieldbearer = 3,
        /// <summary>Spear or atgeir, fighting from behind the shieldbearers at the gate (tier 4).</summary>
        Spearman = 4,
        /// <summary>Two-handed weapon, no shield, hits harder (tier 5).</summary>
        Berserker = 5,
    }

    internal static class CombatRoles
    {
        internal static readonly CombatRole[] All =
            { CombatRole.Warrior, CombatRole.Archer, CombatRole.Shieldbearer, CombatRole.Spearman, CombatRole.Berserker };

        /// <summary>Berserkers' melee hits deal this much more.</summary>
        internal const float BerserkerDamage = 0.15f;

        internal static string Token(CombatRole role) => "$aoj_role_" + role.ToString().ToLowerInvariant();

        /// <summary>The id in tiers.json "unlocks".</summary>
        internal static string UnlockId(CombatRole role) => role.ToString().ToLowerInvariant();

        internal static bool IsUnlocked(CombatRole role, int tier)
        {
            string id = UnlockId(role);
            foreach (Core.Defs.TierDef def in Core.Defs.DefsRegistry.Current.Tiers)
            {
                if (def.Unlocks.Contains(id))
                {
                    return def.Level <= tier;
                }
            }
            return true;
        }

        /// <summary>The banner kind a role stands at by default.</summary>
        internal static BannerKind PostKind(CombatRole role)
        {
            switch (role)
            {
                case CombatRole.Archer:
                    return BannerKind.Wall;
                case CombatRole.Shieldbearer:
                case CombatRole.Spearman:
                    return BannerKind.Gate;
                default:
                    return BannerKind.Rally;
            }
        }
    }

    /// <summary>What a war banner is for. Values are saved in ZDOs: only append.</summary>
    public enum BannerKind
    {
        Rally = 0,
        Wall = 1,
        Gate = 2,
        Shelter = 3,
    }
}
