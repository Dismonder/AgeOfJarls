namespace AgeOfJarls.Army
{
    /// <summary>A settler's place in the settlement's troop. Values are saved in ZDOs: only append.</summary>
    public enum CombatRole
    {
        None = 0,
        Warrior = 1,
        Archer = 2,
        Shieldbearer = 3,
    }

    internal static class CombatRoles
    {
        internal static readonly CombatRole[] All = { CombatRole.Warrior, CombatRole.Archer, CombatRole.Shieldbearer };

        internal static string Token(CombatRole role) => "$aoj_role_" + role.ToString().ToLowerInvariant();

        /// <summary>The id in tiers.json "unlocks".</summary>
        internal static string UnlockId(CombatRole role) => role == CombatRole.Shieldbearer ? "shieldbearer" : role.ToString().ToLowerInvariant();

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
        internal static BannerKind PostKind(CombatRole role) =>
            role == CombatRole.Archer ? BannerKind.Wall : role == CombatRole.Shieldbearer ? BannerKind.Gate : BannerKind.Rally;
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
