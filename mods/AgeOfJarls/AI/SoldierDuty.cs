using System.Collections.Generic;
using System.Linq;
using AgeOfJarls.Army;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// A soldier's duty at home: with missing gear it first takes the best the Armory has for its role (a weapon; a
    /// shield for shieldbearers; a bow and arrows for archers; any armour piece it lacks), then it stands at its post,
    /// a War Banner of the role's kind (or a Rally banner). Soldiers with a job only man their posts during an alarm.
    /// Fighting itself is vanilla: from its post the soldier engages what comes into view.
    /// </summary>
    internal sealed class SoldierDuty
    {
        private const float PostReach = 3f;
        private const float GearCheckSeconds = 10f;
        private const float ArmoryReach = 2.5f;
        private const int Arrows = 50;

        private readonly SettlerAI _ai;
        private readonly Settler _settler;
        private readonly SettlerCharacter _body;
        private readonly PathMover _mover;
        private readonly List<Armory> _armories = new List<Armory>();
        private const float PostCheckSeconds = 2f;

        private float _gearTimer;
        private Armory _armory;
        private Armory _armoryColliderOwner;
        private Collider[] _armoryColliders = new Collider[0];
        private const float ApproachReach = 0.8f;
        private const float ApproachRetrySeconds = 2f;
        private Vector3? _approach;
        private float _approachAt;
        private float _accessTimer;
        private float _askTimer;
        private WarBanner _post;
        private float _postTimer;

        internal SoldierDuty(SettlerAI ai, Settler settler, SettlerCharacter body, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _body = body;
            _mover = mover;
            // Soldiers loaded in the same frame must not all check gear and posts in the same frame ever after.
            _gearTimer = UnityEngine.Random.Range(0f, GearCheckSeconds);
            _postTimer = UnityEngine.Random.Range(0f, PostCheckSeconds);
        }

        /// <summary>True while busy with its duty (arming or standing guard).</summary>
        internal bool Update(float dt, JarlTable table, bool alarm)
        {
            CombatRole role = _settler.Role;
            if (role == CombatRole.None || (_settler.JobId != 0L && !alarm))
            {
                return false;
            }

            if (Arm(dt, table, role))
            {
                _settler.SetActivity(SettlerActivity.Arming);
                return true;
            }

            // On the timer only: without a free banner, asking every frame would scan all banners and settlers.
            _postTimer -= dt;
            if (_postTimer <= 0f)
            {
                _postTimer = PostCheckSeconds;
                _post = Posts.Choose(_settler, table, CombatRoles.PostKind(role)) ?? Posts.Choose(_settler, table, BannerKind.Rally);
            }
            WarBanner post = _post;
            if (post == null)
            {
                _settler.SetPost(0L);
                return false;
            }
            _settler.SetPost(post.Id);
            _body.GetUp();
            _settler.SetActivity(SettlerActivity.OnGuard);
            if (Vector3.Distance(_ai.transform.position, post.transform.position) > WarBanner.PostRadius)
            {
                _mover.MoveTo(dt, post.transform.position, PostReach * 0.6f, PostReach + 1f, run: alarm);
            }
            else
            {
                // At its post it stands guard: vanilla idle movement would stroll around its home spot instead.
                _ai.StopMoving();
            }
            return true;
        }

        internal void Stop()
        {
            _armory = null;
        }

        /// <summary>
        /// The gear of its role stays with a soldier: a worker's weapon leaves its hand for the tool, and must not go to
        /// the chests with the loot (it would stand at its post without it at the next alarm).
        /// </summary>
        internal bool Keeps(ItemDrop.ItemData item)
        {
            CombatRole role = _settler.Role;
            return role != CombatRole.None && !Posts.IsBroken(item) &&
                   (Posts.WeaponFor(role)(item) || Posts.IsArmor(item) ||
                    (Posts.IsShield(item) && role != CombatRole.Archer && role != CombatRole.Berserker));
        }

        // Gear is checked every few seconds; fetching walks to the nearest Armory holding what is missing.
        private bool Arm(float dt, JarlTable table, CombatRole role)
        {
            if (_armory == null)
            {
                _gearTimer -= dt;
                if (_gearTimer > 0f)
                {
                    return false;
                }
                _gearTimer = GearCheckSeconds;
                System.Predicate<ItemDrop.ItemData> wanted = Wanted(role);
                if (wanted == null)
                {
                    // Nothing missing, but a worker called to its post still holds its tool: the role's gear goes on.
                    if (EquipBest(role))
                    {
                        _settler.MarkInventoryDirty();
                    }
                    return false;
                }
                _armory = NearestArmoryWith(table, wanted);
                _accessTimer = 0f;
                if (AiTrace.On)
                {
                    AiTrace.Write(_ai, _armory != null
                        ? $"lacks gear: to the Armory {Vector3.Distance(_ai.transform.position, _armory.transform.position):0} m away"
                        : $"lacks gear, no Armory has it ({Armory.Loaded.Count} loaded)");
                }
                if (_armory == null)
                {
                    return false;
                }
            }

            if (_armory == null || _armory.Container == null || !SettlementStorage.IsUsable(_armory.Container))
            {
                _armory = null;
                return false;
            }
            // Its side, not its middle: a rack's middle lies in a hole of the navmesh. A side a path reaches: the one
            // facing the soldier may stand against a wall (see WorkScanner.FindApproach).
            Vector3 position = _ai.transform.position;
            if (_armoryColliderOwner != _armory)
            {
                _armoryColliderOwner = _armory;
                _armoryColliders = Work.WorkScanner.SolidColliders(_armory);
                _approach = null;
                _approachAt = 0f;
            }
            Vector3 target = Work.WorkScanner.NearestPoint(_armoryColliders, position, _armory.transform.position);
            bool atApproach = _approach != null && Vector3.Distance(position, _approach.Value) <= ApproachReach;
            if (Vector3.Distance(position, target) > ArmoryReach && !atApproach)
            {
                if (_approach == null && Time.time >= _approachAt)
                {
                    _approachAt = Time.time + ApproachRetrySeconds;
                    if (Work.WorkScanner.FindApproach(position, _armoryColliders, _armory.transform.position, ArmoryReach, _ai.m_pathAgentType, out Vector3 spot))
                    {
                        _approach = spot;
                    }
                }
                MoveResult move = _approach != null
                    ? _mover.MoveTo(dt, _approach.Value, ApproachReach * 0.6f, ApproachReach, run: false)
                    : _mover.MoveTo(dt, target, ArmoryReach * 0.6f, ArmoryReach, run: false);
                if (move == MoveResult.Blocked)
                {
                    _armory = null;
                    _approach = null;
                }
                return true;
            }
            _ai.StopMoving();
            // Asked once a second, not every frame: the Armory's owner (another machine) hands it over within a moment.
            _askTimer -= dt;
            bool ask = _askTimer <= 0f;
            if (ask)
            {
                _askTimer = 1f;
            }
            if (!ChestAccess.Acquire(_armory.Container, ask))
            {
                _accessTimer += dt;
                if (_accessTimer > 5f)
                {
                    _armory = null;
                }
                return true;
            }

            // Worn-out weapons go back to the Armory, where a player can take them to be repaired.
            Inventory bag = _body.GetInventory();
            foreach (ItemDrop.ItemData broken in bag.GetAllItems().Where(Posts.IsBroken).ToList())
            {
                _armory.Container.GetInventory().MoveItemToThis(bag, broken);
            }
            System.Predicate<ItemDrop.ItemData> want = Wanted(role);
            if (want != null)
            {
                SettlementStorage.TakeFrom(_armory.Container, bag, item => want(item) && !Posts.IsBroken(item), role == CombatRole.Archer ? Arrows : 1);
            }
            EquipBest(role);
            _settler.MarkInventoryDirty();
            _settler.FlushInventory();
            _armory = null;
            _gearTimer = 0f;
            return true;
        }

        // What the soldier still lacks, as one predicate over armory items; null when fully equipped.
        private System.Predicate<ItemDrop.ItemData> Wanted(CombatRole role)
        {
            List<ItemDrop.ItemData> bag = _body.GetInventory().GetAllItems();
            System.Func<ItemDrop.ItemData, bool> roleWeapon = Posts.WeaponFor(role);
            System.Func<ItemDrop.ItemData, bool> weaponFits = item => roleWeapon(item) && !Posts.IsBroken(item);
            ItemDrop.ItemData bow = bag.FirstOrDefault(item => Posts.IsBow(item) && !Posts.IsBroken(item));
            bool needWeapon = !bag.Any(weaponFits);
            bool needShield = role == CombatRole.Shieldbearer && !bag.Exists(Posts.IsShield);
            bool needArrows = role == CombatRole.Archer && bow != null && bag.Where(i => Posts.IsArrowFor(i, bow)).Sum(i => i.m_stack) < 10;
            var missingArmor = new HashSet<ItemDrop.ItemData.ItemType>(new[]
            {
                ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            }.Where(type => !bag.Exists(i => i.m_shared.m_itemType == type)));
            if (!needWeapon && !needShield && !needArrows && missingArmor.Count == 0)
            {
                return null;
            }
            // Worn-out gear in the Armory waits for a player to repair it.
            return item => !Posts.IsBroken(item) && (
                (needWeapon && weaponFits(item)) ||
                (needShield && Posts.IsShield(item)) ||
                (needArrows && Posts.IsArrowFor(item, bow)) ||
                (Posts.IsArmor(item) && missingArmor.Contains(item.m_shared.m_itemType)));
        }

        private Armory NearestArmoryWith(JarlTable table, System.Predicate<ItemDrop.ItemData> wanted)
        {
            float radius = table.Radius;
            return Armory.Loaded
                .Where(a => a != null && a.Container != null && Vector3.Distance(a.transform.position, table.transform.position) <= radius &&
                            a.Container.GetInventory().GetAllItems().Exists(wanted))
                .OrderBy(a => Vector3.Distance(a.transform.position, _ai.transform.position))
                .FirstOrDefault();
        }

        // The best of each kind goes in hand or on the body. True when anything was put on.
        private bool EquipBest(CombatRole role)
        {
            bool changed = false;
            List<ItemDrop.ItemData> bag = _body.GetInventory().GetAllItems();
            System.Func<ItemDrop.ItemData, bool> roleWeapon = Posts.WeaponFor(role);
            ItemDrop.ItemData weapon = bag.Where(i => roleWeapon(i) && !Posts.IsBroken(i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
            if (weapon != null && !weapon.m_equipped)
            {
                changed |= _body.EquipItem(weapon, triggerEquipEffects: false);
            }
            if (Posts.UsesShield(role, weapon))
            {
                ItemDrop.ItemData shield = bag.Where(Posts.IsShield).OrderByDescending(i => i.m_shared.m_blockPower).FirstOrDefault();
                if (shield != null && !shield.m_equipped)
                {
                    changed |= _body.EquipItem(shield, triggerEquipEffects: false);
                }
            }
            foreach (ItemDrop.ItemData armor in bag.Where(Posts.IsArmor).GroupBy(i => i.m_shared.m_itemType)
                         .Select(g => g.OrderByDescending(i => i.m_shared.m_armor).First()).ToList())
            {
                if (!armor.m_equipped)
                {
                    changed |= _body.EquipItem(armor, triggerEquipEffects: false);
                }
            }
            return changed;
        }
    }
}
