using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgeOfJarls.Core.Defs
{
    /// <summary>
    /// Cleans definitions from files or from the server. Bad entries are dropped with a warning;
    /// null means the whole set is unusable and the caller falls back to another source.
    /// </summary>
    internal static class DefsValidator
    {
        private const string Module = "Defs";
        private const int MaxNameLength = 24;
        private const float MinRadius = 5f;
        private const int MaxCosts = 8;
        private const int MaxCostAmount = 999;
        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$");

        internal static List<TraitDef> Traits(List<TraitDef> traits, string source)
        {
            if (traits == null)
            {
                Log.Warning(Module, $"{source}: no traits");
                return null;
            }

            var result = new List<TraitDef>();
            var seen = new HashSet<string>();
            foreach (TraitDef trait in traits)
            {
                if (trait == null)
                {
                    continue;
                }
                if (trait.Id == null || !IdPattern.IsMatch(trait.Id))
                {
                    Log.Warning(Module, $"{source}: skipped trait with invalid id '{trait.Id}' (allowed: a-z, 0-9, _)");
                    continue;
                }
                if (!seen.Add(trait.Id))
                {
                    Log.Warning(Module, $"{source}: skipped duplicate trait '{trait.Id}'");
                    continue;
                }
                if (trait.Weight <= 0f)
                {
                    Log.Debug(Module, $"{source}: trait '{trait.Id}' disabled (weight {trait.Weight})");
                    continue;
                }
                trait.Origins = Biomes(trait.Origins, trait.Id, source);
                trait.Modifiers = Modifiers(trait.Modifiers, trait.Id, source);
                result.Add(trait);
            }

            if (result.Count == 0)
            {
                Log.Warning(Module, $"{source}: no usable traits");
                return null;
            }

            LinkExclusions(result, seen, source);
            if (!result.Any(t => t.Positive))
            {
                Log.Warning(Module, $"{source}: no positive trait, settlers will only roll negative ones");
            }
            return result;
        }

        internal static List<TierDef> Tiers(List<TierDef> tiers, string source)
        {
            List<TierDef> sorted = tiers?.Where(t => t != null).OrderBy(t => t.Level).ToList();
            if (sorted == null || sorted.Count == 0)
            {
                Log.Warning(Module, $"{source}: no tiers");
                return null;
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                TierDef tier = sorted[i];
                if (tier.Level != i)
                {
                    Log.Warning(Module, $"{source}: tier levels must run 0, 1, 2... without gaps or duplicates (got {tier.Level} at position {i})");
                    return null;
                }
                if (tier.MaxSettlers < 1 || tier.Radius < MinRadius)
                {
                    Log.Warning(Module, $"{source}: tier {i} needs maxSettlers >= 1 and radius >= {MinRadius}");
                    return null;
                }
                tier.RequiredGlobalKey = tier.RequiredGlobalKey?.Trim() ?? "";
                tier.Unlocks = tier.Unlocks == null
                    ? new List<string>()
                    : tier.Unlocks.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct().ToList();
                tier.Cost = Costs(tier.Cost, i, source);
            }
            return sorted;
        }

        // Item names are only checked against ObjectDB when paying: it does not exist yet while definitions load.
        private static List<TierCost> Costs(List<TierCost> costs, int tier, string source)
        {
            var result = new List<TierCost>();
            if (costs == null)
            {
                return result;
            }

            foreach (TierCost cost in costs)
            {
                if (cost == null || string.IsNullOrWhiteSpace(cost.Item) || cost.Amount < 1 || cost.Amount > MaxCostAmount)
                {
                    Log.Warning(Module, $"{source}: tier {tier} has an invalid cost entry (item and amount 1-{MaxCostAmount} required), skipped");
                    continue;
                }
                if (result.Count == MaxCosts)
                {
                    Log.Warning(Module, $"{source}: tier {tier} lists more than {MaxCosts} cost items, the rest is ignored");
                    break;
                }
                cost.Item = cost.Item.Trim();
                result.Add(cost);
            }
            return result;
        }

        internal static NamesDef Names(NamesDef names, string source)
        {
            if (names == null)
            {
                Log.Warning(Module, $"{source}: no names");
                return null;
            }

            names.Male = CleanNames(names.Male, source);
            names.Female = CleanNames(names.Female, source);
            if (names.Male.Count == 0 || names.Female.Count == 0)
            {
                Log.Warning(Module, $"{source}: needs at least one male and one female name");
                return null;
            }
            return names;
        }

        // Waves sorted by tier, empty names dropped; a wave without attackers is skipped.
        internal static RaidsDef Raids(RaidsDef raids, string source)
        {
            if (raids == null)
            {
                Log.Warning(Module, $"{source}: no raids");
                return null;
            }
            raids.SiegeWaves = (raids.SiegeWaves ?? new List<SiegeWaveDef>())
                .Where(w => w != null)
                .Select(w =>
                {
                    w.Common = (w.Common ?? new List<string>()).Select(c => c?.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
                    w.Elite = w.Elite?.Trim() ?? "";
                    return w;
                })
                .Where(w => w.Common.Count > 0)
                .OrderBy(w => w.Tier)
                .ToList();
            var guards = new Dictionary<string, List<string>>();
            foreach (KeyValuePair<string, List<string>> pair in raids.CampGuards ?? new Dictionary<string, List<string>>())
            {
                List<string> list = (pair.Value ?? new List<string>()).Select(g => g?.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToList();
                if (!string.IsNullOrWhiteSpace(pair.Key) && list.Count > 0)
                {
                    guards[pair.Key.Trim()] = list;
                }
            }
            raids.CampGuards = guards;
            if (raids.SiegeWaves.Count == 0)
            {
                Log.Warning(Module, $"{source}: no usable siege waves; sieges will not happen");
            }
            return raids;
        }

        // An item belongs to one kind at most: the first kind listing it keeps it.
        internal static StorageDef Storage(StorageDef storage, string source)
        {
            if (storage?.Kinds == null)
            {
                Log.Warning(Module, $"{source}: no storage kinds");
                return null;
            }

            var owner = new Dictionary<string, string>();
            var kinds = new Dictionary<string, List<string>>();
            foreach (KeyValuePair<string, List<string>> pair in storage.Kinds)
            {
                string kind = pair.Key?.Trim();
                if (string.IsNullOrEmpty(kind) || pair.Value == null)
                {
                    Log.Warning(Module, $"{source}: storage kind without a name or items skipped");
                    continue;
                }
                var items = new List<string>();
                foreach (string raw in pair.Value)
                {
                    string item = raw?.Trim();
                    if (string.IsNullOrEmpty(item))
                    {
                        continue;
                    }
                    if (owner.TryGetValue(item, out string first))
                    {
                        Log.Warning(Module, $"{source}: '{item}' is in storage kinds '{first}' and '{kind}', keeping '{first}'");
                        continue;
                    }
                    owner[item] = kind;
                    items.Add(item);
                }
                if (items.Count > 0)
                {
                    kinds[kind] = items;
                }
            }
            storage.Kinds = kinds;
            return storage;
        }

        // Exclusions are made symmetric, so rolling code only has to check one side.
        private static void LinkExclusions(List<TraitDef> traits, HashSet<string> allIds, string source)
        {
            var byId = traits.ToDictionary(t => t.Id);
            foreach (TraitDef trait in traits)
            {
                var valid = new List<string>();
                foreach (string other in trait.Excludes ?? new List<string>())
                {
                    if (other == trait.Id || valid.Contains(other))
                    {
                        continue;
                    }
                    if (byId.ContainsKey(other))
                    {
                        valid.Add(other);
                    }
                    else if (!allIds.Contains(other))
                    {
                        // Ids of disabled traits (weight 0) are skipped silently; anything else is a typo.
                        Log.Warning(Module, $"{source}: trait '{trait.Id}' excludes unknown trait '{other}'");
                    }
                }
                trait.Excludes = valid;
            }

            foreach (TraitDef trait in traits)
            {
                foreach (string other in trait.Excludes)
                {
                    List<string> reverse = byId[other].Excludes;
                    if (!reverse.Contains(trait.Id))
                    {
                        reverse.Add(trait.Id);
                    }
                }
            }
        }

        private static List<string> Biomes(List<string> names, string traitId, string source)
        {
            var result = new List<string>();
            if (names == null)
            {
                return result;
            }

            foreach (string name in names)
            {
                if (Enum.TryParse(name, true, out Heightmap.Biome biome) && IsSingleBiome(biome))
                {
                    string canonical = biome.ToString();
                    if (!result.Contains(canonical))
                    {
                        result.Add(canonical);
                    }
                }
                else
                {
                    Log.Warning(Module, $"{source}: trait '{traitId}' has unknown origin biome '{name}'");
                }
            }
            return result;
        }

        // Rejects None, combined flags such as All/Land, and numeric strings that are not a single biome.
        private static bool IsSingleBiome(Heightmap.Biome biome)
        {
            int value = (int)biome;
            return value > 0 && (value & (value - 1)) == 0 && Enum.IsDefined(typeof(Heightmap.Biome), biome);
        }

        private static Dictionary<string, float> Modifiers(Dictionary<string, float> modifiers, string traitId, string source)
        {
            var result = new Dictionary<string, float>();
            if (modifiers == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, float> pair in modifiers)
            {
                if (!TraitStat.TryGetCanonical(pair.Key, out string stat))
                {
                    Log.Warning(Module, $"{source}: trait '{traitId}' has unknown stat '{pair.Key}'");
                    continue;
                }
                if (float.IsNaN(pair.Value) || float.IsInfinity(pair.Value))
                {
                    Log.Warning(Module, $"{source}: trait '{traitId}' has an invalid value for '{stat}'");
                    continue;
                }
                result[stat] = pair.Value;
            }
            return result;
        }

        private static List<string> CleanNames(List<string> names, string source)
        {
            var result = new List<string>();
            if (names == null)
            {
                return result;
            }

            foreach (string raw in names)
            {
                string name = raw?.Trim();
                if (string.IsNullOrEmpty(name) || result.Contains(name))
                {
                    continue;
                }
                if (name.Length > MaxNameLength)
                {
                    Log.Warning(Module, $"{source}: name '{name}' is longer than {MaxNameLength} characters, skipped");
                    continue;
                }
                result.Add(name);
            }
            return result;
        }
    }
}
