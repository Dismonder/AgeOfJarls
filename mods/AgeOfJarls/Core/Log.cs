using BepInEx.Logging;

namespace AgeOfJarls.Core
{
    /// <summary>Every line carries a module tag; Debug lines only appear with General.DebugLogging on.</summary>
    internal static class Log
    {
        private static ManualLogSource _source;

        internal static void Init(ManualLogSource source) => _source = source;

        internal static void Info(string module, string message) => _source?.LogInfo($"[{module}] {message}");

        internal static void Warning(string module, string message) => _source?.LogWarning($"[{module}] {message}");

        internal static void Error(string module, string message) => _source?.LogError($"[{module}] {message}");

        internal static void Debug(string module, string message)
        {
            if (AoJConfig.DebugLogging != null && AoJConfig.DebugLogging.Value)
            {
                _source?.LogInfo($"[{module}] {message}");
            }
        }
    }
}
