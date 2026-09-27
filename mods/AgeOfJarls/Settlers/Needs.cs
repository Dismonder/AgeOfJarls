using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Settlement;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Hunger and morale of a settler, kept in its ZDO and updated by its owner. Updates use world time, so time the
    /// settler spent unloaded counts as well (bounded, like the catch-up). Morale (0-100, start 50) comes from
    /// satiety, the bed, meal variety, traits and a feast; it scales work speed from x0.6 to x1.25.
    /// </summary>
    internal static class Needs
    {
        private const float StartSatiety = 70f;
        private const float StartMorale = 50f;
        private const float MaxElapsedDays = 2f;
        private const int MealsRemembered = 3;
        /// <summary>Satiety a meal gives per point of the food's health + stamina + eitr.</summary>
        private const float SatietyPerFoodPoint = 0.5f;
        internal const float MiserableMorale = 15f;
        private const float SatietyStep = 0.5f;

        internal static float Satiety(ZDO zdo) => zdo.GetFloat(Keys.ZdoSettlerSatiety, StartSatiety);

        internal static float Morale(ZDO zdo) => zdo.GetFloat(Keys.ZdoSettlerMorale, StartMorale);

        internal static bool IsHungry(ZDO zdo) => Satiety(zdo) < AoJConfig.HungryBelow.Value;

        /// <summary>Morale and hunger as a work pace: x0.6 at 0 morale, x1.25 at 100; starving halves it.</summary>
        internal static float WorkPace(ZDO zdo)
        {
            float pace = Mathf.Lerp(0.6f, 1.25f, Morale(zdo) / 100f);
            if (Satiety(zdo) < 10f)
            {
                pace *= 0.5f;
            }
            return pace * AoJConfig.WorkSpeed.Value;
        }

        internal static bool RefusesWork(ZDO zdo) => AoJConfig.RefuseWorkWhenMiserable.Value && Morale(zdo) < MiserableMorale;

        /// <summary>Owner only, about once a second.</summary>
        internal static void Update(ZDO zdo, Settler settler, JarlTable home)
        {
            double now = WorldClock.Now;
            if (zdo.GetLong(Keys.ZdoSettlerNeedsTime) == 0L)
            {
                WorldClock.Set(zdo, Keys.ZdoSettlerNeedsTime, now);
            }
            double last = WorldClock.Get(zdo, Keys.ZdoSettlerNeedsTime, now);
            float days = (float)Math.Min(Math.Max(now - last, 0.0) / WorldClock.DayLength, MaxElapsedDays);

            // Hunger is written in steps of half a point (with the clock), so a settler's ZDO does not change every
            // second; the time in between keeps adding up until the next step.
            float satiety = Satiety(zdo);
            float appetite = Mathf.Max(0.1f, 1f + TraitSum(settler, TraitStat.FoodConsumption));
            float hunger = days * AoJConfig.SatietyPerDay.Value * appetite;
            if (hunger >= SatietyStep || (satiety <= 0f && days > 0f))
            {
                satiety = Mathf.Clamp(satiety - hunger, 0f, 100f);
                zdo.Set(Keys.ZdoSettlerSatiety, satiety);
                WorldClock.Set(zdo, Keys.ZdoSettlerNeedsTime, now);
            }

            float morale = ComputeMorale(zdo, settler, home, satiety);
            if (Mathf.Abs(morale - Morale(zdo)) >= 0.5f)
            {
                zdo.Set(Keys.ZdoSettlerMorale, morale);
            }
        }

        /// <summary>Owner only: the settler eats this food (the caller already took it from a cauldron).</summary>
        internal static void Eat(ZDO zdo, ItemDrop.ItemData food)
        {
            ItemDrop.ItemData.SharedData shared = food.m_shared;
            float value = (shared.m_food + shared.m_foodStamina + shared.m_foodEitr) * SatietyPerFoodPoint;
            zdo.Set(Keys.ZdoSettlerSatiety, Mathf.Clamp(Satiety(zdo) + Mathf.Max(value, 5f), 0f, 100f));

            List<string> meals = Meals(zdo);
            meals.Add(shared.m_name);
            while (meals.Count > MealsRemembered)
            {
                meals.RemoveAt(0);
            }
            zdo.Set(Keys.ZdoSettlerMeals, string.Join("|", meals));
        }

        /// <summary>Satiety a food gives, for picking the best meal.</summary>
        internal static float MealValue(ItemDrop.ItemData food) =>
            (food.m_shared.m_food + food.m_shared.m_foodStamina + food.m_shared.m_foodEitr) * SatietyPerFoodPoint;

        internal static bool IsFood(ItemDrop.ItemData item) =>
            item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
            (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f);

        /// <summary>Tokens for the window: satiety, morale and what drives the morale.</summary>
        internal static string Describe(ZDO zdo)
        {
            float satiety = Satiety(zdo);
            string hunger = satiety < 10f ? "$aoj_satiety_starving" : satiety < AoJConfig.HungryBelow.Value ? "$aoj_satiety_hungry" : "$aoj_satiety_fed";
            return $"$aoj_satiety: {Mathf.RoundToInt(satiety)} ({hunger})  ·  $aoj_morale: {Mathf.RoundToInt(Morale(zdo))}  ·  $aoj_pace x{WorkPace(zdo):0.00}";
        }

        private static List<string> Meals(ZDO zdo)
        {
            string meals = zdo.GetString(Keys.ZdoSettlerMeals);
            return meals.Length == 0 ? new List<string>() : meals.Split('|').ToList();
        }

        private const float ComfortCheckSeconds = 60f;
        private static readonly Dictionary<long, (float time, int comfort)> s_comfort = new Dictionary<long, (float, int)>();

        // Vanilla comfort around the bed (fire, chairs, banners...), rechecked once a minute per settler.
        private static int BedComfort(Settler settler, Vector3 bed)
        {
            long uid = settler.Uid;
            if (s_comfort.TryGetValue(uid, out (float time, int comfort) cached) && Time.time - cached.time < ComfortCheckSeconds)
            {
                return cached.comfort;
            }
            int comfort = SE_Rested.CalculateComfortLevel(true, bed);
            s_comfort[uid] = (Time.time, comfort);
            if (s_comfort.Count > 256)
            {
                s_comfort.Clear();
            }
            return comfort;
        }

        // 50 + satiety (-30..+15) + bed (-20..+5) + comfort (0..+10) + variety (0..+10) + safety (-10..0) + traits
        // + feast (+20).
        private static float ComputeMorale(ZDO zdo, Settler settler, JarlTable home, float satiety)
        {
            float morale = StartMorale;
            morale += satiety < 50f ? Mathf.Lerp(-30f, 0f, satiety / 50f) : Mathf.Lerp(0f, 15f, (satiety - 50f) / 50f);

            if (home != null)
            {
                if (settler.TryGetBed(home, out Vector3 bed))
                {
                    morale += 5f + Mathf.Min(10f, Mathf.Max(0, BedComfort(settler, bed) - 1) * 0.75f);
                }
                else
                {
                    morale -= 20f;
                }
                double now = WorldClock.Now;
                if (home.FeastUntil > now)
                {
                    morale += 20f;
                }
                if (home.GriefUntil > now)
                {
                    morale -= 10f;
                }
            }
            else if (settler.HasHome)
            {
                // Home not loaded here: keep the bed part neutral rather than guessing.
            }
            else
            {
                morale -= 10f;
            }

            int variety = Meals(zdo).Distinct().Count();
            morale += variety >= 3 ? 10f : variety == 2 ? 5f : 0f;
            morale += TraitSum(settler, TraitStat.MoraleRecovery) * 20f;
            return Mathf.Clamp(morale, 0f, 100f);
        }

        private static float TraitSum(Settler settler, string stat)
        {
            SettlerIdentity identity = settler.Identity;
            if (identity == null)
            {
                return 0f;
            }
            float sum = 0f;
            foreach (string id in identity.Traits)
            {
                TraitDef trait = DefsRegistry.Current.Traits.Find(t => t.Id == id);
                if (trait != null && trait.Modifiers.TryGetValue(stat, out float value))
                {
                    sum += value;
                }
            }
            return sum;
        }
    }
}
