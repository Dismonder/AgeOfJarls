using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>Rolls a new identity from the active (server-synced) definitions. Only the ZDO owner calls this.</summary>
    internal static class SettlerRoller
    {
        private const string FallbackName = "Viking";

        internal static SettlerIdentity Roll(System.Random random, Heightmap.Biome origin)
        {
            DefsBundle defs = DefsRegistry.Current;
            bool female = random.NextDouble() < 0.5;
            List<string> names = female ? defs.Names.Female : defs.Names.Male;

            var identity = new SettlerIdentity
            {
                Female = female,
                Name = names.Count > 0 ? names[random.Next(names.Count)] : FallbackName,
                Origin = origin,
                Traits = RollTraits(random, defs.Traits, origin),
                Hair = PickCustomization(random, "Hair"),
                Beard = female ? "" : PickCustomization(random, "Beard"),
                SkinColor = RollSkinColor(random),
            };
            identity.HairColor = RollHairColor(random);
            return identity;
        }

        /// <summary>Chance that a child takes over each trait of a parent.</summary>
        internal const float InheritChance = 0.4f;

        /// <summary>
        /// The identity of a child born in a settlement: sex 50/50, a first name not already on the roster, hair colour
        /// from one parent, skin colour the average of the parents', a random hair style (a beard for boys, hidden until
        /// adult), traits inherited from the parents. A parent that is not loaded comes as null and contributes a plain
        /// roll instead (random colour, no traits to pass on). Only the ZDO owner (the spawner) calls this.
        /// </summary>
        internal static SettlerIdentity RollChild(System.Random random, SettlerIdentity mother, SettlerIdentity father,
            Heightmap.Biome origin, ICollection<string> takenNames)
        {
            DefsBundle defs = DefsRegistry.Current;
            bool female = random.NextDouble() < 0.5;
            var identity = new SettlerIdentity
            {
                Female = female,
                Name = PickChildName(random, female ? defs.Names.Female : defs.Names.Male, takenNames),
                Origin = origin,
                Traits = InheritTraits(random, mother?.Traits, father?.Traits, defs.Traits, AoJConfig.SettlerMaxTraits.Value),
                Hair = PickCustomization(random, "Hair"),
                Beard = female ? "" : PickCustomization(random, "Beard"),
            };

            // Skin: the parents' average; hair: one parent's. A missing parent is stood in for by a fresh roll.
            Vector3 motherSkin = mother?.SkinColor ?? RollSkinColor(random);
            Vector3 fatherSkin = father?.SkinColor ?? RollSkinColor(random);
            identity.SkinColor = (motherSkin + fatherSkin) * 0.5f;
            SettlerIdentity hairFrom = random.NextDouble() < 0.5 ? mother ?? father : father ?? mother;
            identity.HairColor = hairFrom?.HairColor ?? RollHairColor(random);
            return identity;
        }

        /// <summary>A first name from the list that nobody on the roster has; any name when all are taken (pure, for tests).</summary>
        internal static string PickChildName(System.Random random, List<string> names, ICollection<string> takenNames)
        {
            if (names == null || names.Count == 0)
            {
                return FallbackName;
            }
            var free = new List<string>();
            foreach (string name in names)
            {
                if (takenNames == null || !takenNames.Contains(name))
                {
                    free.Add(name);
                }
            }
            List<string> candidates = free.Count > 0 ? free : names;
            return candidates[random.Next(candidates.Count)];
        }

        /// <summary>
        /// Each trait of either parent is passed on with <see cref="InheritChance"/>, respecting the exclusions and the
        /// trait limit; a child that inherited nothing gets one random positive trait, so nobody is born a blank (pure,
        /// for tests). Traits no longer in the definitions are skipped.
        /// </summary>
        internal static List<string> InheritTraits(System.Random random, List<string> motherTraits, List<string> fatherTraits,
            List<TraitDef> pool, int maxTraits)
        {
            var chosen = new List<TraitDef>();
            var candidates = new List<string>();
            AddUnique(candidates, motherTraits);
            AddUnique(candidates, fatherTraits);
            foreach (string id in candidates)
            {
                TraitDef trait = pool.Find(t => t.Id == id);
                if (trait == null || chosen.Count >= maxTraits || random.NextDouble() >= InheritChance)
                {
                    continue;
                }
                if (!chosen.Contains(trait) && !chosen.Any(c => c.Excludes.Contains(trait.Id) || trait.Excludes.Contains(c.Id)))
                {
                    chosen.Add(trait);
                }
            }
            if (chosen.Count == 0 && maxTraits > 0 && pool.Count > 0)
            {
                List<TraitDef> positive = pool.Where(t => t.Positive && t.Weight > 0f).ToList();
                List<TraitDef> any = positive.Count > 0 ? positive : pool.Where(t => t.Weight > 0f).ToList();
                if (any.Count > 0)
                {
                    chosen.Add(PickWeighted(random, any, t => t.Weight));
                }
            }
            return chosen.Select(t => t.Id).ToList();
        }

        private static void AddUnique(List<string> into, List<string> traits)
        {
            if (traits == null)
            {
                return;
            }
            foreach (string id in traits)
            {
                if (!into.Contains(id))
                {
                    into.Add(id);
                }
            }
        }

        private static Vector3 RollSkinColor(System.Random random) =>
            Utils.ColorToVec3(Color.Lerp(AppearancePalette.SkinColor0, AppearancePalette.SkinColor1, (float)random.NextDouble()));

        private static Vector3 RollHairColor(System.Random random)
        {
            Color hair = Color.Lerp(AppearancePalette.HairColor0, AppearancePalette.HairColor1, (float)random.NextDouble());
            float level = Mathf.Lerp(AppearancePalette.HairMinLevel, AppearancePalette.HairMaxLevel, (float)random.NextDouble());
            return Utils.ColorToVec3(hair * level);
        }

        private static List<string> RollTraits(System.Random random, List<TraitDef> pool, Heightmap.Biome origin)
        {
            int target = random.Next(1, AoJConfig.SettlerMaxTraits.Value + 1);
            float originBonus = AoJConfig.OriginTraitBonus.Value;
            string originName = origin.ToString();
            var chosen = new List<TraitDef>();

            while (chosen.Count < target)
            {
                // Exclusions are symmetric (DefsValidator), so checking the chosen side is enough.
                List<TraitDef> allowed = pool.Where(t => !chosen.Contains(t) && !chosen.Any(c => c.Excludes.Contains(t.Id))).ToList();
                // The first trait is positive whenever the pool has one, so nobody is only a burden.
                List<TraitDef> candidates = chosen.Count == 0 && allowed.Any(t => t.Positive)
                    ? allowed.Where(t => t.Positive).ToList()
                    : allowed;
                if (candidates.Count == 0)
                {
                    break;
                }
                chosen.Add(PickWeighted(random, candidates, t => t.Weight * (t.Origins.Contains(originName) ? originBonus : 1f)));
            }
            return chosen.Select(t => t.Id).ToList();
        }

        private static TraitDef PickWeighted(System.Random random, List<TraitDef> candidates, Func<TraitDef, float> weight)
        {
            double roll = random.NextDouble() * candidates.Sum(weight);
            foreach (TraitDef trait in candidates)
            {
                roll -= weight(trait);
                if (roll <= 0)
                {
                    return trait;
                }
            }
            return candidates[candidates.Count - 1];
        }

        // Same item lists the vanilla character creator offers (ItemType.Customization, "Hair…" / "Beard…").
        private static string PickCustomization(System.Random random, string prefix)
        {
            List<ItemDrop> items = ObjectDB.instance != null
                ? ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix)
                : null;
            return items == null || items.Count == 0 ? "" : items[random.Next(items.Count)].gameObject.name;
        }
    }
}
