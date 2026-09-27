using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AgeOfJarls.Core.Defs
{
    internal enum DefsSource
    {
        None,
        Local,
        Server,
    }

    /// <summary>
    /// Active definitions. A server, host or single-player game uses its local files; a client gets the server's
    /// set during login (<see cref="Net.DefsSync"/>), so both players always simulate with identical data.
    /// </summary>
    internal static class DefsRegistry
    {
        /// <summary>Bump when the wire format produced by <see cref="Serialize"/> changes.</summary>
        internal const int ProtocolVersion = 4;

        private const string Module = "Defs";
        private const string TraitsFile = "traits.json";
        private const string TiersFile = "tiers.json";
        private const string NamesFile = "names.json";
        private const string StorageFile = "storage.json";
        private const string RaidsFile = "raids.json";

        internal static DefsBundle Current { get; private set; } = new DefsBundle();

        internal static DefsSource Source { get; private set; } = DefsSource.None;

        /// <summary>Stable across machines: equal hashes on two players mean identical definitions.</summary>
        internal static int Hash { get; private set; }

        /// <summary>Loads BepInEx/config/AgeOfJarls/*.json, creating them from the shipped defaults on first run.</summary>
        internal static bool LoadLocal()
        {
            var bundle = new DefsBundle
            {
                Traits = LoadFile<List<TraitDef>>(TraitsFile, DefsValidator.Traits),
                Tiers = LoadFile<List<TierDef>>(TiersFile, DefsValidator.Tiers),
                Names = LoadFile<NamesDef>(NamesFile, DefsValidator.Names),
                Storage = LoadFile<StorageDef>(StorageFile, DefsValidator.Storage),
                Raids = LoadFile<RaidsDef>(RaidsFile, DefsValidator.Raids),
            };
            if (bundle.Traits == null || bundle.Tiers == null || bundle.Names == null || bundle.Storage == null || bundle.Raids == null)
            {
                Log.Error(Module, $"Local definitions are unusable, keeping: {Describe()}");
                return false;
            }

            Activate(bundle, DefsSource.Local);
            return true;
        }

        internal static bool ApplyFromServer(string json)
        {
            DefsBundle bundle;
            try
            {
                bundle = JsonConvert.DeserializeObject<DefsBundle>(json);
            }
            catch (JsonException e)
            {
                Log.Error(Module, $"Server definitions could not be parsed: {e.Message}");
                return false;
            }

            if (bundle != null)
            {
                bundle.Traits = DefsValidator.Traits(bundle.Traits, "server");
                bundle.Tiers = DefsValidator.Tiers(bundle.Tiers, "server");
                bundle.Names = DefsValidator.Names(bundle.Names, "server");
                bundle.Storage = DefsValidator.Storage(bundle.Storage, "server");
                bundle.Raids = DefsValidator.Raids(bundle.Raids, "server");
            }
            if (bundle?.Traits == null || bundle.Tiers == null || bundle.Names == null || bundle.Storage == null || bundle.Raids == null)
            {
                Log.Error(Module, "Server definitions rejected; this client may now disagree with the server");
                return false;
            }

            Activate(bundle, DefsSource.Server);
            return true;
        }

        internal static string Serialize() => JsonConvert.SerializeObject(Current);

        internal static string Describe() =>
            $"{Source}, hash {Hash:X8}, {Current.Traits.Count} traits, {Current.Tiers.Count} tiers, " +
            $"{Current.Names.Male.Count}+{Current.Names.Female.Count} names, {Current.Storage.Kinds.Count} storage kinds, {Current.Raids.SiegeWaves.Count} siege waves";

        private static void Activate(DefsBundle bundle, DefsSource source)
        {
            Current = bundle;
            Source = source;
            Hash = Serialize().GetStableHashCode();
            Log.Info(Module, $"Active definitions: {Describe()}");
        }

        // The user copy in BepInEx/config wins; the shipped file is both the fallback and the template for that copy.
        private static T LoadFile<T>(string fileName, Func<T, string, T> validate) where T : class
        {
            string userPath = Path.Combine(ModPaths.ConfigDir, fileName);
            string shippedPath = Path.Combine(ModPaths.DefsDir, fileName);
            SyncUserCopy(userPath, shippedPath);

            T defs = Read(userPath, $"config/{fileName}", validate);
            if (defs != null)
            {
                return defs;
            }
            if (File.Exists(userPath))
            {
                Log.Warning(Module, $"Using the shipped {fileName} instead of the broken config copy");
            }
            return Read(shippedPath, $"shipped/{fileName}", validate);
        }

        private static T Read<T>(string path, string label, Func<T, string, T> validate) where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return validate(JsonConvert.DeserializeObject<T>(File.ReadAllText(path)), label);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                Log.Error(Module, $"Cannot load {label}: {e.Message}");
                return null;
            }
        }

        // Keeps the editable copy in step with mod updates without ever losing the user's edits:
        //  - no copy yet: create it;
        //  - unedited copy (its hash matches the recorded shipped hash): replace it with the new defaults;
        //  - edited copy: keep it and put the new defaults next to it as *.default.json.
        // Copies made before hashes were recorded count as unedited while their write time is not after their
        // creation time (File.Copy keeps the source's write time; saving an edit moves it forward).
        private static void SyncUserCopy(string userPath, string shippedPath)
        {
            if (!File.Exists(shippedPath))
            {
                return;
            }

            string hashPath = userPath + ".default-hash";
            try
            {
                string shipped = File.ReadAllText(shippedPath);
                string shippedHash = shipped.GetStableHashCode().ToString();
                if (!File.Exists(userPath))
                {
                    WriteUserCopy(userPath, hashPath, shipped, shippedHash, "Created editable");
                    return;
                }

                string userHash = File.ReadAllText(userPath).GetStableHashCode().ToString();
                if (userHash == shippedHash)
                {
                    // Keep the record current, or the next update would take this copy for an edited one.
                    if (!File.Exists(hashPath) || File.ReadAllText(hashPath).Trim() != shippedHash)
                    {
                        File.WriteAllText(hashPath, shippedHash);
                    }
                    return;
                }

                bool unedited = File.Exists(hashPath)
                    ? File.ReadAllText(hashPath).Trim() == userHash
                    : File.GetLastWriteTimeUtc(userPath) <= File.GetCreationTimeUtc(userPath);
                if (unedited)
                {
                    WriteUserCopy(userPath, hashPath, shipped, shippedHash, "Updated to the new defaults:");
                    return;
                }

                string defaultsPath = Path.ChangeExtension(userPath, ".default.json");
                if (!File.Exists(defaultsPath) || File.ReadAllText(defaultsPath) != shipped)
                {
                    File.WriteAllText(defaultsPath, shipped);
                    Log.Warning(Module, $"{Path.GetFileName(userPath)} was edited and is kept; the new defaults are in {defaultsPath}");
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Warning(Module, $"Cannot update {userPath}: {e.Message}");
            }
        }

        private static void WriteUserCopy(string userPath, string hashPath, string content, string hash, string what)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userPath));
            File.WriteAllText(userPath, content);
            File.WriteAllText(hashPath, hash);
            Log.Info(Module, $"{what} {userPath}");
        }
    }
}
