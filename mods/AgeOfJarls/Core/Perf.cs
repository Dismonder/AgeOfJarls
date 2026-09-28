using System;
using System.Collections.Generic;
using System.Diagnostics;
using AgeOfJarls.Settlement;
using AgeOfJarls.Settlers;
using UnityEngine;

namespace AgeOfJarls.Core
{
    /// <summary>
    /// aoj_perf: what the mod's own recurring work costs on this machine, per section, over a few seconds of play.
    /// Probes cost one bool check unless a measurement runs. Sections that run inside another one (planning, jobs,
    /// paths and inventory saves happen in the settler AI) are listed under it and not added to the total twice;
    /// the game's own AI for settlers is shown for comparison only.
    /// </summary>
    internal sealed class Perf : MonoBehaviour
    {
        internal enum Section
        {
            SettlerAi,
            Planning,
            Jobs,
            Paths,
            InventorySaves,
            SettlerTick,
            TableTick,
            Windows,
            VanillaAi,
        }

        private static readonly string[] Labels =
        {
            "settler AI (mod)", "  planning", "  jobs", "  paths", "  inventory saves", "settler tick", "table tick",
            "open windows", "settler AI (game's MonsterAI)",
        };

        private static readonly bool[] InTotal = { true, false, false, false, false, true, true, true, false };

        private static readonly long[] s_ticks = new long[Labels.Length];
        private static readonly long[] s_max = new long[Labels.Length];
        private static readonly int[] s_calls = new int[Labels.Length];
        private static bool s_on;

        private float _seconds;
        private float _elapsed;
        private int _frames;
        private float _worstFrame;

        /// <summary>A timestamp to pass to <see cref="Stop"/>, or 0 when nothing is measured.</summary>
        internal static long Start() => s_on ? Stopwatch.GetTimestamp() : 0L;

        internal static void Stop(Section section, long start)
        {
            if (start == 0L || !s_on)
            {
                return;
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            int index = (int)section;
            s_ticks[index] += elapsed;
            s_calls[index]++;
            if (elapsed > s_max[index])
            {
                s_max[index] = elapsed;
            }
        }

        /// <summary>Starts a measurement; false if one is already running.</summary>
        internal static bool Begin(float seconds)
        {
            if (s_on)
            {
                return false;
            }
            Array.Clear(s_ticks, 0, s_ticks.Length);
            Array.Clear(s_max, 0, s_max.Length);
            Array.Clear(s_calls, 0, s_calls.Length);
            var host = new GameObject("AoJ_Perf");
            DontDestroyOnLoad(host);
            host.AddComponent<Perf>()._seconds = seconds;
            s_on = true;
            return true;
        }

        private void Update()
        {
            float frame = Time.unscaledDeltaTime;
            _elapsed += frame;
            _frames++;
            _worstFrame = Mathf.Max(_worstFrame, frame);
            if (_elapsed < _seconds)
            {
                return;
            }
            s_on = false;
            foreach (string line in Report())
            {
                Log.Info("Perf", line);
                global::Console.instance?.AddString(line);
            }
            Destroy(gameObject);
        }

        private void OnDestroy() => s_on = false;

        private List<string> Report()
        {
            int frames = Mathf.Max(1, _frames);
            double frameMs = _elapsed * 1000.0 / frames;
            int simulated = 0;
            foreach (Settler settler in Settler.Loaded)
            {
                ZNetView view = settler != null ? settler.GetComponent<ZNetView>() : null;
                if (view != null && view.IsValid() && view.IsOwner())
                {
                    simulated++;
                }
            }

            // Invariant numbers: the report ends up in bug reports from players with any system language.
            var lines = new List<string>
            {
                FormattableString.Invariant($"aoj_perf: {_elapsed:0.0} s, {frames / _elapsed:0.0} fps (average frame {frameMs:0.0} ms, worst {_worstFrame * 1000f:0.0} ms)"),
                FormattableString.Invariant($"  settlers loaded {Settler.Loaded.Count}, simulated here {simulated} | tables {JarlTable.Loaded.Count} | ") +
                FormattableString.Invariant($"characters {Character.GetAllCharacters().Count} | pieces {Piece.s_allPieces.Count}"),
                "  section                        ms/frame   calls/s    avg us    max ms",
            };
            double total = 0.0;
            for (int i = 0; i < Labels.Length; i++)
            {
                double ms = Milliseconds(s_ticks[i]);
                if (InTotal[i])
                {
                    total += ms;
                }
                double average = s_calls[i] > 0 ? ms * 1000.0 / s_calls[i] : 0.0;
                lines.Add(FormattableString.Invariant(
                    $"  {Labels[i],-30} {ms / frames,8:0.000} {s_calls[i] / _elapsed,9:0.0} {average,9:0.0} {Milliseconds(s_max[i]),9:0.00}"));
            }
            lines.Add(FormattableString.Invariant($"  mod total: {total / frames:0.000} ms/frame = {100.0 * total / frames / frameMs:0.0}% of the frame ") +
                      "(AI runs where a settler's owner is: in co-op each player carries the settlers near them)");
            return lines;
        }

        private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
