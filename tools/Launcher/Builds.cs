using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace HmmRevive.Launcher
{
    /// <summary>
    /// Game builds the launcher can run. The current (Steam) build is "steam"; a supported older build has its own id,
    /// mod (kit mod\&lt;id&gt;\), patched copy (instance-&lt;id&gt;\) and lobby rules (the game's own pick screen: no car or skin in
    /// the lobby, the build's two arenas). A copy is recognized by the sha256 of its HMM_Data\Managed\Assembly-CSharp-firstpass.dll.
    /// Keep the table in sync with tools/Patcher/Legacy.cs.
    /// </summary>
    public static class Builds
    {
        public const string Steam = "steam", Unsupported = "unsupported";

        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>
        {
            ["0c5527141b81820e"] = "2017",
            ["6b253add011d2318"] = "2017sep", // September 2017
            ["1c5bd4c1219a9229"] = Unsupported, // planned
        };

        /// <summary>Old builds this launcher supports, in the order the page lists them.</summary>
        public static readonly string[] Legacy = { "2017sep", "2017" }; // Sep first: the one players use

        /// <summary>English name, used in messages from the lobby (the page has its own translated names).</summary>
        public static string Label(string build) => build == "2017" ? "Heavy Metal Machines (Nov 2017)" : build == "2017sep" ? "Heavy Metal Machines (Sep 2017)"
            : "Heavy Metal Machines (Steam version)";

        public static bool IsLegacy(string build) => build != null && build != Steam;

        private static readonly Dictionary<string, (long, DateTime, string)> Cache = new Dictionary<string, (long, DateTime, string)>();

        /// <summary>Which build a game folder holds: a known old build, <see cref="Unsupported"/>, or Steam (any other copy of
        /// the current game, as before builds were told apart).</summary>
        public static string Detect(string gameDir)
        {
            string dll = Path.Combine(gameDir, "HMM_Data", "Managed", "Assembly-CSharp-firstpass.dll");
            try
            {
                var fi = new FileInfo(dll);
                if (!fi.Exists) return Unsupported;
                lock (Cache)
                    if (Cache.TryGetValue(dll, out var c) && c.Item1 == fi.Length && c.Item2 == fi.LastWriteTimeUtc) return c.Item3;
                string hash;
                using (var s = File.OpenRead(dll))
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                string build = Known.FirstOrDefault(k => hash.StartsWith(k.Key)).Value;
                // An unknown hash is the current build only if it looks like it (its code is split into many DLLs).
                if (build == null)
                    build = Directory.GetFiles(Path.GetDirectoryName(dll), "HeavyMetalMachines*.dll").Length > 0 ? Steam : Unsupported;
                lock (Cache) Cache[dll] = (fi.Length, fi.LastWriteTimeUtc, build);
                return build;
            }
            catch (Exception e)
            {
                Program.Log($"can't read {dll}: {e.Message}");
                return Unsupported;
            }
        }

        /// <summary>The patch revision an old build's copy must have (tools/Patcher/Legacy.cs writes it to
        /// HMM_Data/Managed/hmmrevive-patch.txt). The mod version cannot tell a copy patched by an older Patcher: 1.8 preview 3
        /// changed the patch (the game's clock reads UTC) under the same 1.8. Revision 1 is a copy with no marker.</summary>
        public const int LegacyPatchRevision = 2;

        public static int InstancePatchRevision(string build)
        {
            try { return int.TryParse(File.ReadAllText(Path.Combine(Instance(build), "HMM_Data", "Managed", "hmmrevive-patch.txt")).Trim(), out int r) && r > 0 ? r : 1; }
            catch { return 1; }
        }

        /// <summary>Whether a set-up copy must be set up again: its mod is not the kit's, or an old build's patch is out of date.</summary>
        public static bool NeedsUpdate(string build, bool ready, string kit, string inst, int patchRevision) =>
            ready && kit != null && (inst != kit || (IsLegacy(build) && patchRevision < LegacyPatchRevision));

        public static string Instance(string build) => IsLegacy(build) ? Paths.Instance + "-" + build : Paths.Instance;
        public static string GameExe(string build) => Path.Combine(Instance(build), "HMM.exe");
        public static bool Ready(string build) => File.Exists(GameExe(build));
        public static string ModDll(string build) => IsLegacy(build) ? "HmmReviveLegacy.dll" : "HmmRevive.dll";
        public static string KitModDll(string build) => IsLegacy(build) ? Path.Combine(Paths.ModDir, build, ModDll(build)) : Path.Combine(Paths.ModDir, ModDll(build));
        public static string KitVersion(string build) => Game.ModVersion(KitModDll(build));
        public static string InstanceVersion(string build) => Game.ModVersion(Path.Combine(Instance(build), "HMM_Data", "Managed", ModDll(build)));

        // ---- old builds from Steam's own depot (1.6): the player runs download_depot in Steam's console with their own
        // account (anyone who ever added the free game keeps its license); we ship nothing of the game and touch no Steam
        // login. Steam puts the files in <Steam>\steamapps\content\app_331360\depot_331361, the same folder for every
        // manifest, so "Add the download" first renames it to hmm-<build> next to it (same drive: instant), then sets it up.

        public const string SteamApp = "331360", SteamDepot = "331361";

        /// <summary>
        /// Manifest of each old build in the game's depot (SteamDB's manifest list); null = not offered (the November build
        /// is experimental, so it isn't offered for now).
        /// </summary>
        public static readonly Dictionary<string, string> Manifests = new Dictionary<string, string> { ["2017sep"] = "543058244645982357", ["2017"] = null };

        public static string DepotFolder
        {
            get
            {
                // Tests (--test-background): a folder standing in for Steam's, so nothing is written there.
                if (Game.TestBackground && Environment.GetEnvironmentVariable("HMMREVIVE_TEST_DEPOT_DIR") is string test) return test;
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                        return key?.GetValue("SteamPath") is string steam
                            ? Path.GetFullPath(Path.Combine(steam, "steamapps", "content", "app_" + SteamApp, "depot_" + SteamDepot)) : null;
                }
                catch { return null; }
            }
        }

        /// <summary>The old builds the page offers to download (those with a known manifest), for Settings.</summary>
        public static object[] DepotsJson()
        {
            if (Game.TestBackground && Environment.GetEnvironmentVariable("HMMREVIVE_TEST_MANIFESTS") is string tm) // "2017sep=123"
                foreach (string kv in tm.Split(';')) if (kv.Contains("=") && Manifests.ContainsKey(kv.Split('=')[0])) Manifests[kv.Split('=')[0]] = kv.Split('=')[1];
            string folder = DepotFolder;
            bool downloaded = folder != null && File.Exists(Path.Combine(folder, "HMM.exe"));
            string there = downloaded ? Detect(folder) : null;
            return Legacy.Where(b => Manifests[b] != null).Select(b => (object)new Dictionary<string, object>
            {
                ["build"] = b, ["command"] = $"download_depot {SteamApp} {SteamDepot} {Manifests[b]}", ["folder"] = folder,
                ["downloaded"] = there == b,
            }).ToArray();
        }

        /// <summary>Renames the finished download to hmm-&lt;build&gt; and returns that folder (to set up like any copy).</summary>
        public static string TakeDownload(string build)
        {
            if (!IsLegacy(build) || Manifests[build] == null) throw new Exception("That build can't be downloaded from Steam.");
            string folder = DepotFolder;
            if (folder == null || !File.Exists(Path.Combine(folder, "HMM.exe")))
                throw new Exception("The download isn't there yet: wait until Steam's console says the depot download is complete.");
            string there = Detect(folder);
            if (there != build) throw new Exception("The download in Steam's folder is a different game build.");
            string target = Path.Combine(Path.GetDirectoryName(folder), "hmm-" + build);
            if (Directory.Exists(target)) throw new Exception($"{target} already exists: remove or rename it first.");
            Directory.Move(folder, target);
            Program.Log($"depot download of {build} moved to {target}");
            return target;
        }

        /// <summary>Builds whose game copy is set up here (the ones this player can host or join).</summary>
        public static List<string> ReadyBuilds() => new[] { Steam }.Concat(Legacy).Where(Ready).ToList();

        /// <summary>The original folder of an old build's copy, as the player added it.</summary>
        public static string CopyDir(string build)
        {
            if (!IsLegacy(build)) return Game.FindGame();
            string dir = Settings.Data.Obj("copies")?.Str(build);
            return dir != null && File.Exists(Path.Combine(dir, "HMM.exe")) ? dir : null;
        }

        /// <summary>Every game copy for Settings: the Steam one (found or chosen) and each supported old build.</summary>
        public static object[] CopiesJson()
        {
            var list = new List<object>();
            foreach (string b in new[] { Steam }.Concat(Legacy))
            {
                string dir = CopyDir(b);
                if (IsLegacy(b) && dir == null && !Ready(b)) continue;
                string kit = KitVersion(b), inst = InstanceVersion(b);
                list.Add(new Dictionary<string, object>
                {
                    ["build"] = b,
                    ["dir"] = dir,
                    ["instance"] = Instance(b),
                    ["ready"] = Ready(b),
                    ["version"] = inst,
                    ["kitVersion"] = kit,
                    ["canSetup"] = Paths.Patcher != null && kit != null,
                    ["needsUpdate"] = NeedsUpdate(b, Ready(b), kit, inst, InstancePatchRevision(b)),
                });
            }
            return list.ToArray();
        }
    }
}
