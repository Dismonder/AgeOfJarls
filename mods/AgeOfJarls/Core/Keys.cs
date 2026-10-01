using System;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// Names shared over the network or stored in saves. Renaming one breaks compatibility.
    /// The game renumbers every ZDOID each time the world loads, so saved data never points at another object by
    /// ZDOID: objects carry their own stable id (<see cref="NewId"/>) and point at each other with it.
    /// </summary>
    internal static class Keys
    {
        internal const string RpcDefsSync = "AoJ_DefsSync";
        internal const string RpcSettlerCommand = "AoJ_SettlerCommand";
        internal const string RpcSettlerGive = "AoJ_SettlerGive";
        internal const string RpcSettlerTakeBack = "AoJ_SettlerTakeBack";
        internal const string RpcSettlementAction = "AoJ_SettlementAction";
        internal const string RpcSettlerGoHome = "AoJ_SettlerGoHome";
        internal const string RpcSettlerRename = "AoJ_SettlerRename";
        internal const string RpcSettlerAttack = "AoJ_SettlerAttack";
        internal const string RpcSettlerFallBack = "AoJ_SettlerFallBack";
        internal const string RpcChestRequest = "AoJ_ChestRequest";
        internal const string RpcSettlerSetJob = "AoJ_SettlerSetJob";
        internal const string RpcTotemConfig = "AoJ_TotemConfig";
        /// <summary>Owner to the player who paid for a totem upgrade it refused: take the materials back.</summary>
        internal const string RpcTotemRefund = "AoJ_TotemRefund";
        internal const string RpcSettlerRole = "AoJ_SettlerRole";
        internal const string RpcBannerConfig = "AoJ_BannerConfig";
        internal const string RpcSettlerFree = "AoJ_SettlerFree";
        internal const string RpcSettlerFeed = "AoJ_SettlerFeed";
        /// <summary>Routed to everybody: a settlement's alarm started or ended (Army.AlarmPins).</summary>
        internal const string RpcAlarmPin = "AoJ_AlarmPin";
        /// <summary>On a chest: give it a kind (Settlement.ChestLabels).</summary>
        internal const string RpcChestKind = "AoJ_ChestKind";
        /// <summary>On a settler: hand one item (or part of a stack) to the asking player (Settler.RPC_TakeItem).</summary>
        internal const string RpcSettlerTakeItem = "AoJ_SettlerTakeItem";
        /// <summary>On a settler, to the asking peer: a parcel for its player, into the bag or at its feet (Net.ItemDelivery).</summary>
        internal const string RpcSettlerDeliver = "AoJ_SettlerDeliver";
        /// <summary>On a settler, back to its owner: the parcel with this id arrived (Net.ItemDelivery).</summary>
        internal const string RpcSettlerDelivered = "AoJ_SettlerDelivered";
        /// <summary>On a settler, to the asking peer: a message for its player (a token and one argument).</summary>
        internal const string RpcSettlerNotify = "AoJ_SettlerNotify";
        /// <summary>On a settler: come along through the portal its leader just took - to this spot (Settler.RPC_Teleport).</summary>
        internal const string RpcSettlerTeleport = "AoJ_SettlerTeleport";

        /// <summary>Chest: the kind of things settlers put in it (a storage.json kind or a built-in one); "" = anything.</summary>
        internal static readonly int ZdoChestKind = "aoj_chest_kind".GetStableHashCode();

        internal const string SettlerPrefab = "AoJ_Settler";
        internal const string JarlTablePrefab = "AoJ_JarlTable";
        /// <summary>Work Totems are "AoJ_Totem_" + the job's name.</summary>
        internal const string TotemPrefabPrefix = "AoJ_Totem_";
        internal const string CauldronPrefab = "AoJ_Cauldron";
        internal const string ArmoryPrefab = "AoJ_Armory";
        internal const string BannerPrefab = "AoJ_WarBanner";

        /// <summary>Stable id of a Work Totem (see the class comment).</summary>
        internal static readonly int ZdoTotemId = "aoj_totem_id".GetStableHashCode();
        internal static readonly int ZdoTotemRadius = "aoj_totem_radius".GetStableHashCode();
        internal static readonly int ZdoTotemAllDay = "aoj_totem_allday".GetStableHashCode();
        /// <summary>0 low, 1 normal (default), 2 high: which totems free settlers take first.</summary>
        internal static readonly int ZdoTotemPriority = "aoj_totem_priority".GetStableHashCode();
        /// <summary>Woodcutter totem: replant every felled tree (off by default: clearing land stays clear).</summary>
        internal static readonly int ZdoTotemReplant = "aoj_totem_replant".GetStableHashCode();
        /// <summary>Totem level 1-3, bought with materials in its window: more worker places, faster work.</summary>
        internal static readonly int ZdoTotemLevel = "aoj_totem_level".GetStableHashCode();
        /// <summary>Settlement: players assign work themselves (free settlers do not take a free place at a totem).</summary>
        internal static readonly int ZdoSettlementManualWork = "aoj_settlement_manual_work".GetStableHashCode();
        /// <summary>Wood (or other output) delivered by the totem's workers and the seconds they worked, measured live for the catch-up.</summary>
        internal static readonly int ZdoTotemOutput = "aoj_totem_output".GetStableHashCode();
        internal static readonly int ZdoTotemWorkTime = "aoj_totem_worktime".GetStableHashCode();

        /// <summary>Stable id of the Work Totem the settler works at; 0 = no job.</summary>
        internal static readonly int ZdoSettlerJob = "aoj_settler_job".GetStableHashCode();
        /// <summary>What keeps the settler from working (a localization token), for the totem's hover on every machine.</summary>
        internal static readonly int ZdoSettlerProblem = "aoj_settler_problem".GetStableHashCode();
        /// <summary>Hunger, 0 (starving) to 100 (full), and morale, 0 to 100.</summary>
        internal static readonly int ZdoSettlerSatiety = "aoj_settler_satiety".GetStableHashCode();
        internal static readonly int ZdoSettlerMorale = "aoj_settler_morale".GetStableHashCode();
        /// <summary>The last meals ("name|name|name"), for the variety bonus.</summary>
        internal static readonly int ZdoSettlerMeals = "aoj_settler_meals".GetStableHashCode();
        /// <summary>World time of the last needs update, so time spent unloaded is counted too.</summary>
        internal static readonly int ZdoSettlerNeedsTime = "aoj_settler_needs_time".GetStableHashCode();
        /// <summary>Job experience per job ("aoj_skill_" + job), 0-100.</summary>
        internal const string SkillPrefix = "aoj_skill_";
        /// <summary>Items the settler produced at its job and the seconds it worked: its live pace, used by the catch-up.</summary>
        internal static readonly int ZdoSettlerWorkOutput = "aoj_settler_work_output".GetStableHashCode();
        internal static readonly int ZdoSettlerWorkTime = "aoj_settler_work_time".GetStableHashCode();
        /// <summary>Combat role (see Army.CombatRole) and the stable id of the banner it stands at.</summary>
        internal static readonly int ZdoSettlerRole = "aoj_settler_role".GetStableHashCode();
        internal static readonly int ZdoSettlerPost = "aoj_settler_post".GetStableHashCode();

        /// <summary>Settlement: world time of the last simulated moment (live or caught up), for the catch-up.</summary>
        internal static readonly int ZdoSettlementSimTime = "aoj_settlement_simtime".GetStableHashCode();
        /// <summary>Settlement: the chronicle, a small versioned blob of recent events.</summary>
        internal static readonly int ZdoSettlementChronicle = "aoj_settlement_chronicle".GetStableHashCode();
        /// <summary>Settlement: alarm on (civilians hide, soldiers man their posts), feast until (world time), fame.</summary>
        internal static readonly int ZdoSettlementAlarm = "aoj_settlement_alarm".GetStableHashCode();
        /// <summary>The alarm was sounded by a player (it stays on until a player ends it; an automatic one ends by itself).</summary>
        internal static readonly int ZdoSettlementAlarmManual = "aoj_settlement_alarm_manual".GetStableHashCode();
        /// <summary>On a creature spawned by a siege: the stable id of the besieged settlement.</summary>
        internal static readonly int ZdoSiegeOf = "aoj_siege_of".GetStableHashCode();
        /// <summary>World-wide global key: the castaways have arrived once.</summary>
        internal const string CastawaysKey = "aoj_castaways";
        internal static readonly int ZdoSettlementFeastUntil = "aoj_settlement_feast".GetStableHashCode();
        /// <summary>World time until which a recent loss or attack weighs on everyone's morale.</summary>
        internal static readonly int ZdoSettlementGriefUntil = "aoj_settlement_grief".GetStableHashCode();
        internal static readonly int ZdoSettlementFame = "aoj_settlement_fame".GetStableHashCode();
        /// <summary>Settlement: a siege is on (every machine reports destroyed buildings to the table meanwhile).</summary>
        internal static readonly int ZdoSettlementBesieged = "aoj_settlement_besieged".GetStableHashCode();
        /// <summary>Settlement: buildings a siege destroyed, for the Builders (see Settlement.Breaches).</summary>
        internal static readonly int ZdoSettlementBreaches = "aoj_settlement_breaches".GetStableHashCode();
        internal static readonly int ZdoSettlementNextSiege = "aoj_settlement_next_siege".GetStableHashCode();

        /// <summary>War banner: stable id, kind and post size.</summary>
        internal static readonly int ZdoBannerId = "aoj_banner_id".GetStableHashCode();
        internal static readonly int ZdoBannerKind = "aoj_banner_kind".GetStableHashCode();

        /// <summary>Location proxy: 1 = a captive camp was added here, 2 = the roll said no (decided once per location).</summary>
        internal static readonly int ZdoCampDecided = "aoj_camp".GetStableHashCode();
        /// <summary>Settler: a captive waiting to be freed (true until a player frees it).</summary>
        internal static readonly int ZdoSettlerCaptive = "aoj_settler_captive".GetStableHashCode();
        /// <summary>Settler: the captive was locked behind bars (breaking them frees it too).</summary>
        internal static readonly int ZdoSettlerCaged = "aoj_settler_caged".GetStableHashCode();
        /// <summary>Settler: sitting down (idle at home); every machine plays the sitting pose from it.</summary>
        internal static readonly int ZdoSettlerSitting = "aoj_settler_sitting".GetStableHashCode();
        /// <summary>Settler: world time until which it lies knocked out (see Settlers.SettlerCharacter.KnockOut).</summary>
        internal static readonly int ZdoSettlerDownUntil = "aoj_settler_down_until".GetStableHashCode();

        /// <summary>ZDO blob with the whole settlement (see Settlement.SettlementData), stored on the Jarl's Table.</summary>
        internal static readonly int ZdoSettlement = "aoj_settlement".GetStableHashCode();

        /// <summary>Stable id of the settlement, on its Jarl's Table (see the class comment).</summary>
        internal static readonly int ZdoSettlementId = "aoj_settlement_id".GetStableHashCode();

        /// <summary>Stable id of the settler, on the settler (see the class comment).</summary>
        internal static readonly int ZdoSettlerUid = "aoj_settler_uid".GetStableHashCode();

        /// <summary>ZDO blob with the settler's identity (see Settlers.SettlerIdentity).</summary>
        internal static readonly int ZdoSettlerIdentity = "aoj_settler_identity".GetStableHashCode();

        /// <summary>ZDO blob with the settler's inventory (vanilla Inventory.Save format).</summary>
        internal static readonly int ZdoSettlerInventory = "aoj_settler_inventory".GetStableHashCode();

        /// <summary>Incremented on every inventory save, so clients can tell whether their copy is stale.</summary>
        internal static readonly int ZdoSettlerInventoryRevision = "aoj_settler_inventory_rev".GetStableHashCode();

        /// <summary>Player ID the settler follows; 0 = waits at its patrol point.</summary>
        internal static readonly int ZdoSettlerFollow = "aoj_settler_follow".GetStableHashCode();

        /// <summary>True after a "wait here" order: the settler holds that spot instead of wandering around it.</summary>
        internal static readonly int ZdoSettlerHold = "aoj_settler_hold".GetStableHashCode();

        /// <summary>Stable id of the settlement the settler belongs to; 0 = homeless.</summary>
        internal static readonly int ZdoSettlerHomeId = "aoj_settler_home_sid".GetStableHashCode();

        /// <summary>Where that settlement's table stood: lets a settler tell a destroyed table from one that is merely far away.</summary>
        internal static readonly int ZdoSettlerHomePosition = "aoj_settler_home_pos".GetStableHashCode();

        /// <summary>What the settler is doing at home (<see cref="Settlers.SettlerActivity"/>), for every client's window.</summary>
        internal static readonly int ZdoSettlerActivity = "aoj_settler_activity".GetStableHashCode();
        /// <summary>An item it carries that no chest takes (its name token), so every machine's settler can say which.</summary>
        internal static readonly int ZdoSettlerNoChestItem = "aoj_settler_nochest_item".GetStableHashCode();

        /// <summary>A random, non-zero 64-bit id for <see cref="ZdoSettlementId"/> and <see cref="ZdoSettlerUid"/>.</summary>
        internal static long NewId()
        {
            long id;
            do
            {
                id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            }
            while (id == 0L);
            return id;
        }
    }
}
