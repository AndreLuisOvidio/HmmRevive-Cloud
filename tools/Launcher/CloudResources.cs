using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HmmRevive.Launcher
{
    /// <summary>One reading of the container's cgroup: what the lobby container used so far and right now.</summary>
    public sealed class CgroupSample
    {
        public long CpuUsec, Periods, ThrottledPeriods, ThrottledUsec; // cumulative since the container started
        public long MemBytes, MemPeakBytes = -1, OomKills = -1;          // -1 = the kernel does not say
        public string TopName; public long TopRssBytes;                  // the biggest process (HMM.exe under Wine)
    }

    /// <summary>
    /// A cloud lobby's resource line, written to the log every heartbeat so the container logs (sent to Log Analytics by
    /// the orchestrator) show whether a match ran out of CPU or memory: the container's cgroup has the CPU it used, how
    /// often the limit throttled it, its memory and the out-of-memory kills. Reads cgroup v2 and falls back to v1.
    /// </summary>
    public static class CloudResources
    {
        internal static Func<string, string> ReadFile = p => { try { return File.ReadAllText(p); } catch { return null; } };
        internal static Func<IEnumerable<string>> ProcStatusFiles = () =>
        {
            try { return Directory.GetDirectories("/proc").Where(d => Path.GetFileName(d).All(char.IsDigit)).Select(d => Path.Combine(d, "status")).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        };

        /// <summary>The current reading, or null when this is not a Linux container (no cgroup files).</summary>
        public static CgroupSample Read()
        {
            CgroupSample s = ReadV2() ?? ReadV1();
            if (s != null) TopProcess(s);
            return s;
        }

        internal static CgroupSample ReadV2()
        {
            string cpu = ReadFile("/sys/fs/cgroup/cpu.stat"), mem = ReadFile("/sys/fs/cgroup/memory.current");
            if (cpu == null || mem == null) return null;
            var c = Keyed(cpu);
            var s = new CgroupSample
            {
                CpuUsec = Get(c, "usage_usec"), Periods = Get(c, "nr_periods"), ThrottledPeriods = Get(c, "nr_throttled"),
                ThrottledUsec = Get(c, "throttled_usec"), MemBytes = Num(mem),
            };
            string peak = ReadFile("/sys/fs/cgroup/memory.peak"), events = ReadFile("/sys/fs/cgroup/memory.events");
            if (peak != null) s.MemPeakBytes = Num(peak);
            if (events != null) s.OomKills = Get(Keyed(events), "oom_kill");
            return s;
        }

        internal static CgroupSample ReadV1()
        {
            string usage = ReadFile("/sys/fs/cgroup/cpuacct/cpuacct.usage") ?? ReadFile("/sys/fs/cgroup/cpu,cpuacct/cpuacct.usage");
            string mem = ReadFile("/sys/fs/cgroup/memory/memory.usage_in_bytes");
            if (usage == null || mem == null) return null;
            var c = Keyed(ReadFile("/sys/fs/cgroup/cpu/cpu.stat") ?? ReadFile("/sys/fs/cgroup/cpu,cpuacct/cpu.stat") ?? "");
            var s = new CgroupSample
            {
                CpuUsec = Num(usage) / 1000, Periods = Get(c, "nr_periods"), ThrottledPeriods = Get(c, "nr_throttled"),
                ThrottledUsec = Get(c, "throttled_time") / 1000, MemBytes = Num(mem),
            };
            string peak = ReadFile("/sys/fs/cgroup/memory/memory.max_usage_in_bytes"), oom = ReadFile("/sys/fs/cgroup/memory/memory.oom_control");
            if (peak != null) s.MemPeakBytes = Num(peak);
            if (oom != null) s.OomKills = Get(Keyed(oom), "oom_kill", -1);
            return s;
        }

        private static void TopProcess(CgroupSample s)
        {
            foreach (string f in ProcStatusFiles())
            {
                var k = Keyed(ReadFile(f) ?? "", ':');
                long rss = Get(k, "VmRSS") * 1024; // "VmRSS:  123 kB"
                if (rss <= s.TopRssBytes) continue;
                s.TopRssBytes = rss;
                s.TopName = k.TryGetValue("Name", out string n) ? n : "?";
            }
        }

        /// <summary>The log line for the time between two readings (seconds apart); prev null = since the container started.</summary>
        public static string Describe(CgroupSample prev, CgroupSample cur, double seconds)
        {
            var inv = CultureInfo.InvariantCulture;
            long cpu = cur.CpuUsec - (prev?.CpuUsec ?? 0), periods = cur.Periods - (prev?.Periods ?? 0);
            long thrP = cur.ThrottledPeriods - (prev?.ThrottledPeriods ?? 0), thrUs = cur.ThrottledUsec - (prev?.ThrottledUsec ?? 0);
            string line = "resources: cpu=" + (seconds > 0 ? (cpu / 1e6 / seconds).ToString("0.00", inv) : "?") + " cores"
                + " throttled=" + (periods > 0 ? (100.0 * thrP / periods).ToString("0", inv) : "0") + "% (" + (thrUs / 1e6).ToString("0.0", inv) + " s)"
                + " mem=" + Gb(cur.MemBytes);
            if (cur.MemPeakBytes >= 0) line += " peak=" + Gb(cur.MemPeakBytes);
            if (cur.OomKills >= 0) line += " oomKills=" + cur.OomKills;
            if (cur.TopName != null) line += " top=" + cur.TopName + " " + Gb(cur.TopRssBytes);
            return line;
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";

        private static Dictionary<string, string> Keyed(string text, char sep = ' ')
        {
            var d = new Dictionary<string, string>();
            foreach (string raw in text.Split('\n'))
            {
                int i = raw.IndexOf(sep);
                if (i <= 0) continue;
                d[raw.Substring(0, i).Trim()] = raw.Substring(i + 1).Trim();
            }
            return d;
        }

        private static long Get(Dictionary<string, string> d, string key, long missing = 0) =>
            d.TryGetValue(key, out string v) ? Num(v.Split(' ')[0]) : missing;

        private static long Num(string s) => long.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;
    }
}
