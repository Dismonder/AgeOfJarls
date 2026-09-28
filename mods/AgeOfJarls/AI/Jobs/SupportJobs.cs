using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Settlement;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI.Jobs
{
    /// <summary>
    /// Hauler: gathers loose items in its zone and empties the input chest standing by its totem, so the home routine
    /// carries everything to the sorted chests. Players can dump anything into the input chest and have it sorted.
    /// </summary>
    internal sealed class HaulerJob : JobBase
    {
        private const int SlotsToKeepFree = 2;
        private const float InputRefreshSeconds = 5f;
        private const float AskSeconds = 1f;
        /// <summary>A player keeps the input chest open this long: the hauler does something else meanwhile.</summary>
        private const float AccessGiveUpSeconds = 5f;
        private const float BusyChestPauseSeconds = 15f;

        private Container _input;
        private float _inputTimer;
        private float _askTimer;
        private float _accessTimer;
        private float _pauseUntil;

        internal HaulerJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Hauler;

        protected override bool Work(float dt)
        {
            Inventory bag = Body.GetInventory();
            if (bag.GetEmptySlots() <= SlotsToKeepFree)
            {
                Problem("");
                return false;
            }
            List<Container> chests = SettlementChests();
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius, item => SettlementStorage.HasDestination(item, chests, Position)))
            {
                Produced(Ctx.Loot.TakePicked());
                Problem("");
                return true;
            }
            Produced(Ctx.Loot.TakePicked());

            _inputTimer -= dt;
            if (_inputTimer <= 0f || _input == null)
            {
                _inputTimer = InputRefreshSeconds;
                _input = InputChest();
            }
            Container input = _input;
            if (input == null)
            {
                Problem("$aoj_problem_no_input");
                return false;
            }
            Problem("");
            System.Predicate<ItemDrop.ItemData> sortable = item => SettlementStorage.HasDestination(item, chests, Position);
            if (Time.time < _pauseUntil || !IsReachable(input) || !SettlementStorage.IsUsable(input) ||
                !input.GetInventory().GetAllItems().Exists(sortable))
            {
                _accessTimer = 0f;
                return false;
            }
            Step walk = WalkToObject(dt, input, 2f);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    MarkUnreachable(input);
                }
                return walk == Step.Busy;
            }
            // The chest's owner (another machine) hands it over within a moment; asked once a second, not every frame.
            _askTimer -= dt;
            bool ask = _askTimer <= 0f;
            if (ask)
            {
                _askTimer = AskSeconds;
            }
            if (!ChestAccess.Acquire(input, ask))
            {
                _accessTimer += dt;
                if (_accessTimer > AccessGiveUpSeconds)
                {
                    _accessTimer = 0f;
                    _pauseUntil = Time.time + BusyChestPauseSeconds;
                }
                return true;
            }
            _accessTimer = 0f;
            Produced(SettlementStorage.TakeFrom(input, bag, sortable, int.MaxValue));
            Settler.FlushInventory();
            return true;
        }

        private Container InputChest()
        {
            Container best = null;
            float bestDistance = SettlementStorage.InputChestRange;
            foreach (Piece piece in Nearby(Totem.transform.position, SettlementStorage.InputChestRange))
            {
                Container chest = piece.GetComponent<Container>();
                float distance = chest != null ? Vector3.Distance(chest.transform.position, Totem.transform.position) : float.MaxValue;
                if (chest != null && chest.m_privacy == Container.PrivacySetting.Public && distance <= bestDistance &&
                    !SettlementStorage.HasOwnPurpose(chest))
                {
                    best = chest;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static readonly List<Piece> s_pieces = new List<Piece>();

        internal static List<Piece> Nearby(Vector3 center, float radius)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(center, radius, s_pieces);
            return s_pieces;
        }
    }

    /// <summary>
    /// Builder: repairs damaged buildings in its zone (the vanilla repair, as with a hammer), then puts back what a siege
    /// destroyed (<see cref="Settlement.Breaches"/>) - the lowest first, so floors stand before the walls on them - with
    /// the materials of each piece's recipe from the settlement's chests. Needs a hammer.
    /// </summary>
    internal sealed class BuilderJob : JobBase
    {
        private const float RepairSeconds = 2f;
        private const float Reach = 2.8f;
        // A player's hammer repairs what it points at up to 5 m away: a roof or the top of a wall from the ground.
        private const float RepairReach = 4f;
        private const float ScanSeconds = 4f;
        private const float RebuildRetrySeconds = 60f;

        // Spots a builder on this machine is working on: two never carry materials to the same spot.
        private static readonly HashSet<long> s_claimed = new HashSet<long>();
        private static readonly List<Piece> s_pieces = new List<Piece>();

        private WearNTear _target;
        private float _timer;
        private float _scanTimer;
        private Collider[] _colliders = new Collider[0];
        private Settlement.Breaches.Entry? _rebuild;
        private float _rebuildScanAt;
        private readonly Dictionary<long, float> _unreachableSpots = new Dictionary<long, float>();

        internal BuilderJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Builder;

        internal override string DebugTarget => _rebuild != null ? $"rebuild {_rebuild.Value.Prefab}" : Describe(_target);

        protected override bool Work(float dt)
        {
            if (TakeTool(ToolKind.Hammer) == null)
            {
                return FetchTool(dt, ToolKind.Hammer);
            }
            // Damage first, unless a rebuild is under way; then what a siege destroyed.
            if (_rebuild == null && Repair(dt))
            {
                return true;
            }
            return Rebuild(dt);
        }

        private bool Repair(float dt)
        {
            if (_target == null || _target.GetHealthPercentage() >= 0.999f || !Reservations.IsFree(_target, Uid))
            {
                // Straight on to the next damaged piece; only a search that found none waits before the next.
                _scanTimer -= dt;
                if (_scanTimer > 0f)
                {
                    return false;
                }
                _target = FindDamaged();
                if (_target == null)
                {
                    _scanTimer = ScanSeconds;
                    Problem("");
                    return false;
                }
                Reservations.Take(_target, Uid);
                _colliders = WorkScanner.SolidColliders(_target);
            }
            Problem("");
            // From a side it can get to: the side of a house wall facing it may be its inside, behind a closed door.
            Step walk = WalkToObject(dt, _target, RepairReach);
            if (walk == Step.Failed)
            {
                // Seen from the wrong side, or no way up: the next most damaged piece first, this one again in a minute.
                MarkUnreachable(_target);
                Reservations.Release(_target, Uid);
                _target = null;
                return false;
            }
            if (walk == Step.Busy)
            {
                return true;
            }
            Face(WorkScanner.NearestPoint(_colliders, Position, _target.transform.position));
            _timer -= dt;
            if (_timer <= 0f)
            {
                _timer = RepairSeconds / Mathf.Max(0.1f, Pace);
                if (_target.Repair())
                {
                    Produced(1);
                    if (AiTrace.On)
                    {
                        AiTrace.Write(Ctx.Ai, $"repairs {Describe(_target)}");
                    }
                }
            }
            return true;
        }

        private bool Rebuild(float dt)
        {
            Settlement.JarlTable table = Totem.Settlement;
            if (table == null)
            {
                return false;
            }
            if (_rebuild == null)
            {
                if (Time.time < _rebuildScanAt)
                {
                    return false;
                }
                _rebuildScanAt = Time.time + ScanSeconds;
                foreach (Settlement.Breaches.Entry entry in table.BreachList.OrderBy(e => e.Position.y))
                {
                    long key = SpotKey(entry.Position);
                    if (InZone(entry.Position) && !(_unreachableSpots.TryGetValue(key, out float until) && Time.time < until) && s_claimed.Add(key))
                    {
                        _rebuild = entry;
                        break;
                    }
                }
                if (_rebuild == null)
                {
                    return false;
                }
            }

            Settlement.Breaches.Entry spot = _rebuild.Value;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(spot.Prefab) : null;
            Piece recipe = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (recipe == null)
            {
                table.ReportRebuilt(spot.Position);
                ForgetRebuild();
                return false;
            }
            Inventory bag = Body.GetInventory();
            foreach (Piece.Requirement need in recipe.m_resources)
            {
                if (need.m_resItem == null || need.m_amount <= 0)
                {
                    continue;
                }
                string item = need.m_resItem.gameObject.name;
                int have = bag.GetAllItems().Where(i => PrefabName(i) == item).Sum(i => i.m_stack);
                if (have >= need.m_amount)
                {
                    continue;
                }
                Step fetch = Fetch(dt, i => PrefabName(i) == item, need.m_amount - have);
                if (fetch == Step.Failed)
                {
                    Problem("$aoj_problem_no_materials");
                    ForgetRebuild();
                    _rebuildScanAt = Time.time + RebuildRetrySeconds;
                    return false;
                }
                return true;
            }

            Step walk = WalkTo(dt, spot.Position, Reach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    // No way to this spot: the other breaches meanwhile, not a minute of nothing and then the same one.
                    if (_unreachableSpots.Count > 64)
                    {
                        _unreachableSpots.Clear();
                    }
                    _unreachableSpots[SpotKey(spot.Position)] = Time.time + RebuildRetrySeconds;
                    ForgetRebuild();
                }
                return walk == Step.Busy;
            }
            Face(spot.Position);
            if (!AlreadyStands(spot))
            {
                GameObject built = Object.Instantiate(prefab, spot.Position, spot.Rotation);
                long jarl = table.Data?.Jarl?.PlayerId ?? 0L;
                if (jarl != 0L)
                {
                    // Like the players' own buildings: the Jarl can take it down again. The platform id only names the
                    // creator from the world's player history, which a settler is not in.
                    built.GetComponent<Piece>()?.SetCreator(jarl, default);
                }
                recipe.m_placeEffect.Create(spot.Position, spot.Rotation, built.transform);
                if (AiTrace.On)
                {
                    AiTrace.Write(Ctx.Ai, $"rebuilds {spot.Prefab}");
                }
                foreach (Piece.Requirement need in recipe.m_resources)
                {
                    if (need.m_resItem != null && need.m_amount > 0)
                    {
                        bag.RemoveItem(need.m_resItem.m_itemData.m_shared.m_name, need.m_amount);
                    }
                }
                Settler.FlushInventory();
                Produced(1);
            }
            table.ReportRebuilt(spot.Position);
            ForgetRebuild();
            Problem("");
            return true;
        }

        // A player may have put it back already.
        private static bool AlreadyStands(Settlement.Breaches.Entry spot)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(spot.Position, Settlement.Breaches.SameSpot, s_pieces);
            bool found = s_pieces.Exists(p => p != null && Utils.GetPrefabName(p.gameObject) == spot.Prefab);
            s_pieces.Clear();
            return found;
        }

        private static long SpotKey(Vector3 position) =>
            ((long)Mathf.Round(position.x * 4f) * 73856093L) ^ ((long)Mathf.Round(position.y * 4f) * 19349663L) ^ ((long)Mathf.Round(position.z * 4f) * 83492791L);

        private void ForgetRebuild()
        {
            if (_rebuild != null)
            {
                s_claimed.Remove(SpotKey(_rebuild.Value.Position));
            }
            _rebuild = null;
        }

        internal override void Stop()
        {
            if (_target != null)
            {
                Reservations.Release(_target, Uid);
            }
            _target = null;
            ForgetRebuild();
            base.Stop();
        }

        private WearNTear FindDamaged()
        {
            WearNTear best = null;
            float worst = 0.999f;
            foreach (Piece piece in HaulerJob.Nearby(Totem.transform.position, Totem.Radius))
            {
                WearNTear wear = piece.GetComponent<WearNTear>();
                if (wear == null || !Reservations.IsFree(wear, Uid) || !IsReachable(wear))
                {
                    continue;
                }
                float health = wear.GetHealthPercentage();
                if (health < worst)
                {
                    best = wear;
                    worst = health;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Farmer: harvests ripe crops in its zone and plants the same crop again on the cultivated spot, with seeds from
    /// its bag or the settlement's chests. The field itself is prepared by a player with the cultivator.
    /// </summary>
    internal sealed class FarmerJob : JobBase
    {
        private const float Reach = 2f;
        private const float ScanSeconds = 4f;
        private const float SpotClearance = 0.4f;
        private const float ReplantDelay = 2f;
        private const float ReplantPatience = 10f;

        private static Dictionary<string, (GameObject sapling, string seed)> s_crops;
        private static readonly Collider[] s_spotHits = new Collider[16];

        private readonly List<(Vector3 position, GameObject sapling, string seed, float readyAt)> _toPlant = new List<(Vector3, GameObject, string, float)>();
        private Pickable _target;
        private float _scanTimer;

        internal FarmerJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Farmer;

        internal override string DebugTarget => Describe(_target);

        // Only what the replanting under way needs: a carrot is also the seed of a seed carrot, so keeping every crop's
        // seed kept the whole harvest in the bag instead of the chests.
        internal override bool Keeps(ItemDrop.ItemData item)
        {
            string name = PrefabName(item);
            return _toPlant.Exists(spot => spot.seed == name);
        }

        protected override bool Work(float dt)
        {
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius, item => true))
            {
                Produced(Ctx.Loot.TakePicked());
                return true;
            }
            Produced(Ctx.Loot.TakePicked());
            Problem("");

            if (_toPlant.Count > 0 && Plant(dt))
            {
                return true;
            }
            return Harvest(dt);
        }

        private bool Harvest(float dt)
        {
            if (_target == null || !_target.CanBePicked() || !Reservations.IsFree(_target, Uid))
            {
                // Straight on to the next ripe crop; only a search that found none waits before the next.
                _scanTimer -= dt;
                if (_scanTimer > 0f)
                {
                    return false;
                }
                _target = FindRipe();
                if (_target == null)
                {
                    _scanTimer = ScanSeconds;
                    return false;
                }
                Reservations.Take(_target, Uid);
            }
            Step walk = WalkTo(dt, _target.transform.position, Reach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    // Fenced off or across water: the other crops first, this one again in a minute.
                    MarkUnreachable(_target);
                    Reservations.Release(_target, Uid);
                    _target = null;
                }
                return walk == Step.Busy;
            }
            Face(_target.transform.position);
            if (Crops().TryGetValue(Utils.GetPrefabName(_target.gameObject), out (GameObject sapling, string seed) crop))
            {
                _toPlant.Add((_target.transform.position, crop.sapling, crop.seed, Time.time + ReplantDelay));
            }
            _target.Interact(Body, false, false);
            BonusCrop(_target);
            Reservations.Release(_target, Uid);
            _target = null;
            return true;
        }

        // Green-thumbed farmers now and then bring in one more of the crop (it drops with the rest and is gathered).
        private void BonusCrop(Pickable crop)
        {
            float chance = Settler.TraitSum(Core.Defs.TraitStat.CropYield);
            ItemDrop drop = crop.m_itemPrefab != null ? crop.m_itemPrefab.GetComponent<ItemDrop>() : null;
            if (chance <= 0f || drop == null || Random.value >= chance)
            {
                return;
            }
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = crop.m_itemPrefab;
            item.m_stack = 1;
            ItemDrop.DropItem(item, 1, crop.transform.position + Vector3.up * 0.5f, Quaternion.identity);
        }

        private bool Plant(float dt)
        {
            (Vector3 position, GameObject sapling, string seed, float readyAt) spot = _toPlant[0];
            // The picked crop takes a moment to disappear; its spot is free a little later.
            if (Time.time < spot.readyAt)
            {
                return false;
            }
            if (!IsFreeCultivatedSpot(spot.position))
            {
                if (Time.time > spot.readyAt + ReplantPatience)
                {
                    _toPlant.RemoveAt(0);
                }
                return false;
            }
            Inventory bag = Body.GetInventory();
            if (!bag.GetAllItems().Exists(i => PrefabName(i) == spot.seed))
            {
                Step fetch = Fetch(dt, i => PrefabName(i) == spot.seed, 10);
                if (fetch == Step.Failed)
                {
                    // No seeds anywhere: the spot stays empty until the player brings some.
                    _toPlant.RemoveAt(0);
                    Problem("$aoj_problem_no_seeds");
                }
                return true;
            }
            Step walk = WalkTo(dt, spot.position, Reach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    _toPlant.RemoveAt(0);
                }
                return true;
            }
            ItemDrop.ItemData seed = bag.GetAllItems().Find(i => PrefabName(i) == spot.seed);
            if (seed != null)
            {
                Object.Instantiate(spot.sapling, spot.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                bag.RemoveItem(seed, 1);
                Settler.FlushInventory();
                Produced(1);
                if (AiTrace.On)
                {
                    AiTrace.Write(Ctx.Ai, $"plants {spot.sapling.name}");
                }
            }
            _toPlant.RemoveAt(0);
            return true;
        }

        private Pickable FindRipe()
        {
            Pickable best = null;
            float bestSqr = float.MaxValue;
            Dictionary<string, (GameObject, string)> crops = Crops();
            foreach (Component component in WorkScanner.Find<Pickable>(Totem))
            {
                var pickable = (Pickable)component;
                if (pickable == null || !crops.ContainsKey(Utils.GetPrefabName(pickable.gameObject)) || !pickable.CanBePicked() ||
                    !Reservations.IsFree(pickable, Uid) || !IsReachable(pickable))
                {
                    continue;
                }
                float sqr = (pickable.transform.position - Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = pickable;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        private static bool IsFreeCultivatedSpot(Vector3 position)
        {
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            if (heightmap == null || !heightmap.IsCultivated(position))
            {
                return false;
            }
            int count = Physics.OverlapSphereNonAlloc(position, SpotClearance, s_spotHits);
            for (int i = 0; i < count; i++)
            {
                Collider collider = s_spotHits[i];
                if (collider.GetComponentInParent<Plant>() != null || collider.GetComponentInParent<Pickable>() != null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Grown crop prefab name -> (the sapling that grows into it, the seed item it is planted from); also for the catch-up.</summary>
        internal static Dictionary<string, (GameObject sapling, string seed)> Crops()
        {
            if (s_crops != null || ZNetScene.instance == null)
            {
                return s_crops ?? new Dictionary<string, (GameObject, string)>();
            }
            s_crops = new Dictionary<string, (GameObject, string)>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                // Plants other mods build in code may lack the lists.
                if (plant == null || plant.m_grownPrefabs == null || piece == null || piece.m_resources == null ||
                    piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null)
                {
                    continue;
                }
                string seed = piece.m_resources[0].m_resItem.gameObject.name;
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown != null && grown.GetComponent<Pickable>() != null && !s_crops.ContainsKey(grown.name))
                    {
                        s_crops[grown.name] = (prefab, seed);
                    }
                }
            }
            return s_crops;
        }
    }

    /// <summary>
    /// Smelter hand: keeps the furnaces, kilns, blast furnaces, windmills, spinning wheels and refineries in its zone
    /// fed with ore and fuel from the settlement's chests. What they produce drops out and is gathered like loot.
    /// </summary>
    internal sealed class SmelterJob : JobBase
    {
        private const float Reach = 2.5f;
        private const float FeedSeconds = 0.6f;
        private const int FetchBatch = 20;

        private Smelter _station;
        private float _timer;

        internal SmelterJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Smelter;

        private readonly HashSet<string> _supplies = new HashSet<string>();

        internal override bool Keeps(ItemDrop.ItemData item) => _supplies.Contains(item.m_shared.m_name);

        protected override bool Work(float dt)
        {
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius, item => true))
            {
                Produced(Ctx.Loot.TakePicked());
                return true;
            }
            Produced(Ctx.Loot.TakePicked());

            List<Smelter> stations = Stations<Smelter>(dt);
            _supplies.Clear();
            foreach (Smelter s in stations)
            {
                s.m_conversion.ForEach(c => { if (c.m_from != null) { _supplies.Add(c.m_from.m_itemData.m_shared.m_name); } });
                if (s.m_fuelItem != null)
                {
                    _supplies.Add(s.m_fuelItem.m_itemData.m_shared.m_name);
                }
            }
            if (stations.Count == 0)
            {
                Problem("$aoj_problem_no_stations");
                return false;
            }
            Problem("");

            Inventory bag = Body.GetInventory();
            _station = stations.FirstOrDefault(s => IsReachable(s) && NeedsOre(s) && bag.GetAllItems().Exists(i => Accepts(s, i))) ??
                       stations.FirstOrDefault(s => IsReachable(s) && NeedsFuel(s) && bag.GetAllItems().Exists(i => IsFuel(s, i)));
            if (_station == null)
            {
                // Nothing in the bag fits a hungry station: bring ore or fuel from the chests.
                bool hungry = stations.Any(s => IsReachable(s) && (NeedsOre(s) || NeedsFuel(s)));
                if (!hungry)
                {
                    return false;
                }
                Step fetch = Fetch(dt, item => stations.Any(s => IsReachable(s) && ((NeedsOre(s) && Accepts(s, item)) || (NeedsFuel(s) && IsFuel(s, item)))), FetchBatch);
                return fetch == Step.Busy || fetch == Step.Done;
            }

            Step walk = WalkToObject(dt, _station, Reach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    MarkUnreachable(_station);
                }
                return walk == Step.Busy;
            }
            Face(_station.transform.position);
            _timer -= dt;
            if (_timer > 0f)
            {
                return true;
            }
            _timer = FeedSeconds / Mathf.Max(0.1f, Pace);
            ZNetView view = _station.GetComponent<ZNetView>();
            ItemDrop.ItemData ore = NeedsOre(_station) ? bag.GetAllItems().Find(i => Accepts(_station, i)) : null;
            if (ore != null)
            {
                view.InvokeRPC("RPC_AddOre", PrefabName(ore), false);
                bag.RemoveItem(ore, 1);
                Produced(1);
                return true;
            }
            ItemDrop.ItemData fuel = NeedsFuel(_station) ? bag.GetAllItems().Find(i => IsFuel(_station, i)) : null;
            if (fuel != null)
            {
                view.InvokeRPC("RPC_AddFuel");
                bag.RemoveItem(fuel, 1);
            }
            return true;
        }

        private static bool NeedsOre(Smelter station) =>
            station.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_queued) < station.m_maxOre;

        private static bool NeedsFuel(Smelter station) =>
            station.m_fuelItem != null && station.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel) < station.m_maxFuel - 1;

        private static bool Accepts(Smelter station, ItemDrop.ItemData item) =>
            station.m_conversion.Exists(c => c.m_from != null && c.m_from.m_itemData.m_shared.m_name == item.m_shared.m_name);

        private static bool IsFuel(Smelter station, ItemDrop.ItemData item) =>
            station.m_fuelItem != null && station.m_fuelItem.m_itemData.m_shared.m_name == item.m_shared.m_name;
    }

    /// <summary>
    /// Cook: cooks raw food from the settlement's chests on the cooking stations in its zone and takes it off before
    /// it burns; cooked food goes to the Settlement Cauldron with the rest of the storing (food goes there first).
    /// </summary>
    internal sealed class CookJob : JobBase
    {
        private const float Reach = 2.2f;
        private const float ActionSeconds = 0.8f;
        private const int FetchBatch = 10;

        private float _timer;

        internal CookJob(JobContext context) : base(context)
        {
        }

        internal override JobType Job => JobType.Cook;

        private readonly HashSet<string> _supplies = new HashSet<string>();

        internal override bool Keeps(ItemDrop.ItemData item) => _supplies.Contains(item.m_shared.m_name);

        protected override bool Work(float dt)
        {
            if (Ctx.Loot.CollectInZone(dt, Totem.transform.position, Totem.Radius, item => true))
            {
                Produced(Ctx.Loot.TakePicked());
                return true;
            }
            Produced(Ctx.Loot.TakePicked());

            List<CookingStation> stations = Stations<CookingStation>(dt);
            _supplies.Clear();
            foreach (CookingStation s in stations)
            {
                s.m_conversion.ForEach(c => { if (c.m_from != null) { _supplies.Add(c.m_from.m_itemData.m_shared.m_name); } });
            }
            if (stations.Count == 0)
            {
                Problem("$aoj_problem_no_stations");
                return false;
            }
            Problem("");

            // Done food first, before it burns.
            CookingStation ready = stations.FirstOrDefault(s => IsReachable(s) && HasDoneItem(s));
            if (ready != null)
            {
                return Act(dt, ready, view => view.InvokeRPC("RPC_RemoveDoneItem", Position + Vector3.up, 1));
            }

            Inventory bag = Body.GetInventory();
            CookingStation free = stations.FirstOrDefault(s => IsReachable(s) && FreeSlots(s) > 0 && bag.GetAllItems().Exists(i => Accepts(s, i)));
            if (free != null)
            {
                return Act(dt, free, view =>
                {
                    ItemDrop.ItemData raw = bag.GetAllItems().Find(i => Accepts(free, i));
                    if (raw != null)
                    {
                        view.InvokeRPC("RPC_AddItem", PrefabName(raw), false);
                        bag.RemoveItem(raw, 1);
                        Produced(1);
                    }
                });
            }

            if (!stations.Any(s => FreeSlots(s) > 0))
            {
                return false;
            }
            Step fetch = Fetch(dt, item => stations.Any(s => Accepts(s, item)), FetchBatch);
            return fetch == Step.Busy || fetch == Step.Done;
        }

        private bool Act(float dt, CookingStation station, System.Action<ZNetView> action)
        {
            Step walk = WalkToObject(dt, station, Reach);
            if (walk != Step.Done)
            {
                if (walk == Step.Failed)
                {
                    MarkUnreachable(station);
                }
                return walk == Step.Busy;
            }
            Face(station.transform.position);
            _timer -= dt;
            if (_timer <= 0f)
            {
                _timer = ActionSeconds / Mathf.Max(0.1f, Pace);
                action(station.GetComponent<ZNetView>());
            }
            return true;
        }

        private static bool Accepts(CookingStation station, ItemDrop.ItemData item) =>
            station.m_conversion.Exists(c => c.m_from != null && c.m_from.m_itemData.m_shared.m_name == item.m_shared.m_name);

        private static int FreeSlots(CookingStation station)
        {
            ZDO zdo = station.GetComponent<ZNetView>().GetZDO();
            int free = 0;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                if (zdo.GetString("slot" + i) == "")
                {
                    free++;
                }
            }
            return free;
        }

        // A slot holding the product of a conversion (or coal from burnt food) is done.
        private static bool HasDoneItem(CookingStation station)
        {
            ZDO zdo = station.GetComponent<ZNetView>().GetZDO();
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                string item = zdo.GetString("slot" + i);
                if (item.Length > 0 && (station.m_conversion.Exists(c => c.m_to != null && c.m_to.gameObject.name == item) ||
                                        (station.m_overCookedItem != null && station.m_overCookedItem.gameObject.name == item)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
