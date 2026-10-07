using System;
using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// Puts a newborn settler into the world (the Jarl's Table owner, at a birth; also the aoj_family test commands).
    /// The machine that instantiates owns the new ZDO, so every key is written in the same frame as Instantiate:
    /// Settler.InitIdentity then finds the identity on every machine and nobody rolls a new one.
    /// </summary>
    internal static class ChildSpawner
    {
        private const string Module = "Family";
        /// <summary>A carrier farther than this from its table gives birth at its bed (or the table) instead.</summary>
        private const float CarrierRange = 80f;
        private const float BesideCarrier = 1.5f;
        private const float BesideBed = 1f;
        private const float StartSatiety = 80f;
        private const float StartMorale = 60f;
        private static int s_groundMask;

        /// <summary>
        /// A child of <paramref name="couple"/>, born at <paramref name="bornAt"/> (world time), next to the carrier
        /// when it is loaded near the table, else at the carrier's bed, else at the table's idle ring. Returns the
        /// loaded settler (null when the prefab is missing) and the record for the blob; the caller adds the record,
        /// the roster entry and the chronicle. A parent that is not loaded here contributes a plain roll to the
        /// looks (SettlerRoller.RollChild); its name still comes from the roster.
        /// </summary>
        internal static Settler SpawnChild(JarlTable table, SettlementData data, Couple couple, double bornAt, out ChildRecord record)
        {
            record = null;
            if (table == null || data == null || couple == null)
            {
                return null;
            }
            long motherUid = couple.ConceptionMother;
            long fatherUid = couple.ConceptionFather;
            Settler mother = Settler.FindByUid(motherUid);
            Settler father = Settler.FindByUid(fatherUid);
            string motherName = NameOf(mother, data, motherUid);
            string fatherName = NameOf(father, data, fatherUid);

            long childUid = Keys.NewId();
            Vector3 tablePosition = table.transform.position;
            Heightmap.Biome origin = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(tablePosition) : Heightmap.Biome.Meadows;
            SettlerIdentity identity = SettlerRoller.RollChild(new System.Random(unchecked((int)childUid)), mother?.Identity, father?.Identity, origin, TakenNames(data));
            // The father's first name; without a father (a widow, two women) the other parent's.
            string patronymic = FamilyRules.Patronymic(fatherName.Length > 0 ? fatherName : motherName, identity.Female);

            Vector3 position = BirthSpot(table, data, mother, motherUid, childUid);
            Settler child = Spawn(position, identity, childUid, bornAt, motherUid, fatherUid, patronymic, table);
            if (child == null)
            {
                return null;
            }
            record = new ChildRecord
            {
                Uid = childUid,
                Mother = motherUid,
                Father = fatherUid,
                Born = bornAt,
                LastStage = (int)FamilyRules.StageFor(WorldClock.Now - bornAt, WorldClock.DayLength, FamilyConfig.Live),
                Female = identity.Female,
                Name = identity.Name,
            };
            Log.Info(Module, $"{identity.Name} {patronymic} was born to {motherName} and {(fatherName.Length > 0 ? fatherName : "nobody")} in {JarlTable.DisplayName(data)}");
            return child;
        }

        /// <summary>
        /// aoj_family child: a test child born now at <paramref name="position"/>, parents unknown, homeless until a
        /// table lists it; <paramref name="firstName"/> (optional) in place of the rolled one.
        /// </summary>
        internal static Settler SpawnTestChild(Vector3 position, string firstName)
        {
            long childUid = Keys.NewId();
            Heightmap.Biome origin = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(position) : Heightmap.Biome.Meadows;
            SettlerIdentity identity = SettlerRoller.RollChild(new System.Random(unchecked((int)childUid)), null, null, origin, null);
            string clean = TextUtil.SanitizeName(firstName ?? "", 24);
            if (clean.Length > 0)
            {
                identity.Name = clean;
            }
            return Spawn(Ground(position), identity, childUid, WorldClock.Now, 0L, 0L, "", null);
        }

        private static string NameOf(Settler loaded, SettlementData data, long uid)
        {
            if (loaded != null && loaded.Identity != null && loaded.Identity.Name.Length > 0)
            {
                return loaded.Identity.Name;
            }
            return FamilyInfo.NameOf(data, uid);
        }

        private static HashSet<string> TakenNames(SettlementData data)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RosterEntry entry in data.Settlers)
            {
                names.Add(entry.Name);
            }
            return names;
        }

        // Beside the carrier when it is here and near its table; else beside its bed; else its own spot on the ring.
        private static Vector3 BirthSpot(JarlTable table, SettlementData data, Settler mother, long motherUid, long childUid)
        {
            Vector3 tablePosition = table.transform.position;
            if (mother != null && mother.IsLoaded && Utils.DistanceXZ(mother.transform.position, tablePosition) <= CarrierRange)
            {
                return Ground(mother.transform.position + mother.transform.right * BesideCarrier);
            }
            RosterEntry entry = data.FindSettler(motherUid);
            if (entry != null && entry.HasBed)
            {
                return Ground(entry.BedPosition + Vector3.right * BesideBed);
            }
            return Ground(tablePosition + Settler.IdleSpot(childUid));
        }

        private static Vector3 Ground(Vector3 position)
        {
            if (s_groundMask == 0) s_groundMask = LayerMask.GetMask("terrain", "piece", "static_solid", "Default");
            // Keep an upstairs birth near its support surface instead of snapping through floors to terrain.
            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f,
                s_groundMask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.5f &&
                hit.collider.GetComponentInParent<Character>() == null)
            {
                position.y = hit.point.y + 0.1f;
            }
            else if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(position, out float height))
            {
                position.y = height + 0.1f;
            }
            return position;
        }

        // Instantiate and seed every key in this frame (see the class comment); the table may be null (test child).
        private static Settler Spawn(Vector3 position, SettlerIdentity identity, long uid, double bornAt, long motherUid, long fatherUid,
            string patronymic, JarlTable table)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Keys.SettlerPrefab) : null;
            if (prefab == null)
            {
                Log.Error(Module, $"Prefab {Keys.SettlerPrefab} not found: no child spawned");
                return null;
            }
            GameObject child = Object.Instantiate(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            ZNetView nview = child.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                Log.Error(Module, "Spawned child has no valid ZNetView; destroyed");
                Object.Destroy(child);
                return null;
            }
            ZDO zdo = nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerUid, uid);
            zdo.Set(Keys.ZdoSettlerIdentity, identity.Serialize());
            zdo.Set(ZDOVars.s_overrideHoverName, identity.Name);
            // No beard until adult (Settler.ApplyStage lifts it for a grown man).
            zdo.Set(ZDOVars.s_noBeard, true);
            WorldClock.Set(zdo, Keys.ZdoSettlerBorn, bornAt);
            zdo.Set(Keys.ZdoSettlerMother, motherUid);
            zdo.Set(Keys.ZdoSettlerFather, fatherUid);
            zdo.Set(Keys.ZdoSettlerPatronymic, patronymic ?? "");
            if (table != null)
            {
                zdo.Set(Keys.ZdoSettlerHomeId, table.SettlementId);
                zdo.Set(Keys.ZdoSettlerHomePosition, table.transform.position);
            }
            zdo.Set(Keys.ZdoSettlerSatiety, StartSatiety);
            zdo.Set(Keys.ZdoSettlerMorale, StartMorale);

            // Pre-seeded identities skip first-time healing; seed full health at the actual age and traits.
            Humanoid humanoid = child.GetComponent<Humanoid>();
            if (humanoid != null)
            {
                float healthTraits = 0f;
                foreach (string id in identity.Traits)
                {
                    Core.Defs.TraitDef trait = Core.Defs.DefsRegistry.Current.Traits.Find(t => t.Id == id);
                    if (trait != null && trait.Modifiers.TryGetValue(Core.Defs.TraitStat.MaxHealth, out float value)) healthTraits += value;
                }
                float health = FamilyRules.SpawnHealth(AoJConfig.SettlerBaseHealth.Value, healthTraits,
                    bornAt, WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live);
                humanoid.SetMaxHealth(health);
                humanoid.SetHealth(health);
            }
            MonsterAI ai = child.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.SetPatrolPoint(position);
            }
            LoveEffects.Birth(position + Vector3.up * 0.5f);
            return child.GetComponent<Settler>();
        }
    }
}
