using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// The settler's Humanoid. Takes its hover text from <see cref="Settler"/> (vanilla Character asks nothing but
    /// Tameable for it), keeps vanilla's tamed-creature skill logic away (settlers get their own skills) and lets the
    /// settler lie in a bed the way Player does. The pose is the synced "attach_bed" animation plus the synced position,
    /// so the other player sees it without running any of this.
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
            // Rest heals: 1% of health per second in bed, on top of vanilla regeneration.
            if (GetHealth() < GetMaxHealth())
            {
                Heal(GetMaxHealth() * BedHealPerSecond * fixedDeltaTime, false);
            }
        }

        private const float BedHealPerSecond = 0.01f;

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

        private void Release()
        {
            SetBedCollisions(ignore: false);
            _lying = false;
            _bedPoint = null;
            _bedColliders = null;
            m_body.useGravity = true;
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
