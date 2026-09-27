using System;
using System.Collections.Generic;

namespace AgeOfJarls.Core.Defs
{
    // Plain public fields: Newtonsoft (de)serializes them directly and matches JSON names case-insensitively.

    public sealed class TraitDef
    {
        public string Id;
        public bool Positive;
        /// <summary>Relative chance to be rolled; 0 disables the trait.</summary>
        public float Weight = 1f;
        /// <summary>Heightmap.Biome names where the trait is more common; empty = any biome.</summary>
        public List<string> Origins = new List<string>();
        /// <summary>Trait ids that can never appear together with this one.</summary>
        public List<string> Excludes = new List<string>();
        /// <summary><see cref="TraitStat"/> key → delta.</summary>
        public Dictionary<string, float> Modifiers = new Dictionary<string, float>();
    }

    public sealed class TierDef
    {
        public int Level;
        /// <summary>Vanilla global key (e.g. "defeated_eikthyr"); empty = available from the start.</summary>
        public string RequiredGlobalKey = "";
        public int MaxSettlers;
        public float Radius;
        public List<string> Unlocks = new List<string>();
        /// <summary>Paid from the inventory of the player who expands the settlement to this tier.</summary>
        public List<TierCost> Cost = new List<TierCost>();
    }

    public sealed class TierCost
    {
        /// <summary>Item prefab name, e.g. "Wood".</summary>
        public string Item;
        public int Amount;
    }

    public sealed class NamesDef
    {
        public List<string> Male = new List<string>();
        public List<string> Female = new List<string>();
    }

    public sealed class StorageDef
    {
        /// <summary>
        /// Kind id → item prefab names. A settler puts an item into a chest that already holds its kind when no chest
        /// holds the item itself; items in no kind only join their own. Trophies, food and gear need no list.
        /// </summary>
        public Dictionary<string, List<string>> Kinds = new Dictionary<string, List<string>>();
    }

    /// <summary>One siege wave line-up: the common attackers and the elite of the last wave, for settlements of a tier.</summary>
    public sealed class SiegeWaveDef
    {
        /// <summary>Used from this settlement tier up (until a higher entry takes over).</summary>
        public int Tier;
        public List<string> Common = new List<string>();
        public string Elite = "";
    }

    public sealed class RaidsDef
    {
        public List<SiegeWaveDef> SiegeWaves = new List<SiegeWaveDef>();
        /// <summary>Heightmap.Biome name → creature prefabs guarding a captive in a camp of that biome.</summary>
        public Dictionary<string, List<string>> CampGuards = new Dictionary<string, List<string>>();
    }

    public sealed class DefsBundle
    {
        public List<TraitDef> Traits = new List<TraitDef>();
        public List<TierDef> Tiers = new List<TierDef>();
        public NamesDef Names = new NamesDef();
        public StorageDef Storage = new StorageDef();
        public RaidsDef Raids = new RaidsDef();
    }

    /// <summary>
    /// Stats a trait may modify. Rates are relative (0.15 = +15%); FleeThreshold is an absolute change of the health
    /// fraction at which a civilian flees (-1 = never flees); NightWork is a flag (1 = works through the night);
    /// CombatStart is the combat experience (0-100) a new settler starts with.
    /// </summary>
    public static class TraitStat
    {
        public const string WorkSpeed = "workSpeed";
        public const string MoraleRecovery = "moraleRecovery";
        public const string CarryWeight = "carryWeight";
        public const string MeleeDamage = "meleeDamage";
        public const string RangedDamage = "rangedDamage";
        public const string DamageTaken = "damageTaken";
        public const string MoveSpeed = "moveSpeed";
        public const string FoodConsumption = "foodConsumption";
        public const string MaxHealth = "maxHealth";
        public const string FleeThreshold = "fleeThreshold";
        public const string CropYield = "cropYield";
        public const string NightWork = "nightWork";
        public const string CombatStart = "combatStart";

        private static readonly Dictionary<string, string> Canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { WorkSpeed, WorkSpeed },
            { MoraleRecovery, MoraleRecovery },
            { CarryWeight, CarryWeight },
            { MeleeDamage, MeleeDamage },
            { RangedDamage, RangedDamage },
            { DamageTaken, DamageTaken },
            { MoveSpeed, MoveSpeed },
            { FoodConsumption, FoodConsumption },
            { MaxHealth, MaxHealth },
            { FleeThreshold, FleeThreshold },
            { CropYield, CropYield },
            { NightWork, NightWork },
            { CombatStart, CombatStart },
        };

        public static bool TryGetCanonical(string key, out string canonical)
        {
            canonical = null;
            return key != null && Canonical.TryGetValue(key, out canonical);
        }
    }
}
