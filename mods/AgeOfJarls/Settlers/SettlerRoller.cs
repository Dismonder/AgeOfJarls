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
                SkinColor = Utils.ColorToVec3(Color.Lerp(AppearancePalette.SkinColor0, AppearancePalette.SkinColor1, (float)random.NextDouble())),
            };

            Color hair = Color.Lerp(AppearancePalette.HairColor0, AppearancePalette.HairColor1, (float)random.NextDouble());
            float level = Mathf.Lerp(AppearancePalette.HairMinLevel, AppearancePalette.HairMaxLevel, (float)random.NextDouble());
            identity.HairColor = Utils.ColorToVec3(hair * level);
            return identity;
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
