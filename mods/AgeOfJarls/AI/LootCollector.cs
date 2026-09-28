using System;
using System.Collections.Generic;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// Picks up loot (creature drops and the like). Only items no player ever held are loot
    /// (<see cref="ItemDrop.ItemData.m_pickedUp"/> is false), so things a player dropped on purpose are left alone.
    /// Following a player, a settler without a settlement hands its loot to that player; one with a settlement keeps
    /// it for the chests at home. At home it also clears loot lying around the settlement. Runs on the ZDO owner.
    /// </summary>
    internal sealed class LootCollector
    {
        private const string Module = "AI";
        private const float ScanInterval = 1f;
        private const float PickupDistance = 1.2f;
        /// <summary>Where the path ends short of a drop (on a table, a low shelf), it may still be taken from this far.</summary>
        private const float ReachDistance = 2f;
        private const float ForgetUnreachableSeconds = 60f;
        private const float HandOverDistance = 2.5f;
        /// <summary>Loot farther than this from the leader is ignored, so a companion never wanders off.</summary>
        private const float LeashRange = 12f;

        // Main thread only; shared by all settlers like the player's auto-pickup buffer.
        private static readonly Collider[] s_hits = new Collider[64];
        private static int s_itemMask;

        private readonly SettlerAI _ai;
        private readonly Humanoid _humanoid;
        private readonly PathMover _mover;
        private readonly HashSet<ItemDrop> _unreachable = new HashSet<ItemDrop>();
        private ItemDrop _target;
        private float _scanTimer;
        private float _forgetTimer;

        internal LootCollector(SettlerAI ai, Humanoid humanoid, PathMover mover)
        {
            _ai = ai;
            _humanoid = humanoid;
            _mover = mover;
        }

        /// <summary>For aoj_debug: the item it is walking to, if any.</summary>
        internal string DebugState() =>
            _target == null ? "-" : $"{_target.m_itemData.m_shared.m_name} {Vector3.Distance(_ai.transform.position, _target.transform.position):0} m";

        /// <summary>While following <paramref name="leader"/>: loot near them, handed over when <paramref name="handOver"/>.</summary>
        internal void Follow(float dt, Player leader, bool handOver)
        {
            if (!AoJConfig.SettlerCollectLoot.Value)
            {
                _target = null;
                return;
            }
            if (handOver && Vector3.Distance(_ai.transform.position, leader.transform.position) <= HandOverDistance)
            {
                HandOver(leader);
            }
            Collect(dt, _ai.transform.position, AoJConfig.SettlerLootRange.Value, leader.transform.position, LeashRange, null);
        }

        /// <summary>At home: loot inside the settlement that has a chest to go to. True while busy with a drop.</summary>
        internal bool CollectAtHome(float dt, Vector3 center, float radius, Predicate<ItemDrop.ItemData> storable)
        {
            if (!AoJConfig.SettlerCollectLoot.Value)
            {
                _target = null;
                return false;
            }
            return Collect(dt, center, radius, center, radius, storable);
        }

        internal void Reset() => _target = null;

        private float _busyAt = float.MinValue;

        /// <summary>Whether it went for or picked up loot in the last <paramref name="seconds"/> (still gathering).</summary>
        internal bool BusyWithin(float seconds) => Time.time - _busyAt < seconds;

        /// <summary>On its way to a drop: finished before anything else takes over.</summary>
        internal bool HasTarget => _target != null;

        private bool Collect(float dt, Vector3 scanCenter, float scanRadius, Vector3 leashCenter, float leash, Predicate<ItemDrop.ItemData> wanted)
        {
            _forgetTimer -= dt;
            if (_forgetTimer <= 0f)
            {
                _forgetTimer = ForgetUnreachableSeconds;
                _unreachable.Clear();
            }
            _scanTimer -= dt;
            if (!IsValidTarget(_target, leashCenter, leash, wanted) && _scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                ItemDrop found = FindLoot(scanCenter, scanRadius, leashCenter, leash, wanted);
                if (found != null && found != _target)
                {
                    _mover.Reset();
                    if (AiTrace.On)
                    {
                        AiTrace.Write(_ai, $"loot {found.m_itemData.m_shared.m_name} x{found.m_itemData.m_stack} {Vector3.Distance(_ai.transform.position, found.transform.position):0} m");
                    }
                }
                _target = found;
            }
            if (!IsValidTarget(_target, leashCenter, leash, wanted))
            {
                _target = null;
                return false;
            }
            _busyAt = Time.time;

            float distance = Vector3.Distance(_ai.transform.position, _target.transform.position);
            if (distance > PickupDistance)
            {
                // Walked like any chore: PathMover jogs to far loot by itself.
                MoveResult move = _mover.MoveTo(dt, _target.transform.position, PickupDistance * 0.5f, ReachDistance, run: false);
                if (move == MoveResult.Moving)
                {
                    return true;
                }
                if (move == MoveResult.Blocked || distance > ReachDistance)
                {
                    // On a roof, behind a wall...: left alone for a while, the next scan looks for something else.
                    _unreachable.Add(_target);
                    _target = null;
                    return true;
                }
            }
            // Like the player's auto-pickup: take ownership of the drop first, pick it up once it arrives.
            if (!_target.CanPickup(false))
            {
                _target.RequestOwn();
                return true;
            }
            int stack = _target.m_itemData.m_stack;
            if (_humanoid.Pickup(_target.gameObject, autoequip: false, autoPickupDelay: false))
            {
                _picked += stack;
            }
            _target = null;
            // Straight on to the next drop instead of standing there until the next scan.
            _scanTimer = 0f;
            return true;
        }

        private int _picked;

        /// <summary>Items picked up since the last call (a job counts them as its output).</summary>
        internal int TakePicked()
        {
            int picked = _picked;
            _picked = 0;
            return picked;
        }

        /// <summary>Loot in a work zone (a job's own drops and anything lying there). True while busy with a drop.</summary>
        internal bool CollectInZone(float dt, Vector3 center, float radius, Predicate<ItemDrop.ItemData> wanted) =>
            Collect(dt, center, radius, center, radius, wanted);

        private ItemDrop FindLoot(Vector3 scanCenter, float scanRadius, Vector3 leashCenter, float leash, Predicate<ItemDrop.ItemData> wanted)
        {
            if (s_itemMask == 0)
            {
                s_itemMask = LayerMask.GetMask("item");
            }

            ItemDrop best = null;
            float bestSqr = float.MaxValue;
            Vector3 origin = _ai.transform.position;
            int count = Physics.OverlapSphereNonAlloc(scanCenter, scanRadius, s_hits, s_itemMask);
            for (int i = 0; i < count; i++)
            {
                ItemDrop drop = s_hits[i].GetComponentInParent<ItemDrop>();
                if (!IsValidTarget(drop, leashCenter, leash, wanted))
                {
                    continue;
                }
                float sqr = (drop.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = drop;
                    bestSqr = sqr;
                }
            }
            return best;
        }

        private bool IsValidTarget(ItemDrop drop, Vector3 leashCenter, float leash, Predicate<ItemDrop.ItemData> wanted) =>
            drop != null
            && !drop.m_itemData.m_pickedUp
            && !_unreachable.Contains(drop)
            && Vector3.Distance(drop.transform.position, leashCenter) <= leash
            && _humanoid.GetInventory().CanAddItem(drop.m_itemData)
            && (wanted == null || wanted(drop.m_itemData));

        // Everything the settler carries that is loot (never held by a player) and not in use goes to the leader.
        private void HandOver(Player leader)
        {
            Inventory carried = _humanoid.GetInventory();
            foreach (ItemDrop.ItemData item in carried.GetAllItems().ToArray())
            {
                if (item.m_equipped || item.m_pickedUp)
                {
                    continue;
                }

                carried.RemoveItem(item);
                if (leader == Player.m_localPlayer && leader.GetInventory().AddItem(item))
                {
                    leader.Message(MessageHud.MessageType.TopLeft, "$msg_added " + item.m_shared.m_name, item.m_stack, item.GetIcon());
                    continue;
                }

                // Leader on another machine (or out of room): drop it at their feet for their auto-pickup. Marked as
                // player-held so no settler picks it up again.
                ItemDrop drop = ItemDrop.DropItem(item, item.m_stack, leader.transform.position + Vector3.up * 0.5f, Quaternion.identity);
                if (drop != null)
                {
                    drop.m_itemData.m_pickedUp = true;
                    drop.Save();
                }
                Log.Debug(Module, $"{_ai.name} handed {item.m_stack}x {item.m_shared.m_name} to {leader.GetPlayerName()}");
            }
        }
    }
}
