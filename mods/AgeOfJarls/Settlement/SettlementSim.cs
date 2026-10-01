using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// The settlement's clock and its catch-up. The game only simulates what is near a player, so a settlement nobody
    /// visits stands still. The table's owner keeps the last simulated moment in the table's ZDO; when a settlement
    /// wakes up after a while, it credits the work of the absence (at most <c>CatchUp.MaxDays</c>), scaled by how long
    /// the food lasted - settlers eat from the cauldrons for the days they were alone:
    /// Woodcutters and Miners deliver their measured pace (or the configured default), limited by what grows or lies in
    /// the zone; Smelter hands and Cooks put the ore, fuel and raw food from the chests through their stations at the
    /// stations' own pace; Farmers bring in the crops that ripened meanwhile (the game grows them by the clock) and
    /// replant them from the seeds in the chests. Only chests this machine owns take part. The result and the new clock
    /// are written together, so a relog never credits the same time twice.
    /// </summary>
    internal static class SettlementSim
    {
        private const string Module = "Settlement";
        /// <summary>Shorter absences are just a pause.</summary>
        private const double MinAbsenceSeconds = 180.0;
        private const float MeasuredAfterSeconds = 300f;
        private const int WoodPerTree = 15;
        private const int StonePerRock = 20;
        private const float TargetSatiety = 60f;

        private static readonly HashSet<long> s_started = new HashSet<long>();
        private static readonly List<Container> s_chests = new List<Container>();

        /// <summary>Owner of the table, every table tick.</summary>
        internal static void Tick(JarlTable table, SettlementData data)
        {
            long id = table.SettlementId;
            ZDO zdo = table.NetView.GetZDO();
            double now = WorldClock.Now;
            if (id == 0L)
            {
                return;
            }
            // The first tick after the table woke up on this machine settles the absence.
            if (s_started.Add(id))
            {
                double last = WorldClock.Get(zdo, Keys.ZdoSettlementSimTime, now);
                if (now - last > MinAbsenceSeconds)
                {
                    CatchUp(table, data, now - last);
                }
            }
            WorldClock.Set(zdo, Keys.ZdoSettlementSimTime, now);
        }

        /// <summary>The table unloaded here: the next time it wakes up, the absence is settled again.</summary>
        internal static void Forget(long settlementId) => s_started.Remove(settlementId);

        /// <summary>Also for the console (<c>aoj_catchup</c>).</summary>
        internal static void CatchUp(JarlTable table, SettlementData data, double awaySeconds)
        {
            double maxSeconds = AoJConfig.CatchUpMaxDays.Value * WorldClock.DayLength;
            if (maxSeconds <= 0.0)
            {
                return;
            }
            double window = Math.Min(awaySeconds, maxSeconds);
            float days = (float)(window / WorldClock.DayLength);
            float radius = JarlTable.RadiusOf(data);
            SettlementStorage.CollectChests(table.transform.position, radius, data, s_chests);

            List<Settler> residents = Settler.Loaded
                .Where(s => s != null && s.Identity != null && s.HomeId == table.SettlementId && s.Zdo != null &&
                            s.GetComponent<ZNetView>().IsOwner())
                .ToList();
            float fedShare = Feed(residents, table, radius);

            var credited = new Dictionary<string, int>();
            Vector3 from = table.transform.position;
            foreach (WorkTotem totem in WorkTotem.Loaded.Where(t => t != null && t.Settlement == table))
            {
                List<Settler> workers = residents.Where(r => r.JobId == totem.Id).ToList();
                if (workers.Count == 0)
                {
                    continue;
                }
                switch (totem.Job)
                {
                    case JobType.Woodcutter:
                        Gather("Wood", WorkScanner.Find<TreeBase>(totem).Count * WoodPerTree, workers, window * totem.PaceBonus, fedShare, from, credited);
                        break;
                    case JobType.Miner:
                        Gather("Stone", (WorkScanner.Find<MineRock5>(totem).Count + WorkScanner.Find<MineRock>(totem).Count) * StonePerRock,
                            workers, window * totem.PaceBonus, fedShare, from, credited);
                        break;
                    case JobType.Smelter:
                        Smelt(totem, window * fedShare, from, credited);
                        break;
                    case JobType.Cook:
                        Cook(totem, window * fedShare, from, credited);
                        break;
                    case JobType.Farmer:
                        Harvest(totem, workers.Average(w => w.TraitSum(Core.Defs.TraitStat.CropYield)), from, credited);
                        break;
                }
            }

            string summary = string.Join(", ", credited.Select(c => $"{ItemToken(c.Key)} +{c.Value}"));
            Log.Info(Module, $"{JarlTable.DisplayName(data)}: caught up {days:0.0} day(s) of absence ({(summary.Length > 0 ? summary : "nothing produced")}, fed {fedShare:P0})");
            if (credited.Count > 0 || residents.Count > 0)
            {
                string report = credited.Count > 0 ? summary : "$aoj_catchup_nothing";
                Chronicle.Add(table.NetView, "$aoj_chr_catchup", days.ToString("0.0"), report);
                if (MessageHud.instance != null)
                {
                    MessageHud.instance.MessageAll(MessageHud.MessageType.Center, $"$aoj_msg_catchup {report}");
                }
            }
        }

        // Woodcutters and Miners: each worker's pace times the time away, no more than the zone holds.
        private static void Gather(string item, int pool, List<Settler> workers, double window, float fedShare, Vector3 from,
            Dictionary<string, int> credited)
        {
            float amount = 0f;
            foreach (Settler worker in workers)
            {
                amount += PerSecond(worker) * (float)window * Needs.WorkPace(worker.Zdo);
            }
            int produced = Mathf.Min(Mathf.FloorToInt(amount * fedShare), pool);
            if (produced > 0)
            {
                Credit(credited, item, Deposit(item, produced, from));
            }
        }

        // Smelter hands: every station in the zone works through ore and fuel from the chests at its own pace
        // (vanilla also finishes whatever was already loaded when it wakes up).
        private static void Smelt(WorkTotem totem, double seconds, Vector3 from, Dictionary<string, int> credited)
        {
            foreach (Component component in WorkScanner.Find<Smelter>(totem))
            {
                if (!(component is Smelter station) || station == null || station.m_secPerProduct <= 0f)
                {
                    continue;
                }
                string fuel = station.m_fuelItem != null && station.m_fuelPerProduct > 0 ? station.m_fuelItem.m_itemData.m_shared.m_name : null;
                int capacity = (int)(seconds / station.m_secPerProduct);
                foreach (Smelter.ItemConversion conversion in station.m_conversion)
                {
                    if (capacity <= 0)
                    {
                        break;
                    }
                    if (conversion.m_from == null || conversion.m_to == null)
                    {
                        continue;
                    }
                    string input = conversion.m_from.m_itemData.m_shared.m_name;
                    int count = Math.Min(capacity, CountInChests(input));
                    if (fuel != null)
                    {
                        count = Math.Min(count, CountInChests(fuel) / station.m_fuelPerProduct);
                    }
                    // Products first: only what found room is paid for.
                    int made = count > 0 ? Deposit(conversion.m_to.gameObject.name, count, from) : 0;
                    if (made <= 0)
                    {
                        continue;
                    }
                    RemoveFromChests(input, made);
                    if (fuel != null)
                    {
                        RemoveFromChests(fuel, made * station.m_fuelPerProduct);
                    }
                    capacity -= made;
                    Credit(credited, conversion.m_to.gameObject.name, made);
                }
            }
        }

        // Cooks: raw food from the chests goes through every slot of the stations in the zone at its cooking time; an
        // oven also burns its fuel meanwhile. Cooked food is stored like any food: the Settlement Cauldron first.
        private static void Cook(WorkTotem totem, double seconds, Vector3 from, Dictionary<string, int> credited)
        {
            foreach (Component component in WorkScanner.Find<CookingStation>(totem))
            {
                if (!(component is CookingStation station) || station == null)
                {
                    continue;
                }
                int slots = Math.Max(1, station.m_slots != null ? station.m_slots.Length : 1);
                string fuel = station.m_useFuel && station.m_fuelItem != null && station.m_secPerFuel > 0 ? station.m_fuelItem.m_itemData.m_shared.m_name : null;
                double slotSeconds = seconds * slots;
                if (fuel != null)
                {
                    slotSeconds = Math.Min(slotSeconds, (double)CountInChests(fuel) * station.m_secPerFuel * slots);
                }
                double burned = 0.0;
                foreach (CookingStation.ItemConversion conversion in station.m_conversion)
                {
                    if (conversion.m_from == null || conversion.m_to == null || conversion.m_cookTime <= 0f || slotSeconds < conversion.m_cookTime)
                    {
                        continue;
                    }
                    string input = conversion.m_from.m_itemData.m_shared.m_name;
                    int count = Math.Min((int)(slotSeconds / conversion.m_cookTime), CountInChests(input));
                    int made = count > 0 ? Deposit(conversion.m_to.gameObject.name, count, from) : 0;
                    if (made <= 0)
                    {
                        continue;
                    }
                    RemoveFromChests(input, made);
                    slotSeconds -= made * conversion.m_cookTime;
                    burned += made * conversion.m_cookTime / slots;
                    Credit(credited, conversion.m_to.gameObject.name, made);
                }
                if (fuel != null && burned > 0.0)
                {
                    RemoveFromChests(fuel, Mathf.CeilToInt((float)(burned / station.m_secPerFuel)));
                }
            }
        }

        // Farmers: crops that ripened while nobody was there (the game grows them by the clock) are brought in and
        // their spots replanted from the seeds in the chests, as the farmer would have done (green-thumbed farmers
        // bring in more). Only crops this machine owns, and only while the chests have room.
        private static void Harvest(WorkTotem totem, float bonus, Vector3 from, Dictionary<string, int> credited)
        {
            float extra = 0f;
            Dictionary<string, (GameObject sapling, string seed)> crops = AI.Jobs.FarmerJob.Crops();
            foreach (Component component in WorkScanner.Find<Pickable>(totem).ToList())
            {
                if (!(component is Pickable crop) || crop == null || crop.m_itemPrefab == null || !crop.CanBePicked() ||
                    !crops.TryGetValue(Utils.GetPrefabName(crop.gameObject), out (GameObject sapling, string seed) planting))
                {
                    continue;
                }
                ZNetView view = crop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner())
                {
                    continue;
                }
                extra += crop.m_amount * bonus;
                int amount = crop.m_amount + (int)extra;
                extra -= (int)extra;
                int stored = Deposit(crop.m_itemPrefab.name, amount, from);
                Vector3 spot = crop.transform.position;
                if (stored < amount)
                {
                    // No room left: what did not fit waits on the ground for the farmer.
                    DropOnGround(crop.m_itemPrefab, amount - stored, spot);
                }
                ZNetScene.instance.Destroy(crop.gameObject);
                string seed = ItemToken(planting.seed);
                if (RemoveFromChests(seed, 1) == 1)
                {
                    UnityEngine.Object.Instantiate(planting.sapling, spot, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                }
                Credit(credited, crop.m_itemPrefab.name, stored);
                if (stored < amount)
                {
                    // Chests full: the rest of the field waits for the farmer.
                    break;
                }
            }
        }

        private static void DropOnGround(GameObject prefab, int amount, Vector3 position)
        {
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null || amount <= 0)
            {
                return;
            }
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Mathf.Min(amount, item.m_shared.m_maxStackSize);
            ItemDrop.DropItem(item, item.m_stack, position + Vector3.up * 0.5f, Quaternion.identity);
        }

        private static void Credit(Dictionary<string, int> credited, string item, int amount)
        {
            if (amount > 0)
            {
                credited[item] = (credited.TryGetValue(item, out int sum) ? sum : 0) + amount;
            }
        }

        // Items (by their shared name, e.g. "$item_copperore") in the settlement's chests this machine owns.
        private static int CountInChests(string sharedName)
        {
            int count = 0;
            foreach (Container chest in s_chests)
            {
                if (chest != null && chest.m_nview.IsOwner())
                {
                    count += chest.GetInventory().CountItems(sharedName);
                }
            }
            return count;
        }

        private static int RemoveFromChests(string sharedName, int amount)
        {
            int left = amount;
            foreach (Container chest in s_chests)
            {
                if (left <= 0)
                {
                    break;
                }
                if (chest == null || !chest.m_nview.IsOwner())
                {
                    continue;
                }
                Inventory inventory = chest.GetInventory();
                int take = Math.Min(left, inventory.CountItems(sharedName));
                if (take > 0)
                {
                    inventory.RemoveItem(sharedName, take);
                    left -= take;
                }
            }
            return amount - left;
        }

        // Items per second: the settler's live pace once measured long enough, else the configured default.
        private static float PerSecond(Settler worker)
        {
            ZDO zdo = worker.Zdo;
            float time = zdo.GetFloat(Keys.ZdoSettlerWorkTime);
            float output = zdo.GetFloat(Keys.ZdoSettlerWorkOutput);
            if (time >= MeasuredAfterSeconds && output > 0f)
            {
                // Measured live, traits and experience included.
                return output / time;
            }
            float traits = Mathf.Max(0.1f, 1f + worker.TraitSum(Core.Defs.TraitStat.WorkSpeed));
            return AoJConfig.CatchUpDefaultRate.Value / (float)WorldClock.DayLength * traits;
        }

        // Settlers catch up on their hunger first (Needs.Update), then eat from the cauldrons until fed. Returns the
        // share of settlers that are not starving, which scales the work credited.
        private static float Feed(List<Settler> residents, JarlTable table, float radius)
        {
            if (residents.Count == 0)
            {
                return 1f;
            }
            List<Container> pots = SettlementCauldron.Loaded
                .Where(c => c != null && c.Container != null && c.GetComponent<ZNetView>().IsOwner() &&
                            Vector3.Distance(c.transform.position, table.transform.position) <= radius)
                .Select(c => c.Container)
                .ToList();
            int fed = 0;
            foreach (Settler settler in residents)
            {
                ZDO zdo = settler.Zdo;
                Needs.Update(zdo, settler, table);
                foreach (Container pot in pots)
                {
                    pot.Load();
                    while (Needs.Satiety(zdo) < TargetSatiety)
                    {
                        ItemDrop.ItemData meal = pot.GetInventory().GetAllItems().Where(Needs.IsFood).OrderByDescending(Needs.MealValue).FirstOrDefault();
                        if (meal == null)
                        {
                            break;
                        }
                        Needs.Eat(zdo, meal);
                        pot.GetInventory().RemoveItem(meal, 1);
                    }
                }
                if (Needs.Satiety(zdo) >= 10f)
                {
                    fed++;
                }
            }
            return fed / (float)residents.Count;
        }

        // Into the settlement's chests the usual sorted way; only chests this machine owns take part.
        private static int Deposit(string itemName, int amount, Vector3 from)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                return 0;
            }
            List<Container> owned = s_chests.Where(c => c.m_nview.IsOwner()).ToList();
            int left = amount;
            int maxStack = drop.m_itemData.m_shared.m_maxStackSize;
            int guard = 1000;
            while (left > 0 && guard-- > 0)
            {
                ItemDrop.ItemData item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = Mathf.Min(left, maxStack);
                item.m_worldLevel = (byte)Game.m_worldLevel;
                Container chest = SettlementStorage.BestChestFor(item, owned, from);
                if (chest == null)
                {
                    break;
                }
                int space = SettlementStorage.FreeSpace(chest.GetInventory(), item);
                item.m_stack = Mathf.Min(item.m_stack, space);
                if (item.m_stack <= 0 || !chest.GetInventory().AddItem(item))
                {
                    break;
                }
                left -= item.m_stack;
            }
            return amount - left;
        }

        private static string ItemToken(string prefabName)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return item != null ? item.m_itemData.m_shared.m_name : prefabName;
        }
    }
}
