using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HmmRevive.Launcher
{
    /// <summary>
    /// Checks GitHub Releases for a newer HMM Revive kit and applies it in place (community PR #5).
    /// Only the files in the release zip are replaced (exe, mod, patcher, scripts, docs), so game copies (instance*\),
    /// launcher-settings.json and the scripts' own settings stay. Every replaced file is first moved into
    /// .hmmrevive-update-old\ (a running exe can be moved, not overwritten), and moved back if anything fails;
    /// the next start deletes that folder.
    /// </summary>
    public static class Updater
    {
        // The public repo by its GitHub id (stays right if the repo is renamed or moved); --update-url for tests.
        public static string ReleasesUrl = "https://api.github.com/repositories/1388905016/releases";

        public static volatile string Status = "";  // "", "checking", "latest", "available", "downloading", "applying", "failed: ..."
        public static volatile string LatestVersion;
        public static volatile string ReleaseNotes;
        public static volatile string ReleaseUrl;
        public static volatile bool IsPreview;
        public static volatile int DownloadPercent;

        private static string _assetUrl;
        private static string _expectedSha256;
        private static long _assetSize;

        private const string Temp = ".hmmrevive-update-temp", Old = ".hmmrevive-update-old", Zip = "hmmrevive-update.zip";

        /// <summary>A kit folder this launcher runs from (not a dev checkout, where build output isn't the kit).</summary>
        public static bool CanUpdate =>
            File.Exists(Path.Combine(Paths.Root, "Patcher.exe")) &&
            string.Equals(Path.GetDirectoryName(Paths.LauncherExe), Paths.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        /// <summary>Preview releases are offered when the player asked for them, and by default on a preview kit.</summary>
        public static bool WantPreview => Settings.Data.ContainsKey("previewUpdates") ? Settings.Data.Bool("previewUpdates") : Program.IsPreview;

        /// <summary>Leftovers of the last update (the old exe can only be deleted once it has quit).</summary>
        public static void CleanUp()
        {
            foreach (string d in new[] { Old, Temp })
                try { if (Directory.Exists(Path.Combine(Paths.Root, d))) Directory.Delete(Path.Combine(Paths.Root, d), true); } catch { }
            try { File.Delete(Path.Combine(Paths.Root, Zip)); } catch { }
        }

        public static void Check()
        {
            if (Status == "checking" || Status == "downloading" || Status == "applying") return;
            Status = "checking";
            bool includePreview = WantPreview;
            new Thread(() =>
            {
                try
                {
                    Dictionary<string, object> best = null, bestZip = null;
                    string bestVer = Program.Release;
                    foreach (var rel in Js.ReadArray(Get(ReleasesUrl)).OfType<Dictionary<string, object>>())
                    {
                        if (rel.Bool("draft") || (rel.Bool("prerelease") && !includePreview)) continue;
                        string tag = rel.Str("tag_name") ?? "";
                        string ver = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                        if (!IsNewer(ver, bestVer)) continue;
                        var zip = rel.List("assets").OfType<Dictionary<string, object>>().FirstOrDefault(a =>
                            (a.Str("name") ?? "").StartsWith("HMM-Revive-friend-kit", StringComparison.OrdinalIgnoreCase) &&
                            (a.Str("name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                        string digest = zip?.Str("digest") ?? "";
                        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) continue; // no checksum: not offered
                        best = rel; bestZip = zip; bestVer = ver;
                    }
                    if (best == null)
                    {
                        Status = "latest";
                        Program.Log("update check: up to date (" + Program.Release + ")");
                        return;
                    }
                    LatestVersion = bestVer;
                    ReleaseNotes = best.Str("body") ?? "";
                    ReleaseUrl = best.Str("html_url") ?? "";
                    IsPreview = best.Bool("prerelease");
                    _assetUrl = bestZip.Str("browser_download_url");
                    _assetSize = (long)bestZip.Double("size");
                    _expectedSha256 = bestZip.Str("digest").Substring(7);
                    Status = "available";
                    Program.Log($"update available: {bestVer} (preview={IsPreview})");
                }
                catch (Exception e)
                {
                    Status = "failed: " + e.Message;
                    Program.Log("update check failed: " + e.Message);
                }
            }) { IsBackground = true, Name = "update-check" }.Start();
        }

        /// <summary>Downloads and applies the update found by Check, then restarts the launcher on the same page port.</summary>
        public static string Apply()
        {
            if (Status != "available" || string.IsNullOrEmpty(_assetUrl)) return "No update available.";
            if (!CanUpdate) return "This folder isn't an HMM Revive kit, so it can't update itself.";
            if (Game.Running(Game.Server) || Game.Running(Game.Client) || Game.SetupStatus == "running")
                return "Close the game (and wait for the setup) before updating.";
            Status = "downloading";
            DownloadPercent = 0;
            new Thread(() =>
            {
                string zipPath = Path.Combine(Paths.Root, Zip);
                try
                {
                    Download(_assetUrl, zipPath, _assetSize);
                    if (!Sha256(zipPath).Equals(_expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("The download is damaged (checksum mismatch). Try again.");
                    Status = "applying";
                    Replace(zipPath, Paths.Root);
                    Program.Log("update applied: " + LatestVersion);
                    Program.Restart();
                }
                catch (Exception e)
                {
                    Status = "failed: " + (e is IOException && !(e is FileNotFoundException)
                        ? "A file of the kit is in use, so nothing was changed. Close what runs from this folder and try again." : e.Message);
                    Program.Log("update failed: " + e);
                }
                finally
                {
                    try { File.Delete(zipPath); } catch { }
                }
            }) { IsBackground = true, Name = "update-apply" }.Start();
            return null;
        }

        /// <summary>
        /// 1.6 &gt; 1.5; a final release &gt; its previews (1.6 &gt; 1.6-preview); 1.6-preview.2 &gt; 1.6-preview (= preview.1).
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            Parse(candidate, out int[] cNum, out int cPre);
            Parse(current, out int[] rNum, out int rPre);
            for (int i = 0; i < Math.Max(cNum.Length, rNum.Length); i++)
            {
                int cv = i < cNum.Length ? cNum[i] : 0, rv = i < rNum.Length ? rNum[i] : 0;
                if (cv != rv) return cv > rv;
            }
            return cPre > rPre;
        }

        // "1.6" -> [1,6], pre = int.MaxValue (final); "1.6-preview" -> pre 1; "1.6-preview.3" -> pre 3.
        private static void Parse(string v, out int[] num, out int pre)
        {
            var m = Regex.Match(v ?? "", @"^(\d+(?:\.\d+)*)(?:-[A-Za-z]+(?:\.(\d+))?)?");
            num = m.Success ? m.Groups[1].Value.Split('.').Select(int.Parse).ToArray() : new int[0];
            pre = !m.Success || !(v ?? "").Contains("-") ? int.MaxValue : m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
        }

        private static void Download(string url, string path, long expectedSize)
        {
            var req = Request(url, 30000);
            using (var res = (HttpWebResponse)req.GetResponse())
            using (Stream src = res.GetResponseStream())
            using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = res.ContentLength > 0 ? res.ContentLength : expectedSize;
                byte[] buf = new byte[65536];
                long done = 0;
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    if (total > 0) DownloadPercent = (int)Math.Min(100, done * 100 / total);
                }
            }
            DownloadPercent = 100;
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
        }

        // Never written by an update even if a zip had them: the player's game copies, settings and logs.
        private static bool Kept(string relative)
        {
            string first = relative.Split('\\')[0];
            return first.StartsWith("instance", StringComparison.OrdinalIgnoreCase) || first.StartsWith(".hmmrevive", StringComparison.OrdinalIgnoreCase)
                || first.Equals("logs", StringComparison.OrdinalIgnoreCase) || first.Equals(Zip, StringComparison.OrdinalIgnoreCase)
                || (!relative.Contains('\\') && (first.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase) || first.EndsWith(".log", StringComparison.OrdinalIgnoreCase)));
        }

        private static void Replace(string zipPath, string root)
        {
            string temp = Path.Combine(root, Temp), old = Path.Combine(root, Old);
            CleanUpDir(temp);
            CleanUpDir(old); // an earlier update's leftovers (normally deleted at start)
            ZipFile.ExtractToDirectory(zipPath, temp); // refuses entries outside temp
            string source = temp;
            if (Directory.GetFiles(temp).Length == 0 && Directory.GetDirectories(temp).Length == 1) source = Directory.GetDirectories(temp)[0];
            if (!File.Exists(Path.Combine(source, "HMM-Revive.exe")) || !File.Exists(Path.Combine(source, "Patcher.exe")))
                throw new Exception("The download doesn't look like an HMM Revive kit.");

            var moved = new List<string>(); // relative paths moved into old\
            var added = new List<string>(); // relative paths that didn't exist before
            try
            {
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(source.Length).TrimStart('\\');
                    if (Kept(rel)) continue;
                    string target = Path.Combine(root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                    {
                        string backup = Path.Combine(old, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Move(target, backup);
                        moved.Add(rel);
                    }
                    else added.Add(rel);
                    File.Copy(file, target);
                }
            }
            catch
            {
                foreach (string rel in added) try { File.Delete(Path.Combine(root, rel)); } catch { }
                foreach (string rel in moved)
                    try
                    {
                        string target = Path.Combine(root, rel);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(Path.Combine(old, rel), target);
                    }
                    catch { }
                throw;
            }
            finally { CleanUpDir(temp); }
        }

        private static void CleanUpDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static HttpWebRequest Request(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HmmRevive-Launcher/" + Program.Release;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            return req;
        }

        private static string Get(string url)
        {
            using (var res = (HttpWebResponse)Request(url, 15000).GetResponse())
            using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }
    }
}
