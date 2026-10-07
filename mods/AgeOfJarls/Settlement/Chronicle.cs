using System;
using System.Collections.Generic;
using System.IO;
using AgeOfJarls.Core;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// The settlement's chronicle: arrivals, departures, deaths, alarms, sieges, feasts and reports from while nobody
    /// was around. A small versioned blob in the Jarl's Table ZDO keeping the latest entries; only the table's owner
    /// writes it (entries from elsewhere arrive with the actions that cause them).
    /// </summary>
    internal static class Chronicle
    {
        private const string Module = "Settlement";
        private const int FormatVersion = 1;
        private const int MaxEntries = 40;

        internal struct Entry
        {
            internal double Time;
            /// <summary>A localization template with $1.. placeholders, and its words.</summary>
            internal string Text;
            internal string[] Words;
        }

        /// <summary>Owner of the table only.</summary>
        internal static void Add(ZNetView table, string text, params string[] words)
            => Add(table, WorldClock.Now, text, words);

        /// <summary>Owner only; explicit world time preserves the day of overdue family events.</summary>
        internal static void Add(ZNetView table, double time, string text, params string[] words)
        {
            if (table == null || !table.IsValid() || !table.IsOwner())
            {
                return;
            }
            List<Entry> entries = Read(table.GetZDO());
            entries.Add(new Entry { Time = time, Text = text, Words = words ?? new string[0] });
            while (entries.Count > MaxEntries)
            {
                entries.RemoveAt(0);
            }
            var package = new ZPackage();
            package.Write(FormatVersion);
            package.Write(entries.Count);
            foreach (Entry entry in entries)
            {
                package.Write(entry.Time);
                package.Write(entry.Text);
                package.Write(entry.Words.Length);
                foreach (string word in entry.Words)
                {
                    package.Write(word ?? "");
                }
            }
            table.GetZDO().Set(Keys.ZdoSettlementChronicle, package.GetArray());
        }

        internal static List<Entry> Read(ZDO zdo)
        {
            var entries = new List<Entry>();
            byte[] data = zdo?.GetByteArray(Keys.ZdoSettlementChronicle);
            if (data == null)
            {
                return entries;
            }
            try
            {
                var package = new ZPackage(data);
                int version = package.ReadInt();
                if (version < 1 || version > FormatVersion)
                {
                    return entries;
                }
                int count = Math.Min(package.ReadInt(), MaxEntries);
                for (int i = 0; i < count; i++)
                {
                    var entry = new Entry { Time = package.ReadDouble(), Text = package.ReadString() };
                    int words = Math.Min(package.ReadInt(), 8);
                    entry.Words = new string[words];
                    for (int w = 0; w < words; w++)
                    {
                        entry.Words[w] = package.ReadString();
                    }
                    entries.Add(entry);
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Unreadable chronicle ignored: {e.Message}");
            }
            return entries;
        }

        /// <summary>"Day 12: text" lines, newest first, localized.</summary>
        internal static string Describe(ZDO zdo, int max)
        {
            List<Entry> entries = Read(zdo);
            if (entries.Count == 0)
            {
                return Localize("$aoj_chronicle_empty");
            }
            var lines = new List<string>();
            double day = WorldClock.DayLength;
            for (int i = entries.Count - 1; i >= 0 && lines.Count < max; i--)
            {
                Entry entry = entries[i];
                int dayNumber = (int)(entry.Time / day) + 1;
                // The game inserts words as they are: tokens among them (a tier, a rank) are localized first.
                // Names never hold a '$' (they are sanitized), so they pass through unchanged.
                string[] words = Array.ConvertAll(entry.Words, word => Localize(word));
                lines.Add($"<color=#e0c080>{Localize("$aoj_day")} {dayNumber}</color>: {Localize(entry.Text, words)}");
            }
            return string.Join("\n", lines);
        }

        private static string Localize(string text, params string[] words) =>
            AgeOfJarls.Core.TextUtil.Localize(text, words);
    }
}
