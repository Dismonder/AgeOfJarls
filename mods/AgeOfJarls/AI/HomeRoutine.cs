using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.AI.Jobs;
using AgeOfJarls.Army;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// A settler's own life at home, when nobody gives it orders. Priorities, highest first:
    /// carrying what it holds to the sorted chests (while working, only a full load), taking shelter or manning its post
    /// during an alarm, eating from the Settlement Cauldron when hungry, working at its Work Totem in working hours,
    /// sleeping in its bed at night, standing guard (soldiers without a job), clearing loot by day, and otherwise
    /// strolling around its home spot (<see cref="Settler.HomeAnchor"/>), walking back on its own path from afar.
    /// Home is the settlement and, for a worker whose totem stands outside it, the land out to that totem's zone.
    /// Runs on the ZDO owner inside <see cref="SettlerAI.UpdateAI"/> while the settler is calm and at home.
    /// </summary>
    internal sealed class HomeRoutine
    {
        private const string Module = "AI";
        private const float PlanSeconds = 3f;
        private const float ChestStopDistance = 1.2f;
        /// <summary>Chests are used from this far: the path ends at the chest's edge, sometimes behind a thin wall.</summary>
        private const float ChestReach = 2.5f;
        private const float TripSeconds = 45f;
        private const float UnreachableSeconds = 120f;
        private const float FullChestSeconds = 30f;
        private const float AskSeconds = 1f;
        private const float AccessSeconds = 5f;
        private const int MaxAvoided = 64;
        private const float BedStopDistance = 0.8f;
        private const float BedReach = 2f;
        /// <summary>A bed behind a door it cannot open is reached anyway after trying this long, if it is this close.</summary>
        private const float BedTripSeconds = 20f;
        private const float BedShortcutRange = 10f;
        /// <summary>Beyond the settlement radius plus this, the settler first walks home.</summary>
        private const float HomeMargin = 5f;
        /// <summary>Beyond twice the wander range plus this from its home spot, the settler walks back on its own path.</summary>
        private const float ReturnMargin = 2f;
        private const float ReturnStopDistance = 3f;
        /// <summary>The home spot is the table or a bed, both solid: the path ends at their edge.</summary>
        private const float ReturnReach = 5f;
        private const float ReturnRetrySeconds = 30f;
        private const float MealRetrySeconds = 20f;
        private const float ShelterReach = 3f;
        private const int FreeSlotsBeforeStoring = 2;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly SettlerCharacter _character;
        private readonly PathMover _mover;
        private readonly LootCollector _loot;
        private readonly SoldierDuty _duty;
        private readonly System.Func<JobType, JobBase> _jobFactory;
        private readonly Dictionary<JobType, JobBase> _jobs = new Dictionary<JobType, JobBase>();
        private readonly List<Container> _chests = new List<Container>();
        private readonly Dictionary<ZDOID, float> _avoidUntil = new Dictionary<ZDOID, float>();
        private readonly System.Predicate<ItemDrop.ItemData> _hasChest;
        private readonly System.Predicate<Container> _avoided;
        private readonly System.Predicate<ItemDrop.ItemData> _keep;

        private JarlTable _table;
        /// <summary>Its settlement's table is loaded here (at the last plan); <see cref="_table"/> is also null far from home.</summary>
        private bool _homeLoaded;
        private float _radius;
        private Vector3 _anchor;
        private float _returnRetryAt;
        private Bed _bed;
        private Container _chest;
        private bool _carrying;
        private float _planTimer;
        private float _tripTimer;
        private float _accessTimer;
        private float _askTimer;
        private float _bedTimer;

        private JobBase _activeJob;
        private bool _jobIdle;

        private SettlementCauldron _cauldron;
        private float _mealRetryAt;
        private float _mealTimer;

        internal HomeRoutine(SettlerAI ai, Settler settler, SettlerCharacter character, PathMover mover, LootCollector loot,
            SoldierDuty duty, System.Func<JobType, JobBase> jobFactory)
        {
            _ai = ai;
            _settler = settler;
            _character = character;
            _mover = mover;
            _loot = loot;
            _duty = duty;
            _jobFactory = jobFactory;
            // Settlers loaded together (world load, a player arriving) would otherwise all plan in the same frame.
            _planTimer = UnityEngine.Random.Range(0f, PlanSeconds);
            _hasChest = item => SettlementStorage.HasDestination(item, _chests, _ai.transform.position);
            _avoided = chest => _avoidUntil.TryGetValue(chest.m_nview.GetZDO().m_uid, out float until) && Time.time < until;
            _keep = item => (AssignedJob()?.Keeps(item) ?? false) || (_helpJob?.Keeps(item) ?? false) || _duty.Keeps(item);
            _courtesy = new Courtesy(ai);
        }

        private readonly Courtesy _courtesy;

        internal void Update(float dt)
        {
            _sitRequested = false;
            _idleThisFrame = false;
            UpdateInner(dt);
            if (!_sitRequested)
            {
                StopSitting();
            }
            if (!_idleThisFrame)
            {
                _idleTime = 0f;
            }
        }

        private void UpdateInner(float dt)
        {
            _planTimer -= dt;
            if (_planTimer <= 0f)
            {
                _planTimer = PlanSeconds;
                long planning = Perf.Start();
                Plan();
                Perf.Stop(Perf.Section.Planning, planning);
            }
            if (_table == null)
            {
                // Its settlement is not loaded here (a player is out at a far totem with it): the work there goes on
                // while there is some. Otherwise, or far from home, vanilla idle movement walks it back to its home spot.
                if (!_homeLoaded && WorkAway(dt))
                {
                    return;
                }
                Stop(FarFromHomeSpot() ? SettlerActivity.Returning : SettlerActivity.Idle);
                return;
            }

            // A player walking into it gets the way for a moment, whatever it was about to do - unless it lies in bed.
            if (!_character.IsLyingDown && _courtesy.Update(dt))
            {
                return;
            }
            if (_chest != null)
            {
                CarryToChest(dt);
                return;
            }
            bool alarm = _table.AlarmOn;
            if (alarm && TakeCover(dt))
            {
                return;
            }
            if (Recover(dt))
            {
                return;
            }
            ZDO zdo = _settler.Zdo;
            bool hungry = zdo != null && Needs.IsHungry(zdo);
            // Not hungry yet but close to it, and between two tasks: a bite now, from the bag or the cauldron, rather
            // than a trip from the far end of the zone in the middle of the next job.
            bool peckish = !hungry && zdo != null && !_carrying && (_activeJob == null || (_jobIdle && !_helpBusy)) && Needs.IsPeckish(zdo);
            if ((hungry || peckish) && Eat(dt, lightMeal: peckish))
            {
                return;
            }
            bool night = EnvMan.IsNight();
            long perf = Perf.Start();
            bool working = Work(dt);
            Perf.Stop(Perf.Section.Jobs, perf);
            if (working)
            {
                return;
            }
            if (night && _bed != null)
            {
                GoToBed(dt);
                return;
            }
            if (!night && _duty.Update(dt, _table, alarm: false))
            {
                return;
            }

            _character.GetUp();
            _bedTimer = 0f;
            if (!night && !_character.IsLyingDown && _loot.CollectAtHome(dt, _table.transform.position, _radius, _hasChest))
            {
                _settler.SetActivity(SettlerActivity.Collecting);
                _collecting = true;
                return;
            }
            if (_collecting && _loot.BusyWithin(GatherPauseSeconds))
            {
                // Between two drops (the next one is being looked for): stay put rather than set off home for a step.
                _ai.StopMoving();
                return;
            }
            if (_collecting)
            {
                // The last thing lying around is picked up: the trip to the chests is planned right after the pause,
                // instead of sitting down first and getting up again.
                _collecting = false;
                _planTimer = Mathf.Min(_planTimer, 0.1f);
            }
            if (ReturnHome(dt))
            {
                _settler.SetActivity(SettlerActivity.Returning);
                return;
            }
            // Nothing to do: vanilla idle movement strolls around the home spot, and after a while it sits down.
            _idleThisFrame = true;
            _idleTime += dt;
            IdleSit(dt);
            bool noFood = _starving && hungry;
            _settler.SetActivity(_carrying ? SettlerActivity.NoChest : noFood ? SettlerActivity.NoFood : SettlerActivity.Idle);
        }

        /// <summary>Hungry and no cauldron with food at the last look.</summary>
        private bool _starving;

        /// <summary>For aoj_debug: home, trip, job and its target.</summary>
        internal string DebugState()
        {
            if (_table == null)
            {
                return _activeJob != null ? $"working away: {_activeJob.Job}{(_jobIdle ? " (idle)" : "")}" : _homeLoaded ? "far from home" : "home not loaded";
            }
            var parts = new List<string>();
            if (_chest != null)
            {
                parts.Add($"to chest {Vector3.Distance(_ai.transform.position, _chest.transform.position):0} m");
            }
            if (_carrying)
            {
                parts.Add(_unplaced.Length > 0 ? $"carrying ({_unplaced}: no chest)" : "carrying");
            }
            if (_activeJob != null)
            {
                string target = _activeJob.DebugTarget;
                parts.Add($"{_activeJob.Job}{(_jobIdle ? " (idle)" : "")}{(target.Length > 0 ? " -> " + target : "")}");
            }
            if (_helpJob != null)
            {
                string target = _helpJob.DebugTarget;
                parts.Add($"helping: {_helpJob.Job}{(_helpBusy ? "" : " (idle)")}{(target.Length > 0 ? " -> " + target : "")}");
            }
            if (_cauldron != null)
            {
                parts.Add("to cauldron");
            }
            if (_bed != null)
            {
                parts.Add(_character.IsLyingDown ? "in bed" : "has bed");
            }
            return parts.Count == 0 ? "home" : string.Join(", ", parts);
        }

        /// <summary>Orders, fights and journeys come first: out of bed and no half-finished trip or job step.</summary>
        internal void Stop() => Stop(SettlerActivity.Idle);

        private void Stop(SettlerActivity activity)
        {
            _chest = null;
            StopSitting();
            _cauldron = null;
            _bedTimer = 0f;
            StopJob();
            _duty.Stop();
            _character.GetUp();
            _settler.SetActivity(activity);
        }

        /// <summary>Beyond a stroll from where vanilla idle movement takes it (the home spot, or home from afar).</summary>
        private bool FarFromHomeSpot() =>
            _ai.GetPatrolPoint(out Vector3 spot) && Utils.DistanceXZ(spot, _ai.transform.position) > ReturnDistance();

        // ---------------------------------------------------------------- planning

        private void Plan()
        {
            _table = _settler.HomeTable;
            SettlementData data = _table != null ? _table.Data : null;
            _homeLoaded = data != null;
            if (data == null)
            {
                _table = null;
                return;
            }
            _radius = JarlTable.RadiusOf(data);
            if (!IsHome(_ai.transform.position))
            {
                _table = null;
                return;
            }

            _bed = _settler.TryGetBed(_table, out Vector3 bedPosition) ? JarlTable.FindBedAt(bedPosition) : null;
            _anchor = _settler.HomeAnchor(_table);
            AutoWork(data);

            SettlementStorage.CollectChests(_table, _chests);
            _chests.RemoveAll(_avoided);
            if (_chest == null && ShouldStore())
            {
                _chest = SettlementStorage.NextDestination(_character.GetInventory(), _chests, _ai.transform.position, out _carrying, _keep);
                if (_chest != null)
                {
                    _tripTimer = 0f;
                    _accessTimer = 0f;
                    _askTimer = 0f;
                    _mover.Reset();
                    if (AiTrace.On)
                    {
                        AiTrace.Write(_ai, $"to chest {Vector3.Distance(_ai.transform.position, _chest.transform.position):0} m carrying {CarriedCount()} item(s)");
                    }
                }
            }
            else if (_chest == null)
            {
                // Nothing to put away now (still gathering, at work, only its food ration): nor anything waiting for a chest.
                _carrying = false;
            }
            // Carrying with no chest to go to means no chest takes any of it: the first such thing is named (its voice
            // tells the player what chest is missing).
            string unplaced = _chest == null && _carrying ? FirstStorableName() : "";
            if (unplaced != _unplaced)
            {
                _unplaced = unplaced;
                _settler.SetNoChestItem(unplaced);
                if (AiTrace.On && unplaced.Length > 0)
                {
                    AiTrace.Write(_ai, $"no chest takes {unplaced}");
                }
            }
            if (_avoidUntil.Count > MaxAvoided)
            {
                _avoidUntil.Clear();
            }
        }

        // Within the settlement and a margin - or, for a worker whose totem stands outside it, as far out as that totem's
        // zone reaches: the way there and back is home life too, not a journey for vanilla idle movement to cut short.
        private bool IsHome(Vector3 position)
        {
            float work = Mathf.Max(WorkReach(_settler.JobTotem), WorkReach(_helpTotem));
            return Utils.DistanceXZ(position, _table.transform.position) <= HomeReach(_radius, work);
        }

        /// <summary>The far edge of a totem's zone, from the table; 0 for none or another settlement's.</summary>
        private float WorkReach(WorkTotem totem) =>
            totem != null && totem.Settlement == _table
                ? Utils.DistanceXZ(totem.transform.position, _table.transform.position) + totem.Radius + WorkTotem.Overreach
                : 0f;

        /// <summary>
        /// How far from its table a settler still counts as at home: its settlement or its work, whichever reaches
        /// farther (<paramref name="workReach"/>: the far edge of its totem's zone, from the table), and a margin.
        /// </summary>
        internal static float HomeReach(float settlementRadius, float workReach) => Mathf.Max(settlementRadius, workReach) + HomeMargin;

        // Out at its totem while its settlement is not loaded here (a player went there with it): it works on as long
        // as there is work and room in its bag - storing, meals and bed wait until it is home, where it then heads.
        private bool WorkAway(float dt)
        {
            WorkTotem totem = _settler.JobTotem;
            // A totem serving a settlement that is loaded here is another settlement's.
            if (totem == null || totem.Settlement != null ||
                Utils.DistanceXZ(_ai.transform.position, totem.transform.position) > totem.Radius + WorkTotem.Overreach + HomeMargin)
            {
                return false;
            }
            long perf = Perf.Start();
            bool working = Work(dt);
            Perf.Stop(Perf.Section.Jobs, perf);
            return working;
        }

        private const float AutoWorkSeconds = 10f;
        private float _autoWorkTimer;

        // A free civilian at home takes a free place at one of its settlement's totems, the highest priority first,
        // then the emptiest, then the nearest - unless the settlement assigns work by hand. A totem with more workers
        // than places (a member left, a tier setting changed) lets its newest ones go: every machine orders the
        // workers by id alike, so exactly the extra ones leave.
        private void AutoWork(SettlementData data)
        {
            _autoWorkTimer -= PlanSeconds;
            if (_autoWorkTimer > 0f)
            {
                return;
            }
            _autoWorkTimer = AutoWorkSeconds;
            WorkTotem current = _settler.JobTotem;
            if (current != null)
            {
                List<Settler> workers = current.Workers();
                int capacity = current.Capacity;
                if (workers.Count > capacity && workers.OrderBy(w => w.Uid).Skip(capacity).Contains(_settler))
                {
                    _settler.TakeJob(0L);
                }
                return;
            }
            if (_settler.JobId != 0L || _settler.Role != CombatRole.None || _table.ManualWork)
            {
                return;
            }
            WorkTotem best = WorkTotem.Loaded
                .Where(t => t != null && t.Id != 0L && t.Settlement == _table && JobInfo.IsUnlocked(t.Job, data.Tier) &&
                            t.WorkerCount() < t.Capacity)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.WorkerCount() / (float)Mathf.Max(1, t.Capacity))
                .ThenBy(t => Vector3.Distance(t.transform.position, _ai.transform.position))
                .FirstOrDefault();
            if (best != null)
            {
                _settler.TakeJob(best.Id);
            }
        }

        /// <summary>Storable items in the bag, the job's own tools and seeds left out.</summary>
        private int CarriedCount() =>
            _character.GetInventory().GetAllItems().Where(i => SettlementStorage.IsStorable(i) && !_keep(i)).Sum(i => i.m_stack);

        /// <summary>A settler doing chores carries its gathering to the chests once nothing new was picked up for this long.</summary>
        private const float GatherPauseSeconds = 1.5f;
        private bool _collecting;

        // Nobody walks to the chests with a single item. A worker carries its whole take - or up to Work/CarryLimit, more
        // for a Strong one - and goes when its bag is nearly full or its work is done (the evening, nothing left to do);
        // a settler doing chores first picks up everything lying around, then makes one trip.
        private bool ShouldStore()
        {
            Inventory bag = _character.GetInventory();
            if (bag.GetEmptySlots() <= FreeSlotsBeforeStoring)
            {
                return true;
            }
            if (_deliverFood)
            {
                if (bag.GetAllItems().Exists(Needs.IsFood))
                {
                    return true;
                }
                _deliverFood = false;
            }
            if ((_activeJob != null && !_jobIdle) || _helpBusy)
            {
                int limit = AoJConfig.CarryLimit.Value;
                return limit > 0 && CarriedCount() >= limit * Mathf.Max(0.25f, 1f + _settler.TraitSum(TraitStat.CarryWeight));
            }
            // Work just ran out with a part load: more often comes within moments (a drop, the input chest filled, a
            // tree another worker felled), so the trip to the chests waits a little for it - a fuller load, fewer trips.
            if (_activeJob != null && _jobIdle && Time.time - _jobIdleSince < IdleStoreDelaySeconds &&
                bag.GetEmptySlots() > FreeSlotsBeforeStoring + 2)
            {
                return false;
            }
            return !_loot.BusyWithin(GatherPauseSeconds) && HasLoad(bag);
        }

        private const float IdleStoreDelaySeconds = 12f;
        private float _jobIdleSince;

        /// <summary>A few servings of food a settler keeps on it and eats when hungry: no reason for a trip.</summary>
        private const int RationServings = 3;
        private string _unplaced = "";

        private string FirstStorableName()
        {
            foreach (ItemDrop.ItemData item in _character.GetInventory().GetAllItems())
            {
                if (SettlementStorage.IsStorable(item) && !_keep(item))
                {
                    return item.m_shared.m_name;
                }
            }
            return "";
        }

        // Something worth a trip to the chests: anything storable but its own food ration (a berry left over from a meal
        // would otherwise be carried back to the chest it came from). A cook's food is its work, for everyone: no ration.
        private bool HasLoad(Inventory bag)
        {
            int ration = AssignedJob() is CookJob ? 0 : RationServings;
            int food = 0;
            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (!SettlementStorage.IsStorable(item) || _keep(item))
                {
                    continue;
                }
                if (!Needs.IsFood(item))
                {
                    return true;
                }
                food += item.m_stack;
            }
            return food > ration;
        }

        // ---------------------------------------------------------------- storing

        private void CarryToChest(float dt)
        {
            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Storing);
            if (!SettlementStorage.IsUsable(_chest))
            {
                // Destroyed, or a player has it open: plan again.
                _chest = null;
                _planTimer = 0f;
                return;
            }

            Vector3 target = _chest.transform.position;
            _tripTimer += dt;
            MoveResult move = _mover.MoveTo(dt, target, ChestStopDistance, ChestReach, run: false);
            if (move == MoveResult.Moving && _tripTimer < TripSeconds)
            {
                return;
            }

            ZDOID chestId = _chest.m_nview.GetZDO().m_uid;
            if (move != MoveResult.Arrived || Vector3.Distance(_ai.transform.position, target) > ChestReach)
            {
                // No way there (a door it cannot open, a wall) or stuck: the other chests first.
                _avoidUntil[chestId] = Time.time + UnreachableSeconds;
                Log.Debug(Module, $"{_settler.DisplayName} cannot reach the chest at {target:F0}");
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"chest unreachable ({move}, {_tripTimer:0} s)");
                }
            }
            else if (!ChestAccess.Acquire(_chest, ask: TimeToAsk(dt)))
            {
                _accessTimer += dt;
                if (_accessTimer < AccessSeconds)
                {
                    // The chest's owner (the other player's machine) hands it over within a moment.
                    return;
                }
                _avoidUntil[chestId] = Time.time + FullChestSeconds;
            }
            else if (SettlementStorage.StoreInto(_chest, _character.GetInventory(), _chests, _ai.transform.position, _settler.DisplayName, _keep) > 0)
            {
                _settler.FlushInventory();
            }
            else
            {
                // Filled up meanwhile (by the other player?): do not walk back to it at once.
                _avoidUntil[chestId] = Time.time + FullChestSeconds;
            }
            _chest = null;
            _planTimer = 0f;
        }

        private bool TimeToAsk(float dt)
        {
            _askTimer -= dt;
            if (_askTimer > 0f)
            {
                return false;
            }
            _askTimer = AskSeconds;
            return true;
        }

        // ---------------------------------------------------------------- alarm

        // Soldiers man their posts; civilians hide at a Shelter banner, else in bed, else by the table.
        private bool TakeCover(float dt)
        {
            if (_duty.Update(dt, _table, alarm: true))
            {
                return true;
            }
            StopJob();
            _settler.SetActivity(SettlerActivity.Sheltering);
            WarBanner shelter = Posts.Shelter(_table, _ai.transform.position);
            if (shelter != null)
            {
                _character.GetUp();
                if (Vector3.Distance(_ai.transform.position, shelter.transform.position) > ShelterReach)
                {
                    _mover.MoveTo(dt, shelter.transform.position, ShelterReach * 0.6f, ShelterReach + 1f, run: true);
                }
                else
                {
                    // Sheltering, not strolling off towards its home spot.
                    _ai.StopMoving();
                }
                return true;
            }
            if (_bed != null)
            {
                GoToBed(dt, SettlerActivity.Sheltering);
                return true;
            }
            // No shelter and no bed: stay by the Jarl's Table, among the defenders.
            _character.GetUp();
            Vector3 table = _table.transform.position;
            if (Vector3.Distance(_ai.transform.position, table) > ShelterReach + 2f)
            {
                _mover.MoveTo(dt, table, ShelterReach, ShelterReach + 3f, run: true);
            }
            else
            {
                _ai.StopMoving();
            }
            return true;
        }

        // ---------------------------------------------------------------- meals

        // The nearest Settlement Cauldron with food; the best meal in it (most satiety) is eaten on the spot. A light
        // meal (peckish, not hungry) comes from the bag or a cauldron only: no fetching from chests, no foraging.
        private bool Eat(float dt, bool lightMeal = false)
        {
            // Food in its own bag (fetched from a chest, picked up) is eaten on the spot. Every frame while hungry, so
            // a plain loop rather than LINQ.
            Inventory bag = _character.GetInventory();
            ItemDrop.ItemData packed = null;
            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (Needs.IsFood(item) && (packed == null || Needs.MealValue(item) > Needs.MealValue(packed)))
                {
                    packed = item;
                }
            }
            if (packed != null && _settler.Zdo != null)
            {
                Needs.Eat(_settler.Zdo, packed);
                bag.RemoveItem(packed, 1);
                _settler.FlushInventory();
                _starving = false;
                Log.Debug(Module, $"{_settler.DisplayName} ate {packed.m_shared.m_name} from its bag");
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"eats {packed.m_shared.m_name} from its bag (satiety {Needs.Satiety(_settler.Zdo):0})");
                }
                return true;
            }
            if (_pantry != null)
            {
                if (FetchFood(dt))
                {
                    return true;
                }
                _mealRetryAt = Time.time + MealRetrySeconds;
                return false;
            }
            if (_forage != null || Time.time < _forageGatherUntil)
            {
                if (Forage(dt))
                {
                    return true;
                }
                _mealRetryAt = Time.time + MealRetrySeconds;
                return false;
            }
            if (_gatheringFood)
            {
                if (GatherGroundFood(dt))
                {
                    return true;
                }
                _gatheringFood = false;
                _mealRetryAt = Time.time + MealRetrySeconds;
                return false;
            }
            if (Time.time < _mealRetryAt)
            {
                return false;
            }
            // Servings are counted when choosing a cauldron; on the way only its presence is checked.
            if (_cauldron == null || !SettlementStorage.IsUsable(_cauldron.Container))
            {
                _cauldron = SettlementCauldron.Loaded
                    .Where(c => c != null && c.Container != null && c.Servings() > 0 &&
                                Vector3.Distance(c.transform.position, _table.transform.position) <= _radius &&
                                SettlementStorage.IsUsable(c.Container))
                    .OrderBy(c => Vector3.Distance(c.transform.position, _ai.transform.position))
                    .FirstOrDefault();
                _mealTimer = 0f;
                if (_cauldron == null && lightMeal)
                {
                    _mealRetryAt = Time.time + MealRetrySeconds;
                    return false;
                }
                _starving = _cauldron == null;
                if (_cauldron == null)
                {
                    if (FetchFood(dt) || (_gatheringFood = GatherGroundFood(dt)) || Forage(dt))
                    {
                        _starving = false;
                        return true;
                    }
                    // Shown while it idles until the next look (see UpdateInner), not for a single frame.
                    _mealRetryAt = Time.time + MealRetrySeconds;
                    return false;
                }
            }

            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Eating);
            Vector3 target = _cauldron.transform.position;
            _mealTimer += dt;
            MoveResult move = _mover.MoveTo(dt, target, ChestStopDistance, ChestReach, run: false);
            if (move == MoveResult.Moving && _mealTimer < TripSeconds)
            {
                return true;
            }
            if (move != MoveResult.Arrived || Vector3.Distance(_ai.transform.position, target) > ChestReach)
            {
                _cauldron = null;
                _mealRetryAt = Time.time + MealRetrySeconds;
                return false;
            }
            Container pot = _cauldron.Container;
            if (!ChestAccess.Acquire(pot, ask: TimeToAsk(dt)))
            {
                if (_mealTimer > TripSeconds + AccessSeconds)
                {
                    _cauldron = null;
                    _mealRetryAt = Time.time + MealRetrySeconds;
                }
                return true;
            }

            pot.Load();
            Inventory food = pot.GetInventory();
            ItemDrop.ItemData meal = food.GetAllItems().Where(Needs.IsFood).OrderByDescending(Needs.MealValue).FirstOrDefault();
            if (meal != null && _settler.Zdo != null)
            {
                Needs.Eat(_settler.Zdo, meal);
                food.RemoveItem(meal, 1);
                Log.Debug(Module, $"{_settler.DisplayName} ate {meal.m_shared.m_name}");
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"eats {meal.m_shared.m_name} at the cauldron (satiety {Needs.Satiety(_settler.Zdo):0})");
                }
            }
            _cauldron = null;
            return true;
        }

        /// <summary>Taken from a chest for an empty cauldron: the rest goes into the cauldron for everyone.</summary>
        private const int RestockServings = 10;
        /// <summary>Without any cauldron, only what the settler eats now.</summary>
        private const int OwnServings = 2;
        private Container _pantry;
        private float _pantryTimer;
        /// <summary>Carries food fetched for the cauldron: taken there at once, whatever its job, so the others can eat.</summary>
        private bool _deliverFood;

        // No cauldron has food but a chest of the settlement does: the hungry settler fetches some - enough for the
        // others too when there is a cauldron to put the rest in (the storing trip takes it there, food goes to a
        // cauldron first) - and eats from its bag. True while busy with it.
        private bool FetchFood(float dt)
        {
            if (_pantry == null)
            {
                _pantry = SettlementStorage.FindHolding(_chests, Needs.IsFood, _ai.transform.position);
                _pantryTimer = 0f;
                if (_pantry == null)
                {
                    return false;
                }
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"hungry, no food in a cauldron: fetches some from a chest {Vector3.Distance(_ai.transform.position, _pantry.transform.position):0} m away");
                }
            }
            if (!SettlementStorage.IsUsable(_pantry))
            {
                _pantry = null;
                return false;
            }

            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Eating);
            Vector3 target = _pantry.transform.position;
            _pantryTimer += dt;
            MoveResult move = _mover.MoveTo(dt, target, ChestStopDistance, ChestReach, run: false);
            if (move == MoveResult.Moving && _pantryTimer < TripSeconds)
            {
                return true;
            }
            if (move != MoveResult.Arrived || Vector3.Distance(_ai.transform.position, target) > ChestReach)
            {
                _avoidUntil[_pantry.m_nview.GetZDO().m_uid] = Time.time + UnreachableSeconds;
                _pantry = null;
                return false;
            }
            if (!ChestAccess.Acquire(_pantry, ask: TimeToAsk(dt)))
            {
                if (_pantryTimer > TripSeconds + AccessSeconds)
                {
                    _pantry = null;
                    return false;
                }
                return true;
            }
            bool cauldron = SettlementCauldron.Loaded.Exists(c => c != null && c.Container != null &&
                                                                 Vector3.Distance(c.transform.position, _table.transform.position) <= _radius);
            int taken = SettlementStorage.TakeFrom(_pantry, _character.GetInventory(), Needs.IsFood, cauldron ? RestockServings : OwnServings);
            _settler.FlushInventory();
            _pantry = null;
            _deliverFood = cauldron && taken > 0;
            if (AiTrace.On)
            {
                AiTrace.Write(_ai, $"took {taken} food from the chest{(cauldron ? " (the rest goes to the cauldron)" : "")}");
            }
            return taken > 0;
        }

        // ---------------------------------------------------------------- foraging

        private bool _gatheringFood;

        // Food lying about in the settlement (dropped by a player, or by the game when a chest was full) is picked up and
        // eaten from the bag before anything wild is foraged: a settler starved next to cooked meat on the ground.
        private bool GatherGroundFood(float dt)
        {
            // What lies around is only theirs to take with Settlers/CollectLoot on.
            if (!AoJConfig.SettlerCollectLoot.Value)
            {
                return false;
            }
            if (!_gatheringFood)
            {
                // A look right now: the collector's own next scan (for the chores) may be seconds away, and this is only
                // asked once per meal retry.
                _loot.ScanNow();
            }
            if (!_loot.CollectInZone(dt, _table.transform.position, _radius, Needs.IsFood))
            {
                return false;
            }
            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Eating);
            return true;
        }

        /// <summary>Beyond the settlement's edge by at most this much: the trip stays well within home (HomeMargin).</summary>
        private const float ForageMargin = 2f;
        private const float ForageReach = 1.6f;
        private const float ForageScanSeconds = 5f;
        /// <summary>After picking, the food that dropped is gathered for at most this long.</summary>
        private const float ForageGatherSeconds = 6f;
        private const float ForageGatherRadius = 4f;
        /// <summary>The drops appear (on the plant's owner) and are looked for within this; then an empty spot ends it.</summary>
        private const float ForageSettleSeconds = 1.5f;
        private Pickable _forage;
        private float _forageTimer;
        private float _forageScanAt;
        private Vector3 _forageSpot;
        private float _forageGatherStart;
        private float _forageGatherUntil;

        // The last resort of a hungry settler when the settlement has no food at all (no cauldron with any, no chest
        // with any, nothing in its bag): it picks what grows wild nearby - berries, mushrooms, never a player's crops -
        // gathers what drops and eats it from its bag. True while busy with it.
        private bool Forage(float dt)
        {
            if (Time.time < _forageGatherUntil)
            {
                // Its own pick, so gathered whatever the loot setting says; eaten from the bag (see Eat).
                _settler.SetActivity(SettlerActivity.Eating);
                if (_loot.CollectInZone(dt, _forageSpot, ForageGatherRadius, Needs.IsFood))
                {
                    return true;
                }
                if (Time.time < _forageGatherStart + ForageSettleSeconds)
                {
                    _ai.StopMoving();
                    return true;
                }
                _forageGatherUntil = 0f;
                return false;
            }
            if (_forage == null || !_forage.CanBePicked())
            {
                _forage = null;
                if (Time.time < _forageScanAt)
                {
                    return false;
                }
                _forageScanAt = Time.time + ForageScanSeconds;
                _forage = FindForage();
                _forageTimer = 0f;
                if (_forage == null)
                {
                    return false;
                }
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"hungry, no food in the settlement: forages {Utils.GetPrefabName(_forage.gameObject)} {Vector3.Distance(_ai.transform.position, _forage.transform.position):0} m away");
                }
            }

            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Eating);
            Vector3 target = _forage.transform.position;
            _forageTimer += dt;
            MoveResult move = _mover.MoveTo(dt, target, ForageReach * 0.7f, ForageReach + 1f, run: false);
            if (move == MoveResult.Moving && _forageTimer < TripSeconds)
            {
                return true;
            }
            if (move != MoveResult.Arrived || Vector3.Distance(_ai.transform.position, target) > ForageReach + 1f)
            {
                _forage = null;
                return false;
            }
            _ai.StopMoving();
            _forage.Interact(_character, false, false);
            _forageSpot = target;
            _forageGatherStart = Time.time;
            _forageGatherUntil = Time.time + ForageGatherSeconds;
            _forage = null;
            _loot.ScanNow();
            return true;
        }

        // The nearest wild plant that gives food and can be picked now, within the settlement and a margin around it.
        private Pickable FindForage() => WildFood.Nearest(_ai.transform.position, _table.transform.position, _radius + ForageMargin);

        // ---------------------------------------------------------------- work

        private JobBase AssignedJob()
        {
            WorkTotem totem = _settler.JobTotem;
            return totem != null ? Job(totem.Job) : null;
        }

        private JobBase Job(JobType type)
        {
            if (!_jobs.TryGetValue(type, out JobBase job))
            {
                job = _jobFactory(type);
                _jobs[type] = job;
            }
            return job;
        }

        private bool Work(float dt)
        {
            WorkTotem totem = _settler.JobTotem;
            if (totem == null)
            {
                if (_settler.JobId != 0L)
                {
                    _settler.SetJobProblem("$aoj_problem_totem_gone");
                }
                StopJob();
                return false;
            }
            // A Night Owl keeps working at a day-only totem through the night.
            if (!totem.IsWorkTime && _settler.TraitSum(TraitStat.NightWork) <= 0f)
            {
                if (_activeJob != null)
                {
                    // The working day is over: the day's take goes to the chests before bed, planned right away.
                    _planTimer = Mathf.Min(_planTimer, 0.1f);
                }
                StopJob();
                return false;
            }
            ZDO zdo = _settler.Zdo;
            if (zdo != null && Needs.RefusesWork(zdo))
            {
                _settler.SetJobProblem("$aoj_problem_miserable");
                StopJob();
                return false;
            }
            // At home the totem must serve this settlement and its job be unlocked there. Out at the totem with the
            // settlement not loaded (WorkAway), that cannot be looked at: the job was given when it held.
            if (_table != null && totem.Settlement != _table)
            {
                _settler.SetJobProblem("$aoj_totem_outside");
                StopJob();
                return false;
            }
            if (_table != null && !JobInfo.IsUnlocked(totem.Job, _table.Data?.Tier ?? 0))
            {
                _settler.SetJobProblem("$aoj_job_locked");
                StopJob();
                return false;
            }

            JobBase job = Job(totem.Job);
            if (job == null)
            {
                return false;
            }
            // A chore under way (gathering what lies around) is finished first: otherwise job and chores take turns
            // over the same drops and the settler keeps changing course. The job itself is asked every frame - its own
            // timers (the scan for the next tree or rock) only run while it is.
            if (_activeJob == job && _jobIdle && _collecting && (_loot.HasTarget || _loot.BusyWithin(GatherPauseSeconds)))
            {
                return false;
            }
            if (_activeJob != job)
            {
                StopJob();
                _activeJob = job;
            }
            _character.GetUp();
            bool busy = job.Update(dt, totem);
            if (busy && _helpTotem != null)
            {
                // Its own work is back: the hand lent elsewhere comes home.
                StopHelping();
            }
            if (!busy && !_jobIdle)
            {
                // Out of work (or of room in its bag) just now: the trip to the chests is planned in a moment, not after
                // up to a whole planning period of chores.
                _planTimer = Mathf.Min(_planTimer, GatherPauseSeconds);
                _jobIdleSince = Time.time;
            }
            _jobIdle = !busy;
            _helpBusy = !busy && Help(dt, totem);
            busy |= _helpBusy;
            if (busy)
            {
                _settler.SetActivity(SettlerActivity.Working);
            }
            return busy;
        }

        private void StopJob()
        {
            StopHelping();
            if (_activeJob != null)
            {
                _activeJob.Stop();
                _activeJob = null;
            }
            _jobIdle = false;
        }

        // ---------------------------------------------------------------- lending a hand

        /// <summary>Idle at its own totem this long (no trees left, an empty input chest) before it looks elsewhere.</summary>
        private const float HelpAfterSeconds = 15f;
        /// <summary>A totem that gives no work for this long is left alone for a while.</summary>
        private const float HelpGiveUpSeconds = 5f;
        private const float HelpTotemRetrySeconds = 90f;
        /// <summary>How far beyond the settlement's radius a totem's zone may reach for a helper to go there.</summary>
        private const float MaxHelpBeyond = 60f;

        private WorkTotem _helpTotem;
        private JobBase _helpJob;
        private bool _helpBusy;
        private float _helpIdleSince;
        private readonly Dictionary<long, float> _helpRetryAt = new Dictionary<long, float>();

        // A worker whose own totem has nothing for it lends a hand at another totem of the settlement with work - the
        // highest priority first, the nearest among equals - with its assignment unchanged; the tool it needs there is
        // fetched from the chests like for its own job. Its own totem is still asked every frame, so it is back the
        // moment its own work is. True while busy elsewhere.
        private bool Help(float dt, WorkTotem own)
        {
            if (_helpTotem != null && (_helpTotem.Settlement != _table || !_helpTotem.IsWorkTime))
            {
                StopHelping();
            }
            if (_helpTotem == null)
            {
                if (_table == null || Time.time - _jobIdleSince < HelpAfterSeconds || _settler.TraitSum(TraitStat.NightWork) <= 0f && !own.IsWorkTime)
                {
                    return false;
                }
                _helpTotem = PickHelpTotem(own);
                if (_helpTotem == null)
                {
                    return false;
                }
                _helpJob = Job(_helpTotem.Job);
                if (_helpJob == null || _helpJob == _activeJob)
                {
                    _helpTotem = null;
                    _helpJob = null;
                    return false;
                }
                _helpJob.Helping = true;
                _helpIdleSince = Time.time;
                Log.Debug(Module, $"{_settler.DisplayName} lends a hand at the {_helpTotem.Job} totem");
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, $"nothing at its own totem: lends a hand at the {_helpTotem.Job} totem {Vector3.Distance(_ai.transform.position, _helpTotem.transform.position):0} m away");
                }
            }
            bool busy = _helpJob.Update(dt, _helpTotem);
            if (busy)
            {
                _helpIdleSince = Time.time;
                return true;
            }
            if (Time.time - _helpIdleSince > HelpGiveUpSeconds)
            {
                if (_helpRetryAt.Count > 32)
                {
                    _helpRetryAt.Clear();
                }
                _helpRetryAt[_helpTotem.Id] = Time.time + HelpTotemRetrySeconds;
                StopHelping();
            }
            return false;
        }

        private WorkTotem PickHelpTotem(WorkTotem own)
        {
            WorkTotem best = null;
            int bestPriority = int.MinValue;
            float bestSqr = float.MaxValue;
            int tier = _table.Data?.Tier ?? 0;
            Vector3 me = _ai.transform.position;
            foreach (WorkTotem totem in WorkTotem.Loaded)
            {
                if (totem == null || totem == own || totem.Id == 0L || totem.Settlement != _table || !totem.IsWorkTime ||
                    !JobInfo.IsUnlocked(totem.Job, tier) || totem.Job == own.Job ||
                    (_helpRetryAt.TryGetValue(totem.Id, out float retryAt) && Time.time < retryAt) ||
                    WorkReach(totem) > _radius + MaxHelpBeyond)
                {
                    continue;
                }
                float sqr = (totem.transform.position - me).sqrMagnitude;
                int priority = totem.Priority;
                if (priority > bestPriority || (priority == bestPriority && sqr < bestSqr))
                {
                    best = totem;
                    bestPriority = priority;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        private void StopHelping()
        {
            if (_helpJob != null)
            {
                _helpJob.Stop();
                _helpJob.Helping = false;
            }
            _helpJob = null;
            _helpTotem = null;
            _helpBusy = false;
        }

        // ---------------------------------------------------------------- wounds

        private const float WoundedBelow = 0.4f;
        private const float HealedAbove = 0.95f;
        /// <summary>By day a worker gets up again at this much health - fit for work, the rest heals on its feet - rather than lying in bed till evening.</summary>
        private const float FitForWorkAbove = 0.75f;
        private bool _recovering;

        // A badly wounded settler goes to bed and stays there (healing faster, see SettlerCharacter) until it is well.
        private bool Recover(float dt)
        {
            float health = _character.GetHealthPercentage();
            float healed = EnvMan.IsNight() || _settler.JobId == 0L ? HealedAbove : FitForWorkAbove;
            if (_recovering && health >= healed)
            {
                _recovering = false;
            }
            else if (!_recovering && health < WoundedBelow && _bed != null)
            {
                _recovering = true;
            }
            if (!_recovering || _bed == null)
            {
                return false;
            }
            StopJob();
            GoToBed(dt, SettlerActivity.Recovering);
            return true;
        }

        // ---------------------------------------------------------------- sleep and idling

        // One activity per frame: set twice (sleeping, then sheltering) the ZDO would be written, and sent, every frame.
        private void GoToBed(float dt, SettlerActivity activity = SettlerActivity.Sleeping)
        {
            _settler.SetActivity(activity);
            if (_character.IsLyingDown)
            {
                return;
            }

            Vector3 bed = _bed.m_spawnPoint != null ? _bed.m_spawnPoint.position : _bed.transform.position;
            _bedTimer += dt;
            MoveResult move = _mover.MoveTo(dt, bed, BedStopDistance, BedReach, run: false);
            if (move == MoveResult.Moving && _bedTimer < BedTripSeconds)
            {
                return;
            }

            float distance = Vector3.Distance(_ai.transform.position, bed);
            // A door it cannot open (a key, a ward) keeps a settler out: after trying for a while, one that got
            // close enough goes to bed anyway.
            if ((move == MoveResult.Arrived && distance <= BedReach) || (_bedTimer >= BedTripSeconds && distance <= BedShortcutRange))
            {
                _character.LieDown(_bed);
            }
        }

        // Vanilla idle movement strolls around the home anchor but cannot open doors, so from farther away the settler
        // walks back with its own path. Where even that fails, vanilla gets its turn again for a while.
        private bool ReturnHome(float dt)
        {
            // Once on its way it walks well inside, where vanilla strolling takes over - not in and out at the edge:
            // pushed back by a log lying right there, it switched between the two at every frame.
            float distance = Utils.DistanceXZ(_anchor, _ai.transform.position);
            if (Time.time < _returnRetryAt || distance <= (_returning ? ReturnDistance() - ReturnHysteresis : ReturnDistance()))
            {
                _returning = false;
                return false;
            }
            _returning = true;
            MoveResult move = _mover.MoveTo(dt, _anchor, ReturnStopDistance, ReturnReach, run: false);
            if (move == MoveResult.Blocked)
            {
                _returnRetryAt = Time.time + ReturnRetrySeconds;
                _returning = false;
                return false;
            }
            if (move != MoveResult.Moving)
            {
                _returning = false;
            }
            return move == MoveResult.Moving;
        }

        private const float ReturnHysteresis = 4f;
        private bool _returning;

        private static float ReturnDistance() => AoJConfig.SettlerWanderRange.Value * 2f + ReturnMargin;

        // ---------------------------------------------------------------- idle pose

        /// <summary>Only a settler idle this long in one go sits down: not in a short gap between two tasks.</summary>
        private const float SitAfterIdleSeconds = 6f;
        private bool _sitRequested;
        private bool _sitting;
        private float _sitTimer = 30f;
        private bool _idleThisFrame;
        private float _idleTime;

        // Now and then an idle settler sits down near its home spot for a while (every machine sees the pose).
        private void IdleSit(float dt)
        {
            if (_carrying || _idleTime < SitAfterIdleSeconds ||
                Utils.DistanceXZ(_anchor, _ai.transform.position) > AoJConfig.SettlerWanderRange.Value + 1f)
            {
                return;
            }
            _sitTimer -= dt;
            if (!_sitting && _sitTimer > 0f)
            {
                return;
            }
            if (!_sitting)
            {
                _sitting = true;
                _sitTimer = Random.Range(20f, 45f);
                _settler.SetSitting(true);
            }
            else if (_sitTimer <= 0f)
            {
                StopSitting();
                return;
            }
            _sitRequested = true;
            _ai.StopMoving();
        }

        // Always clears the pose in the ZDO, not only one this routine set: a settler that sat down before the world was
        // saved (or on its previous owner's machine) would otherwise stay seated - and a seated character cannot walk.
        private void StopSitting()
        {
            if (_sitting)
            {
                _sitting = false;
                _sitTimer = Random.Range(40f, 120f);
            }
            _settler.SetSitting(false);
        }
    }
}
