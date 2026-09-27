namespace AgeOfJarls.Settlers
{
    /// <summary>What a settler does at home, saved in its ZDO for the order window. Values are stored: only append.</summary>
    internal enum SettlerActivity
    {
        Idle = 0,
        Storing = 1,
        Sleeping = 2,
        /// <summary>Carries things that no chest of the settlement takes (none, all full, or all hold other things).</summary>
        NoChest = 3,
        Collecting = 4,
        Returning = 5,
        Working = 6,
        Eating = 7,
        /// <summary>Hungry, and no Settlement Cauldron with food.</summary>
        NoFood = 8,
        Sheltering = 9,
        OnGuard = 10,
        Arming = 11,
        /// <summary>Wounded, healing in bed.</summary>
        Recovering = 12,
    }
}
