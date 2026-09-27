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
    /// at home may be besieged: 2-4 waves come from beyond the settlement's edge, hunting players and settlers - from
    /// one direction, from two once "full sieges" are unlocked (tier 4), from three from tier 6. The attackers match the
    /// settlement's progress (Black Forest foes at tier 1, the swamp at tier 2 and so on); from the second wave a siege
    /// unit (a troll, an abomination...) comes along to break buildings, and the last wave brings an elite. Buildings
    /// destroyed meanwhile are remembered for the Builders to put back (<see cref="Breaches"/>). Beating every wave
    /// earns fame, a day of high morale and a chronicle entry. A settlement nobody is in is never besieged; if everyone
    /// leaves, the siege peters out.
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
            internal readonly List<Vector3> Entries = new List<Vector3>();
            internal readonly List<Character> Attackers = new List<Character>();
        }

        private static readonly Dictionary<long, Siege> s_active = new Dictionary<long, Siege>();

        // Common attackers, the elite of the last wave and the siege unit, by the settlement's tier (the bosses beaten).
        private static readonly (string[] common, string elite, string siegeUnit)[] Waves =
        {
            (new[] { "Greyling", "Greydwarf" }, "Greydwarf_Elite", ""),
            (new[] { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" }, "Troll", "Troll"),
            (new[] { "Draugr", "Skeleton", "Draugr_Elite" }, "Abomination", "Abomination"),
            (new[] { "Wolf", "Fenring", "Ulv" }, "StoneGolem", "StoneGolem"),
            (new[] { "Goblin", "GoblinArcher", "GoblinShaman" }, "GoblinBrute", "GoblinBrute"),
            (new[] { "Seeker", "SeekerSoldier", "Dverger" }, "Gjall", "SeekerBrute"),
            (new[] { "Charred_Melee", "Charred_Archer", "Charred_Mage" }, "Morgen", "Morgen"),
            (new[] { "Charred_Melee", "Charred_Archer", "Charred_Mage", "Asksvin" }, "Morgen", "Morgen"),
        };

        internal static bool IsBesieged(long settlementId) => s_active.ContainsKey(settlementId);

        /// <summary>Table owner: a breach the table accepted (see JarlTable), counted for the chronicle.</summary>
        internal static void CountBreach(long settlementId)
        {
            if (s_active.TryGetValue(settlementId, out Siege siege))
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

        // A building destroyed by a blow, on the machine that owns it (not always the table's): during a siege it is
        // reported to the table, which remembers it for the Builders.
        [HarmonyLib.HarmonyPatch(typeof(WearNTear), "Destroy")]
        private static class BreachPatch
        {
            [HarmonyLib.HarmonyPrefix]
            private static void Prefix(WearNTear __instance, HitData hitData)
            {
                // Deconstructing (no hit) is not a breach.
                if (hitData == null || __instance == null || __instance.GetComponent<Piece>() == null)
                {
                    return;
                }
                JarlTable table = JarlTable.FindContaining(__instance.transform.position);
                if (table != null && table.UnderSiege)
                {
                    table.ReportBreach(Utils.GetPrefabName(__instance.gameObject), __instance.transform.position, __instance.transform.rotation);
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
            if (table.UnderSiege)
            {
                // The siege ran on a machine that is gone (the table changed hands): it ends here.
                table.SetUnderSiege(false);
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
            int directions = !JarlTable.HasUnlock(data, UnlockFullSieges) ? 1 : data.Tier >= 6 ? 3 : 2;
            List<Vector3> entries = FindEntries(table, directions);
            if (entries.Count == 0)
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
            };
            siege.Entries.AddRange(entries);
            s_active[id] = siege;
            table.SetUnderSiege(true);
            table.Mourn();
            Chronicle.Add(table.NetView, "$aoj_chr_siege");
            MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, $"$aoj_msg_siege {JarlTable.DisplayName(data)}");
            Alarm.Set(table, true, manual: false);
            Log.Info(Module, $"Siege of {JarlTable.DisplayName(data)}: {siege.WavesLeft} waves of about {siege.Strength}, from {entries.Count} side(s)");
            SpawnWave(table, data, id, siege);
            return true;
        }

        /// <summary>The unlock id (tiers.json) that brings attacks from several sides.</summary>
        private const string UnlockFullSieges = "sieges";

        private static void End(JarlTable table, long id)
        {
            s_active.Remove(id);
            table.SetUnderSiege(false);
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
                    End(table, id);
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
        private static (string[] common, string elite, string siegeUnit) RosterFor(int tier)
        {
            List<Core.Defs.SiegeWaveDef> waves = Core.Defs.DefsRegistry.Current.Raids.SiegeWaves;
            Core.Defs.SiegeWaveDef best = waves.LastOrDefault(w => w.Tier <= tier) ?? waves.FirstOrDefault();
            if (best != null)
            {
                return (best.Common.ToArray(), best.Elite, best.SiegeUnit);
            }
            return Waves[Mathf.Clamp(tier - 1, 0, Waves.Length - 1)];
        }

        private static void SpawnWave(JarlTable table, SettlementData data, long id, Siege siege)
        {
            (string[] common, string elite, string siegeUnit) roster = RosterFor(data.Tier);
            bool last = siege.WavesLeft == 1;
            int count = siege.Strength + siege.Wave;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                string prefab = last && i == 0 && !string.IsNullOrEmpty(roster.elite) ? roster.elite : roster.common[Random.Range(0, roster.common.Length)];
                // Round the sides: every direction gets its share.
                Character attacker = Spawn(prefab, siege.Entries[i % siege.Entries.Count], id, table.transform.position, huntPlayers: true);
                if (attacker != null)
                {
                    siege.Attackers.Add(attacker);
                    spawned++;
                }
            }
            // From the second wave: a siege unit that goes for the buildings, not the people.
            if (siege.Wave >= 1 && !string.IsNullOrEmpty(roster.siegeUnit))
            {
                Character breaker = Spawn(roster.siegeUnit, siege.Entries[Random.Range(0, siege.Entries.Count)], id, table.transform.position, huntPlayers: false);
                if (breaker != null)
                {
                    siege.Attackers.Add(breaker);
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

        private static Character Spawn(string prefabName, Vector3 entry, long settlementId, Vector3 target, bool huntPlayers)
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
                // A siege unit left to itself goes for the nearest buildings (MonsterAI's static targets).
                ai.SetHuntPlayer(huntPlayers);
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
            End(table, id);
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

        // Dry spots beyond the settlement's edge, spread around it (one per side, fewer if the land is mostly water).
        private static List<Vector3> FindEntries(JarlTable table, int count)
        {
            var entries = new List<Vector3>();
            float distance = table.Radius + EntryMargin;
            float water = ZoneSystem.instance.m_waterLevel;
            float start = Random.Range(0f, 360f);
            for (int side = 0; side < count; side++)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float angle = start + side * 360f / count + Random.Range(-30f, 30f);
                    Vector3 point = table.transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                    if (ZoneSystem.instance.GetGroundHeight(point, out float height) && height > water + 1f)
                    {
                        point.y = height;
                        entries.Add(point);
                        break;
                    }
                }
            }
            return entries;
        }
    }
}
