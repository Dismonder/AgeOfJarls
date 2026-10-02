using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using AgeOfJarls.Core.Defs;
using AgeOfJarls.Net;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    internal enum SettlementAction
    {
        AddSettlers = 2,
        RemoveSettler = 3,
        HandOver = 4,
        Rename = 5,
        UpgradeTier = 6,
        SetAlarm = 7,
        Feast = 8,
        SetRank = 9,
        SetManualWork = 10,
        /// <summary>A building a siege destroyed (from the machine that owned it).</summary>
        Breach = 11,
        /// <summary>A Builder put a destroyed building back.</summary>
        Rebuilt = 12,
        /// <summary>An area marked on the map added or changed (Hersir and up).</summary>
        SetZone = 13,
        RemoveZone = 14,
    }

    /// <summary>
    /// The Jarl's Table owns the settlement data (one blob in its ZDO). Only the ZDO owner writes it: players send
    /// actions by RPC and the owner checks the sender's rank (<see cref="Permissions"/>) before applying them.
    /// A settlement built by several players grows with them: see <see cref="ExtraMembers"/>.
    /// </summary>
    public class JarlTable : MonoBehaviour, Hoverable, Interactable
    {
        private const string Module = "Settlement";
        private const float MarkerSeconds = 0.5f;
        private const float FallbackRadius = 30f;
        private const int MaxBatch = 32;
        private const float OwnerTickSeconds = 10f;

        /// <summary>Loaded, networked tables (placement ghosts excluded).</summary>
        internal static readonly List<JarlTable> Loaded = new List<JarlTable>();

        /// <summary>Radius circle cloned from the ward; assigned when the prefab is built.</summary>
        public CircleProjector m_areaMarker;

        private ZNetView _nview;
        private Piece _piece;
        private SettlementData _data;
        private uint _dataRevision = uint.MaxValue;
        private string _hoverText;
        private uint _hoverRevision = uint.MaxValue;
        private readonly List<Piece> _pieces = new List<Piece>();
        private readonly List<Vector3> _freeBeds = new List<Vector3>();
        private static readonly List<Piece> s_bedSearch = new List<Piece>();

        /// <summary>Beds are matched by position (they never move); saved floats come back exactly, this is slack.</summary>
        private const float SameSpotSqr = 0.05f * 0.05f;

        /// <summary>Current settlement, re-read only after the ZDO changed. Null before founding or when unreadable.</summary>
        internal SettlementData Data
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return null;
                }
                ZDO zdo = _nview.GetZDO();
                if (zdo.DataRevision != _dataRevision)
                {
                    _dataRevision = zdo.DataRevision;
                    byte[] blob = zdo.GetByteArray(Keys.ZdoSettlement);
                    _data = blob != null ? SettlementData.Deserialize(blob) : null;
                }
                return _data;
            }
        }

        /// <summary>
        /// The settlement's stable id; 0 until the table's owner gives an older table one. Saved data never uses the
        /// table's ZDOID: the game renumbers ZDOIDs on every world load.
        /// </summary>
        internal long SettlementId => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetLong(Keys.ZdoSettlementId) : 0L;

        /// <summary>World time until which the last feast lifts morale.</summary>
        internal double FeastUntil => _nview != null && _nview.IsValid() ? WorldClock.Get(_nview.GetZDO(), Keys.ZdoSettlementFeastUntil, 0.0) : 0.0;

        /// <summary>World time until which a recent loss or siege weighs on morale.</summary>
        internal double GriefUntil => _nview != null && _nview.IsValid() ? WorldClock.Get(_nview.GetZDO(), Keys.ZdoSettlementGriefUntil, 0.0) : 0.0;

        /// <summary>Owner only: a loss or an attack; everyone's morale is lower for a game day.</summary>
        internal void Mourn()
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                WorldClock.Set(_nview.GetZDO(), Keys.ZdoSettlementGriefUntil, WorldClock.Now + WorldClock.DayLength);
            }
        }

        /// <summary>Alarm: civilians take shelter, the troop mans its posts.</summary>
        internal bool AlarmOn => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoSettlementAlarm);

        /// <summary>Players assign all work themselves; otherwise free civilians take free places at the totems (by priority).</summary>
        internal bool ManualWork => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoSettlementManualWork);

        /// <summary>A siege is on (readable on every machine; the siege itself runs on the table's owner).</summary>
        internal bool UnderSiege => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(Keys.ZdoSettlementBesieged);

        /// <summary>Owner only (Sieges.SiegeDirector).</summary>
        internal void SetUnderSiege(bool on)
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && UnderSiege != on)
            {
                _nview.GetZDO().Set(Keys.ZdoSettlementBesieged, on);
            }
        }

        /// <summary>Destroyed buildings waiting for the Builders.</summary>
        internal List<Breaches.Entry> BreachList => _nview != null && _nview.IsValid() ? Breaches.Read(_nview.GetZDO()) : new List<Breaches.Entry>();

        internal void ReportBreach(string prefab, Vector3 position, Quaternion rotation) =>
            Send(SettlementAction.Breach, p =>
            {
                p.Write(prefab ?? "");
                p.Write(position);
                p.Write(rotation);
            });

        internal void ReportRebuilt(Vector3 position) => Send(SettlementAction.Rebuilt, p => p.Write(position));

        internal void RequestManualWork(bool manual) => Send(SettlementAction.SetManualWork, p => p.Write(manual));

        /// <summary>Renown from won sieges.</summary>
        internal int Fame => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetInt(Keys.ZdoSettlementFame) : 0;

        internal ZNetView NetView => _nview;

        /// <summary>The settlement a settler with this home id belongs to, when its table is loaded here.</summary>
        internal static JarlTable FindById(long settlementId)
        {
            if (settlementId == 0L)
            {
                return null;
            }
            foreach (JarlTable table in Loaded)
            {
                if (table.SettlementId == settlementId)
                {
                    return table;
                }
            }
            return null;
        }

        internal float Radius => RadiusOf(Data);

        internal int Capacity => CapacityOf(Data);

        /// <summary>A tier's own radius and settler limit (tiers.json), before the members' bonus.</summary>
        internal static float RadiusFor(int tier) => Tier(tier)?.Radius ?? FallbackRadius;

        internal static int CapacityFor(int tier) => Tier(tier)?.MaxSettlers ?? 0;

        /// <summary>The members' bonus never takes the area past this: all of it stays loaded around a player at the table.</summary>
        private const float MaxScaledRadius = 80f;

        /// <summary>
        /// Members beyond the first, up to Settlement/MaxScaledMembers. A settlement built by several players grows
        /// with them: each one adds Settlement/MemberBonus of the tier's settler limit, Settlement/MemberRadiusBonus
        /// of its radius and Work/TotemSlotsPerMember places at every Work Totem. Every machine computes the same
        /// from the synced config and the members in the table's ZDO.
        /// </summary>
        internal static int ExtraMembers(SettlementData data) =>
            data == null ? 0 : Mathf.Clamp(data.Members.Count, 1, AoJConfig.MaxScaledMembers.Value) - 1;

        internal static float RadiusOf(SettlementData data)
        {
            if (data == null)
            {
                return RadiusFor(0);
            }
            float radius = RadiusFor(data.Tier);
            return Mathf.Min(radius * (1f + AoJConfig.MemberRadiusBonus.Value * ExtraMembers(data)), Mathf.Max(radius, MaxScaledRadius));
        }

        internal static int CapacityOf(SettlementData data) =>
            data == null ? 0 : Mathf.RoundToInt(CapacityFor(data.Tier) * (1f + AoJConfig.MemberBonus.Value * ExtraMembers(data)));

        private static TierDef Tier(int tier)
        {
            List<TierDef> tiers = DefsRegistry.Current.Tiers;
            return tiers.Count == 0 ? null : tiers[Mathf.Clamp(tier, 0, tiers.Count - 1)];
        }

        /// <summary>Exact tier, or null past the last one (unlike <see cref="Tier"/>, which clamps).</summary>
        internal static TierDef TierAt(int tier)
        {
            List<TierDef> tiers = DefsRegistry.Current.Tiers;
            return tier >= 0 && tier < tiers.Count ? tiers[tier] : null;
        }

        internal static bool IsUnlocked(TierDef tier) =>
            tier.RequiredGlobalKey.Length == 0 || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(tier.RequiredGlobalKey));

        /// <summary>Unlock ids in tiers.json for perks with no job or role behind them.</summary>
        internal const string UnlockTotemSlot = "totem_slot";
        internal const string UnlockFastHealing = "fast_healing";

        /// <summary>Whether a tier up to the settlement's lists this unlock id (a perk, a job, a role...).</summary>
        internal static bool HasUnlock(SettlementData data, string id)
        {
            if (data == null)
            {
                return false;
            }
            foreach (TierDef def in DefsRegistry.Current.Tiers)
            {
                if (def.Level <= data.Tier && def.Unlocks.Contains(id))
                {
                    return true;
                }
            }
            return false;
        }

        internal static JarlTable FindNearest(Vector3 position, float maxDistance)
        {
            JarlTable best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (JarlTable table in Loaded)
            {
                float sqr = (table.transform.position - position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    best = table;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        /// <summary>The table whose settlement area contains the position, or null.</summary>
        internal static JarlTable FindContaining(Vector3 position) => Loaded.FirstOrDefault(t => t.Contains(position));

        /// <summary>Inside the table's radius or inside one of the settlement's areas marked on the map.</summary>
        internal bool Contains(Vector3 position)
        {
            if (Utils.DistanceXZ(transform.position, position) <= Radius)
            {
                return true;
            }
            SettlementData data = Data;
            return data != null && data.ZoneAt(position) != null;
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        /// <summary>
        /// The settlement a point works for: the one it lies in, else the one whose edge is nearest, if at most
        /// <paramref name="beyondEdge"/> away - a Work Totem by a forest or a mine outside serves that settlement.
        /// </summary>
        internal static JarlTable FindServing(Vector3 position, float beyondEdge)
        {
            JarlTable best = null;
            float bestGap = float.MaxValue;
            foreach (JarlTable table in Loaded)
            {
                if (table == null)
                {
                    continue;
                }
                float gap = EdgeGap(table.transform.position, table.Radius, position);
                if (gap <= beyondEdge && gap < bestGap)
                {
                    best = table;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>How far past a settlement's edge a point lies; negative inside.</summary>
        internal static float EdgeGap(Vector3 table, float radius, Vector3 point) => Utils.DistanceXZ(table, point) - radius;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            SetMarkerRadius(RadiusFor(0));
            if (_nview == null || !_nview.IsValid())
            {
                // Placement ghost: the marker stays visible while the player chooses the spot.
                return;
            }

            Loaded.Add(this);
            HideMarker();
            _nview.Register<ZPackage>(Keys.RpcSettlementAction, RPC_Action);
        }

        private void Start()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }
            if (_nview.IsOwner())
            {
                // The builder is recorded right after placement, i.e. before Start runs.
                if (_nview.GetZDO().GetByteArray(Keys.ZdoSettlement) == null)
                {
                    Found();
                }
                UpgradeSavedData();
            }
            // On every client: whoever owns the table when the tick fires does the work.
            InvokeRepeating(nameof(OwnerTick), OwnerTickSeconds, OwnerTickSeconds);
            InvokeRepeating(nameof(AlarmTick), AlarmTickSeconds, AlarmTickSeconds);
            Invoke(nameof(EnsureMapPin), PinDelaySeconds);
        }

        private const float PinDelaySeconds = 5f;
        private const float PinMatchRange = 5f;

        // Members get a saved map pin at their settlement (once: an existing pin there is kept).
        private void EnsureMapPin()
        {
            SettlementData data = Data;
            Minimap map = Minimap.instance;
            if (data == null || map == null || !RoleOf(Player.m_localPlayer).Allows(SettlementRight.Live))
            {
                return;
            }
            Vector3 position = transform.position;
            if (map.m_pins.Exists(p => p.m_type == Minimap.PinType.Icon1 && Utils.DistanceXZ(p.m_pos, position) <= PinMatchRange))
            {
                return;
            }
            map.AddPin(position, Minimap.PinType.Icon1, DisplayName(data), true, false);
        }

        private const float AlarmTickSeconds = 2f;

        private void AlarmTick()
        {
            if (_nview.IsValid() && _nview.IsOwner() && Data != null)
            {
                long perf = Perf.Start();
                Army.Alarm.Check(this);
                Perf.Stop(Perf.Section.TableTick, perf);
            }
        }

        // Tables from older mod versions get their stable id and the current data format from their owner.
        private void UpgradeSavedData()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetByteArray(Keys.ZdoSettlement) != null && zdo.GetLong(Keys.ZdoSettlementId) == 0L)
            {
                zdo.Set(Keys.ZdoSettlementId, Keys.NewId());
            }
            SettlementData data = Data;
            if (data != null && data.NeedsRewrite)
            {
                data.NeedsRewrite = false;
                Write(data);
                Log.Info(Module, $"{DisplayName(data)} saved in the current format");
            }
        }

        private void OwnerTick()
        {
            if (!_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }
            long perf = Perf.Start();
            OwnerTickInner();
            Perf.Stop(Perf.Section.TableTick, perf);
        }

        private void OwnerTickInner()
        {
            // Ownership may have come from an older client or version: make sure the id and format are current.
            UpgradeSavedData();
            SettlementData data = Data;
            if (data != null && data.Settlers.Count > 0 && ReconcileBeds(data))
            {
                Write(data);
            }
            if (data != null)
            {
                // Work done while nobody was here, then the live clock; sieges only while a member is at home.
                SettlementSim.Tick(this, data);
                Sieges.SiegeDirector.Tick(this, data);
            }
        }

        // Settlers keep beds that still exist and that no player claimed; free, unclaimed vanilla beds inside the
        // settlement go to settlers without one. A player's bed (their spawn point) is never taken. Beds are known by
        // their position, which, unlike their ZDOID, survives a world reload.
        private bool ReconcileBeds(SettlementData data)
        {
            _pieces.Clear();
            _freeBeds.Clear();
            Piece.GetAllPiecesInRadius(transform.position, RadiusOf(data), _pieces);
            foreach (Piece piece in _pieces)
            {
                ZNetView view = piece.GetComponent<Bed>() != null ? piece.GetComponent<ZNetView>() : null;
                if (view != null && view.IsValid() && view.GetZDO().GetLong(ZDOVars.s_owner) == 0L)
                {
                    _freeBeds.Add(piece.transform.position);
                }
            }

            bool changed = false;
            foreach (RosterEntry settler in data.Settlers)
            {
                if (!settler.HasBed || TakeFreeBed(settler.BedPosition) || !IsBedLost(settler.BedPosition))
                {
                    continue;
                }
                settler.HasBed = false;
                changed = true;
            }
            foreach (RosterEntry settler in data.Settlers)
            {
                if (settler.HasBed || _freeBeds.Count == 0)
                {
                    continue;
                }
                settler.HasBed = true;
                settler.BedPosition = _freeBeds[_freeBeds.Count - 1];
                _freeBeds.RemoveAt(_freeBeds.Count - 1);
                changed = true;
            }
            _pieces.Clear();
            return changed;
        }

        private bool TakeFreeBed(Vector3 position)
        {
            for (int i = 0; i < _freeBeds.Count; i++)
            {
                if ((_freeBeds[i] - position).sqrMagnitude <= SameSpotSqr)
                {
                    _freeBeds.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        // An assigned bed that is not free any more is lost when it is loaded here (claimed by a player), or when its
        // spot is loaded but empty (destroyed). A bed that is merely not loaded keeps its settler.
        private static bool IsBedLost(Vector3 position) =>
            FindBedAt(position) != null || ZNetScene.InActiveArea(position, ZNet.instance.GetReferencePosition());

        /// <summary>The loaded vanilla bed standing at this spot, or null.</summary>
        internal static Bed FindBedAt(Vector3 position)
        {
            s_bedSearch.Clear();
            Piece.GetAllPiecesInRadius(position, 0.5f, s_bedSearch);
            Bed found = null;
            foreach (Piece piece in s_bedSearch)
            {
                Bed bed = piece.GetComponent<Bed>();
                if (bed != null && (piece.transform.position - position).sqrMagnitude <= SameSpotSqr)
                {
                    found = bed;
                    break;
                }
            }
            s_bedSearch.Clear();
            return found;
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
            if (_nview != null && _nview.GetZDO() != null)
            {
                SettlementSim.Forget(_nview.GetZDO().GetLong(Keys.ZdoSettlementId));
            }
        }

        private void Found()
        {
            long creator = _piece != null ? _piece.GetCreator() : 0L;
            Player founder = creator != 0L ? Player.GetPlayer(creator) : null;
            if (founder == null)
            {
                Log.Warning(Module, "Jarl's Table without a known builder stays without a settlement");
                return;
            }

            var data = new SettlementData();
            data.Members.Add(new SettlementMember { PlayerId = creator, Name = founder.GetPlayerName(), Role = SettlementRole.Jarl });
            _nview.GetZDO().Set(Keys.ZdoSettlementId, Keys.NewId());
            Write(data);
            Log.Info(Module, $"Settlement founded by {founder.GetPlayerName()}");
            Chronicle.Add(_nview, "$aoj_chr_founded", TextUtil.SanitizeName(founder.GetPlayerName(), 32));
            Recruitment.Castaways.OnSettlementFounded(this);
        }

        private void Write(SettlementData data)
        {
            _nview.GetZDO().Set(Keys.ZdoSettlement, data.Serialize());
        }

        /// <summary>
        /// Cheat (<c>aoj_breach</c>, for testing the Builders): the piece is destroyed as if a siege broke it, and listed
        /// for rebuilding - on the table's owner only. Null when done.
        /// </summary>
        internal string CheatBreach(Piece piece)
        {
            if (Data == null || !_nview.IsOwner())
            {
                return "Only the machine that owns the table can do that (single player, the host, or stand right by it).";
            }
            string prefab = Utils.GetPrefabName(piece.gameObject);
            if (!Breaches.IsRebuildable(prefab))
            {
                return $"{prefab} is nothing a Builder puts back.";
            }
            List<Breaches.Entry> entries = Breaches.Read(_nview.GetZDO());
            entries.Add(new Breaches.Entry { Prefab = prefab, Position = piece.transform.position, Rotation = piece.transform.rotation });
            Breaches.Write(_nview.GetZDO(), entries);
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (wear != null)
            {
                // Broken, not taken down: nothing comes back from it.
                wear.Remove(blockDrop: true);
            }
            else
            {
                ZNetScene.instance.Destroy(piece.gameObject);
            }
            Log.Info(Module, $"{prefab} broken for a rebuild test (cheat)");
            return null;
        }

        /// <summary>
        /// Cheat (<c>aoj_tier</c>, for testing): the tier without its costs or boss, written by the table's owner only -
        /// so on the machine simulating the table (single player, the host, or the player standing by it). Null when set.
        /// </summary>
        internal string CheatSetTier(int tier)
        {
            SettlementData data = Data;
            if (data == null || !_nview.IsOwner())
            {
                return "Only the machine that owns the table can do that (single player, the host, or stand right by it).";
            }
            List<TierDef> tiers = DefsRegistry.Current.Tiers;
            data.Tier = Mathf.Clamp(tier, 0, Mathf.Max(0, tiers.Count - 1));
            Write(data);
            Log.Info(Module, $"{DisplayName(data)} set to tier {data.Tier} (cheat)");
            return null;
        }

        // ---------------------------------------------------------------- interaction

        // [E] opens the settlement window (read-only for guests), [Shift+E] accepts followers without it.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Data == null || !(user is Player player))
            {
                return false;
            }
            if (alt)
            {
                AcceptFollowers(player);
            }
            else
            {
                UI.JarlTableWindow.Open(this);
            }
            return true;
        }

        internal SettlementRole RoleOf(Player player) =>
            player != null && Data != null ? Data.RoleOf(player.GetPlayerID()) : SettlementRole.Guest;

        /// <summary>Settlers following the player inside the area join, up to the tier's limit (the owner decides).</summary>
        internal void AcceptFollowers(Player player)
        {
            SettlementData data = Data;
            if (data == null)
            {
                return;
            }
            if (!data.RoleOf(player.GetPlayerID()).Allows(SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }

            float radius = Radius;
            long me = player.GetPlayerID();
            List<Settler> following = Settler.Loaded
                .Where(s => s.FollowedPlayerId == me && Utils.DistanceXZ(s.transform.position, transform.position) <= radius)
                .ToList();
            // A follower whose own Jarl's Table is not loaded here has lost it (destroyed far away, where nobody saw
            // it go) or left it far behind with the player who leads it: it may be taken in. One whose table stands
            // here still lives there. Its settler adopts the new home from this roster (Settler.UpdateHome).
            List<Settler> followers = following.Where(s => !s.HasHome || FindById(s.HomeId) == null).ToList();
            if (followers.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, following.Count == 0
                    ? Localize("$aoj_msg_nobody_follows")
                    : Localize("$aoj_msg_followers_have_home", string.Join(", ", following.Select(s => s.DisplayName))));
                return;
            }

            int free = Capacity - data.Settlers.Count;
            if (free <= 0)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_settlement_full", data.Settlers.Count.ToString(), Capacity.ToString()));
                return;
            }

            // The owner decides; this prediction only fills the message.
            List<Settler> joining = followers.Take(Math.Min(free, MaxBatch)).ToList();
            RequestAddSettlers(joining);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_settlers_joined", string.Join(", ", joining.Select(s => s.DisplayName))));
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ---------------------------------------------------------------- expanding the settlement

        // Checked and paid on the requester's side (its own inventory); the owner re-checks role, order and boss key.
        internal void TryUpgrade(Player player)
        {
            SettlementData data = Data;
            if (data == null)
            {
                return;
            }
            if (!data.RoleOf(player.GetPlayerID()).Allows(SettlementRight.Manage))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Manage));
                return;
            }

            TierDef next = TierAt(data.Tier + 1);
            if (next == null)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_max_tier"));
                return;
            }
            if (!IsUnlocked(next))
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_tier_locked", Localize("$aoj_key_" + next.RequiredGlobalKey)));
                return;
            }

            Inventory inventory = player.GetInventory();
            string missing = MissingCost(next, inventory);
            if (missing != null)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_tier_missing", missing));
                return;
            }

            foreach (TierCost cost in next.Cost)
            {
                inventory.RemoveItem(ItemToken(cost.Item), cost.Amount, -1, false);
            }
            RequestUpgrade(data.Tier + 1);
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_tier_up", Localize("$aoj_tier_" + (data.Tier + 1))));
        }

        // Null when everything is there; otherwise what is still missing, e.g. "12 Wood, 3 Deer hide".
        private static string MissingCost(TierDef tier, Inventory inventory)
        {
            var missing = new List<string>();
            foreach (TierCost cost in tier.Cost)
            {
                string token = ItemToken(cost.Item);
                if (token == null)
                {
                    Log.Warning(Module, $"tiers.json: unknown item '{cost.Item}' in the cost of tier {tier.Level}");
                    missing.Add(cost.Item);
                    continue;
                }
                int have = inventory.CountItems(token, -1, false);
                if (have < cost.Amount)
                {
                    missing.Add($"{cost.Amount - have} {Localize(token)}");
                }
            }
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        /// <summary>Localization token of an item prefab (what inventories count by), or null if the prefab is unknown.</summary>
        private static string ItemToken(string prefabName)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return item != null ? item.m_itemData.m_shared.m_name : null;
        }

        // Tokens only; localized together with the rest of the hover text.
        private static string RequirementText(TierDef tier)
        {
            IEnumerable<string> parts = tier.Cost.Select(c => $"{c.Amount} {ItemToken(c.Item) ?? c.Item}");
            if (tier.RequiredGlobalKey.Length > 0)
            {
                parts = new[] { "$aoj_key_" + tier.RequiredGlobalKey }.Concat(parts);
            }
            return string.Join(", ", parts);
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Gives a player a rank (Guest = off the list); the owner checks it with the same rules as the caller.</summary>
        /// <summary>An area marked on the map, new (id 0: the owner gives it one) or changed (Hersir and up).</summary>
        internal void RequestSetZone(SettlementZone zone) =>
            Send(SettlementAction.SetZone, p =>
            {
                p.Write(zone.Id);
                p.Write(zone.Name ?? "");
                p.Write(zone.Kind ?? SettlementZone.KindOther);
                p.Write(zone.Center);
                p.Write(zone.Radius);
            });

        internal void RequestRemoveZone(long zoneId) => Send(SettlementAction.RemoveZone, p => p.Write(zoneId));

        /// <summary>How far from the table an area may be marked: a warehouse across a portal, not across the world.</summary>
        internal const float MaxZoneDistance = 400f;

        internal void RequestSetRank(long playerId, string name, SettlementRole rank) =>
            Send(SettlementAction.SetRank, p =>
            {
                p.Write(playerId);
                p.Write(name ?? "");
                p.Write((int)rank);
            });

        /// <summary>A Jarl hands its title (and seat) to another player and stays as a Hersir.</summary>
        internal void RequestHandOver(long playerId, string name) =>
            Send(SettlementAction.HandOver, p =>
            {
                p.Write(playerId);
                p.Write(name ?? "");
            });

        /// <summary>
        /// The rank change as the owner will judge it, with the message for a refusal (null = allowed), so buttons and
        /// console tell the truth before the request travels.
        /// </summary>
        internal string RankProblem(Player requester, long target, SettlementRole rank)
        {
            SettlementData data = Data;
            if (data == null || requester == null)
            {
                return "$aoj_msg_rank_denied";
            }
            return data.RankChangeProblem(requester.GetPlayerID(), target, rank, AoJConfig.MaxJarls.Value);
        }

        internal void RequestRename(string name) => Send(SettlementAction.Rename, p => p.Write(name ?? ""));

        /// <summary>Jarl or Hersir sends a settler away; the owner checks the role, the settler notices it is off the roster.</summary>
        internal void RequestDismiss(long settlerUid) => Send(SettlementAction.RemoveSettler, p => p.Write(settlerUid));

        internal void RequestAlarm(bool on) => Send(SettlementAction.SetAlarm, p => p.Write(on));

        /// <summary>Days a feast lifts morale.</summary>
        internal const float FeastDays = 2f;
        internal const int FeastFood = 5;

        /// <summary>
        /// A member holds a feast: one mead and five portions of food from its own inventory, paid here like an
        /// upgrade; the owner then lifts everyone's morale for two days.
        /// </summary>
        internal void TryFeast(Player player)
        {
            SettlementData data = Data;
            if (data == null || !data.RoleOf(player.GetPlayerID()).Allows(SettlementRight.Live))
            {
                player.Message(MessageHud.MessageType.Center, Permissions.Denied(SettlementRight.Live));
                return;
            }
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData mead = inventory.GetAllItems().Find(IsMead);
            List<ItemDrop.ItemData> food = inventory.GetAllItems().Where(Needs.IsFood).ToList();
            if (mead == null || food.Sum(f => f.m_stack) < FeastFood)
            {
                player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_feast_needs", FeastFood.ToString()));
                return;
            }
            inventory.RemoveItem(mead, 1);
            int left = FeastFood;
            foreach (ItemDrop.ItemData portion in food)
            {
                int take = Mathf.Min(left, portion.m_stack);
                inventory.RemoveItem(portion, take);
                left -= take;
                if (left <= 0)
                {
                    break;
                }
            }
            Send(SettlementAction.Feast, p => { });
            player.Message(MessageHud.MessageType.Center, Localize("$aoj_msg_feast"));
        }

        // Meads are consumables that give no food but a status effect (vanilla names them "$item_mead_...").
        private static bool IsMead(ItemDrop.ItemData item) =>
            item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && item.m_shared.m_food <= 0f &&
            item.m_shared.m_name.StartsWith("$item_mead", StringComparison.Ordinal);

        private void RequestUpgrade(int tier) => Send(SettlementAction.UpgradeTier, p => p.Write(tier));

        private void RequestAddSettlers(List<Settler> settlers) =>
            Send(SettlementAction.AddSettlers, p =>
            {
                p.Write(settlers.Count);
                foreach (Settler settler in settlers)
                {
                    p.Write(settler.Uid);
                    p.Write(settler.DisplayName);
                }
            });

        private void Send(SettlementAction action, Action<ZPackage> writeArguments)
        {
            var package = new ZPackage();
            package.Write((int)action);
            writeArguments(package);
            _nview.InvokeRPC(Keys.RpcSettlementAction, package);
        }

        /// <summary>
        /// A settler's death, reported by its owner. With the table not loaded here nobody can take the report now;
        /// the server's roster check (<see cref="SettlementServer"/>) drops the settler later instead.
        /// </summary>
        internal static void RequestRemoveSettler(long settlementId, long settlerUid)
        {
            JarlTable table = FindById(settlementId);
            if (table != null)
            {
                table._nview.InvokeRPC(Keys.RpcSettlementAction, RemoveSettlerPackage(settlerUid));
            }
        }

        /// <summary>Server side: straight to the peer that owns (and therefore has loaded) the table.</summary>
        internal static void SendRemoveSettler(long ownerPeer, ZDOID tableId, long settlerUid)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ownerPeer, tableId, Keys.RpcSettlementAction, RemoveSettlerPackage(settlerUid));
        }

        private static ZPackage RemoveSettlerPackage(long settlerUid)
        {
            var package = new ZPackage();
            package.Write((int)SettlementAction.RemoveSettler);
            package.Write(settlerUid);
            return package;
        }

        private void RPC_Action(long sender, ZPackage package)
        {
            // Ownership moved while the RPC travelled: passed on to the current owner (Net.OwnerRpc).
            if (!Net.OwnerRpc.Handles(_nview, Keys.RpcSettlementAction, package))
            {
                return;
            }

            SettlementData data = Data;
            if (data == null)
            {
                Log.Warning(Module, $"Settlement action from peer {sender} ignored: no readable settlement here");
                return;
            }

            // The requester's rank comes from the network identity, never from what the package claims.
            Player requester = Peers.FindPlayer(sender);
            long requesterId = requester != null ? requester.GetPlayerID() : 0L;
            SettlementRole role = requesterId != 0L ? data.RoleOf(requesterId) : SettlementRole.Guest;
            // For the chronicle ("who did it"); player names go into texts others read, so they are cleaned.
            string actor = requester != null ? TextUtil.SanitizeName(requester.GetPlayerName(), 32) : "";
            try
            {
                if (Apply(data, requesterId, role, actor, sender, package))
                {
                    Write(data);
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Error(Module, $"Malformed settlement action from peer {sender}: {e.Message}");
            }
        }

        private bool Apply(SettlementData data, long requesterId, SettlementRole role, string actor, long sender, ZPackage package)
        {
            var action = (SettlementAction)package.ReadInt();
            switch (action)
            {
                case SettlementAction.SetRank:
                {
                    long target = package.ReadLong();
                    string name = package.ReadString();
                    var rank = (SettlementRole)package.ReadInt();
                    string problem = requesterId != 0L ? data.RankChangeProblem(requesterId, target, rank, AoJConfig.MaxJarls.Value) : "unknown player";
                    if (problem != null)
                    {
                        Log.Warning(Module, $"Rank {rank} for player {target} from peer {sender} refused ({problem})");
                        return false;
                    }
                    string shown = data.Members.Find(m => m.PlayerId == target)?.Name ?? TextUtil.SanitizeName(name, 32);
                    data.SetRank(target, name, rank);
                    Log.Info(Module, $"{shown} is now {rank} in {DisplayName(data)} ({data.Members.Count} member(s))");
                    if (target == requesterId)
                    {
                        Chronicle.Add(_nview, rank == SettlementRole.Guest ? "$aoj_chr_member_left" : "$aoj_chr_rank", shown, Permissions.Token(rank));
                    }
                    else
                    {
                        Chronicle.Add(_nview, rank == SettlementRole.Guest ? "$aoj_chr_member_left_by" : "$aoj_chr_rank_by", shown, Permissions.Token(rank), actor);
                    }
                    return true;
                }
                case SettlementAction.AddSettlers:
                {
                    int count = package.ReadInt();
                    if (count < 0 || count > MaxBatch)
                    {
                        throw new ArgumentException($"invalid batch size {count}");
                    }
                    if (!role.Allows(SettlementRight.Live))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to accept settlers without being a member");
                        return false;
                    }
                    int capacity = CapacityOf(data);
                    bool changed = false;
                    for (int i = 0; i < count; i++)
                    {
                        long uid = package.ReadLong();
                        string name = package.ReadString();
                        if (data.TryAddSettler(uid, name, capacity))
                        {
                            changed = true;
                            Log.Info(Module, $"{name} joined {DisplayName(data)} ({data.Settlers.Count}/{capacity})");
                            Chronicle.Add(_nview, "$aoj_chr_joined_by", data.FindSettler(uid).Name, actor);
                        }
                    }
                    return changed;
                }
                case SettlementAction.RemoveSettler:
                {
                    long uid = package.ReadLong();
                    // A settler's owner reports its death (or the server one that no longer exists); a settler that
                    // is not loaded here usually means it is already gone.
                    Settler settler = Settler.FindByUid(uid);
                    ZDO settlerZdo = settler != null ? settler.GetComponent<ZNetView>().GetZDO() : null;
                    bool ownerReport = settlerZdo == null || settlerZdo.GetOwner() == sender;
                    if (!ownerReport && !role.Allows(SettlementRight.Manage))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to remove a settler it does not own");
                        return false;
                    }
                    RosterEntry entry = data.FindSettler(uid);
                    bool removed = data.RemoveSettler(uid);
                    if (removed)
                    {
                        Log.Info(Module, $"{entry.Name} left {DisplayName(data)}");
                        bool alive = settler != null && !settler.GetComponent<Character>().IsDead();
                        bool dismissed = alive && role.Allows(SettlementRight.Manage);
                        if (dismissed)
                        {
                            Chronicle.Add(_nview, "$aoj_chr_dismissed_by", entry.Name, actor);
                        }
                        else if (alive)
                        {
                            // Its owner reports it alive and elsewhere: it was taken into another settlement
                            // (Settler.DropStaleRosters). Nobody died, nobody mourns.
                            Chronicle.Add(_nview, "$aoj_chr_moved", entry.Name);
                        }
                        else
                        {
                            Chronicle.Add(_nview, "$aoj_chr_lost", entry.Name);
                            Mourn();
                        }
                    }
                    return removed;
                }
                case SettlementAction.HandOver:
                {
                    long target = package.ReadLong();
                    string name = package.ReadString();
                    if (role != SettlementRole.Jarl || !data.HandOver(requesterId, target, name))
                    {
                        Log.Warning(Module, $"Handing over the title to player {target} from peer {sender} refused");
                        return false;
                    }
                    string shown = data.Members.Find(m => m.PlayerId == target)?.Name ?? name;
                    Log.Info(Module, $"{shown} is the new Jarl of {DisplayName(data)}");
                    Chronicle.Add(_nview, "$aoj_chr_handover", shown, actor);
                    return true;
                }
                case SettlementAction.SetZone:
                {
                    var zone = new SettlementZone
                    {
                        Id = package.ReadLong(),
                        Name = package.ReadString(),
                        Kind = package.ReadString(),
                        Center = package.ReadVector3(),
                        Radius = package.ReadSingle(),
                    };
                    if (!role.Allows(SettlementRight.Manage))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to mark an area without the rank for it");
                        return false;
                    }
                    if (float.IsNaN(zone.Radius) || float.IsInfinity(zone.Radius) || !IsFinite(zone.Center) ||
                        Utils.DistanceXZ(zone.Center, transform.position) > MaxZoneDistance)
                    {
                        Log.Warning(Module, $"Peer {sender} tried to mark an area too far from the table or with bad numbers");
                        return false;
                    }
                    if (zone.Id == 0L)
                    {
                        zone.Id = Keys.NewId();
                    }
                    bool added = data.FindZone(zone.Id) == null;
                    if (!data.SetZone(zone))
                    {
                        Log.Warning(Module, $"Area '{zone.Name}' not saved: {DisplayName(data)} has {SettlementData.MaxZones} areas already");
                        return false;
                    }
                    Log.Info(Module, $"Area '{zone.Name}' ({zone.Kind}, {zone.Radius:0} m) {(added ? "marked" : "changed")} in {DisplayName(data)} by {actor}");
                    return true;
                }
                case SettlementAction.RemoveZone:
                {
                    long zoneId = package.ReadLong();
                    if (!role.Allows(SettlementRight.Manage))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to remove an area without the rank for it");
                        return false;
                    }
                    SettlementZone gone = data.FindZone(zoneId);
                    if (gone == null || !data.RemoveZone(zoneId))
                    {
                        return false;
                    }
                    Log.Info(Module, $"Area '{gone.Name}' removed from {DisplayName(data)} by {actor}");
                    return true;
                }
                case SettlementAction.Rename:
                {
                    string name = package.ReadString();
                    if (!role.Allows(SettlementRight.Manage))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to rename the settlement without the rank for it");
                        return false;
                    }
                    bool changed = data.Rename(name);
                    if (changed)
                    {
                        Log.Info(Module, $"Settlement renamed to '{DisplayName(data)}'");
                        Chronicle.Add(_nview, "$aoj_chr_renamed_by", DisplayName(data), actor);
                    }
                    return changed;
                }
                case SettlementAction.UpgradeTier:
                {
                    int target = package.ReadInt();
                    TierDef next = TierAt(target);
                    if (!role.Allows(SettlementRight.Manage) || next == null || target != data.Tier + 1 || !IsUnlocked(next))
                    {
                        // E.g. both players paid at the same moment: the second request arrives one tier too late.
                        Log.Warning(Module, $"Upgrade to tier {target} from peer {sender} refused (settlement is at tier {data.Tier})");
                        return false;
                    }
                    data.Tier = target;
                    Log.Info(Module, $"{DisplayName(data)} reached tier {target}");
                    Chronicle.Add(_nview, "$aoj_chr_tier_by", "$aoj_tier_" + target, actor);
                    return true;
                }
                case SettlementAction.SetAlarm:
                {
                    bool on = package.ReadBool();
                    if (!role.Allows(SettlementRight.Military))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to sound the alarm without the rank for it");
                        return false;
                    }
                    Army.Alarm.Set(this, on, manual: true, by: actor);
                    return false;
                }
                case SettlementAction.Feast:
                {
                    if (!role.Allows(SettlementRight.Live))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to hold a feast without being a member");
                        return false;
                    }
                    // The requester already paid the mead and the food from its own inventory.
                    WorldClock.Set(_nview.GetZDO(), Keys.ZdoSettlementFeastUntil, WorldClock.Now + FeastDays * WorldClock.DayLength);
                    Chronicle.Add(_nview, "$aoj_chr_feast_by", actor);
                    Log.Info(Module, $"A feast in {DisplayName(data)}");
                    return false;
                }
                case SettlementAction.SetManualWork:
                {
                    bool manual = package.ReadBool();
                    if (!role.Allows(SettlementRight.Manage))
                    {
                        Log.Warning(Module, $"Peer {sender} tried to change how work is assigned without the rank for it");
                        return false;
                    }
                    _nview.GetZDO().Set(Keys.ZdoSettlementManualWork, manual);
                    return false;
                }
                case SettlementAction.Breach:
                {
                    // Any machine reports what it saw destroyed; the table takes only a real building, inside the
                    // settlement, while a siege is on - so a made-up report costs at most some Builder materials.
                    string prefab = package.ReadString();
                    Vector3 position = package.ReadVector3();
                    Quaternion rotation = package.ReadQuaternion();
                    if (!UnderSiege || Utils.DistanceXZ(position, transform.position) > Radius || !Breaches.IsRebuildable(prefab))
                    {
                        return false;
                    }
                    List<Breaches.Entry> entries = Breaches.Read(_nview.GetZDO());
                    if (!entries.Exists(e => (e.Position - position).sqrMagnitude <= Breaches.SameSpot * Breaches.SameSpot))
                    {
                        entries.Add(new Breaches.Entry { Prefab = prefab, Position = position, Rotation = rotation });
                        Breaches.Write(_nview.GetZDO(), entries);
                        Sieges.SiegeDirector.CountBreach(SettlementId);
                    }
                    return false;
                }
                case SettlementAction.Rebuilt:
                {
                    Vector3 position = package.ReadVector3();
                    List<Breaches.Entry> entries = Breaches.Read(_nview.GetZDO());
                    if (entries.RemoveAll(e => (e.Position - position).sqrMagnitude <= Breaches.SameSpot * Breaches.SameSpot) > 0)
                    {
                        Breaches.Write(_nview.GetZDO(), entries);
                    }
                    return false;
                }
                default:
                    Log.Warning(Module, $"Unknown settlement action {(int)action}");
                    return false;
            }
        }

        /// <summary>Role of a player in the settlement with this id; Guest if its table is not loaded here.</summary>
        internal static SettlementRole RoleIn(long settlementId, Player player)
        {
            JarlTable table = player != null ? FindById(settlementId) : null;
            SettlementData data = table != null ? table.Data : null;
            return data != null ? data.RoleOf(player.GetPlayerID()) : SettlementRole.Guest;
        }

        // ---------------------------------------------------------------- hover and area marker

        public string GetHoverText()
        {
            ShowMarker();
            SettlementData data = Data;
            if (data == null)
            {
                return Localize("$aoj_piece_jarltable");
            }

            // Called every frame while hovered: rebuilt only when the settlement changed.
            if (_hoverText == null || _hoverRevision != _dataRevision)
            {
                _hoverText = BuildHoverText(data);
                _hoverRevision = _dataRevision;
            }
            return _hoverText;
        }

        public string GetHoverName() => Localize("$aoj_piece_jarltable");

        public float GetHoverOffset() => 0f;

        private string BuildHoverText(SettlementData data)
        {
            var text = new StringBuilder();
            text.Append("<b>").Append(DisplayName(data)).Append("</b>\n").Append(BuildSummary(data));
            text.Append("\n[<color=yellow><b>$KEY_Use</b></color>] $aoj_manage");
            if (RoleOf(Player.m_localPlayer).Allows(SettlementRight.Live))
            {
                text.Append("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $aoj_accept_followers");
            }
            return Localize(text.ToString());
        }

        private const int MaxNamesShown = 6;

        /// <summary>Ranks, population, tier, the members' bonus and what the next tier needs, as tokens (hover and window).</summary>
        internal string BuildSummary(SettlementData data)
        {
            var text = new StringBuilder();
            List<string> jarls = data.NamesWith(SettlementRole.Jarl);
            text.Append(jarls.Count > 1 ? "$aoj_jarls: " : "$aoj_role_jarl: ").Append(jarls.Count > 0 ? string.Join(", ", jarls) : "?").Append('\n');
            text.Append("$aoj_hersirs: ").Append(NameList(data.NamesWith(SettlementRole.Hersir))).Append('\n');
            List<string> huskarls = data.NamesWith(SettlementRole.Huskarl);
            List<string> karls = data.NamesWith(SettlementRole.Karl);
            if (huskarls.Count > 0 || karls.Count > 0)
            {
                text.Append("$aoj_huskarls: ").Append(NameList(huskarls)).Append(" · $aoj_karls: ").Append(NameList(karls)).Append('\n');
            }
            text.Append("$aoj_settlers: ").Append(data.Settlers.Count).Append('/').Append(CapacityOf(data));
            text.Append(" · $aoj_beds: ").Append(data.SettlersWithBed).Append('/').Append(data.Settlers.Count).Append('\n');
            text.Append("$aoj_tier_").Append(data.Tier).Append(" · $aoj_radius ").Append(Mathf.RoundToInt(RadiusOf(data))).Append(" m\n");
            int extra = ExtraMembers(data);
            if (extra > 0)
            {
                text.Append("<color=#e0c080>").Append(Localize("$aoj_shared_bonus", data.Members.Count.ToString(),
                    (1f + AoJConfig.MemberBonus.Value * extra).ToString("0.##"),
                    Mathf.RoundToInt((RadiusOf(data) / RadiusFor(data.Tier) - 1f) * 100f).ToString(),
                    (AoJConfig.TotemSlotsPerMember.Value * extra).ToString())).Append("</color>\n");
            }
            text.Append("$aoj_your_role: ").Append(Permissions.Token(RoleOf(Player.m_localPlayer)));
            TierDef next = TierAt(data.Tier + 1);
            if (next != null)
            {
                text.Append("\n<color=#b0b0b0>$aoj_next_tier: $aoj_tier_").Append(next.Level).Append(" (").Append(RequirementText(next)).Append(')');
                if (next.Unlocks.Count > 0)
                {
                    text.Append("\n$aoj_unlocks: ").Append(string.Join(", ", next.Unlocks.Select(u => "$aoj_unlock_" + u)));
                }
                text.Append("</color>");
            }
            return text.ToString();
        }

        private static string NameList(List<string> names) =>
            names.Count == 0 ? "-"
            : names.Count <= MaxNamesShown ? string.Join(", ", names)
            : string.Join(", ", names.Take(MaxNamesShown)) + $" (+{names.Count - MaxNamesShown})";

        /// <summary>The roster for the window; * marks settlers with a bed.</summary>
        internal static string BuildRosterText(SettlementData data) =>
            data.Settlers.Count == 0
                ? "$aoj_no_settlers"
                : string.Join(", ", data.Settlers.Select(s => s.HasBed ? SettlerName(s) + "*" : SettlerName(s)));

        /// <summary>A renamed settler's new name lives in its own ZDO; the roster keeps the one it joined with.</summary>
        internal static string SettlerName(RosterEntry entry)
        {
            Settler settler = Settler.FindByUid(entry.Uid);
            return settler != null ? settler.DisplayName : entry.Name;
        }

        /// <summary>The settler a loaded settlement gave the bed at this spot to, or null.</summary>
        internal static string SettlerInBed(Vector3 bedPosition)
        {
            foreach (JarlTable table in Loaded)
            {
                SettlementData data = table.Data;
                if (data == null)
                {
                    continue;
                }
                foreach (RosterEntry entry in data.Settlers)
                {
                    if (entry.HasBed && (entry.BedPosition - bedPosition).sqrMagnitude <= SameSpotSqr)
                    {
                        return SettlerName(entry);
                    }
                }
            }
            return null;
        }

        internal static string DisplayName(SettlementData data) =>
            data.Name.Length > 0 ? data.Name : Localize("$aoj_settlement_default", data.Jarl?.Name ?? "?");

        internal void ShowMarker()
        {
            if (m_areaMarker == null)
            {
                return;
            }
            SetMarkerRadius(Radius);
            m_areaMarker.gameObject.SetActive(true);
            CancelInvoke(nameof(HideMarker));
            Invoke(nameof(HideMarker), MarkerSeconds);
        }

        private void HideMarker()
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.gameObject.SetActive(false);
            }
        }

        private void SetMarkerRadius(float radius)
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.m_radius = radius;
            }
        }

        private static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text, words) : text;
    }
}
