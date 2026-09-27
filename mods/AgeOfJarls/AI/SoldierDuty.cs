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
        private float _accessTimer;
        private WarBanner _post;
        private float _postTimer;

        internal SoldierDuty(SettlerAI ai, Settler settler, SettlerCharacter body, PathMover mover)
        {
            _ai = ai;
            _settler = settler;
            _body = body;
            _mover = mover;
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

            _postTimer -= dt;
            if (_postTimer <= 0f || _post == null)
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
            return true;
        }

        internal void Stop()
        {
            _armory = null;
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
                    return false;
                }
                _armory = NearestArmoryWith(table, wanted);
                _accessTimer = 0f;
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
            Vector3 target = _armory.transform.position;
            if (Vector3.Distance(_ai.transform.position, target) > ArmoryReach)
            {
                if (_mover.MoveTo(dt, target, ArmoryReach * 0.6f, ArmoryReach, run: false) == MoveResult.Blocked)
                {
                    _armory = null;
                }
                return true;
            }
            if (!ChestAccess.Acquire(_armory.Container, ask: true))
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

        // The best of each kind goes in hand or on the body.
        private void EquipBest(CombatRole role)
        {
            List<ItemDrop.ItemData> bag = _body.GetInventory().GetAllItems();
            System.Func<ItemDrop.ItemData, bool> roleWeapon = Posts.WeaponFor(role);
            ItemDrop.ItemData weapon = bag.Where(i => roleWeapon(i) && !Posts.IsBroken(i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
            if (weapon != null && !weapon.m_equipped)
            {
                _body.EquipItem(weapon, triggerEquipEffects: false);
            }
            if (Posts.UsesShield(role, weapon))
            {
                ItemDrop.ItemData shield = bag.Where(Posts.IsShield).OrderByDescending(i => i.m_shared.m_blockPower).FirstOrDefault();
                if (shield != null && !shield.m_equipped)
                {
                    _body.EquipItem(shield, triggerEquipEffects: false);
                }
            }
            foreach (ItemDrop.ItemData armor in bag.Where(Posts.IsArmor).GroupBy(i => i.m_shared.m_itemType)
                         .Select(g => g.OrderByDescending(i => i.m_shared.m_armor).First()))
            {
                if (!armor.m_equipped)
                {
                    _body.EquipItem(armor, triggerEquipEffects: false);
                }
            }
        }
    }
}
