using System;
using System.Collections.Generic;
using System.IO;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>Who a settler is. Rolled once by the ZDO owner and stored in the settler's ZDO.</summary>
    internal sealed class SettlerIdentity
    {
        /// <summary>Bump on format changes; <see cref="Deserialize"/> must keep reading older versions.</summary>
        private const int FormatVersion = 1;
        private const int MaxTraits = 16;
        private const string Module = "Settlers";

        internal string Name = "";
        internal bool Female;
        internal Heightmap.Biome Origin = Heightmap.Biome.Meadows;
        internal List<string> Traits = new List<string>();
        internal string Hair = "";
        internal string Beard = "";
        internal Vector3 SkinColor = Vector3.one;
        internal Vector3 HairColor = Vector3.one;

        internal byte[] Serialize()
        {
            var package = new ZPackage();
            package.Write(FormatVersion);
            package.Write(Name);
            package.Write(Female);
            package.Write((int)Origin);
            package.Write(Traits.Count);
            foreach (string trait in Traits)
            {
                package.Write(trait);
            }
            package.Write(Hair);
            package.Write(Beard);
            package.Write(SkinColor);
            package.Write(HairColor);
            return package.GetArray();
        }

        /// <summary>Null when the data is from a newer mod version or corrupted; callers must then leave it untouched.</summary>
        internal static SettlerIdentity Deserialize(byte[] data)
        {
            try
            {
                var package = new ZPackage(data);
                int version = package.ReadInt();
                if (version != FormatVersion)
                {
                    Log.Error(Module, $"Settler identity format {version} is not supported by this mod version");
                    return null;
                }

                var identity = new SettlerIdentity
                {
                    Name = package.ReadString(),
                    Female = package.ReadBool(),
                    Origin = (Heightmap.Biome)package.ReadInt(),
                };
                int traitCount = package.ReadInt();
                if (traitCount < 0 || traitCount > MaxTraits)
                {
                    Log.Error(Module, $"Settler identity has an invalid trait count {traitCount}");
                    return null;
                }
                for (int i = 0; i < traitCount; i++)
                {
                    identity.Traits.Add(package.ReadString());
                }
                identity.Hair = package.ReadString();
                identity.Beard = package.ReadString();
                identity.SkinColor = package.ReadVector3();
                identity.HairColor = package.ReadVector3();
                return identity;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Error(Module, $"Corrupted settler identity: {e.Message}");
                return null;
            }
        }
    }
}
