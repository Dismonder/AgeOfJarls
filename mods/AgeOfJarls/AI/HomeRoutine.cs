using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.AI.Jobs;
using AgeOfJarls.Army;
using AgeOfJarls.Core;
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
            _hasChest = item => SettlementStorage.HasDestination(item, _chests, _ai.transform.position);
            _avoided = chest => _avoidUntil.TryGetValue(chest.m_nview.GetZDO().m_uid, out float until) && Time.time < until;
            _keep = item => AssignedJob()?.Keeps(item) ?? false;
        }

        internal void Update(float dt)
        {
            _sitRequested = false;
            UpdateInner(dt);
            if (!_sitRequested)
            {
                StopSitting();
            }
        }

        private void UpdateInner(float dt)
        {
            _planTimer -= dt;
            if (_planTimer <= 0f)
            {
                _planTimer = PlanSeconds;
                Plan();
            }
            if (_table == null)
            {
                // Away from home or the table is not loaded: vanilla idle movement walks back to the home anchor.
                Stop();
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
            if (zdo != null && Needs.IsHungry(zdo) && Eat(dt))
            {
                return;
            }
            bool night = EnvMan.IsNight();
            if (Work(dt))
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
                return;
            }
            if (ReturnHome(dt))
            {
                _settler.SetActivity(SettlerActivity.Returning);
                return;
            }
            IdleSit(dt);
            _settler.SetActivity(_carrying ? SettlerActivity.NoChest : SettlerActivity.Idle);
        }

        /// <summary>For aoj_debug: home, trip, job and its target.</summary>
        internal string DebugState()
        {
            if (_table == null)
            {
                return "not home";
            }
            var parts = new List<string>();
            if (_chest != null)
            {
                parts.Add($"to chest {Vector3.Distance(_ai.transform.position, _chest.transform.position):0} m");
            }
            if (_carrying)
            {
                parts.Add("carrying");
            }
            if (_activeJob != null)
            {
                string target = _activeJob.DebugTarget;
                parts.Add($"{_activeJob.Job}{(_jobIdle ? " (idle)" : "")}{(target.Length > 0 ? " -> " + target : "")}");
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
        internal void Stop()
        {
            _chest = null;
            StopSitting();
            _cauldron = null;
            _bedTimer = 0f;
            StopJob();
            _duty.Stop();
            _character.GetUp();
            _settler.SetActivity(SettlerActivity.Idle);
        }

        // ---------------------------------------------------------------- planning

        private void Plan()
        {
            _table = _settler.HomeTable;
            SettlementData data = _table != null ? _table.Data : null;
            if (data == null)
            {
                _table = null;
                return;
            }
            _radius = JarlTable.RadiusOf(data);
            if (Vector3.Distance(_ai.transform.position, _table.transform.position) > _radius + HomeMargin)
            {
                _table = null;
                return;
            }

            _bed = _settler.TryGetBed(_table, out Vector3 bedPosition) ? JarlTable.FindBedAt(bedPosition) : null;
            _anchor = _settler.HomeAnchor(_table);

            SettlementStorage.CollectChests(_table.transform.position, _radius, _chests);
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
                }
            }
            if (_avoidUntil.Count > MaxAvoided)
            {
                _avoidUntil.Clear();
            }
        }

        // A worker gathers a load before walking to the chests; everyone else stores right away.
        private bool ShouldStore()
        {
            Inventory bag = _character.GetInventory();
            if (_activeJob == null || _jobIdle)
            {
                return true;
            }
            int carried = bag.GetAllItems().Where(i => SettlementStorage.IsStorable(i) && !_keep(i)).Sum(i => i.m_stack);
            return carried >= AoJConfig.WorkerLoad.Value || bag.GetEmptySlots() <= FreeSlotsBeforeStoring;
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
                return true;
            }
            if (_bed != null)
            {
                GoToBed(dt);
                _settler.SetActivity(SettlerActivity.Sheltering);
                return true;
            }
            // No shelter and no bed: stay by the Jarl's Table, among the defenders.
            _character.GetUp();
            Vector3 table = _table.transform.position;
            if (Vector3.Distance(_ai.transform.position, table) > ShelterReach + 2f)
            {
                _mover.MoveTo(dt, table, ShelterReach, ShelterReach + 3f, run: true);
            }
            return true;
        }

        // ---------------------------------------------------------------- meals

        // The nearest Settlement Cauldron with food; the best meal in it (most satiety) is eaten on the spot.
        private bool Eat(float dt)
        {
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
                if (_cauldron == null)
                {
                    _settler.SetActivity(SettlerActivity.NoFood);
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
            }
            _cauldron = null;
            return true;
        }

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
            if (!totem.IsWorkTime)
            {
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
            if (totem.Settlement != _table)
            {
                _settler.SetJobProblem("$aoj_totem_outside");
                StopJob();
                return false;
            }
            if (!JobInfo.IsUnlocked(totem.Job, _table.Data?.Tier ?? 0))
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
            if (_activeJob != job)
            {
                StopJob();
                _activeJob = job;
            }
            _character.GetUp();
            bool busy = job.Update(dt, totem);
            _jobIdle = !busy;
            if (busy)
            {
                _settler.SetActivity(SettlerActivity.Working);
            }
            return busy;
        }

        private void StopJob()
        {
            if (_activeJob != null)
            {
                _activeJob.Stop();
                _activeJob = null;
            }
            _jobIdle = false;
        }

        // ---------------------------------------------------------------- wounds

        private const float WoundedBelow = 0.4f;
        private const float HealedAbove = 0.95f;
        private bool _recovering;

        // A badly wounded settler goes to bed and stays there (healing faster, see SettlerCharacter) until it is well.
        private bool Recover(float dt)
        {
            float health = _character.GetHealthPercentage();
            if (_recovering && health >= HealedAbove)
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
            GoToBed(dt);
            _settler.SetActivity(SettlerActivity.Recovering);
            return true;
        }

        // ---------------------------------------------------------------- sleep and idling

        private void GoToBed(float dt)
        {
            _settler.SetActivity(SettlerActivity.Sleeping);
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
            if (Time.time < _returnRetryAt || Utils.DistanceXZ(_anchor, _ai.transform.position) <= ReturnDistance())
            {
                return false;
            }
            MoveResult move = _mover.MoveTo(dt, _anchor, ReturnStopDistance, ReturnReach, run: false);
            if (move == MoveResult.Blocked)
            {
                _returnRetryAt = Time.time + ReturnRetrySeconds;
                return false;
            }
            return move == MoveResult.Moving;
        }

        private static float ReturnDistance() => AoJConfig.SettlerWanderRange.Value * 2f + ReturnMargin;

        // ---------------------------------------------------------------- idle pose

        private bool _sitRequested;
        private bool _sitting;
        private float _sitTimer = 30f;

        // Now and then an idle settler sits down near its home spot for a while (every machine sees the pose).
        private void IdleSit(float dt)
        {
            if (_carrying || Utils.DistanceXZ(_anchor, _ai.transform.position) > AoJConfig.SettlerWanderRange.Value + 1f)
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

        private void StopSitting()
        {
            if (!_sitting)
            {
                return;
            }
            _sitting = false;
            _sitTimer = Random.Range(40f, 120f);
            _settler.SetSitting(false);
        }
    }
}
