using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Settlement;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Settler logic on every client. Identity, inventory and orders live in the ZDO: only the ZDO owner writes them,
    /// other clients send RPCs. Ownership moves between players, so a new owner rebuilds its state from the ZDO first.
    /// </summary>
    public class Settler : MonoBehaviour, Interactable
    {
        private const string Module = "Settlers";
        private const float IdentityRetrySeconds = 1f;
        private const float TickSeconds = 1f;
        private const float MaxFleeThreshold = 0.9f;
        /// <summary>Seconds a table that should be loaded must stay missing before its settlers give up their home.</summary>
        private const int MissingTableTicks = 30;
        /// <summary>A table this close to the local reference point is surely inside a loaded zone.</summary>
        private const float LoadedTableRange = 40f;
        private const int MaxNameLength = 24;

        /// <summary>How far a settler told to wait may step away from that spot.</summary>
        private const float HoldRange = 1f;

        /// <summary>Loaded, networked settlers.</summary>
        internal static readonly List<Settler> Loaded = new List<Settler>();

        private ZNetView _nview;
        private Humanoid _humanoid;
        private MonsterAI _ai;
        private VisEquipment _visEquipment;

        private bool _wasOwner;
        private bool _inventoryDirty;
        private bool _loadingInventory;
        private bool _inventoryUnreadable;
        private int _loadedInventoryRevision = -1;
        private int _homeMissingTicks;
        private string _traitsText = "";

        internal SettlerIdentity Identity { get; private set; }

        /// <summary>
        /// Stable id, saved in the settler's ZDO (its ZDOID changes on every world load). 0 only for a settler from an
        /// older mod version until its owner gives it one.
        /// </summary>
        internal long Uid => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerUid) : 0L;

        internal static Settler FindByUid(long uid)
        {
            if (uid == 0L)
            {
                return null;
            }
            foreach (Settler settler in Loaded)
            {
                if (settler.Uid == uid)
                {
                    return settler;
                }
            }
            return null;
        }

        /// <summary>From the ZDO, so a rename made on another machine shows up at once.</summary>
        internal string DisplayName
        {
            get
            {
                string name = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(ZDOVars.s_overrideHoverName) : "";
                return name.Length > 0 ? name : Identity?.Name ?? Localize("$aoj_settler");
            }
        }

        internal long FollowedPlayerId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerFollow) : 0L;

        /// <summary>Stable id of the settler's settlement; 0 = homeless.</summary>
        internal long HomeId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerHomeId) : 0L;

        internal bool HasHome => HomeId != 0L;

        /// <summary>Has a settlement and no orders (neither following nor told to wait): lives its own life there.</summary>
        internal bool IsAtHome
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return false;
                }
                ZDO zdo = _nview.GetZDO();
                return zdo.GetLong(Keys.ZdoSettlerHomeId) != 0L && zdo.GetLong(Keys.ZdoSettlerFollow) == 0L &&
                       !zdo.GetBool(Keys.ZdoSettlerHold);
            }
        }

        /// <summary>The settler's Jarl's Table, when it is loaded on this machine.</summary>
        internal JarlTable HomeTable => JarlTable.FindById(HomeId);

        /// <summary>Where the bed the settlement gave this settler stands, from the roster; false without one.</summary>
        internal bool TryGetBed(JarlTable table, out Vector3 bedPosition)
        {
            SettlementData data = table != null ? table.Data : null;
            RosterEntry entry = data?.FindSettler(Uid);
            bedPosition = entry != null ? entry.BedPosition : Vector3.zero;
            return entry != null && entry.HasBed;
        }

        /// <summary>Stable id of the Work Totem the settler works at; 0 = no job.</summary>
        internal long JobId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerJob) : 0L;

        /// <summary>The totem of its job, when loaded here.</summary>
        internal Work.WorkTotem JobTotem => Work.WorkTotem.FindById(JobId);

        /// <summary>What keeps it from working, as a token ("" = nothing).</summary>
        internal string JobProblem => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(Keys.ZdoSettlerProblem) : "";

        internal ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        /// <summary>Owner only; written when it changes.</summary>
        internal void SetJobProblem(string token)
        {
            token = token ?? "";
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && _nview.GetZDO().GetString(Keys.ZdoSettlerProblem) != token)
            {
                _nview.GetZDO().Set(Keys.ZdoSettlerProblem, token);
            }
        }

        /// <summary>Owner only: an equipment change (a tool taken in hand) is saved with the next inventory save.</summary>
        internal void MarkInventoryDirty()
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _inventoryDirty = true;
            }
        }

        /// <summary>A captive from a camp: takes no orders until a player frees it.</summary>
        internal bool IsCaptive => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoSettlerCaptive);

        /// <summary>In the world on this machine (its ZDO is alive; false once its zone unloads or it is destroyed).</summary>
        internal bool IsLoaded => this != null && _nview != null && _nview.IsValid();

        /// <summary>Knocked out for a moment instead of dead (see SettlerCharacter.KnockOut).</summary>
        internal bool IsDown => _humanoid is SettlerCharacter body && body.Down;

        /// <summary>
        /// For aoj_debug: what every machine knows from the ZDO and, on the machine that simulates the settler, what
        /// its AI is doing. Localized.
        /// </summary>
        internal string DebugText()
        {
            if (_nview == null || !_nview.IsValid() || _humanoid == null)
            {
                return "";
            }
            ZDO zdo = _nview.GetZDO();
            bool owner = _nview.IsOwner();
            var text = new StringBuilder();
            text.Append("<b>").Append(DisplayName).Append("</b> ").Append(owner ? "[mine]" : $"[peer {zdo.GetOwner()}]")
                .Append($"  HP {Mathf.CeilToInt(_humanoid.GetHealth())}/{Mathf.CeilToInt(_humanoid.GetMaxHealth())}")
                .Append(IsDown ? "  DOWN" : "").Append(IsCaptive ? "  CAPTIVE" : "").Append('\n');
            string home = !HasHome ? "none" : HomeTable != null ? (IsAtHome ? "at home" : "away") : "not loaded";
            text.Append($"{(SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)} · follows {zdo.GetLong(Keys.ZdoSettlerFollow)}")
                .Append(zdo.GetBool(Keys.ZdoSettlerHold) ? " · holds" : "").Append($" · home {home}\n");
            Work.WorkTotem totem = JobTotem;
            string job = totem != null ? totem.Job.ToString() : JobId != 0L ? "far" : "-";
            string problem = JobProblem.Length > 0 ? $" ({JobProblem})" : "";
            text.Append($"job {job}{problem} · role {Role} · food {Needs.Satiety(zdo):0} · morale {Needs.Morale(zdo):0}");
            if (owner && _ai is AI.SettlerAI ai)
            {
                text.Append('\n').Append(ai.DebugState());
            }
            return Localize(text.ToString());
        }

        internal Army.CombatRole Role => _nview != null && _nview.IsValid() ? (Army.CombatRole)_nview.GetZDO().GetInt(Keys.ZdoSettlerRole) : Army.CombatRole.None;

        /// <summary>Stable id of the war banner it is posted at; 0 = none.</summary>
        internal long PostId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerPost) : 0L;

        internal void RequestSetRole(Army.CombatRole role) => _nview.InvokeRPC(Keys.RpcSettlerRole, (int)role);

        private void RPC_SetRole(long sender, int role)
        {
            if (!OwnerHandles(Keys.RpcSettlerRole, role))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }
            // A role the settlement has not unlocked yet is refused when its table is loaded here to tell.
            JarlTable home = HomeTable;
            bool locked = role != (int)Army.CombatRole.None && home != null && home.Data != null &&
                          !Army.CombatRoles.IsUnlocked((Army.CombatRole)role, home.Data.Tier);
            if (!MayOrder(sender, SettlementRight.Military) || !Enum.IsDefined(typeof(Army.CombatRole), role) || locked)
            {
                Log.Warning(Module, $"Role change for {DisplayName} from peer {sender} refused");
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerRole, role);
            if (role == (int)Army.CombatRole.None)
            {
                zdo.Set(Keys.ZdoSettlerPost, 0L);
            }
            Log.Info(Module, $"{DisplayName} is now {(Army.CombatRole)role}");
        }

        /// <summary>Owner only: the banner post a soldier stands at (chosen by Army.Posts).</summary>
        internal void SetPost(long bannerId)
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && PostId != bannerId)
            {
                _nview.GetZDO().Set(Keys.ZdoSettlerPost, bannerId);
            }
        }

        /// <summary>Owner only: turns a freshly spawned settler into a captive (<paramref name="caged"/>: locked behind bars).</summary>
        internal void MakeCaptive(bool caged = false)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerCaptive, true);
            zdo.Set(Keys.ZdoSettlerCaged, caged);
            zdo.Set(Keys.ZdoSettlerFollow, 0L);
            zdo.Set(Keys.ZdoSettlerHold, true);
            if (_ai != null)
            {
                _ai.SetFollowTarget(null);
                _ai.SetPatrolPoint();
            }
        }

        internal void RequestFree(Player player) => _nview.InvokeRPC(Keys.RpcSettlerFree, player.GetPlayerID());

        /// <summary>A captive once locked behind bars that are gone now (broken by a player).</summary>
        internal bool CageBroken =>
            _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoSettlerCaged) && !Recruitment.CaptiveCage.AnyNear(transform.position);

        /// <summary>Nothing keeps the captive in any more: no guard alive nearby, or its bars broken.</summary>
        internal bool CanBeFreed => IsCaptive && (GuardsNearby() == 0 || CageBroken);

        // Guards alive nearby (and unbroken bars) keep the captive locked in; freed, it follows its rescuer.
        private void RPC_Free(long sender, long playerId)
        {
            if (!OwnerHandles(Keys.RpcSettlerFree, playerId))
            {
                return;
            }
            Player player = Player.GetPlayer(playerId);
            if (player == null || !CanBeFreed)
            {
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerCaptive, false);
            zdo.Set(Keys.ZdoSettlerFollow, playerId);
            zdo.Set(Keys.ZdoSettlerHold, false);
            if (_ai != null)
            {
                _ai.ResetPatrolPoint();
                _ai.SetFollowTarget(player.gameObject);
            }
            // The camp's pin has done its work: off everybody's map.
            Recruitment.RecruitPins.Broadcast(Recruitment.RecruitPins.CaptiveToken, transform.position, false);
            Log.Info(Module, $"{DisplayName} was freed by {player.GetPlayerName()}");
        }

        internal const float GuardRange = 20f;

        /// <summary>Living creatures hostile to settlers within the guard range.</summary>
        internal int GuardsNearby()
        {
            int guards = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character != null && !character.IsDead() && !character.IsPlayer() && !character.IsTamed() &&
                    character.GetComponent<Settler>() == null && BaseAI.IsEnemy(_humanoid, character) &&
                    Vector3.Distance(character.transform.position, transform.position) <= GuardRange)
                {
                    guards++;
                }
            }
            return guards;
        }

        internal void RequestSetJob(long totemId) => _nview.InvokeRPC(Keys.RpcSettlerSetJob, totemId);

        /// <summary>
        /// Owner only: the settlement's own assignment (a free place at a totem, or leaving one that has too many
        /// workers), with no player behind it to check.
        /// </summary>
        internal void TakeJob(long totemId)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || JobId == totemId)
            {
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerJob, totemId);
            zdo.Set(Keys.ZdoSettlerProblem, "");
            Work.WorkTotem totem = Work.WorkTotem.FindById(totemId);
            Log.Info(Module, totemId == 0L ? $"{DisplayName} leaves a full totem" : $"{DisplayName} takes up work as {totem?.Job.ToString() ?? "a worker"}");
        }

        private void RPC_SetJob(long sender, long totemId)
        {
            if (!OwnerHandles(Keys.RpcSettlerSetJob, totemId))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }
            if (!MayOrder(sender, SettlementRight.Manage))
            {
                Log.Warning(Module, $"Job change for {DisplayName} from peer {sender} refused");
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerJob, totemId);
            zdo.Set(Keys.ZdoSettlerProblem, "");
            Work.WorkTotem totem = Work.WorkTotem.FindById(totemId);
            Log.Info(Module, totemId == 0L ? $"{DisplayName} has no job now" : $"{DisplayName} now works as {totem?.Job.ToString() ?? "a worker"}");
        }

        /// <summary>Owner only; written when it changes: an item no chest takes ("" = none), for every machine's voice.</summary>
        internal void SetNoChestItem(string token)
        {
            token = token ?? "";
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && _nview.GetZDO().GetString(Keys.ZdoSettlerNoChestItem) != token)
            {
                _nview.GetZDO().Set(Keys.ZdoSettlerNoChestItem, token);
            }
        }

        /// <summary>Owner only; written when it changes, for the order window on every machine.</summary>
        internal void SetActivity(SettlerActivity activity)
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() &&
                _nview.GetZDO().GetInt(Keys.ZdoSettlerActivity) != (int)activity)
            {
                if (AI.AiTrace.On)
                {
                    AI.AiTrace.Write(this, $"{(SettlerActivity)_nview.GetZDO().GetInt(Keys.ZdoSettlerActivity)} -> {activity}");
                }
                _nview.GetZDO().Set(Keys.ZdoSettlerActivity, (int)activity);
            }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<MonsterAI>();
            _visEquipment = GetComponent<VisEquipment>();
            if (_nview != null && _nview.IsValid())
            {
                Loaded.Add(this);
                _nview.Register<long>(Keys.RpcSettlerCommand, RPC_Command);
                _nview.Register<ZPackage>(Keys.RpcSettlerGive, RPC_Give);
                _nview.Register<ZPackage>(Keys.RpcSettlerTakeBack, RPC_TakeBack);
                _nview.Register<ZPackage>(Keys.RpcSettlerTakeItem, RPC_TakeItem);
                _nview.Register<long, ZPackage>(Keys.RpcSettlerDeliver, RPC_Deliver);
                _nview.Register<long>(Keys.RpcSettlerDelivered, RPC_Delivered);
                _nview.Register<string, string>(Keys.RpcSettlerNotify, RPC_Notify);
                _nview.Register<ZPackage>(Keys.RpcSettlerTeleport, RPC_Teleport);
                _nview.Register(Keys.RpcSettlerGoHome, RPC_GoHome);
                _nview.Register<string>(Keys.RpcSettlerRename, RPC_Rename);
                _nview.Register<ZDOID>(Keys.RpcSettlerAttack, RPC_Attack);
                _nview.Register<long>(Keys.RpcSettlerFallBack, RPC_FallBack);
                _nview.Register<long>(Keys.RpcSettlerSetJob, RPC_SetJob);
                _nview.Register<int>(Keys.RpcSettlerRole, RPC_SetRole);
                _nview.Register<long>(Keys.RpcSettlerFree, RPC_Free);
                _nview.Register<string>(Keys.RpcSettlerFeed, RPC_Feed);
            }
        }

        private void Start()
        {
            if (_nview == null || !_nview.IsValid() || _humanoid == null)
            {
                return;
            }

            _humanoid.GetInventory().m_onChanged += OnInventoryChanged;
            _humanoid.m_onDeath += OnDeath;
            UndoEnemyScaling();
            StartCoroutine(InitIdentity());
            // A random phase: settlers loaded in one frame would otherwise all tick in one frame every second.
            InvokeRepeating(nameof(Tick), UnityEngine.Random.Range(0.2f, TickSeconds), TickSeconds);
            SettlerCollision.OnSettlerLoaded(this);
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
            // Zone unload or logout: keep the last change instead of losing it with the pending tick.
            if (_inventoryDirty && _nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                SaveInventory();
            }
        }

        // Character.Awake scales every non-player by the "enemy speed & size" world modifier and the world level, and
        // its movement code multiplies their speed the same way. Settlers are not enemies, so every client undoes both
        // locally. Default worlds have a factor of 1 and skip this.
        private void UndoEnemyScaling()
        {
            float worldLevelFactor = Game.instance != null ? Game.m_worldLevel * Game.instance.m_worldLevelEnemyMoveSpeedMultiplier : 0f;
            float factor = Game.m_enemySpeedSize * (1f + worldLevelFactor);
            if (factor <= 0f || Mathf.Approximately(factor, 1f))
            {
                return;
            }

            transform.localScale = Vector3.one;
            _humanoid.m_walkSpeed /= factor;
            _humanoid.m_runSpeed /= factor;
            _humanoid.m_crouchSpeed /= factor;
            Log.Debug(Module, $"Undid enemy world scaling x{factor:0.##}");
        }

        // ---------------------------------------------------------------- identity

        // A remote client can load the settler before the owner's first ZDO update arrives, so it waits for it.
        private IEnumerator InitIdentity()
        {
            while (_nview.IsValid())
            {
                byte[] data = _nview.GetZDO().GetByteArray(Keys.ZdoSettlerIdentity);
                if (data != null)
                {
                    // Unreadable data (newer mod version, corruption) is never overwritten by a new roll.
                    Identity = SettlerIdentity.Deserialize(data);
                    if (Identity != null)
                    {
                        Apply(firstTime: false);
                    }
                    yield break;
                }
                if (_nview.IsOwner())
                {
                    Roll();
                    Apply(firstTime: true);
                    yield break;
                }
                yield return new WaitForSeconds(IdentityRetrySeconds);
            }
        }

        private void Roll()
        {
            ZDO zdo = _nview.GetZDO();
            Heightmap.Biome origin = WorldGenerator.instance != null
                ? WorldGenerator.instance.GetBiome(transform.position)
                : Heightmap.Biome.Meadows;
            Identity = SettlerRoller.Roll(new System.Random(zdo.m_uid.GetHashCode()), origin);

            zdo.Set(Keys.ZdoSettlerIdentity, Identity.Serialize());
            zdo.Set(ZDOVars.s_overrideHoverName, Identity.Name);
            if (Identity.Female)
            {
                // Otherwise VisEquipment's NPC logic may give a random beard to a model without one.
                zdo.Set(ZDOVars.s_noBeard, true);
            }
            Log.Info(Module, $"New settler {Identity.Name} ({(Identity.Female ? "female" : "male")}, {origin}), traits: {string.Join(", ", Identity.Traits)}");
        }

        // Owner only. New settlers get their id with the identity; settlers from older versions on their next load.
        private void EnsureUid()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetLong(Keys.ZdoSettlerUid) == 0L)
            {
                zdo.Set(Keys.ZdoSettlerUid, Keys.NewId());
            }
        }

        /// <summary>Localization token of a trait in the settler's grammatical gender (Polish adjectives differ).</summary>
        internal static string TraitToken(string traitId, bool female) => "$aoj_trait_" + traitId + (female ? "_f" : "");

        private void Apply(bool firstTime)
        {
            _traitsText = string.Join(", ", Identity.Traits.Select(t => TraitToken(t, Identity.Female)));
            // Every client keeps Humanoid's own copies in sync: if this client becomes the owner later, its next
            // equipment update then re-sends the right hair and beard instead of clearing them.
            _humanoid.m_hairItem = Identity.Hair;
            _humanoid.m_beardItem = Identity.Beard;
            // Vanilla BaseAI regeneration (it also catches up on world time spent unloaded) at the configured pace.
            _humanoid.m_regenAllHPTime = AoJConfig.SettlerFullHealSeconds.Value;
            if (_ai != null)
            {
                // AI only runs on the owner, but ownership moves between players, so every client sets the same value.
                _ai.m_fleeIfLowHealth = Mathf.Clamp(AoJConfig.SettlerFleeThreshold.Value + TraitSum(TraitStat.FleeThreshold), 0f, MaxFleeThreshold);
            }
            // Movement runs on the owner too, and ownership moves: every client sets the same speeds (from the speeds
            // left after UndoEnemyScaling, so a second Apply never stacks them).
            if (_baseWalkSpeed < 0f)
            {
                _baseWalkSpeed = _humanoid.m_walkSpeed;
                _baseRunSpeed = _humanoid.m_runSpeed;
            }
            float speed = Mathf.Max(0.5f, 1f + TraitSum(TraitStat.MoveSpeed));
            _humanoid.m_walkSpeed = _baseWalkSpeed * speed;
            _humanoid.m_runSpeed = _baseRunSpeed * speed;

            if (!_nview.IsOwner())
            {
                return;
            }
            EnsureUid();
            if (firstTime)
            {
                // Veterans start with combat experience.
                float start = Mathf.Clamp(TraitSum(TraitStat.CombatStart), 0f, 100f);
                if (start > CombatSkill.Level(_nview.GetZDO()))
                {
                    _nview.GetZDO().Set(CombatSkill.SkillKey, start);
                }
            }

            // Tamed = the game's own notion of "on the players' side": no fleeing from no-monster areas such as the
            // start temple, no striking back at players, no hitting allies with area attacks. Non-owners read it
            // from the ZDO every second.
            if (!_humanoid.IsTamed())
            {
                _humanoid.SetTamed(true);
            }
            AnchorIfIdle();

            if (_visEquipment != null)
            {
                _visEquipment.SetModel(Identity.Female ? 1 : 0);
                _visEquipment.SetSkinColor(Identity.SkinColor);
                _visEquipment.SetHairColor(Identity.HairColor);
                // Humanoid only forwards hair and beard to VisEquipment for players, so NPCs set them directly.
                _visEquipment.SetHairItem(ItemHash(Identity.Hair));
                _visEquipment.SetBeardItem(ItemHash(Identity.Beard));
            }

            float maxHealth = AoJConfig.SettlerBaseHealth.Value * (1f + TraitSum(TraitStat.MaxHealth));
            _humanoid.SetMaxHealth(maxHealth);
            if (firstTime)
            {
                _humanoid.SetHealth(maxHealth);
            }
        }

        private static int ItemHash(string itemName) => string.IsNullOrEmpty(itemName) ? 0 : itemName.GetStableHashCode();

        private float _baseWalkSpeed = -1f;
        private float _baseRunSpeed = -1f;

        /// <summary>The sum of the settler's traits for one stat (<see cref="TraitStat"/>); 0 before its identity is known.</summary>
        internal float TraitSum(string stat)
        {
            if (Identity == null)
            {
                return 0f;
            }
            float sum = 0f;
            foreach (string id in Identity.Traits)
            {
                // Traits removed from the definitions since the roll simply stop having an effect.
                TraitDef trait = DefsRegistry.Current.Traits.Find(t => t.Id == id);
                if (trait != null && trait.Modifiers.TryGetValue(stat, out float value))
                {
                    sum += value;
                }
            }
            return sum;
        }

        // ---------------------------------------------------------------- per-second bookkeeping

        // Once per second on every client; only flag checks and ZDO reads, no searches.
        private void Tick()
        {
            long perf = Perf.Start();
            TickInner();
            Perf.Stop(Perf.Section.SettlerTick, perf);
        }

        private void TickInner()
        {
            if (!_nview.IsValid())
            {
                return;
            }

            // AI runs on the owner, but ownership moves, so every client keeps the same wander range.
            if (_ai != null)
            {
                _ai.m_randomMoveRange = _nview.GetZDO().GetBool(Keys.ZdoSettlerHold) ? HoldRange : AoJConfig.SettlerWanderRange.Value;
            }
            UpdatePose();

            bool owner = _nview.IsOwner();
            if (_humanoid is SettlerCharacter body)
            {
                // Monster AIs on any machine check it; the owner alone lets the settler get up (SettlerCharacter).
                if (SettlerCharacter.IsDownIn(_nview.GetZDO()))
                {
                    body.Down = true;
                }
                else if (!owner)
                {
                    body.Down = false;
                }
            }
            if (Identity != null)
            {
                _voice = _voice ?? new SettlerVoice(this);
                _voice.Tick(_nview.GetZDO(), _humanoid is SettlerCharacter knocked && knocked.Down);
            }
            if (owner && !_wasOwner)
            {
                OnBecameOwner();
            }
            _wasOwner = owner;
            if (!owner)
            {
                return;
            }

            if (_inventoryDirty)
            {
                SaveInventory();
            }
            RefreshFollowTarget();
            UpdateHome();
            if (_humanoid is SettlerCharacter sleeper)
            {
                sleeper.BedHealing = JarlTable.HasUnlock(HomeTable?.Data, JarlTable.UnlockFastHealing) ? FastHealing : 1f;
            }
            if (Identity != null && !IsCaptive)
            {
                Needs.Update(_nview.GetZDO(), this, HomeTable);
            }
            // aoj_trace: what it is doing, every few seconds, between the decisions that are traced as they happen.
            if (AI.AiTrace.On && Time.time >= _nextSnapshot)
            {
                _nextSnapshot = Time.time + SnapshotSeconds;
                AI.AiTrace.Write(this, "now: " + DebugText().Replace('\n', '|'));
            }
        }

        private const float SnapshotSeconds = 10f;
        private float _nextSnapshot;
        private SettlerVoice _voice;

        /// <summary>Healing in bed in a settlement that unlocked healers (tiers.json "fast_healing").</summary>
        private const float FastHealing = 2f;

        private const string SitAnimation = "emote_sit";
        private bool _sitting;

        // The sitting pose is the player's looping sit emote (an animator bool): captives sit tied up, settlers sit down
        // when idle at home. The animator is not synced for this, so every client sets it from the ZDO.
        private void UpdatePose()
        {
            bool sit = IsCaptive || _nview.GetZDO().GetBool(Keys.ZdoSettlerSitting);
            if (sit == _sitting || _humanoid == null || _humanoid.m_animator == null)
            {
                return;
            }
            _sitting = sit;
            _humanoid.m_animator.SetBool(SitAnimation, sit);
        }

        /// <summary>Owner only: sit down or stand up (idle at home).</summary>
        internal void SetSitting(bool sitting)
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && _nview.GetZDO().GetBool(Keys.ZdoSettlerSitting) != sitting)
            {
                _nview.GetZDO().Set(Keys.ZdoSettlerSitting, sitting);
                UpdatePose();
            }
        }

        // ---------------------------------------------------------------- settlement membership

        // The roster on the Jarl's Table is the truth; the settler's own ZDO follows it. Adopting the home here
        // instead of by RPC keeps working when the settler changes owner or was unloaded when it was accepted.
        private void UpdateHome()
        {
            ZDO zdo = _nview.GetZDO();
            long uid = zdo.GetLong(Keys.ZdoSettlerUid);
            long home = zdo.GetLong(Keys.ZdoSettlerHomeId);
            if (uid == 0L)
            {
                return;
            }
            if (home == 0L)
            {
                AdoptListedHome(zdo, uid);
                return;
            }

            JarlTable table = JarlTable.FindById(home);
            if (table != null)
            {
                _homeMissingTicks = 0;
                SettlementData data = table.Data;
                if (data != null && !data.HasSettler(uid))
                {
                    ClearHome(zdo, "no longer on the roster");
                    return;
                }
                DropStaleRosters(uid, home);
                UpdateHomeAnchor(zdo, table);
                return;
            }

            // Its table is not here, but a table that is lists it: it was accepted there while its old home was
            // gone (destroyed far away, where nobody could see it go). The roster is the truth.
            if (AdoptListedHome(zdo, uid))
            {
                return;
            }

            // A missing table only counts where it would have to be loaded, and only once the player has been in the
            // world for a while: zones and their objects load over many frames after a world load or a teleport.
            Vector3 tablePosition = zdo.GetVec3(Keys.ZdoSettlerHomePosition, transform.position);
            bool shouldBeLoaded = Utils.DistanceXZ(tablePosition, ZNet.instance.GetReferencePosition()) <= LoadedTableRange &&
                                  Player.m_localPlayer != null && !Player.m_localPlayer.IsTeleporting();
            _homeMissingTicks = shouldBeLoaded ? _homeMissingTicks + 1 : 0;
            if (_homeMissingTicks >= MissingTableTicks)
            {
                ClearHome(zdo, "its Jarl's Table is gone");
            }
        }

        private bool AdoptListedHome(ZDO zdo, long uid)
        {
            foreach (JarlTable table in JarlTable.Loaded)
            {
                SettlementData data = table.Data;
                long settlementId = table.SettlementId;
                if (data == null || settlementId == 0L || !data.HasSettler(uid))
                {
                    continue;
                }

                zdo.Set(Keys.ZdoSettlerHomeId, settlementId);
                zdo.Set(Keys.ZdoSettlerHomePosition, table.transform.position);
                zdo.Set(Keys.ZdoSettlerFollow, 0L);
                // At home a settler strolls around; holding still is only for an explicit "wait here".
                zdo.Set(Keys.ZdoSettlerHold, false);
                if (_ai != null)
                {
                    _ai.SetFollowTarget(null);
                }
                UpdateHomeAnchor(zdo, table);
                Log.Info(Module, $"{DisplayName} settled in {JarlTable.DisplayName(data)}");
                return true;
            }
            return false;
        }

        // A loaded table other than its home that still lists it (its old home, before it was re-homed elsewhere)
        // is told by its owner to drop it, so that roster stops counting a settler it no longer has. Asked every
        // few seconds, not every tick: the table's owner answers over the network.
        private void DropStaleRosters(long uid, long home)
        {
            if (Time.time < _nextStaleRosterCheck)
            {
                return;
            }
            _nextStaleRosterCheck = Time.time + StaleRosterSeconds;
            foreach (JarlTable table in JarlTable.Loaded)
            {
                SettlementData data = table.Data;
                if (data != null && table.SettlementId != 0L && table.SettlementId != home && data.HasSettler(uid))
                {
                    JarlTable.RequestRemoveSettler(table.SettlementId, uid);
                }
            }
        }

        /// <summary>
        /// Where a settler without orders spends its time: at the Jarl's Table, the settlement's hall, by day; at its
        /// bed by night (at the table without one). Night is world time, the same on every machine.
        /// </summary>
        internal Vector3 HomeAnchor(JarlTable table)
        {
            bool night = EnvMan.IsNight();
            // Soldiers stand at their post by day, and at any hour during an alarm.
            Army.WarBanner post = Role != Army.CombatRole.None ? Army.WarBanner.FindById(PostId) : null;
            if (post != null && (!night || table.AlarmOn) && (JobId == 0L || table.AlarmOn))
            {
                return post.transform.position;
            }
            return night && TryGetBed(table, out Vector3 bed) ? bed : table.transform.position + IdleSpot();
        }

        /// <summary>Civilians idle in a ring around the table, each at a spot of its own: not all on top of it, and of the player using it.</summary>
        private const float IdleRingRadius = 3.5f;

        private Vector3 IdleSpot()
        {
            long uid = Uid;
            if (uid == 0L)
            {
                return Vector3.zero;
            }
            // Spread by the golden angle: settlers with any ids end up around the whole ring, not bunched on one side.
            float degrees = (uid % 360L + 360L) % 360L * 137.5f % 360f;
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * IdleRingRadius;
        }

        // Idle movement strolls around the patrol point and walks back to it, so moving that point is what sends a
        // settler home (farther trips, through doors, are HomeRoutine's).
        private void UpdateHomeAnchor(ZDO zdo, JarlTable table)
        {
            if (_ai == null || zdo.GetLong(Keys.ZdoSettlerFollow) != 0L || zdo.GetBool(Keys.ZdoSettlerHold))
            {
                return;
            }
            Vector3 anchor = HomeAnchor(table);
            if (!_ai.GetPatrolPoint(out Vector3 current) || Utils.DistanceXZ(current, anchor) > 1f)
            {
                _ai.SetPatrolPoint(anchor);
            }
        }

        private void ClearHome(ZDO zdo, string reason)
        {
            zdo.Set(Keys.ZdoSettlerHomeId, 0L);
            _homeMissingTicks = 0;
            Log.Info(Module, $"{DisplayName} is homeless: {reason}");
        }

        private void OnBecameOwner()
        {
            if (Identity != null)
            {
                EnsureUid();
            }
            // Its leader at once, not at the next tick: a follower that changed hands (or was just told to follow)
            // sets off within the frame instead of standing for up to a second.
            RefreshFollowTarget();
            ZDO zdo = _nview.GetZDO();
            byte[] data = zdo.GetByteArray(Keys.ZdoSettlerInventory);
            if (data == null)
            {
                // Items handed over before the first tick (e.g. aoj_spawn with a weapon) are not in the ZDO yet.
                if (_humanoid.GetInventory().GetAllItems().Count > 0)
                {
                    SaveInventory();
                }
                return;
            }

            int revision = zdo.GetInt(Keys.ZdoSettlerInventoryRevision);
            if (revision != _loadedInventoryRevision)
            {
                LoadInventory(data, revision);
            }
        }

        // ---------------------------------------------------------------- inventory persistence

        private void OnInventoryChanged()
        {
            // Batched: Tick writes the ZDO at most once per second however many changes happen in between.
            if (!_loadingInventory && _nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _inventoryDirty = true;
            }
        }

        private void LoadInventory(byte[] data, int revision)
        {
            _loadingInventory = true;
            try
            {
                _humanoid.UnequipAllItems();
                Inventory inventory = _humanoid.GetInventory();
                inventory.Load(new ZPackage(data));
                foreach (ItemDrop.ItemData item in inventory.GetAllItems().Where(i => i.m_equipped).ToList())
                {
                    item.m_equipped = false;
                    _humanoid.EquipItem(item, triggerEquipEffects: false);
                }
                _loadedInventoryRevision = revision;
                _inventoryDirty = false;
                _inventoryUnreadable = false;
            }
            catch (Exception e)
            {
                // Never save over data we could not read: another client or a newer mod version may still read it.
                _inventoryUnreadable = true;
                Log.Error(Module, $"Cannot load the inventory of {Identity?.Name ?? "a settler"}, saving disabled for it: {e.Message}");
            }
            finally
            {
                _loadingInventory = false;
            }
        }

        /// <summary>
        /// Owner only: saves a pending inventory change now instead of at the next tick. Used right after items moved
        /// between the settler and the world, so an ownership change in between cannot duplicate or lose them.
        /// </summary>
        internal void FlushInventory()
        {
            if (_inventoryDirty && _nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                SaveInventory();
            }
        }

        private void SaveInventory()
        {
            _inventoryDirty = false;
            if (_inventoryUnreadable)
            {
                return;
            }

            long perf = Perf.Start();
            var package = new ZPackage();
            _humanoid.GetInventory().Save(package);
            ZDO zdo = _nview.GetZDO();
            int revision = zdo.GetInt(Keys.ZdoSettlerInventoryRevision) + 1;
            zdo.Set(Keys.ZdoSettlerInventory, package.GetArray());
            zdo.Set(Keys.ZdoSettlerInventoryRevision, revision);
            _loadedInventoryRevision = revision;
            Perf.Stop(Perf.Section.InventorySaves, perf);
            Log.Debug(Module, $"Saved the inventory of {Identity?.Name} (revision {revision})");
        }

        // ---------------------------------------------------------------- orders

        // [E] opens the order window, [Shift+E] toggles following without it.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Identity == null || !(user is Player player) || _nview == null || !_nview.IsValid())
            {
                return false;
            }

            if (IsCaptive)
            {
                int guards = GuardsNearby();
                if (!CanBeFreed)
                {
                    player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_guards_alive", guards.ToString()));
                }
                else
                {
                    RequestFree(player);
                    player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_freed", DisplayName));
                }
                return true;
            }

            if (alt)
            {
                ToggleFollow(player);
            }
            else
            {
                UI.SettlerWindow.Open(this);
            }
            return true;
        }

        internal bool IsFollowing(Player player) => player != null && FollowedPlayerId == player.GetPlayerID();

        internal void ToggleFollow(Player player)
        {
            bool follow = !IsFollowing(player);
            // The RPC carries the wanted state, not a toggle, so double clicks and lag cannot flip it back.
            _nview.InvokeRPC(Keys.RpcSettlerCommand, follow ? player.GetPlayerID() : 0L);
            player.Message(MessageHud.MessageType.Center, Localize(follow ? "$aoj_msg_follow" : "$aoj_msg_wait", DisplayName));
        }

        /// <summary>
        /// Everything out of the settler's bag (and off its body) into the player's: the settler's owner takes it all
        /// out and sends it to this player's machine (<see cref="Net.ItemDelivery"/>); what does not fit lands at the
        /// player's feet.
        /// </summary>
        internal void RequestTakeBack(Player player)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }
            if (!IsFollowing(player) && !MayBeOrderedBy(player, SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }
            ClaimIfOwnerless();
            _nview.InvokeRPC(Keys.RpcSettlerTakeBack, Request(ZDOMan.GetSessionID(), new ZPackage()));
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_takeback", DisplayName));
        }

        /// <summary>
        /// One thing out of the settler's bag (or off its body) into the player's: the settler's owner takes it out and
        /// sends it to this player's machine (<see cref="Net.ItemDelivery"/>). Whoever may take everything back may
        /// take one thing.
        /// </summary>
        internal void RequestTakeItem(Player player, ItemDrop.ItemData item, int amount)
        {
            if (item == null || _nview == null || !_nview.IsValid())
            {
                return;
            }
            if (!IsFollowing(player) && !MayBeOrderedBy(player, SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }
            var body = new ZPackage();
            body.Write(item.m_shared.m_name);
            body.Write(item.m_quality);
            body.Write(item.m_equipped);
            body.Write(Mathf.Clamp(amount, 1, Mathf.Max(1, item.m_stack)));
            ClaimIfOwnerless();
            _nview.InvokeRPC(Keys.RpcSettlerTakeItem, Request(ZDOMan.GetSessionID(), body));
        }

        // ---------------------------------------------------------------- requests that answer the asking peer

        /// <summary>
        /// A request whose answer (items, a message) goes back to the one who asked: its peer id in front of the body.
        /// The owner takes the asker's identity from the network, not from this field (<see cref="Requester"/>); the
        /// field matters when the request is passed on, because the one passing it on becomes the network sender.
        /// </summary>
        private static ZPackage Request(long requester, ZPackage body)
        {
            var package = new ZPackage();
            package.Write(requester);
            package.Write(body);
            return package;
        }

        /// <summary>
        /// Who asked: the network sender, unless the request names the server or came through it. The server relays
        /// a request when the machine it was aimed at lost ownership meanwhile, and it writes the asker it got the
        /// request from; nobody else is believed about somebody else asking. Naming the server gains nothing either
        /// way: the answer goes to the host's player, who could have asked.
        /// </summary>
        private static long Requester(long sender, long claimed)
        {
            long server = Net.Peers.ServerPeerId();
            return claimed != 0L && server != 0L && (sender == server || claimed == server) ? claimed : sender;
        }

        /// <summary>
        /// Owner-only requests: true when this machine owns the settler. One nobody owns (its owner just left the
        /// area) is claimed, the way the game claims a chest nobody simulates, instead of being asked by a broadcast
        /// that every machine would pass on again. Otherwise the request goes on to the owner.
        /// </summary>
        private bool OwnerHandles(string method, params object[] args)
        {
            if (_nview.IsOwner())
            {
                return true;
            }
            if (!_nview.HasOwner())
            {
                // Passing it on to owner 0 would be a broadcast the game also handles on this machine at once: this
                // method again, without end (0.6.0 crashed the host that way), so the settler is taken instead.
                Claim();
                return true;
            }
            // Two machines that each believe the other owns the settler would pass a request back and forth forever.
            if (Time.time > _forwardWindowEnd)
            {
                _forwardWindowEnd = Time.time + 1f;
                _forwardsInWindow = 0;
            }
            if (++_forwardsInWindow > MaxForwardsPerSecond)
            {
                if (_forwardsInWindow == MaxForwardsPerSecond + 1)
                {
                    Log.Warning(Module, $"{DisplayName}: {method} passed on to peer {_nview.GetZDO().GetOwner()} too often, dropped until the owner settles");
                }
                return false;
            }
            _nview.InvokeRPC(method, args);
            return false;
        }

        private const int MaxForwardsPerSecond = 10;
        private const float StaleRosterSeconds = 10f;
        private float _forwardWindowEnd;
        private int _forwardsInWindow;
        private float _nextStaleRosterCheck;

        /// <summary>Before asking the owner: a settler nobody owns would be asked by a broadcast, so this machine takes it.</summary>
        private void ClaimIfOwnerless()
        {
            if (!_nview.HasOwner())
            {
                Claim();
            }
        }

        private void Claim()
        {
            _nview.ClaimOwnership();
            // Now, not at the next tick: whatever runs next reads the bag, which non-owners do not keep up to date.
            OnBecameOwner();
            _wasOwner = true;
        }

        /// <summary>A message on the asking player's screen, on this machine or over the network.</summary>
        private void Notify(long peer, string token, string arg = "")
        {
            if (peer == ZDOMan.GetSessionID())
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localize(token, arg));
            }
            else if (_nview != null && _nview.IsValid())
            {
                _nview.InvokeRPC(peer, Keys.RpcSettlerNotify, token, arg);
            }
        }

        private void RPC_Notify(long sender, string token, string arg)
        {
            // Network boundary: only the mod's own texts, and an argument that is one of them or plain text.
            if (!IsToken(token) || Player.m_localPlayer == null)
            {
                return;
            }
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localize(token, IsToken(arg) ? arg : TextUtil.SanitizeName(arg, 40)));
        }

        private static bool IsToken(string text) =>
            text != null && text.Length > 5 && text.Length <= 48 && text.StartsWith("$aoj_", StringComparison.Ordinal) &&
            text.All(c => c == '$' || c == '_' || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));

        /// <summary>Owner: the parcel to the asking peer (<see cref="Net.ItemDelivery"/>).</summary>
        internal void SendParcel(long peer, long id, ZPackage package) => _nview.InvokeRPC(peer, Keys.RpcSettlerDeliver, id, package);

        private void RPC_Deliver(long sender, long id, ZPackage package)
        {
            if (Net.ItemDelivery.Receive(package))
            {
                _nview.InvokeRPC(sender, Keys.RpcSettlerDelivered, id);
            }
        }

        private void RPC_Delivered(long sender, long id) => Net.ItemDelivery.Acknowledged(id, sender);

        /// <summary>Its leader took a portal: come along, to a spot next to where the leader will arrive (any machine).</summary>
        internal void RequestTeleport(Vector3 arrival)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }
            ClaimIfOwnerless();
            if (_nview.IsOwner())
            {
                FollowLeader(arrival);
                return;
            }
            var body = new ZPackage();
            body.Write(arrival);
            _nview.InvokeRPC(Keys.RpcSettlerTeleport, Request(ZDOMan.GetSessionID(), body));
        }

        private void RPC_Teleport(long sender, ZPackage package)
        {
            long claimed;
            ZPackage body;
            Vector3 arrival;
            try
            {
                claimed = package.ReadLong();
                body = package.ReadPackage();
                arrival = body.ReadVector3();
                body.SetPos(0);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed portal request from peer {sender}: {e.Message}");
                return;
            }
            long requester = Requester(sender, claimed);
            if (!OwnerHandles(Keys.RpcSettlerTeleport, Request(requester, body)))
            {
                return;
            }
            // Only the leader's own machine asks (Settlers.PortalFollow), for the settlers that follow that player.
            Player leader = Net.Peers.FindPlayer(requester);
            if (leader == null || !IsFollowing(leader))
            {
                Log.Warning(Module, $"Portal jump of {DisplayName} asked by peer {requester}, who it does not follow, refused");
                return;
            }
            FollowLeader(arrival);
        }

        private void FollowLeader(Vector3 arrival)
        {
            Teleport(arrival);
            Log.Info(Module, $"{DisplayName} followed its leader through a portal");
        }

        /// <summary>Owner: through a portal on its own (AI.PortalTravel), landing where a player would.</summary>
        internal void JumpTo(Vector3 exit, string via)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }
            Teleport(exit, scatter: false);
            Log.Info(Module, $"{DisplayName} went through {via}");
        }

        // Owner: like the player's own jump, the body is set down at the spot; the ZDO carries the new position to
        // everybody, and this machine drops the instance until the zone there is loaded, if it is not already.
        private void Teleport(Vector3 arrival, bool scatter = true)
        {
            Vector3 spot = scatter ? arrival + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * 2f : arrival;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(spot, out float ground))
            {
                spot.y = Mathf.Max(spot.y, ground + 0.3f);
            }
            FlushInventory();
            _ai?.StopMoving();
            transform.position = spot;
            if (_humanoid.m_body != null)
            {
                _humanoid.m_body.position = spot;
                _humanoid.m_body.linearVelocity = Vector3.zero;
            }
            // As the player's own jump does: the fall-damage mark starts here, not where the settler stood.
            _humanoid.m_maxAirAltitude = spot.y;
            _nview.GetZDO().SetPosition(spot);
        }

        /// <summary>Owner: a parcel nobody took comes back into the bag. False when this machine cannot (not the owner any more).</summary>
        internal bool TakeBackParcel(Inventory parcel)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return false;
            }
            Inventory inventory = _humanoid.GetInventory();
            foreach (ItemDrop.ItemData item in parcel.GetAllItems().ToList())
            {
                if (!inventory.AddItem(item))
                {
                    ItemDrop.DropItem(item, item.m_stack, DropPoint(), transform.rotation);
                }
            }
            MarkInventoryDirty();
            FlushInventory();
            return true;
        }

        private void RPC_TakeItem(long sender, ZPackage package)
        {
            long claimed;
            ZPackage body;
            try
            {
                claimed = package.ReadLong();
                body = package.ReadPackage();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed take-item request from peer {sender}: {e.Message}");
                return;
            }
            long requester = Requester(sender, claimed);
            if (!OwnerHandles(Keys.RpcSettlerTakeItem, Request(requester, body)))
            {
                return;
            }
            if (!IsLeader(requester) && !MayOrder(requester, SettlementRight.Live))
            {
                Log.Warning(Module, $"Handing an item of {DisplayName} to peer {requester} refused");
                Notify(requester, "$aoj_msg_needs_rank", Permissions.Token(Permissions.Required(SettlementRight.Live)));
                return;
            }
            string name;
            int quality;
            bool equipped;
            int amount;
            try
            {
                name = body.ReadString();
                quality = body.ReadInt();
                equipped = body.ReadBool();
                amount = body.ReadInt();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed take-item request from peer {sender}: {e.Message}");
                return;
            }

            Inventory inventory = _humanoid.GetInventory();
            // The very stack the player clicked when it is still there, otherwise the same item from another stack.
            ItemDrop.ItemData item = inventory.GetAllItems().Find(i => i.m_shared.m_name == name && i.m_quality == quality && i.m_equipped == equipped)
                                     ?? inventory.GetAllItems().Find(i => i.m_shared.m_name == name && i.m_quality == quality);
            if (item == null)
            {
                // Taken by somebody else, or eaten, since that window was drawn.
                Notify(requester, "$aoj_msg_item_gone", DisplayName);
                return;
            }
            if (item.m_equipped)
            {
                _humanoid.UnequipItem(item);
            }
            amount = Mathf.Clamp(amount, 1, item.m_stack);
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = amount;
            copy.m_equipped = false;
            var parcel = new Inventory("aoj_take", null, 1, 1);
            if (!parcel.AddItem(copy))
            {
                return;
            }
            inventory.RemoveItem(item, amount);
            MarkInventoryDirty();
            FlushInventory();
            Net.ItemDelivery.Send(this, requester, parcel);
            Log.Info(Module, $"{DisplayName} handed {amount}x {name} to peer {requester}");
        }

        internal void RequestGoHome(Player player)
        {
            if (!HasHome)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_no_home", DisplayName));
                return;
            }
            if (!IsFollowing(player) && !MayBeOrderedBy(player, SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }
            _nview.InvokeRPC(Keys.RpcSettlerGoHome);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_going_home", DisplayName));
        }

        internal void RequestRename(string name) => _nview.InvokeRPC(Keys.RpcSettlerRename, name ?? "");

        // Quick commands (the command wheel): plain requests, the caller reports the result once for the whole squad.

        internal void SendFollow(Player player) => _nview.InvokeRPC(Keys.RpcSettlerCommand, player.GetPlayerID());

        internal void SendWait() => _nview.InvokeRPC(Keys.RpcSettlerCommand, 0L);

        internal void SendGoHome() => _nview.InvokeRPC(Keys.RpcSettlerGoHome);

        /// <summary>The target by its ZDOID: fine for a request that lives a few seconds (ZDOIDs only change on a world load).</summary>
        internal void SendAttack(Character target) => _nview.InvokeRPC(Keys.RpcSettlerAttack, target.GetZDOID());

        internal void SendFallBack(Player player) => _nview.InvokeRPC(Keys.RpcSettlerFallBack, player.GetPlayerID());

        private void RPC_Attack(long sender, ZDOID targetId)
        {
            if (!OwnerHandles(Keys.RpcSettlerAttack, targetId))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }
            // Only its leader, or an officer of its settlement, sends a settler into a fight.
            Player commander = Net.Peers.FindPlayer(sender);
            bool allowed = commander != null && (FollowedPlayerId == commander.GetPlayerID() || MayOrder(sender, SettlementRight.Military));
            GameObject targetObject = allowed ? ZNetScene.instance.FindInstance(targetId) : null;
            Character target = targetObject != null ? targetObject.GetComponent<Character>() : null;
            if (target != null && !target.IsDead() && _ai is AI.SettlerAI ai)
            {
                ai.OrderAttack(target);
            }
        }

        private void RPC_FallBack(long sender, long playerId)
        {
            if (!OwnerHandles(Keys.RpcSettlerFallBack, playerId))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }
            Player player = Player.GetPlayer(playerId);
            if (player == null || _ai == null)
            {
                return;
            }
            // Same as "follow me", plus breaking off the fight for a moment so the retreat actually happens.
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerFollow, playerId);
            zdo.Set(Keys.ZdoSettlerHold, false);
            _ai.ResetPatrolPoint();
            _ai.SetFollowTarget(player.gameObject);
            if (_ai is AI.SettlerAI ai)
            {
                ai.OrderCeaseFire();
            }
        }

        // A homed settler takes orders that change its life only from members of its settlement with the rank for them
        // (Settlement.Permissions); a homeless one from anybody. The sender comes from the network, never from the package.
        private bool MayOrder(long sender, SettlementRight right)
        {
            long home = HomeId;
            return home == 0L || JarlTable.RoleIn(home, Net.Peers.FindPlayer(sender)).Allows(right);
        }

        // Whoever it follows may also send it home or have it hand its things back - far from its table too.
        private bool IsLeader(long sender)
        {
            Player player = Net.Peers.FindPlayer(sender);
            return player != null && FollowedPlayerId == player.GetPlayerID();
        }

        /// <summary>
        /// The same check on this machine, for buttons and the command wheel. With its settlement not loaded here it
        /// cannot be told: the answer is yes and the owner decides.
        /// </summary>
        internal bool MayBeOrderedBy(Player player, SettlementRight right)
        {
            long home = HomeId;
            return player != null && (home == 0L || JarlTable.FindById(home) == null || JarlTable.RoleIn(home, player).Allows(right));
        }

        private void RPC_GoHome(long sender)
        {
            if (!OwnerHandles(Keys.RpcSettlerGoHome))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }

            ZDO zdo = _nview.GetZDO();
            if (_ai == null || zdo.GetLong(Keys.ZdoSettlerHomeId) == 0L || !(IsLeader(sender) || MayOrder(sender, SettlementRight.Live)))
            {
                Log.Warning(Module, $"'Go home' for {DisplayName} from peer {sender} refused");
                return;
            }

            zdo.Set(Keys.ZdoSettlerFollow, 0L);
            zdo.Set(Keys.ZdoSettlerHold, false);
            _ai.SetFollowTarget(null);
            JarlTable table = HomeTable;
            if (table != null)
            {
                UpdateHomeAnchor(zdo, table);
            }
            else
            {
                // Table not loaded here: head for it, the bed takes over once it is in range.
                _ai.SetPatrolPoint(zdo.GetVec3(Keys.ZdoSettlerHomePosition, transform.position));
            }
        }

        private void RPC_Rename(long sender, string name)
        {
            if (!OwnerHandles(Keys.RpcSettlerRename, name))
            {
                return;
            }

            string clean = TextUtil.SanitizeName(name, MaxNameLength);
            if (Identity == null || clean.Length == 0 || !MayOrder(sender, SettlementRight.Live))
            {
                Log.Warning(Module, $"Renaming {DisplayName} from peer {sender} refused");
                return;
            }

            string old = Identity.Name;
            Identity.Name = clean;
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerIdentity, Identity.Serialize());
            zdo.Set(ZDOVars.s_overrideHoverName, clean);
            Log.Info(Module, $"{old} is now called {clean}");
        }

        private void RPC_Command(long sender, long followPlayerId)
        {
            // Ownership may have moved while the RPC travelled: pass it on to the current owner.
            if (!OwnerHandles(Keys.RpcSettlerCommand, followPlayerId))
            {
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }

            Player player = followPlayerId != 0L ? Player.GetPlayer(followPlayerId) : null;
            if (_ai == null || (followPlayerId != 0L && player == null))
            {
                return;
            }

            _nview.GetZDO().Set(Keys.ZdoSettlerFollow, followPlayerId);
            _nview.GetZDO().Set(Keys.ZdoSettlerHold, player == null);
            if (player != null)
            {
                _ai.ResetPatrolPoint();
                _ai.SetFollowTarget(player.gameObject);
            }
            else
            {
                _ai.SetFollowTarget(null);
                _ai.SetPatrolPoint();
            }
        }

        // After a reload or an ownership change the AI has no follow target yet; the ZDO still knows the player.
        private void RefreshFollowTarget()
        {
            long playerId = _nview.GetZDO().GetLong(Keys.ZdoSettlerFollow);
            if (playerId == 0L || _ai == null || _ai.GetFollowTarget() != null)
            {
                return;
            }

            Player player = Player.GetPlayer(playerId);
            if (player != null)
            {
                _ai.SetFollowTarget(player.gameObject);
            }
            else
            {
                // The followed player is away or logged out: wait here until they return.
                AnchorIfIdle();
            }
        }

        // Tamed creatures idle around wherever they currently stand (BaseAI.IdleMovement), so a settler without a
        // patrol point would slowly drift off. Anchoring it where it stands keeps it in place; follow orders still win.
        private void AnchorIfIdle()
        {
            if (_ai != null && _ai.GetFollowTarget() == null && !_nview.GetZDO().GetBool(ZDOVars.s_patrol))
            {
                _ai.SetPatrolPoint();
            }
        }

        // ---------------------------------------------------------------- giving and taking items

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (Identity == null || item == null || !(user is Player player) || _nview == null || !_nview.IsValid())
            {
                return false;
            }

            string itemName = Localize(item.m_shared.m_name);
            if (IsCaptive)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_refuse", Identity.Name, itemName));
                return true;
            }
            // Food given by hand is eaten on the spot (followers far from any cauldron need this).
            if (Needs.IsFood(item) && item.m_dropPrefab != null)
            {
                _nview.InvokeRPC(Keys.RpcSettlerFeed, item.m_dropPrefab.name);
                player.GetInventory().RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_fed", Identity.Name, itemName));
                return true;
            }

            ItemDrop.ItemData copy = item.Clone();
            copy.m_equipped = false;
            var parcel = new Inventory("aoj_give", null, 1, 1);
            if (!parcel.AddItem(copy))
            {
                return false;
            }

            var package = new ZPackage();
            parcel.Save(package);
            player.UnequipItem(item);
            player.GetInventory().RemoveItem(item);
            _nview.InvokeRPC(Keys.RpcSettlerGive, package);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_given", Identity.Name, itemName));
            return true;
        }

        /// <summary>Hands an item over through the owner, the way a player's [use item] does (aoj_give).</summary>
        internal bool Give(ItemDrop.ItemData item)
        {
            var parcel = new Inventory("aoj_give", null, 1, 1);
            if (_nview == null || !_nview.IsValid() || !parcel.AddItem(item))
            {
                return false;
            }
            var package = new ZPackage();
            parcel.Save(package);
            _nview.InvokeRPC(Keys.RpcSettlerGive, package);
            return true;
        }

        private void RPC_Give(long sender, ZPackage package)
        {
            if (!OwnerHandles(Keys.RpcSettlerGive, package))
            {
                return;
            }

            var parcel = new Inventory("aoj_give", null, 1, 1);
            try
            {
                parcel.Load(package);
            }
            catch (Exception e)
            {
                Log.Error(Module, $"Malformed item parcel from peer {sender}: {e.Message}");
                return;
            }

            Inventory inventory = _humanoid.GetInventory();
            foreach (ItemDrop.ItemData item in parcel.GetAllItems().ToList())
            {
                if (inventory.AddItem(item))
                {
                    // Weapons, shields and armour go on; tools (hammer, hoe, cultivator) stay in the bag for work.
                    if (item.IsEquipable() && item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Tool && inventory.ContainsItem(item))
                    {
                        _humanoid.EquipItem(item);
                    }
                }
                else
                {
                    // A gift is never lost: what the settler cannot carry lands at its feet.
                    ItemDrop.DropItem(item, item.m_stack, DropPoint(), transform.rotation);
                }
            }
        }

        // The food's stats come from its prefab, so the sender cannot claim more than the item gives.
        private void RPC_Feed(long sender, string prefabName)
        {
            if (!OwnerHandles(Keys.RpcSettlerFeed, prefabName))
            {
                return;
            }
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop food = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (food != null && Needs.IsFood(food.m_itemData))
            {
                Needs.Eat(_nview.GetZDO(), food.m_itemData);
            }
        }

        private void RPC_TakeBack(long sender, ZPackage package)
        {
            long claimed;
            ZPackage body;
            try
            {
                claimed = package.ReadLong();
                body = package.ReadPackage();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Malformed take-back request from peer {sender}: {e.Message}");
                return;
            }
            long requester = Requester(sender, claimed);
            if (!OwnerHandles(Keys.RpcSettlerTakeBack, Request(requester, body)))
            {
                return;
            }
            if (!IsLeader(requester) && !MayOrder(requester, SettlementRight.Live))
            {
                Log.Warning(Module, $"Handing back the items of {DisplayName} to peer {requester} refused");
                Notify(requester, "$aoj_msg_needs_rank", Permissions.Token(Permissions.Required(SettlementRight.Live)));
                return;
            }

            // Into the asking player's bag, like a single item; the whole bag travels as one parcel.
            _humanoid.UnequipAllItems();
            Inventory inventory = _humanoid.GetInventory();
            var parcel = new Inventory("aoj_take", null, inventory.GetWidth(), inventory.GetHeight());
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (!parcel.AddItem(item))
                {
                    ItemDrop.DropItem(item, item.m_stack, DropPoint(), transform.rotation);
                }
            }
            int count = parcel.NrOfItems();
            inventory.RemoveAll();
            MarkInventoryDirty();
            FlushInventory();
            Net.ItemDelivery.Send(this, requester, parcel);
            Log.Info(Module, $"{DisplayName} handed its {count} items to peer {requester}");
        }

        // Character.OnDeath runs on the owner - only with Settlers/PermanentDeath, otherwise a settler is knocked out
        // instead. Its gear waits in a grave where it fell, like a player's, instead of vanishing with it.
        private void OnDeath()
        {
            if (_nview.IsValid() && _nview.IsOwner())
            {
                if (!MoveGearToGrave())
                {
                    DropAllItems();
                }
                _inventoryDirty = false;
                if (HasHome)
                {
                    JarlTable.RequestRemoveSettler(HomeId, Uid);
                }
            }
        }

        private const string GravePrefab = "Player_tombstone";

        // The player's grave, with the settler's name on it and no owner, so any player may open it.
        private bool MoveGearToGrave()
        {
            Inventory inventory = _humanoid.GetInventory();
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(GravePrefab) : null;
            if (prefab == null || inventory.NrOfItems() == 0)
            {
                return false;
            }
            // The grave takes only unequipped items.
            _humanoid.UnequipAllItems();
            GameObject grave = Instantiate(prefab, _humanoid.GetCenterPoint(), transform.rotation);
            Container container = grave.GetComponent<Container>();
            if (container == null)
            {
                return false;
            }
            container.GetInventory().MoveInventoryToGrave(inventory);
            grave.GetComponent<TombStone>()?.Setup(DisplayName, 0L);
            return true;
        }

        private void DropAllItems()
        {
            _humanoid.UnequipAllItems();
            Inventory inventory = _humanoid.GetInventory();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                ItemDrop.DropItem(item, item.m_stack, DropPoint(), transform.rotation);
            }
            inventory.RemoveAll();
        }

        private Vector3 DropPoint() => transform.position + transform.forward * 0.5f + Vector3.up;

        // ---------------------------------------------------------------- hover

        internal string GetHoverText()
        {
            // Destroyed this frame (zone unloaded, removed): the ZDO is gone but the HUD still asks until the frame ends.
            if (_nview == null || !_nview.IsValid())
            {
                return "";
            }
            if (Identity == null)
            {
                return Localize("$aoj_settler");
            }

            if (IsCaptive)
            {
                return Localize($"{DisplayName}\n<color=#b0b0b0>$aoj_captive · {_traitsText}\n$aoj_captive_how</color>\n" +
                                "[<color=yellow><b>$KEY_Use</b></color>] $aoj_free_captive");
            }

            long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            bool following = me != 0L && _nview.IsValid() && _nview.GetZDO().GetLong(Keys.ZdoSettlerFollow) == me;
            ZDO zdo = _nview.GetZDO();
            string doing = IsDown ? "$aoj_down"
                : IsAtHome ? "$aoj_activity_" + ((SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)).ToString().ToLowerInvariant()
                : OrderText(zdo);
            Work.WorkTotem totem = JobTotem;
            string work = totem != null ? Work.JobInfo.Token(totem.Job) + " · " : Role != Army.CombatRole.None ? Army.CombatRoles.Token(Role) + " · " : "";
            return Localize($"{DisplayName}\n<color=#b0b0b0>{_traitsText}\n{HomeText()} · {work}{doing}</color>\n" +
                            "[<color=yellow><b>$KEY_Use</b></color>] $aoj_orders\n" +
                            $"[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] {(following ? "$aoj_stay" : "$aoj_follow")}\n" +
                            UI.CommandWheel.HoverHint +
                            "$aoj_give_hint");
        }

        /// <summary>
        /// Status lines for the order window, as localization tokens. Everything comes from the ZDO, so the player
        /// who does not simulate the settler (and has no copy of its inventory) sees the same as its owner.
        /// </summary>
        internal string BuildDetails()
        {
            ZDO zdo = _nview.GetZDO();
            var text = new StringBuilder();
            text.Append("$aoj_traits: ").Append(_traitsText.Length > 0 ? _traitsText : "-").Append('\n');
            text.Append("$aoj_health: ").Append(Mathf.CeilToInt(_humanoid.GetHealth())).Append('/').Append(Mathf.CeilToInt(_humanoid.GetMaxHealth()));
            if (Identity != null)
            {
                text.Append("  ·  $aoj_origin: $biome_").Append(Identity.Origin.ToString().ToLowerInvariant());
            }
            text.Append('\n');
            text.Append("$aoj_settlement: ").Append(HomeText()).Append('\n');
            text.Append("$aoj_order: ").Append(OrderText(zdo)).Append('\n');
            Work.WorkTotem totem = JobTotem;
            string job = JobId == 0L ? "-" : totem != null ? Work.JobInfo.Token(totem.Job) : "$aoj_job_far";
            text.Append("$aoj_job: ").Append(job);
            if (Role != Army.CombatRole.None)
            {
                text.Append("  ·  $aoj_role: ").Append(Army.CombatRoles.Token(Role));
            }
            string problem = JobProblem;
            if (problem.Length > 0)
            {
                text.Append("  <color=#ff9060>(").Append(problem).Append(")</color>");
            }
            text.Append('\n').Append(Needs.Describe(zdo)).Append('\n');
            if (HasHome)
            {
                JarlTable table = HomeTable;
                string bed = table == null ? "?" : TryGetBed(table, out _) ? "$aoj_bed_own" : "$aoj_bed_none";
                text.Append("$aoj_bed: ").Append(bed);
                if (IsAtHome)
                {
                    var activity = (SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity);
                    text.Append("  ·  ").Append("$aoj_activity_").Append(activity.ToString().ToLowerInvariant());
                }
                text.Append('\n');
            }
            text.Append("$aoj_weapon: ").Append(ItemName(zdo.GetInt(ZDOVars.s_rightItem)));
            text.Append("  ·  $aoj_shield: ").Append(ItemName(zdo.GetInt(ZDOVars.s_leftItem))).Append('\n');
            text.Append("$aoj_items: ").Append(InventoryCount(zdo));
            return text.ToString();
        }

        /// <summary>One line for lists (the Jarl's Table roster): health, then the order or, at home, what it is doing.</summary>
        internal string ShortStatus()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return "";
            }
            ZDO zdo = _nview.GetZDO();
            string doing = IsDown ? "$aoj_down"
                : IsAtHome ? "$aoj_activity_" + ((SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)).ToString().ToLowerInvariant()
                : OrderText(zdo);
            Work.WorkTotem totem = JobTotem;
            string job = totem != null ? " · " + Work.JobInfo.Token(totem.Job) : Role != Army.CombatRole.None ? " · " + Army.CombatRoles.Token(Role) : "";
            return $"$aoj_health {Mathf.CeilToInt(_humanoid.GetHealth())}/{Mathf.CeilToInt(_humanoid.GetMaxHealth())} · {doing}{job}";
        }

        /// <summary>Experience at each job it has done and in combat (0-100), as tokens; "-" when none yet.</summary>
        internal string SkillsText()
        {
            ZDO zdo = Zdo;
            if (zdo == null)
            {
                return "-";
            }
            var parts = new List<string>();
            foreach (Work.JobType job in Work.JobInfo.All)
            {
                float skill = zdo.GetFloat(Keys.SkillPrefix + job.ToString().ToLowerInvariant());
                if (skill >= 1f)
                {
                    parts.Add($"{Work.JobInfo.Token(job)} {Mathf.FloorToInt(skill)}");
                }
            }
            float combat = CombatSkill.Level(zdo);
            if (combat >= 1f)
            {
                parts.Add($"$aoj_skill_combat {Mathf.FloorToInt(combat)}");
            }
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        /// <summary>Experience at one job, 0-100.</summary>
        internal float JobSkill(Work.JobType job) => Zdo?.GetFloat(Keys.SkillPrefix + job.ToString().ToLowerInvariant()) ?? 0f;

        /// <summary>The traits with what they change, one per line, as tokens.</summary>
        internal string TraitDetails()
        {
            if (Identity == null || Identity.Traits.Count == 0)
            {
                return "-";
            }
            var text = new StringBuilder();
            foreach (string id in Identity.Traits)
            {
                text.Append("<color=#e0c080>").Append(TraitToken(id, Identity.Female)).Append("</color>");
                TraitDef trait = DefsRegistry.Current.Traits.Find(t => t.Id == id);
                // Carry weight only matters when the server limits what workers carry (Work/CarryLimit).
                string[] effects = trait == null ? new string[0] : trait.Modifiers
                    .Where(m => m.Key != TraitStat.CarryWeight || AoJConfig.CarryLimit.Value > 0)
                    .Select(m => StatText(m.Key, m.Value))
                    .ToArray();
                if (effects.Length > 0)
                {
                    text.Append(": ").Append(string.Join(", ", effects));
                }
                text.Append('\n');
            }
            return text.ToString().TrimEnd('\n');
        }

        // Rates are relative (0.15 = +15%) and the flee threshold an absolute share of health; night work is a flag
        // and the starting combat experience a plain number.
        private static string StatText(string stat, float value)
        {
            string token = "$aoj_stat_" + stat.ToLowerInvariant();
            string sign = value >= 0f ? "+" : "-";
            switch (stat)
            {
                case TraitStat.NightWork:
                    return token;
                case TraitStat.CombatStart:
                    return $"{token} {Mathf.RoundToInt(value)}";
                case TraitStat.FleeThreshold when value <= -1f:
                    return "$aoj_stat_neverflees";
                default:
                    return $"{token} {sign}{Mathf.RoundToInt(Mathf.Abs(value) * 100f)}%";
            }
        }

        /// <summary>What the settler carries, from the saved inventory (readable on every machine), as tokens.</summary>
        internal List<string> CarriedItems()
        {
            var lines = new List<string>();
            foreach (ItemDrop.ItemData item in CarriedItemData())
            {
                string line = item.m_stack > 1 ? $"{item.m_shared.m_name} ×{item.m_stack}" : item.m_shared.m_name;
                lines.Add(item.m_equipped ? line + " <color=#b0b0b0>($aoj_equipped)</color>" : line);
            }
            return lines;
        }

        /// <summary>What the settler carries, from the saved inventory: copies, worn ones first, for the window.</summary>
        internal List<ItemDrop.ItemData> CarriedItemData()
        {
            var items = new List<ItemDrop.ItemData>();
            byte[] data = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetByteArray(Keys.ZdoSettlerInventory) : null;
            if (data == null)
            {
                return items;
            }
            var view = new Inventory("aoj_view", null, 8, 8);
            try
            {
                view.Load(new ZPackage(data));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                return items;
            }
            items.AddRange(view.GetAllItems());
            items.Sort((a, b) => a.m_equipped == b.m_equipped ? string.CompareOrdinal(a.m_shared.m_name, b.m_shared.m_name) : a.m_equipped ? -1 : 1);
            return items;
        }

        private string OrderText(ZDO zdo)
        {
            long followed = zdo.GetLong(Keys.ZdoSettlerFollow);
            if (followed != 0L)
            {
                Player player = Player.GetPlayer(followed);
                return Localize("$aoj_order_follow", player != null ? player.GetPlayerName() : "?");
            }
            if (zdo.GetBool(Keys.ZdoSettlerHold))
            {
                return "$aoj_order_wait";
            }
            return HasHome ? "$aoj_order_home" : "$aoj_order_free";
        }

        private static string ItemName(int itemHash)
        {
            GameObject prefab = itemHash != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemHash) : null;
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return item != null ? item.m_itemData.m_shared.m_name : "-";
        }

        // Only the header of the saved inventory (format version, item count) is read.
        private static int InventoryCount(ZDO zdo)
        {
            byte[] data = zdo.GetByteArray(Keys.ZdoSettlerInventory);
            if (data == null)
            {
                return 0;
            }
            try
            {
                var package = new ZPackage(data);
                package.ReadInt();
                return package.ReadUShort();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                return 0;
            }
        }

        // Dictionary lookup plus the table's cached data: cheap enough for a per-frame hover.
        private string HomeText()
        {
            if (!HasHome)
            {
                return "$aoj_homeless";
            }
            JarlTable table = HomeTable;
            SettlementData data = table != null ? table.Data : null;
            return data != null ? JarlTable.DisplayName(data) : "$aoj_settlement";
        }

        private static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text, words) : text;
    }
}
