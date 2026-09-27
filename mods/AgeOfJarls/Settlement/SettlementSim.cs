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
    /// wakes up after a while, it credits the work of the absence analytically:
    /// for each Woodcutter and Miner totem, its workers' measured pace (or the configured default) times the time away
    /// (at most <c>CatchUp.MaxDays</c>), limited by what grows or lies in the zone and by how long the food lasted,
    /// is put into the settlement's chests; settlers eat from the cauldrons for the days they were alone. The result
    /// and the new clock are written together, so a relog never credits the same time twice.
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
            SettlementStorage.CollectChests(table.transform.position, radius, s_chests);

            List<Settler> residents = Settler.Loaded
                .Where(s => s != null && s.Identity != null && s.HomeId == table.SettlementId && s.Zdo != null &&
                            s.GetComponent<ZNetView>().IsOwner())
                .ToList();
            float fedShare = Feed(residents, table, radius);

            var credited = new Dictionary<string, int>();
            foreach (WorkTotem totem in WorkTotem.Loaded.Where(t => t != null && t.Settlement == table))
            {
                string item;
                int pool;
                if (totem.Job == JobType.Woodcutter)
                {
                    item = "Wood";
                    pool = WorkScanner.Find<TreeBase>(totem).Count * WoodPerTree;
                }
                else if (totem.Job == JobType.Miner)
                {
                    item = "Stone";
                    pool = (WorkScanner.Find<MineRock5>(totem).Count + WorkScanner.Find<MineRock>(totem).Count) * StonePerRock;
                }
                else
                {
                    continue;
                }

                float amount = 0f;
                foreach (Settler worker in residents.Where(r => r.JobId == totem.Id))
                {
                    amount += PerSecond(worker) * (float)window * Needs.WorkPace(worker.Zdo);
                }
                int produced = Mathf.Min(Mathf.FloorToInt(amount * fedShare), pool);
                if (produced > 0)
                {
                    int stored = Deposit(item, produced, table.transform.position);
                    if (stored > 0)
                    {
                        credited[item] = (credited.TryGetValue(item, out int sum) ? sum : 0) + stored;
                    }
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

        // Items per second: the settler's live pace once measured long enough, else the configured default.
        private static float PerSecond(Settler worker)
        {
            ZDO zdo = worker.Zdo;
            float time = zdo.GetFloat(Keys.ZdoSettlerWorkTime);
            float output = zdo.GetFloat(Keys.ZdoSettlerWorkOutput);
            if (time >= MeasuredAfterSeconds && output > 0f)
            {
                return output / time;
            }
            return AoJConfig.CatchUpDefaultRate.Value / (float)WorldClock.DayLength;
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
