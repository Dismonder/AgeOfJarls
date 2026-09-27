using System.Collections.Generic;
using AgeOfJarls.Core;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>
    /// The settlement's alarm, run by the Jarl's Table owner: it sounds by itself when hostile creatures come inside the
    /// settlement and ends once it has been quiet for a while; the Jarl or a Hersir can also sound it (and end it) from
    /// the table. During an alarm civilians take shelter and the troop mans its posts (see HomeRoutine / SoldierDuty).
    /// </summary>
    internal static class Alarm
    {
        private const string Module = "Army";
        private const float QuietSeconds = 20f;

        private static readonly Dictionary<long, float> s_lastThreat = new Dictionary<long, float>();

        /// <summary>Owner of the table only (RPC action or the automatic check).</summary>
        internal static void Set(JarlTable table, bool on, bool manual)
        {
            ZNetView view = table.NetView;
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            bool was = zdo.GetBool(Keys.ZdoSettlementAlarm);
            zdo.Set(Keys.ZdoSettlementAlarm, on);
            zdo.Set(Keys.ZdoSettlementAlarmManual, on && manual);
            if (was == on)
            {
                return;
            }
            SettlementData data = table.Data;
            string name = data != null ? JarlTable.DisplayName(data) : "";
            Chronicle.Add(view, on ? "$aoj_chr_alarm" : "$aoj_chr_alarm_over");
            if (MessageHud.instance != null)
            {
                MessageHud.instance.MessageAll(MessageHud.MessageType.Center, on ? $"$aoj_msg_alarm {name}" : $"$aoj_msg_alarm_over {name}");
            }
            Log.Info(Module, on ? $"Alarm in {name}{(manual ? " (sounded by a player)" : "")}" : $"Alarm over in {name}");
        }

        /// <summary>Every few seconds on the table's owner.</summary>
        internal static void Check(JarlTable table)
        {
            long id = table.SettlementId;
            if (id == 0L)
            {
                return;
            }
            ZDO zdo = table.NetView.GetZDO();
            int threats = CountThreats(table);
            if (threats > 0)
            {
                s_lastThreat[id] = Time.time;
                if (!table.AlarmOn)
                {
                    Set(table, true, manual: false);
                }
                return;
            }
            bool manual = zdo.GetBool(Keys.ZdoSettlementAlarmManual);
            if (table.AlarmOn && !manual && Time.time - (s_lastThreat.TryGetValue(id, out float last) ? last : 0f) > QuietSeconds)
            {
                Set(table, false, manual: false);
            }
        }

        /// <summary>
        /// Hostile creatures inside the settlement that mean it: alerted ones (they noticed someone) and siege attackers.
        /// Not players, tamed creatures, settlers, grazing animals or dvergr; a greydwarf wandering by unaware (or any
        /// monster in a world with passive mobs) does not send everyone into hiding.
        /// </summary>
        internal static int CountThreats(JarlTable table)
        {
            float radius = table.Radius;
            Vector3 center = table.transform.position;
            int count = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (IsThreat(character) && Utils.DistanceXZ(character.transform.position, center) <= radius)
                {
                    count++;
                }
            }
            return count;
        }

        internal static bool IsThreat(Character character)
        {
            if (character == null || character.IsDead() || character.IsPlayer() || character.IsTamed() || character.GetComponent<Settler>() != null)
            {
                return false;
            }
            Character.Faction faction = character.GetFaction();
            if (faction == Character.Faction.Players || faction == Character.Faction.AnimalsVeg || faction == Character.Faction.Dverger ||
                faction == Character.Faction.PlayerSpawned)
            {
                return false;
            }
            ZNetView view = character.GetComponent<ZNetView>();
            bool attacker = view != null && view.IsValid() && view.GetZDO().GetLong(Keys.ZdoSiegeOf) != 0L;
            BaseAI ai = character.GetBaseAI();
            return attacker || (ai != null && ai.IsAlerted());
        }
    }
}
