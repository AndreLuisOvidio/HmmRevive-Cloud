using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace HmmRevive.Launcher
{
    /// <summary>
    /// A cloud lobby's match server that ends by itself (match over, crash, killed for memory) is logged with its exit
    /// code and the end of its logs: the container's logs are the only ones that outlive the container, and the game's own
    /// files go with it. A server the lobby stopped (<see cref="Game.StopServer"/> clears <see cref="Game.Server"/>) is not reported.
    /// </summary>
    public sealed class ServerExitReport
    {
        public const int ModLines = 30, UnityLines = 20;
        private Process _seen;
        private DateTime _since;
        private bool _reported;

        /// <summary>Call every tick with the current server; returns the report once when the one seen before exited.</summary>
        public string Check(Process current, Func<Process, bool> exited, DateTime now, string modLog, string unityLog)
        {
            if (current == null) { _seen = null; return null; } // none, or stopped by the lobby: nothing to report
            if (!ReferenceEquals(current, _seen)) { _seen = current; _since = now; _reported = false; }
            if (_reported || !exited(current)) return null;
            _reported = true; // Game.Server keeps the exited process until the next match starts
            Process p = current;
            string code;
            try { code = p.ExitCode.ToString(); } catch { code = "?"; }
            var sb = new StringBuilder();
            sb.Append("match server exited by itself: code=").Append(code).Append(" after ").Append((int)(now - _since).TotalSeconds).Append(" s");
            foreach (string l in Tail(modLog, ModLines, null)) sb.Append("\n  mod| ").Append(l);
            foreach (string l in Tail(unityLog, UnityLines, IsProblem)) sb.Append("\n  unity| ").Append(l);
            return sb.ToString();
        }

        internal static bool IsProblem(string line) =>
            line.IndexOf("Exception", StringComparison.Ordinal) >= 0 || line.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("Crash", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The last n lines of a file (only those matching keep, when given); none when it cannot be read.</summary>
        internal static IEnumerable<string> Tail(string path, int n, Func<string, bool> keep)
        {
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long take = Math.Min(f.Length, 256 * 1024);
                    f.Seek(-take, SeekOrigin.End);
                    var buf = new byte[take];
                    int got = 0, r;
                    while (got < take && (r = f.Read(buf, got, (int)take - got)) > 0) got += r;
                    var lines = Encoding.UTF8.GetString(buf, 0, got).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);
                    if (keep != null) lines = lines.Where(keep);
                    var all = lines.ToList();
                    return all.Skip(Math.Max(0, all.Count - n)).Select(l => l.Length > 300 ? l.Substring(0, 300) + "..." : l).ToList();
                }
            }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
