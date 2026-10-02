using System.Collections.Generic;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// The settler's Humanoid. Takes its hover text from <see cref="Settler"/> (vanilla Character asks nothing but
    /// Tameable for it), keeps vanilla's tamed-creature skill logic away (settlers get their own skills) and lets the
    /// settler lie in a bed the way Player does. The pose is the synced "attach_bed" animation plus the synced position,
    /// so the other player sees it without running any of this. Unless Settlers/PermanentDeath is on, a settler whose
    /// health runs out is knocked out instead of dying (<see cref="KnockOut"/>).
    /// </summary>
    public class SettlerCharacter : Humanoid
    {
        private const string BedAnimation = "attach_bed";
        /// <summary>Where Bed.Interact puts a player who gets up.</summary>
        private static readonly Vector3 BedExitOffset = new Vector3(0f, 0.5f, 0f);

        private Settler _settler;
        private bool _lying;
        private Transform _bedPoint;
        private Collider[] _bedColliders;

        internal bool IsLyingDown => _lying;

        public override string GetHoverText()
        {
            if (_settler == null)
            {
                _settler = GetComponent<Settler>();
            }
            return _settler != null ? _settler.GetHoverText() : base.GetHoverText();
        }

        // Vanilla levels up the Tameable of a tamed creature here and warns on every hit when there is none.
        public override void RaiseSkill(Skills.SkillType skill, float value = 1f)
        {
        }

        // ---------------------------------------------------------------- bed

        /// <summary>Owner only: lie down in the bed until <see cref="GetUp"/>.</summary>
        internal void LieDown(Bed bed)
        {
            if (_lying || bed == null || !m_nview.IsValid() || !m_nview.IsOwner())
            {
                return;
            }
            FinishPassingThrough();
            _lying = true;
            _bedPoint = bed.m_spawnPoint != null ? bed.m_spawnPoint : bed.transform;
            _bedColliders = bed.GetComponentsInChildren<Collider>();
            SetBedCollisions(ignore: true);
            m_zanim.SetBool(BedAnimation, true);
            m_nview.GetZDO().Set(ZDOVars.s_inBed, true);
            PinToBed();
        }

        /// <summary>
        /// Owner only: get out of bed. Also clears a bed flag left in the ZDO by a previous owner, so it is safe to
        /// call every frame the settler should be up.
        /// </summary>
        internal void GetUp()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner())
            {
                return;
            }
            if (_lying)
            {
                if (_bedPoint != null)
                {
                    transform.position = _bedPoint.TransformPoint(BedExitOffset);
                    m_body.position = transform.position;
                }
                Release();
            }
            else if (!InBed())
            {
                return;
            }
            m_zanim.SetBool(BedAnimation, false);
            m_nview.GetZDO().Set(ZDOVars.s_inBed, false);
        }

        public override bool IsAttached() => _lying || base.IsAttached();

        public override bool InBed() => m_nview.IsValid() && m_nview.GetZDO().GetBool(ZDOVars.s_inBed);

        public override void CustomFixedUpdate(float fixedDeltaTime)
        {
            base.CustomFixedUpdate(fixedDeltaTime);
            if (_passThrough != null &&
                (Utils.DistanceXZ(transform.position, _passThroughFrom) > PassThroughClearance || Time.time - _passThroughSince > PassThroughSeconds))
            {
                FinishPassingThrough();
            }
            if (Down && m_nview.IsValid() && m_nview.IsOwner())
            {
                // Lies where it fell until the time is up: Chill gets up, Realistic without help dies.
                m_body.linearVelocity = Vector3.zero;
                if (!IsDownIn(m_nview.GetZDO()))
                {
                    // Switched to Chill while it waited: it gets up after all.
                    if (m_nview.GetZDO().GetBool(Keys.ZdoSettlerNeedsRescue) && AoJConfig.Realistic)
                    {
                        DieUnaided();
                    }
                    else
                    {
                        m_nview.GetZDO().Set(Keys.ZdoSettlerNeedsRescue, false);
                        StandUp();
                    }
                }
            }
            if (!_lying)
            {
                return;
            }
            if (!m_nview.IsValid() || !m_nview.IsOwner())
            {
                // Ownership moved on: the new owner decides whether the settler keeps sleeping.
                Release();
                return;
            }
            if (_bedPoint == null)
            {
                // The bed was destroyed under the sleeper.
                GetUp();
                return;
            }
            PinToBed();
            // Rest heals: 1% of health per second in bed (more in a settlement with healers), on top of vanilla regeneration.
            if (GetHealth() < GetMaxHealth())
            {
                Heal(GetMaxHealth() * BedHealPerSecond * BedHealing * fixedDeltaTime, false);
            }
        }

        /// <summary>
        /// Owner only: holds the block up or lets it down, the way a player's block key does; the game's own block logic
        /// (the timer for a perfect block, the animation, the synced flag) runs on it (<see cref="AI.CombatSense"/>).
        /// </summary>
        internal void SetBlocking(bool on) => m_blocking = on;

        private const float BedHealPerSecond = 0.01f;

        /// <summary>Multiplier for healing in bed; set by the settler each second from its settlement's perks.</summary>
        internal float BedHealing { get; set; } = 1f;

        private void PinToBed()
        {
            transform.position = _bedPoint.position;
            transform.rotation = _bedPoint.rotation;
            m_body.useGravity = false;
            m_body.linearVelocity = Vector3.zero;
            m_body.angularVelocity = Vector3.zero;
            // No fall damage for the drop from the bed when getting up.
            m_maxAirAltitude = transform.position.y;
        }

        // Collisions with the bed come back only once the settler is clear of it: restored at once, it could stand
        // wedged between the bed and a wall where it got up, unable to take a step.
        private void Release()
        {
            FinishPassingThrough();
            _passThrough = _bedColliders;
            _passThroughFrom = _bedPoint != null ? _bedPoint.position : transform.position;
            _passThroughSince = Time.time;
            _lying = false;
            _bedPoint = null;
            _bedColliders = null;
            m_body.useGravity = true;
        }

        /// <summary>
        /// Owner only: walks through these colliders (furniture it is wedged against) until it is clear of the spot
        /// it started from, as it does when leaving its bed.
        /// </summary>
        internal void PassThrough(List<Collider> colliders, Vector3 from)
        {
            FinishPassingThrough();
            foreach (Collider collider in colliders)
            {
                if (collider != null)
                {
                    Physics.IgnoreCollision(m_collider, collider, true);
                }
            }
            _passThrough = colliders.ToArray();
            _passThroughFrom = from;
            _passThroughSince = Time.time;
        }

        private const float PassThroughClearance = 1.5f;
        private const float PassThroughSeconds = 10f;
        private Collider[] _passThrough;
        private Vector3 _passThroughFrom;
        private float _passThroughSince;

        private void FinishPassingThrough()
        {
            if (_passThrough == null)
            {
                return;
            }
            foreach (Collider collider in _passThrough)
            {
                if (collider != null)
                {
                    Physics.IgnoreCollision(m_collider, collider, false);
                }
            }
            _passThrough = null;
        }

        // ---------------------------------------------------------------- knocked out

        /// <summary>How long a knocked-out settler stays down, and the share of its health it gets up with.</summary>
        private const float DownSeconds = 15f;
        private const float GetUpHealth = 0.1f;

        /// <summary>
        /// Knocked out: lies still, takes no damage and is nobody's enemy (<see cref="KnockoutPatches"/>). The owner
        /// sets it at once; every other machine reads it from the ZDO each second (Settler.Tick).
        /// </summary>
        internal bool Down { get; set; }

        internal static bool IsDownIn(ZDO zdo) => zdo != null && WorldClock.Get(zdo, Keys.ZdoSettlerDownUntil, 0.0) > WorldClock.Now;

        /// <summary>
        /// Owner only, in place of dying: the settler falls with a little health and all its gear, lies on the ground
        /// for a moment (the synced bed pose), then gets up; low on health, it keeps away from fights and heals.
        /// </summary>
        internal void KnockOut()
        {
            if (_lying)
            {
                // Caught in bed (fire, a raid): it falls out of it.
                Release();
                m_nview.GetZDO().Set(ZDOVars.s_inBed, false);
            }
            SetHealth(Mathf.Max(1f, GetMaxHealth() * GetUpHealth));
            ZDO zdo = m_nview.GetZDO();
            bool realistic = AoJConfig.Realistic;
            double downFor = realistic ? AoJConfig.RescueMinutes.Value * 60.0 : DownSeconds;
            WorldClock.Set(zdo, Keys.ZdoSettlerDownUntil, WorldClock.Now + downFor);
            zdo.Set(Keys.ZdoSettlerNeedsRescue, realistic);
            Down = true;
            m_zanim.SetBool(BedAnimation, true);
            m_body.linearVelocity = Vector3.zero;
            Player.MessageAllInRange(transform.position, KnockoutMessageRange, MessageHud.MessageType.TopLeft,
                realistic ? $"{GetHoverName()}: $aoj_msg_settler_down_rescue" : $"{GetHoverName()}: $aoj_msg_settler_down");
        }

        private const float KnockoutMessageRange = 40f;

        /// <summary>Realistic mode: nobody came in time. Dies for real (KnockoutPatches lets this death through).</summary>
        internal bool ForceDeath { get; private set; }

        /// <summary>Realistic mode: seconds a knocked-out settler can still wait for help (any machine).</summary>
        internal static double RescueSecondsLeft(ZDO zdo) =>
            zdo != null && zdo.GetBool(Keys.ZdoSettlerNeedsRescue) ? System.Math.Max(0.0, WorldClock.Get(zdo, Keys.ZdoSettlerDownUntil, 0.0) - WorldClock.Now) : 0.0;

        /// <summary>
        /// Owner only: helped up by a player or another settler - on its feet with a little health. Read from the ZDO,
        /// not <see cref="Down"/>: a machine that just took the settler over sets that only at its next tick. Too late
        /// (the time ran out) and the help does nothing: the next fixed update lets it die.
        /// </summary>
        internal bool Rescue()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner())
            {
                return false;
            }
            ZDO zdo = m_nview.GetZDO();
            if (!zdo.GetBool(Keys.ZdoSettlerNeedsRescue) || !IsDownIn(zdo))
            {
                return false;
            }
            zdo.Set(Keys.ZdoSettlerNeedsRescue, false);
            WorldClock.Set(zdo, Keys.ZdoSettlerDownUntil, 0.0);
            SetHealth(Mathf.Max(GetHealth(), GetMaxHealth() * GetUpHealth));
            StandUp();
            return true;
        }

        private void DieUnaided()
        {
            ZDO zdo = m_nview.GetZDO();
            zdo.Set(Keys.ZdoSettlerNeedsRescue, false);
            StandUp();
            ForceDeath = true;
            Player.MessageAllInRange(transform.position, KnockoutMessageRange, MessageHud.MessageType.TopLeft,
                $"{GetHoverName()}: $aoj_msg_settler_died_unaided");
            // No hit: armour, resistances and other mods' damage patches could soften one. The death check runs at
            // once: left to the next fixed update, the settler could change owners first, and the new owner (without
            // ForceDeath) would knock it out again.
            SetHealth(0f);
            CheckDeath();
        }

        private void StandUp()
        {
            Down = false;
            m_zanim.SetBool(BedAnimation, false);
        }

        private void SetBedCollisions(bool ignore)
        {
            if (_bedColliders == null)
            {
                return;
            }
            foreach (Collider collider in _bedColliders)
            {
                if (collider != null)
                {
                    Physics.IgnoreCollision(m_collider, collider, ignore);
                }
            }
        }
    }
}
