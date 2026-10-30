using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    // CloudResources and the cgroup seams are static: keep these tests in one collection so they never run at the same time.
    [Collection("cgroup")]
    public class CloudResourcesTests : IDisposable
    {
        private readonly Func<string, string> _read = CloudResources.ReadFile;
        private readonly Func<IEnumerable<string>> _procs = CloudResources.ProcStatusFiles;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        public CloudResourcesTests()
        {
            CloudResources.ReadFile = p => _files.TryGetValue(p, out string v) ? v : null;
            CloudResources.ProcStatusFiles = () => _files.Keys.Where(k => k.StartsWith("/proc/")).ToList();
        }

        public void Dispose() { CloudResources.ReadFile = _read; CloudResources.ProcStatusFiles = _procs; }

        [Fact]
        public void CgroupV2IsReadWithThrottlingMemoryAndOomKills()
        {
            _files["/sys/fs/cgroup/cpu.stat"] = "usage_usec 90000000\nuser_usec 1\nnr_periods 1000\nnr_throttled 250\nthrottled_usec 4500000\n";
            _files["/sys/fs/cgroup/memory.current"] = "2147483648\n";
            _files["/sys/fs/cgroup/memory.peak"] = "3221225472\n";
            _files["/sys/fs/cgroup/memory.events"] = "low 0\nhigh 0\nmax 3\noom 1\noom_kill 1\n";
            _files["/proc/12/status"] = "Name:\tHMM.exe\nVmRSS:\t 2097152 kB\n";
            _files["/proc/7/status"] = "Name:\tmono\nVmRSS:\t  102400 kB\n";

            CgroupSample s = CloudResources.Read();
            Assert.Equal(90000000, s.CpuUsec);
            Assert.Equal(250, s.ThrottledPeriods);
            Assert.Equal(1, s.OomKills);
            Assert.Equal("HMM.exe", s.TopName);
            Assert.Equal(2147483648L, s.TopRssBytes);
        }

        [Fact]
        public void CgroupV1IsTheFallback()
        {
            _files["/sys/fs/cgroup/cpuacct/cpuacct.usage"] = "5000000000\n"; // nanoseconds
            _files["/sys/fs/cgroup/cpu/cpu.stat"] = "nr_periods 10\nnr_throttled 2\nthrottled_time 300000000\n";
            _files["/sys/fs/cgroup/memory/memory.usage_in_bytes"] = "1073741824\n";
            _files["/sys/fs/cgroup/memory/memory.oom_control"] = "oom_kill_disable 0\nunder_oom 0\noom_kill 2\n";

            CgroupSample s = CloudResources.Read();
            Assert.Equal(5000000, s.CpuUsec);
            Assert.Equal(300000, s.ThrottledUsec);
            Assert.Equal(2, s.OomKills);
            Assert.Equal(-1, s.MemPeakBytes);
        }

        [Fact]
        public void NoCgroupMeansNoReading() => Assert.Null(CloudResources.Read());

        [Fact]
        public void TheLineShowsCoresAndThrottlingBetweenTwoReadings()
        {
            var a = new CgroupSample { CpuUsec = 10000000, Periods = 1000, ThrottledPeriods = 100, ThrottledUsec = 1000000, MemBytes = 1L << 30 };
            var b = new CgroupSample { CpuUsec = 10000000 + 57000000, Periods = 1300, ThrottledPeriods = 220, ThrottledUsec = 4500000,
                MemBytes = 3L << 30, MemPeakBytes = 3L << 30, OomKills = 0, TopName = "HMM.exe", TopRssBytes = 2L << 30 };

            string line = CloudResources.Describe(a, b, 30);
            Assert.Equal("resources: cpu=1.90 cores throttled=40% (3.5 s) mem=3.00 GB peak=3.00 GB oomKills=0 top=HMM.exe 2.00 GB", line);
        }
    }

    public class ServerExitReportTests : IDisposable
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 29, 22, 0, 0, DateTimeKind.Utc);
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hmmrevive-tests-" + Guid.NewGuid().ToString("N"));
        private readonly List<Process> _started = new List<Process>();
        public ServerExitReportTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { foreach (var p in _started) p.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

        private Process Exited(int code)
        {
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit " + code) { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit();
            _started.Add(p);
            return p;
        }

        private string File(string name, string text) { string p = Path.Combine(_dir, name); System.IO.File.WriteAllText(p, text); return p; }

        [Fact]
        public void AServerThatEndsByItselfIsReportedOnceWithItsCodeAndLogs()
        {
            Process p = Exited(3);
            bool running = true;
            string mod = File("mod.log", string.Join("\n", Enumerable.Range(1, 40).Select(i => "mod line " + i)) + "\n");
            string unity = File("unity.log", "Loading\nNullReferenceException: boom\nfine\nCrash!!!\n");
            var r = new ServerExitReport();

            Assert.Null(r.Check(p, _ => !running, T0, mod, unity));
            running = false;
            string report = r.Check(p, _ => !running, T0.AddSeconds(95), mod, unity);

            Assert.StartsWith("match server exited by itself: code=3 after 95 s", report);
            Assert.Contains("mod| mod line 40", report);
            Assert.DoesNotContain("mod line 10\n", report); // only the last 30 lines
            Assert.Contains("unity| NullReferenceException: boom", report);
            Assert.Contains("unity| Crash!!!", report);
            Assert.DoesNotContain("fine", report);
            Assert.Null(r.Check(p, _ => true, T0.AddSeconds(100), mod, unity)); // the lobby still holds it: once only
        }

        [Fact]
        public void AServerTheLobbyStoppedIsNotReported()
        {
            Process p = Exited(0);
            var r = new ServerExitReport();
            Assert.Null(r.Check(p, _ => false, T0, "none", "none"));
            Assert.Null(r.Check(null, _ => true, T0.AddSeconds(5), "none", "none")); // StopServer cleared Game.Server
        }

        [Fact]
        public void ANewMatchIsWatchedAfterAReport()
        {
            Process a = Exited(1), b = Exited(2);
            var r = new ServerExitReport();
            Assert.NotNull(r.Check(a, _ => true, T0, "none", "none"));
            Assert.Null(r.Check(b, x => false, T0.AddSeconds(10), "none", "none"));
            Assert.StartsWith("match server exited by itself: code=2", r.Check(b, x => true, T0.AddSeconds(20), "none", "none"));
        }
    }
}
