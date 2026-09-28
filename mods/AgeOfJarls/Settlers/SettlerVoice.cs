using AgeOfJarls.Core;
using AgeOfJarls.UI;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Short speech bubbles over a settler for the player standing near: a greeting when they come back after a while,
    /// and what is wrong - a job problem, hunger with an empty cauldron, no chest for its load - so the settlement tells
    /// its needs without opening a window. Every machine decides from the ZDO for its own player, so nothing goes over
    /// the network; and rarely: one bubble per settler per <see cref="MinGap"/>, the same line not again for
    /// <see cref="SameLineGap"/>.
    /// </summary>
    internal sealed class SettlerVoice
    {
        private const float Range = 12f;
        private const float GreetRange = 6f;
        private const float AwayBeforeGreeting = 90f;
        private const float MinGap = 45f;
        private const float SameLineGap = 180f;
        private const float ShowSeconds = 5f;
        private const int Greetings = 3;
        private static readonly Vector3 Offset = new Vector3(0f, 2.3f, 0f);

        /// <summary>No chorus: a line one settler said is not repeated by another for this long.</summary>
        private const float ChorusGap = 30f;
        private static readonly System.Collections.Generic.Dictionary<string, float> s_saidAt =
            new System.Collections.Generic.Dictionary<string, float>();

        private readonly Settler _settler;
        /// <summary>Settlers loaded together start at different moments instead of speaking in step.</summary>
        private float _nextBarkAt = Time.time + Random.Range(1f, 8f);
        private string _lastLine = "";
        private float _lastLineAt = float.MinValue;
        private float _lastNearAt = float.MinValue;
        private bool _greetPending;

        internal SettlerVoice(Settler settler)
        {
            _settler = settler;
        }

        /// <summary>About once a second, on every machine.</summary>
        internal void Tick(ZDO zdo, bool down)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Chat.instance == null || zdo == null || DebugOverlay.Enabled)
            {
                return;
            }
            float distance = Vector3.Distance(player.transform.position, _settler.transform.position);
            if (distance > Range)
            {
                _greetPending = false;
                return;
            }
            if (Time.time - _lastNearAt > AwayBeforeGreeting)
            {
                _greetPending = true;
            }
            _lastNearAt = Time.time;
            if (Time.time < _nextBarkAt || down)
            {
                return;
            }

            string line = Choose(zdo, player, distance);
            if (line == null || (line == _lastLine && Time.time - _lastLineAt < SameLineGap) ||
                (s_saidAt.TryGetValue(line, out float saidAt) && Time.time - saidAt < ChorusGap))
            {
                return;
            }
            _lastLine = line;
            _lastLineAt = Time.time;
            _nextBarkAt = Time.time + MinGap;
            if (s_saidAt.Count > 64)
            {
                s_saidAt.Clear();
            }
            s_saidAt[line] = Time.time;
            Chat.instance.SetNpcText(_settler.gameObject, Offset, Range + 8f, ShowSeconds, "", line, false);
            if (AI.AiTrace.On)
            {
                AI.AiTrace.Write(_settler, "says: " + line);
            }
        }

        // The most useful thing to say now, or null.
        private string Choose(ZDO zdo, Player player, float distance)
        {
            if (_settler.IsCaptive)
            {
                return Localize("$aoj_bark_captive");
            }
            var activity = (SettlerActivity)zdo.GetInt(Keys.ZdoSettlerActivity);
            if (activity == SettlerActivity.Sleeping)
            {
                return EnvMan.IsNight() ? Localize("$aoj_bark_sleep") : null;
            }
            if (_greetPending && distance <= GreetRange)
            {
                _greetPending = false;
                return Localize("$aoj_bark_greet_" + Random.Range(1, Greetings + 1), player.GetPlayerName());
            }
            string problem = _settler.JobProblem;
            if (problem.Length > 0)
            {
                string text = Localize(problem);
                return text.Length > 0 ? char.ToUpper(text[0]) + text.Substring(1) + "!" : null;
            }
            switch (activity)
            {
                case SettlerActivity.NoFood:
                    return Localize("$aoj_bark_hungry");
                case SettlerActivity.NoChest:
                    // Which thing, so the player knows what chest to add.
                    string item = zdo.GetString(Keys.ZdoSettlerNoChestItem);
                    return item.Length > 0 ? Localize("$aoj_bark_nochest_item", Localize(item)) : Localize("$aoj_bark_nochest");
                default:
                    return null;
            }
        }

        private static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text, words) : text;
    }
}
