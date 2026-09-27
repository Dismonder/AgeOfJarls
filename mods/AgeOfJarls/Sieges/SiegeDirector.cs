using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Army;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using UnityEngine;

namespace AgeOfJarls.Sieges
{
    /// <summary>
    /// Sieges, run by the Jarl's Table owner. From tier 1, every few game days (configurable) a settlement with a player
    /// at home may be besieged: 2-4 waves come from one direction, from beyond the settlement's edge, hunting players
    /// and settlers. The attackers match the settlement's progress (Black Forest foes at tier 1, the swamp at tier 2 and
    /// so on), the last wave brings an elite. Beating every wave earns fame, a day of high morale and a chronicle entry.
    /// A settlement nobody is in is never besieged; if everyone leaves, the siege peters out.
    /// </summary>
    internal static class SiegeDirector
    {
        private const string Module = "Sieges";
        private const float EntryMargin = 25f;
        private const float WaveSeconds = 180f;
        private const float AbandonSeconds = 180f;
        private const float Spread = 5f;
        private const float AttackerRange = 150f;

        private sealed class Siege
        {
            internal int Breaches;
            internal int Wave;
            internal int WavesLeft;
            internal int Strength;
            internal int LastWaveSize;
            internal float NextWaveAt;
            internal float EmptySince = -1f;
            internal Vector3 Entry;
            internal readonly List<Character> Attackers = new List<Character>();
        }

        private static readonly Dictionary<long, Siege> s_active = new Dictionary<long, Siege>();

        // Common attackers and the elite of the last wave, by the settlement's tier (i.e. the bosses beaten).
        private static readonly (string[] common, string elite)[] Waves =
        {
            (new[] { "Greyling", "Greydwarf" }, "Greydwarf_Elite"),
            (new[] { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" }, "Troll"),
            (new[] { "Draugr", "Skeleton", "Draugr_Elite" }, "Abomination"),
            (new[] { "Wolf", "Fenring", "Ulv" }, "StoneGolem"),
            (new[] { "Goblin", "GoblinArcher", "GoblinShaman" }, "GoblinBrute"),
            (new[] { "Seeker", "SeekerSoldier", "Dverger" }, "Gjall"),
            (new[] { "Charred_Melee", "Charred_Archer", "Charred_Mage" }, "Morgen"),
            (new[] { "Charred_Melee", "Charred_Archer", "Charred_Mage", "Asksvin" }, "Morgen"),
        };

        internal static bool IsBesieged(long settlementId) => s_active.ContainsKey(settlementId);

        /// <summary>A building destroyed by a blow (on its owner): counted as a breach of a besieged settlement here.</summary>
        internal static void RecordBreach(Vector3 position)
        {
            if (s_active.Count == 0)
            {
                return;
            }
            JarlTable table = JarlTable.FindContaining(position);
            if (table != null && s_active.TryGetValue(table.SettlementId, out Siege siege))
            {
                siege.Breaches++;
            }
        }

        private static void ReportBreaches(JarlTable table, Siege siege)
        {
            if (siege.Breaches > 0)
            {
                Chronicle.Add(table.NetView, "$aoj_chr_breaches", siege.Breaches.ToString());
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(WearNTear), "Destroy")]
        private static class BreachPatch
        {
            [HarmonyLib.HarmonyPrefix]
            private static void Prefix(WearNTear __instance, HitData hitData)
            {
                // Deconstructing (no hit) is not a breach.
                if (hitData != null && __instance != null)
                {
                    RecordBreach(__instance.transform.position);
                }
            }
        }

        /// <summary>Owner of the table, every table tick.</summary>
        internal static void Tick(JarlTable table, SettlementData data)
        {
            long id = table.SettlementId;
            if (id == 0L)
            {
                return;
            }
            if (s_active.TryGetValue(id, out Siege siege))
            {
                Update(table, data, id, siege);
                return;
            }
            if (!AoJConfig.Sieges.Value || data.Tier < 1)
            {
                return;
            }

            ZDO zdo = table.NetView.GetZDO();
            double now = WorldClock.Now;
            double next = WorldClock.Get(zdo, Keys.ZdoSettlementNextSiege, 0.0);
            if (next <= 0.0)
            {
                Schedule(zdo, now);
                return;
            }
            if (now < next)
            {
                return;
            }
            Schedule(zdo, now);
            if (PlayersAtHome(table) > 0 && Random.value <= AoJConfig.SiegeChance.Value)
            {
                Start(table, data, id);
            }
        }

        /// <summary>Starts a siege at once (console: <c>aoj_siege</c>).</summary>
        internal static bool Start(JarlTable table, SettlementData data, long id)
        {
            if (s_active.ContainsKey(id))
            {
                return false;
            }
            Vector3? entry = FindEntry(table);
            if (entry == null)
            {
                Log.Warning(Module, $"No dry ground around {JarlTable.DisplayName(data)} to attack from");
                return false;
            }
            int population = data.Settlers.Count;
            // More players at home, more attackers (Sieges/PerPlayer for each one beyond the first).
            float defenders = 1f + AoJConfig.SiegePerPlayer.Value * Mathf.Max(0, PlayersAtHome(table) - 1);
            var siege = new Siege
            {
                WavesLeft = Mathf.Clamp(2 + data.Tier / 2, 2, 4),
                Strength = Mathf.Max(2, Mathf.RoundToInt((2 + data.Tier + population / 5f) * AoJConfig.SiegeStrength.Value * defenders)),
                Entry = entry.Value,
            };
            s_active[id] = siege;
            table.Mourn();
            Chronicle.Add(table.NetView, "$aoj_chr_siege");
            MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, $"$aoj_msg_siege {JarlTable.DisplayName(data)}");
            Alarm.Set(table, true, manual: false);
            Log.Info(Module, $"Siege of {JarlTable.DisplayName(data)}: {siege.WavesLeft} waves of about {siege.Strength}, from {siege.Entry:F0}");
            SpawnWave(table, data, id, siege);
            return true;
        }

        private static void Update(JarlTable table, SettlementData data, long id, Siege siege)
        {
            siege.Attackers.RemoveAll(a => a == null || a.IsDead() || Vector3.Distance(a.transform.position, table.transform.position) > AttackerRange);

            if (PlayersAtHome(table) == 0)
            {
                if (siege.EmptySince < 0f)
                {
                    siege.EmptySince = Time.time;
                }
                if (Time.time - siege.EmptySince > AbandonSeconds)
                {
                    s_active.Remove(id);
                    Chronicle.Add(table.NetView, "$aoj_chr_siege_faded");
                    ReportBreaches(table, siege);
                    Log.Info(Module, $"Siege of {JarlTable.DisplayName(data)} petered out: nobody at home");
                }
                return;
            }
            siege.EmptySince = -1f;

            bool waveBroken = siege.Attackers.Count <= siege.LastWaveSize / 4;
            if (siege.WavesLeft > 0 && (waveBroken || Time.time >= siege.NextWaveAt))
            {
                SpawnWave(table, data, id, siege);
                return;
            }
            if (siege.WavesLeft == 0 && siege.Attackers.Count == 0)
            {
                Victory(table, data, id);
            }
        }

        // The line-up for the settlement's tier from raids.json (the highest entry not above the tier), else built in.
        private static (string[] common, string elite) RosterFor(int tier)
        {
            List<Core.Defs.SiegeWaveDef> waves = Core.Defs.DefsRegistry.Current.Raids.SiegeWaves;
            Core.Defs.SiegeWaveDef best = waves.LastOrDefault(w => w.Tier <= tier) ?? waves.FirstOrDefault();
            if (best != null)
            {
                return (best.Common.ToArray(), best.Elite);
            }
            return Waves[Mathf.Clamp(tier - 1, 0, Waves.Length - 1)];
        }

        private static void SpawnWave(JarlTable table, SettlementData data, long id, Siege siege)
        {
            (string[] common, string elite) roster = RosterFor(data.Tier);
            bool last = siege.WavesLeft == 1;
            int count = siege.Strength + siege.Wave;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                string prefab = last && i == 0 && !string.IsNullOrEmpty(roster.elite) ? roster.elite : roster.common[Random.Range(0, roster.common.Length)];
                Character attacker = Spawn(prefab, siege.Entry, id, table.transform.position);
                if (attacker != null)
                {
                    siege.Attackers.Add(attacker);
                    spawned++;
                }
            }
            siege.Wave++;
            siege.WavesLeft--;
            siege.LastWaveSize = spawned;
            siege.NextWaveAt = Time.time + WaveSeconds;
            if (siege.Wave > 1)
            {
                MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, "$aoj_msg_siege_wave");
            }
            Log.Info(Module, $"Wave {siege.Wave}: {spawned} attackers");
        }

        private static Character Spawn(string prefabName, Vector3 entry, long settlementId, Vector3 target)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab == null)
            {
                return null;
            }
            Vector2 offset = Random.insideUnitCircle * Spread;
            Vector3 spot = entry + new Vector3(offset.x, 0f, offset.y);
            if (ZoneSystem.instance.GetGroundHeight(spot, out float height))
            {
                spot.y = height + 0.5f;
            }
            GameObject spawned = Object.Instantiate(prefab, spot, Quaternion.LookRotation(target - spot));
            spawned.GetComponent<ZNetView>()?.GetZDO()?.Set(Keys.ZdoSiegeOf, settlementId);
            BaseAI ai = spawned.GetComponent<BaseAI>();
            if (ai != null)
            {
                ai.SetHuntPlayer(true);
                ai.SetAlerted(true);
            }
            return spawned.GetComponent<Character>();
        }

        private static void Victory(JarlTable table, SettlementData data, long id)
        {
            if (s_active.TryGetValue(id, out Siege siege))
            {
                ReportBreaches(table, siege);
            }
            s_active.Remove(id);
            ZDO zdo = table.NetView.GetZDO();
            int fame = data.Tier + 1;
            zdo.Set(Keys.ZdoSettlementFame, zdo.GetInt(Keys.ZdoSettlementFame) + fame);
            double morale = WorldClock.Now + WorldClock.DayLength;
            if (WorldClock.Get(zdo, Keys.ZdoSettlementFeastUntil, 0.0) < morale)
            {
                WorldClock.Set(zdo, Keys.ZdoSettlementFeastUntil, morale);
            }
            Chronicle.Add(table.NetView, "$aoj_chr_siege_won", fame.ToString());
            MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, $"$aoj_msg_siege_won {JarlTable.DisplayName(data)}");
            Log.Info(Module, $"{JarlTable.DisplayName(data)} held out: fame +{fame}");
        }

        private static void Schedule(ZDO zdo, double now)
        {
            double interval = AoJConfig.SiegeIntervalDays.Value * WorldClock.DayLength * Random.Range(0.75f, 1.25f);
            WorldClock.Set(zdo, Keys.ZdoSettlementNextSiege, now + interval);
        }

        internal static int PlayersAtHome(JarlTable table) =>
            Player.GetAllPlayers().Count(p => p != null && !p.IsDead() && Utils.DistanceXZ(p.transform.position, table.transform.position) <= table.Radius);

        // A dry spot beyond the settlement's edge in a random direction.
        private static Vector3? FindEntry(JarlTable table)
        {
            float distance = table.Radius + EntryMargin;
            float water = ZoneSystem.instance.m_waterLevel;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Vector3 point = table.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * distance;
                if (ZoneSystem.instance.GetGroundHeight(point, out float height) && height > water + 1f)
                {
                    point.y = height;
                    return point;
                }
            }
            return null;
        }
    }
}
