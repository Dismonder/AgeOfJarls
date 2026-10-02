using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ModMenu
{
    /// <summary>
    /// Looks up the newest versions online; no account or API key needed. Nexus: one GraphQL query for every mod
    /// installed through Vortex. Thunderstore: the package API (namespace found with the public search when unknown).
    /// Nothing is downloaded or installed - the menu only marks mods and opens their page.
    /// </summary>
    internal static class UpdateChecker
    {
        private const string NexusApi = "https://api.nexusmods.com/v2/graphql";
        private const string ThunderstorePackage = "https://thunderstore.io/api/experimental/package/{0}/{1}/";
        private const string ThunderstoreSearch = "https://thunderstore.io/api/cyberstorm/listing/valheim/?q={0}";

        private const int ParallelRequests = 6;
        private const int NexusBatch = 50;
        private const int NameBatch = 20;

        public static bool Running { get; private set; }
        public static bool Done { get; private set; }
        public static int Progress { get; private set; }
        public static int Total { get; private set; }
        public static string LastError { get; private set; }

        public static IEnumerator Run(List<ModEntry> mods, Action onChanged)
        {
            if (Running)
            {
                yield break;
            }
            Running = true;
            LastError = null;
            List<ModEntry> nexus = mods.Where(m => m.Source?.Kind == SourceKind.Nexus).ToList();
            List<ModEntry> thunderstore = mods.Where(m => m.Source?.Kind == SourceKind.Thunderstore).ToList();
            Total = (nexus.Count + NexusBatch - 1) / NexusBatch + thunderstore.Count + 1;
            Progress = 0;
            onChanged();

            for (int i = 0; i < nexus.Count; i += NexusBatch)
            {
                yield return CheckNexus(nexus.Skip(i).Take(NexusBatch).ToList());
                Progress++;
                onChanged();
            }

            // Thunderstore answers one package per request: several at once, or a big mod list takes minutes.
            int next = 0;
            int workers = 0;
            IEnumerator Worker()
            {
                while (next < thunderstore.Count)
                {
                    ModEntry mod = thunderstore[next++];
                    yield return CheckThunderstore(mod);
                    Progress++;
                    onChanged();
                }
                workers--;
            }
            for (int i = 0; i < ParallelRequests; i++)
            {
                workers++;
                Plugin.Instance.StartCoroutine(Worker());
            }
            float deadline = Time.realtimeSinceStartup + 180f;
            while (workers > 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            // Hand-installed Nexus mods carry no manifest: look them up on Nexus by their exact name.
            List<ModEntry> unknown = thunderstore.Where(m => m.Source.Guessed && m.Source.ThunderstoreNamespace == null).ToList();
            for (int i = 0; i < unknown.Count; i += NameBatch)
            {
                yield return SearchNexusByName(unknown.Skip(i).Take(NameBatch).ToList());
            }
            Progress++;

            Running = false;
            Done = true;
            List<ModEntry> updates = mods.Where(m => m.UpdateAvailable).ToList();
            Plugin.Log.LogInfo($"Update check done: {mods.Count(m => m.LatestVersion != null)} mods checked, {updates.Count} updates"
                + (updates.Count > 0 ? ": " + string.Join(", ", updates.Select(m => $"{m.Name} {m.Source.InstalledVersion ?? m.Version} -> {m.LatestVersion}").ToArray()) : ""));
            onChanged();
        }

        private static IEnumerator CheckNexus(List<ModEntry> mods)
        {
            string ids = string.Join(", ", mods.Select(m => m.Source.NexusId).Distinct().Select(id => $"{{gameDomain: \"valheim\", modId: {id}}}").ToArray());
            yield return PostGraphQL("{ legacyModsByDomain(ids: [" + ids + "]) { nodes { modId version } } }", result =>
            {
                var latest = new Dictionary<int, string>();
                foreach (JToken node in result.SelectToken("data.legacyModsByDomain.nodes") as JArray ?? new JArray())
                {
                    latest[(int)node["modId"]] = (string)node["version"];
                }
                foreach (ModEntry mod in mods)
                {
                    if (latest.TryGetValue(mod.Source.NexusId, out string version))
                    {
                        SetLatest(mod, version);
                    }
                }
            });
        }

        /// <summary>
        /// One request for several names (GraphQL aliases). A match counts when it is the only mod with that exact name,
        /// or its author is named in the plugin's GUID - the same rule as the Thunderstore guess.
        /// </summary>
        private static IEnumerator SearchNexusByName(List<ModEntry> mods)
        {
            var parts = new List<string>();
            for (int i = 0; i < mods.Count; i++)
            {
                parts.Add($"m{i}: mods(filter: {{ gameDomainName: [{{value: \"valheim\"}}], name: [{{value: {JsonConvert.ToString(mods[i].Name)}, op: EQUALS}}] }}) {{ nodes {{ modId version author }} }}");
            }
            yield return PostGraphQL("{ " + string.Join(" ", parts.ToArray()) + " }", result =>
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    List<JToken> nodes = (result.SelectToken($"data.m{i}.nodes") as JArray ?? new JArray()).ToList();
                    string[] guidParts = mods[i].Guid.ToLowerInvariant().Split('.', '_', '-');
                    List<JToken> byAuthor = nodes.Where(n => guidParts.Contains(((string)n["author"] ?? "").Replace(" ", "").ToLowerInvariant())).ToList();
                    JToken match = byAuthor.Count == 1 ? byAuthor[0] : nodes.Count == 1 ? nodes[0] : null;
                    if (match == null)
                    {
                        continue;
                    }
                    mods[i].Source = new ModSource { Kind = SourceKind.Nexus, NexusId = (int)match["modId"] };
                    SetLatest(mods[i], (string)match["version"]);
                }
            });
        }

        private static IEnumerator PostGraphQL(string query, Action<JObject> onResult)
        {
            string body = JsonConvert.SerializeObject(new { query });
            using (var request = new UnityWebRequest(NexusApi, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail($"Nexus: {request.error}");
                    yield break;
                }
                try
                {
                    onResult(JObject.Parse(request.downloadHandler.text));
                }
                catch (Exception e)
                {
                    Fail($"Nexus: {e.Message}");
                }
            }
        }
        private static IEnumerator CheckThunderstore(ModEntry mod)
        {
            ModSource source = mod.Source;
            if (source.ThunderstoreNamespace == null)
            {
                string found = null;
                yield return Get(string.Format(ThunderstoreSearch, Uri.EscapeDataString(source.ThunderstoreName)), text => found = PickNamespace(mod, text));
                if (found == null)
                {
                    yield break;
                }
                source.ThunderstoreNamespace = found;
            }
            yield return Get(string.Format(ThunderstorePackage, source.ThunderstoreNamespace, source.ThunderstoreName), text =>
            {
                SetLatest(mod, (string)JObject.Parse(text).SelectToken("latest.version_number"));
            });
        }

        /// <summary>
        /// The search matches loosely, so only an exact package name counts. A guessed package (no manifest) must also
        /// come from an author named in the plugin's GUID or be the only package with that name, or "Foo" by a
        /// stranger would be taken for this mod.
        /// </summary>
        private static string PickNamespace(ModEntry mod, string json)
        {
            ModSource source = mod.Source;
            IEnumerable<JToken> exact = (JObject.Parse(json)["results"] as JArray ?? new JArray())
                .Where(r => string.Equals((string)r["name"], source.ThunderstoreName, StringComparison.OrdinalIgnoreCase));
            if (source.Guessed)
            {
                List<JToken> candidates = exact.ToList();
                string[] guidParts = mod.Guid.Split('.', '_', '-');
                List<JToken> byAuthor = candidates.Where(r => guidParts.Contains((string)r["namespace"], StringComparer.OrdinalIgnoreCase)).ToList();
                // GUIDs often carry a different author name than the Thunderstore team, so a unique exact name also counts.
                exact = byAuthor.Count > 0 ? byAuthor : candidates.Count == 1 ? candidates : new List<JToken>();
            }
            JToken best = exact.OrderByDescending(r => (long?)r["download_count"] ?? 0).FirstOrDefault();
            if (best == null)
            {
                return null;
            }
            source.ThunderstoreName = (string)best["name"];
            return (string)best["namespace"];
        }

        private static IEnumerator Get(string url, Action<string> onText)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 404 = no such package: simply no update info for this mod.
                    if (request.responseCode != 404)
                    {
                        Fail($"Thunderstore: {request.error}");
                    }
                    yield break;
                }
                try
                {
                    onText(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Fail($"Thunderstore: {e.Message}");
                }
            }
        }

        private static void SetLatest(ModEntry mod, string latest)
        {
            if (string.IsNullOrEmpty(latest))
            {
                return;
            }
            mod.LatestVersion = latest;
            // Newer than both the downloaded file and the plugin itself: Vortex archive names go stale when a mod is
            // updated by hand (Chest Label: archive 0.3.0, DLL 0.4.0), and plugin versions may follow their own scheme.
            mod.UpdateAvailable = ModSources.IsNewer(latest, mod.Version)
                && (mod.Source.InstalledVersion == null || ModSources.IsNewer(latest, mod.Source.InstalledVersion));
        }

        private static void Fail(string error)
        {
            LastError = error;
            Plugin.Log.LogWarning($"Update check: {error}");
        }
    }
}
