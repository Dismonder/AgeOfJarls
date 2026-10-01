using BepInEx.Configuration;
using Jotunn.Extensions;
using UnityEngine;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// Gameplay entries are bound with synced: true (admin-only, pushed from the server by Jotunn before the player
    /// enters the world); purely local preferences use synced: false.
    /// </summary>
    internal static class AoJConfig
    {
        internal static ConfigEntry<bool> DebugLogging;
        internal static ConfigEntry<bool> GuardWorldStartup;

        internal static ConfigEntry<float> SettlerBaseHealth;
        internal static ConfigEntry<float> SettlerFleeThreshold;
        internal static ConfigEntry<int> SettlerMaxTraits;
        internal static ConfigEntry<float> OriginTraitBonus;
        internal static ConfigEntry<bool> SettlerFriendlyFire;
        internal static ConfigEntry<float> SettlerFullHealSeconds;
        internal static ConfigEntry<bool> SettlerCollectLoot;
        internal static ConfigEntry<float> SettlerLootRange;
        internal static ConfigEntry<float> SettlerWanderRange;
        internal static ConfigEntry<bool> PermanentDeath;

        internal static ConfigEntry<float> SettlementMinDistance;
        internal static ConfigEntry<int> MaxJarls;
        internal static ConfigEntry<float> MemberBonus;
        internal static ConfigEntry<float> MemberRadiusBonus;
        internal static ConfigEntry<int> MaxScaledMembers;

        internal static ConfigEntry<bool> CommandGestures;
        internal static ConfigEntry<float> CommandRange;
        internal static ConfigEntry<KeyCode> CommandWheelKey;
        internal static ConfigEntry<float> CommandLookRange;

        internal static ConfigEntry<int> TotemBaseSlots;
        internal static ConfigEntry<int> TotemMaxSlots;
        internal static ConfigEntry<int> TotemSlotsPerMember;
        internal static ConfigEntry<float> TotemReach;
        internal static ConfigEntry<float> WorkSpeed;
        internal static ConfigEntry<int> CarryLimit;

        internal static ConfigEntry<float> SatietyPerDay;
        internal static ConfigEntry<float> HungryBelow;
        internal static ConfigEntry<bool> RefuseWorkWhenMiserable;

        internal static ConfigEntry<float> CatchUpMaxDays;
        internal static ConfigEntry<float> CatchUpDefaultRate;

        internal static ConfigEntry<float> CaptiveCampChance;
        internal static ConfigEntry<string> CaptiveCampLocations;
        internal static ConfigEntry<bool> Castaways;

        internal static ConfigEntry<bool> Sieges;
        internal static ConfigEntry<float> SiegeIntervalDays;
        internal static ConfigEntry<float> SiegeChance;
        internal static ConfigEntry<float> SiegeStrength;
        internal static ConfigEntry<float> SiegePerPlayer;

        internal static ConfigEntry<float> GearWear;

        internal static ConfigEntry<bool> SettlerBlocking;
        internal static ConfigEntry<int> AlarmMinThreats;
        internal static ConfigEntry<float> AlarmCooldownSeconds;
        internal static ConfigEntry<float> UiScale;

        internal static void Bind(ConfigFile config)
        {
            UiScale = config.BindConfig("UI", "Scale", 1.3f,
                "Size of the mod's windows (settler, Jarl's Table, totem) and their text; 1 = the original size. " +
                "A window is never bigger than the screen.", synced: false,
                acceptableValues: new AcceptableValueRange<float>(0.7f, 2f));
            SettlerBlocking = config.BindConfig("Settlers", "Blocking", true,
                "Settlers raise their shield (or weapon) against a blow about to land, time it for a perfect block once they " +
                "know the attacker's wind-up, and turn to the enemy that is striking them.", synced: true);
            AlarmMinThreats = config.BindConfig("Sieges", "AlarmMinThreats", 2,
                "Alerted hostile creatures inside the settlement before the alarm sounds by itself (a siege always sounds it). " +
                "1 = any single greydwarf that notices someone.", synced: true,
                acceptableValues: new AcceptableValueRange<int>(1, 20));
            AlarmCooldownSeconds = config.BindConfig("Sieges", "AlarmCooldownSeconds", 90f,
                "After an automatic alarm ends, no new automatic alarm for this long (a siege always sounds it).", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 600f));
            DebugLogging = config.BindConfig("General", "DebugLogging", false,
                "Write detailed Age of Jarls diagnostics to the BepInEx log.", synced: false);
            GuardWorldStartup = config.BindConfig("General", "GuardWorldStartup", true,
                "If another mod's ZNet.Awake postfix throws, log it and still load the world instead of hanging on the loading screen.",
                synced: false);

            SettlerBaseHealth = config.BindConfig("Settlers", "BaseHealth", 100f,
                "Maximum health of a settler before trait bonuses.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(10f, 1000f));
            SettlerFleeThreshold = config.BindConfig("Settlers", "FleeHealthThreshold", 0.3f,
                "Health fraction below which a settler runs from a fight (traits such as Brave change it).", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 0.9f));
            SettlerMaxTraits = config.BindConfig("Settlers", "MaxTraits", 3,
                "Maximum number of traits rolled for a new settler (always at least one).", synced: true,
                acceptableValues: new AcceptableValueRange<int>(1, 5));
            OriginTraitBonus = config.BindConfig("Settlers", "OriginTraitBonus", 2f,
                "Weight multiplier for traits typical of the biome a settler comes from.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(1f, 10f));
            SettlerFriendlyFire = config.BindConfig("Settlers", "FriendlyFire", false,
                "Allow players to damage settlers (weapons, arrows, fire, bombs).", synced: true);
            SettlerFullHealSeconds = config.BindConfig("Settlers", "FullHealSeconds", 1200f,
                "In-game seconds a settler needs to regenerate from 0 to full health (1200 = one day; vanilla NPCs: 3600).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(60f, 36000f));
            SettlerCollectLoot = config.BindConfig("Settlers", "CollectLoot", true,
                "Settlers pick up loot: around the player they follow (a settler without a settlement hands it to that player, " +
                "one with a settlement keeps it for its chests) and, by day, lying around their settlement.", synced: true);
            SettlerLootRange = config.BindConfig("Settlers", "LootRange", 10f,
                "How far (m) a following settler looks for loot.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(2f, 30f));
            SettlerWanderRange = config.BindConfig("Settlers", "WanderRange", 5f,
                "How far (m) an idle settler strolls around its home spot (the Jarl's Table by day, its bed at night) " +
                "or around the place where it was left.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(1f, 20f));
            PermanentDeath = config.BindConfig("Settlers", "PermanentDeath", false,
                "A settler whose health runs out dies for good and leaves its gear in a grave anyone can open. " +
                "Off (default): it is knocked out for a moment - nobody attacks it, it keeps all its gear - and gets up " +
                "with a little health.", synced: true);

            SettlementMinDistance = config.BindConfig("Settlement", "MinDistance", 150f,
                "Minimum distance in meters between two Jarl's Tables.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(30f, 1000f));
            MaxJarls = config.BindConfig("Settlement", "MaxJarls", 2,
                "How many players can rule one settlement together as Jarls with equal rights (the senior one alone may demote the other).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 4));
            MemberBonus = config.BindConfig("Settlement", "MemberBonus", 1f,
                "A settlement built by several players grows with them: each member (Karl and up) beyond the first adds this share " +
                "of the tier's settler limit. 1 = two players have twice the settlers.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 2f));
            MemberRadiusBonus = config.BindConfig("Settlement", "MemberRadiusBonus", 0.15f,
                "Each member beyond the first adds this share of the tier's radius (the area never exceeds 80 m, so it stays loaded " +
                "around a player at the table).", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 0.5f));
            MaxScaledMembers = config.BindConfig("Settlement", "MaxScaledMembers", 4,
                "Members counted for the bonuses above and for the extra worker places at the totems.", synced: true,
                acceptableValues: new AcceptableValueRange<int>(1, 10));

            CommandGestures = config.BindConfig("Commands", "Gestures", true,
                "Your character makes a matching gesture (come here, point, battle cry...) when you give an order from the command wheel.",
                synced: false);
            CommandRange = config.BindConfig("Commands", "FollowMeRange", 20f,
                "How far (m) \"Follow me!\" from the command wheel reaches settlers that follow nobody.", synced: false,
                acceptableValues: new AcceptableValueRange<float>(5f, 60f));
            CommandWheelKey = config.BindConfig("Commands", "WheelKey", KeyCode.H,
                "Opens the command wheel straight away, the way T opens the emotes: the orders of the settler you look at " +
                "(from afar too), otherwise your squad's orders with the creature you look at as the target. " +
                "The game's wheel (G) also gives a settler's orders when your crosshair is on it.", synced: false);
            CommandLookRange = config.BindConfig("Commands", "LookRange", 40f,
                "How far (m) the command wheel reaches the settler or the creature you look at.", synced: false,
                acceptableValues: new AcceptableValueRange<float>(5f, 50f));

            TotemBaseSlots = config.BindConfig("Work", "TotemBaseSlots", 2,
                "Workers a Work Totem takes in a new settlement; one more every second tier.", synced: true,
                acceptableValues: new AcceptableValueRange<int>(1, 10));
            TotemMaxSlots = config.BindConfig("Work", "TotemMaxSlots", 6,
                "Most workers one Work Totem ever takes (before the members' bonus below).", synced: true,
                acceptableValues: new AcceptableValueRange<int>(1, 20));
            TotemSlotsPerMember = config.BindConfig("Work", "TotemSlotsPerMember", 1,
                "Extra workers every Work Totem takes for each settlement member beyond the first.", synced: true,
                acceptableValues: new AcceptableValueRange<int>(0, 5));
            TotemReach = config.BindConfig("Work", "TotemReach", 50f,
                "How far (m) beyond a settlement's edge its Work Totems may stand (a forest, a mine nearby); a totem serves " +
                "the settlement it stands in, else the one whose edge is nearest. Workers only go to a totem that is loaded, " +
                "so one far from where players are works only while somebody is around.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 150f));
            WorkSpeed = config.BindConfig("Work", "WorkSpeed", 1f,
                "Multiplier for how fast settlers work (swings, repairs, loading), live and while nobody is around.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0.1f, 5f));
            // A new key, not WorkerLoad: existing config files kept the old default of 40 and would never get "no limit".
            CarryLimit = config.BindConfig("Work", "CarryLimit", 0,
                "Items a worker gathers before carrying them to the chests; 0 = no limit: it carries its whole take and goes " +
                "when its bag is nearly full or its work is done (Strong settlers carry more when there is a limit).", synced: true,
                acceptableValues: new AcceptableValueRange<int>(0, 1000));

            SatietyPerDay = config.BindConfig("Needs", "SatietyPerDay", 100f,
                "How much satiety (0-100) a settler loses per game day. Two good meals a day cover 100.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 400f));
            HungryBelow = config.BindConfig("Needs", "HungryBelow", 45f,
                "Satiety below which a settler goes to eat from the Settlement Cauldron.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(5f, 95f));
            RefuseWorkWhenMiserable = config.BindConfig("Needs", "RefuseWorkWhenMiserable", true,
                "Settlers with morale below 15 stop working until things get better (food, a bed).", synced: true);

            CatchUpMaxDays = config.BindConfig("CatchUp", "MaxDays", 2f,
                "When you come back to a settlement, at most this many game days of work are credited (0 = off).", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 30f));
            CatchUpDefaultRate = config.BindConfig("CatchUp", "DefaultItemsPerDay", 120f,
                "Items one worker delivers per game day while you are away, until its own pace has been measured live.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 5000f));

            CaptiveCampChance = config.BindConfig("Recruitment", "CaptiveCampChance", 0.4f,
                "Chance that a camp from the list below holds a captive settler (decided once per camp, also in old worlds).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
            CaptiveCampLocations = config.BindConfig("Recruitment", "CaptiveCampLocations",
                "Greydwarf_camp,GoblinCamp,DraugrVillage,SwampHut,Mistlands_DvergrTown,Charred,MountainCave",
                "Parts of vanilla location names (comma separated) that may hold captives.", synced: true);
            Castaways = config.BindConfig("Recruitment", "Castaways", true,
                "When the first Jarl's Table of the world is built, castaways reach the nearest shore.", synced: true);

            Sieges = config.BindConfig("Sieges", "Enabled", true,
                "Settlements from tier 1 are sometimes besieged while a member is at home.", synced: true);
            SiegeIntervalDays = config.BindConfig("Sieges", "IntervalDays", 3f,
                "Game days between siege checks of one settlement.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0.5f, 60f));
            SiegeChance = config.BindConfig("Sieges", "Chance", 0.35f,
                "Chance that a siege check starts a siege.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 1f));
            SiegeStrength = config.BindConfig("Sieges", "Strength", 1f,
                "Multiplier for the number of attackers.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0.1f, 5f));
            SiegePerPlayer = config.BindConfig("Sieges", "PerPlayer", 0.25f,
                "Extra attackers for each player at home beyond the first, as a share of the wave (more defenders, bigger sieges).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 2f));

            GearWear = config.BindConfig("Army", "GearWear", 0.5f,
                "How fast soldiers wear out their weapons, as a share of a player's rate (0 = never). A worn-out weapon goes " +
                "back to the Armory for a player to repair, and the soldier takes a working one.", synced: true,
                acceptableValues: new AcceptableValueRange<float>(0f, 2f));
        }
    }
}
