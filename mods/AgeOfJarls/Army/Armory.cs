using System.Collections.Generic;
using UnityEngine;

namespace AgeOfJarls.Army
{
    /// <summary>
    /// The Armory: a chest the troop takes its gear from (the best weapon, shield, armour and arrows for each role).
    /// Settlers never store their loot in it. Taking gear uses the same chest hand-over as storing (ChestAccess).
    /// </summary>
    public class Armory : MonoBehaviour
    {
        internal static readonly List<Armory> Loaded = new List<Armory>();

        internal Container Container { get; private set; }

        private void Awake()
        {
            Container = GetComponent<Container>();
            ZNetView view = GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                Loaded.Add(this);
            }
        }

        private void OnDestroy()
        {
            Loaded.Remove(this);
        }
    }
}
