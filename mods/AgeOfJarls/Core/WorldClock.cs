namespace AgeOfJarls.Core
{
    /// <summary>
    /// World time in seconds (the same on every machine, it pauses when nobody plays). ZDOs have no double, so times
    /// are stored as whole milliseconds in a long.
    /// </summary>
    internal static class WorldClock
    {
        internal static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        internal static double DayLength => EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1200.0;

        internal static double Get(ZDO zdo, int key, double defaultValue) => zdo.GetLong(key, (long)(defaultValue * 1000.0)) / 1000.0;

        internal static void Set(ZDO zdo, int key, double seconds) => zdo.Set(key, (long)(seconds * 1000.0));
    }
}
