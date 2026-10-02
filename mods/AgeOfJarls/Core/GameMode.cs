namespace AgeOfJarls.Core
{
    /// <summary>How hard the settlers' lives are (General/Mode, the server's value applies to everyone).</summary>
    public enum GameMode
    {
        /// <summary>A knocked-out settler gets up by itself; workers carry their whole take.</summary>
        Chill,
        /// <summary>A knocked-out settler needs help within Settlers/RescueMinutes or dies; workers carry a limited load.</summary>
        Realistic,
    }
}
