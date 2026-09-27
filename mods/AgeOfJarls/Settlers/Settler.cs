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

        internal Army.CombatRole Role => _nview != null && _nview.IsValid() ? (Army.CombatRole)_nview.GetZDO().GetInt(Keys.ZdoSettlerRole) : Army.CombatRole.None;

        /// <summary>Stable id of the war banner it is posted at; 0 = none.</summary>
        internal long PostId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlerPost) : 0L;

        internal void RequestSetRole(Army.CombatRole role) => _nview.InvokeRPC(Keys.RpcSettlerRole, (int)role);

        private void RPC_SetRole(long sender, int role)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerRole, role);
                return;
            }
            if (IsCaptive)
            {
                // A captive takes no orders until a player frees it.
                return;
            }
            if (!MayOrder(sender, SettlementRight.Military) || !Enum.IsDefined(typeof(Army.CombatRole), role))
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

        /// <summary>Owner only: turns a freshly spawned settler into a captive.</summary>
        internal void MakeCaptive()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }
            ZDO zdo = _nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerCaptive, true);
            zdo.Set(Keys.ZdoSettlerFollow, 0L);
            zdo.Set(Keys.ZdoSettlerHold, true);
            if (_ai != null)
            {
                _ai.SetFollowTarget(null);
                _ai.SetPatrolPoint();
            }
        }

        internal void RequestFree(Player player) => _nview.InvokeRPC(Keys.RpcSettlerFree, player.GetPlayerID());

        // Guards alive nearby keep the captive locked in; freed, it follows its rescuer.
        private void RPC_Free(long sender, long playerId)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerFree, playerId);
                return;
            }
            Player player = Player.GetPlayer(playerId);
            if (!IsCaptive || player == null || GuardsNearby() > 0)
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

        private void RPC_SetJob(long sender, long totemId)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerSetJob, totemId);
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

        /// <summary>Owner only; written when it changes, for the order window on every machine.</summary>
        internal void SetActivity(SettlerActivity activity)
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() &&
                _nview.GetZDO().GetInt(Keys.ZdoSettlerActivity) != (int)activity)
            {
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
                _nview.Register(Keys.RpcSettlerTakeBack, RPC_TakeBack);
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
            InvokeRepeating(nameof(Tick), TickSeconds, TickSeconds);
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

            if (!_nview.IsOwner())
            {
                return;
            }
            EnsureUid();

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

        private float TraitSum(string stat)
        {
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
            if (Identity != null && !IsCaptive)
            {
                Needs.Update(_nview.GetZDO(), this, HomeTable);
            }
        }

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
                UpdateHomeAnchor(zdo, table);
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

        private void AdoptListedHome(ZDO zdo, long uid)
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
                return;
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
            return night && TryGetBed(table, out Vector3 bed) ? bed : table.transform.position;
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

            var package = new ZPackage();
            _humanoid.GetInventory().Save(package);
            ZDO zdo = _nview.GetZDO();
            int revision = zdo.GetInt(Keys.ZdoSettlerInventoryRevision) + 1;
            zdo.Set(Keys.ZdoSettlerInventory, package.GetArray());
            zdo.Set(Keys.ZdoSettlerInventoryRevision, revision);
            _loadedInventoryRevision = revision;
            Log.Debug(Module, $"Saved the inventory of {Identity?.Name} (revision {revision})");
        }

        // ---------------------------------------------------------------- orders

        // [E] opens the order window, [Shift+E] toggles following without it.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Identity == null || !(user is Player player))
            {
                return false;
            }

            if (IsCaptive)
            {
                int guards = GuardsNearby();
                if (guards > 0)
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

        internal void RequestTakeBack(Player player)
        {
            if (!IsFollowing(player) && !MayBeOrderedBy(player, SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }
            _nview.InvokeRPC(Keys.RpcSettlerTakeBack);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_takeback", DisplayName));
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerAttack, targetId);
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerFallBack, playerId);
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerGoHome);
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerRename, name);
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerCommand, followPlayerId);
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
            if (Identity == null || item == null || !(user is Player player))
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

        private void RPC_Give(long sender, ZPackage package)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerGive, package);
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
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerFeed, prefabName);
                return;
            }
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop food = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (food != null && Needs.IsFood(food.m_itemData))
            {
                Needs.Eat(_nview.GetZDO(), food.m_itemData);
            }
        }

        private void RPC_TakeBack(long sender)
        {
            if (!_nview.IsOwner())
            {
                _nview.InvokeRPC(Keys.RpcSettlerTakeBack);
                return;
            }
            if (!IsLeader(sender) && !MayOrder(sender, SettlementRight.Live))
            {
                Log.Warning(Module, $"Handing back the items of {DisplayName} to peer {sender} refused");
                return;
            }

            DropAllItems();
        }

        // Character.OnDeath runs on the owner. The gear drops where the settler fell instead of vanishing with it.
        private void OnDeath()
        {
            if (_nview.IsValid() && _nview.IsOwner())
            {
                DropAllItems();
                _inventoryDirty = false;
                if (HasHome)
                {
                    JarlTable.RequestRemoveSettler(HomeId, Uid);
                }
            }
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
            if (Identity == null)
            {
                return Localize("$aoj_settler");
            }

            if (IsCaptive)
            {
                return Localize($"{DisplayName}\n<color=#b0b0b0>$aoj_captive · {_traitsText}</color>\n" +
                                "[<color=yellow><b>$KEY_Use</b></color>] $aoj_free_captive");
            }

            long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            bool following = me != 0L && _nview.IsValid() && _nview.GetZDO().GetLong(Keys.ZdoSettlerFollow) == me;
            ZDO zdo = _nview.GetZDO();
            string doing = IsAtHome
                ? "$aoj_activity_" + ((SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)).ToString().ToLowerInvariant()
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
            string doing = IsAtHome
                ? "$aoj_activity_" + ((SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity)).ToString().ToLowerInvariant()
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
                if (trait != null && trait.Modifiers.Count > 0)
                {
                    text.Append(": ").Append(string.Join(", ", trait.Modifiers.Select(m => StatText(m.Key, m.Value))));
                }
                text.Append('\n');
            }
            return text.ToString().TrimEnd('\n');
        }

        // Rates are relative (0.15 = +15%); the flee threshold is an absolute share of health.
        private static string StatText(string stat, float value)
        {
            string sign = value >= 0f ? "+" : "-";
            return $"$aoj_stat_{stat.ToLowerInvariant()} {sign}{Mathf.RoundToInt(Mathf.Abs(value) * 100f)}%";
        }

        /// <summary>What the settler carries, from the saved inventory (readable on every machine), as tokens.</summary>
        internal List<string> CarriedItems()
        {
            var lines = new List<string>();
            byte[] data = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetByteArray(Keys.ZdoSettlerInventory) : null;
            if (data == null)
            {
                return lines;
            }
            var view = new Inventory("aoj_view", null, 8, 8);
            try
            {
                view.Load(new ZPackage(data));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                return lines;
            }
            foreach (ItemDrop.ItemData item in view.GetAllItems())
            {
                string line = item.m_stack > 1 ? $"{item.m_shared.m_name} ×{item.m_stack}" : item.m_shared.m_name;
                lines.Add(item.m_equipped ? line + " <color=#b0b0b0>($aoj_equipped)</color>" : line);
            }
            return lines;
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
