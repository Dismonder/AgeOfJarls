using System;
using System.Collections.Generic;
using System.IO;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Buildings a siege destroyed, for the Builders to put back: what stood where, as a small versioned blob in the
    /// Jarl's Table ZDO. Only the table's owner writes it (reports and removals arrive as settlement actions); anyone
    /// may read it. Kept to the most recent entries.
    /// </summary>
    internal static class Breaches
    {
        private const string Module = "Settlement";
        private const int FormatVersion = 1;
        internal const int MaxEntries = 64;
        /// <summary>Two reports this close are the same spot.</summary>
        internal const float SameSpot = 0.3f;

        internal struct Entry
        {
            internal string Prefab;
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        internal static List<Entry> Read(ZDO zdo)
        {
            var entries = new List<Entry>();
            byte[] blob = zdo?.GetByteArray(Keys.ZdoSettlementBreaches);
            if (blob == null)
            {
                return entries;
            }
            try
            {
                var package = new ZPackage(blob);
                if (package.ReadInt() != FormatVersion)
                {
                    return entries;
                }
                int count = package.ReadInt();
                for (int i = 0; i < count && i < MaxEntries; i++)
                {
                    entries.Add(new Entry { Prefab = package.ReadString(), Position = package.ReadVector3(), Rotation = package.ReadQuaternion() });
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Warning(Module, $"Unreadable breach list: {e.Message}");
            }
            return entries;
        }

        /// <summary>Table owner only.</summary>
        internal static void Write(ZDO zdo, List<Entry> entries)
        {
            var package = new ZPackage();
            package.Write(FormatVersion);
            int start = Math.Max(0, entries.Count - MaxEntries);
            package.Write(entries.Count - start);
            for (int i = start; i < entries.Count; i++)
            {
                package.Write(entries[i].Prefab ?? "");
                package.Write(entries[i].Position);
                package.Write(entries[i].Rotation);
            }
            zdo.Set(Keys.ZdoSettlementBreaches, package.GetArray());
        }

        /// <summary>
        /// Whether a destroyed object may go on the list: a building piece players can build (it has a recipe), not a
        /// piece of this mod's own (the table, totems and the like are put back by players).
        /// </summary>
        internal static bool IsRebuildable(string prefab)
        {
            GameObject source = ZNetScene.instance != null && !string.IsNullOrEmpty(prefab) ? ZNetScene.instance.GetPrefab(prefab) : null;
            Piece piece = source != null ? source.GetComponent<Piece>() : null;
            return piece != null && piece.m_resources != null && piece.m_resources.Length > 0 &&
                   source.GetComponent<WearNTear>() != null && !prefab.StartsWith("AoJ_", StringComparison.Ordinal);
        }
    }
}
