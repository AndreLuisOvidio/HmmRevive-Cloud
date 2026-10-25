using System;
using System.IO;
using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    // A copy set up by an older patcher keeps the same mod version string (1.8), so the version alone cannot say it must be
    // set up again. 1.8 preview 3 changed the patch (the game's clock reads UTC): players who updated from a preview 2 and
    // did not set the copy up again sat at a -59:xx match clock in a cloud lobby.
    public class BuildsPatchRevisionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hmmrevive-tests-" + Guid.NewGuid().ToString("N"));

        public BuildsPatchRevisionTests()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "instance-2017sep", "HMM_Data", "Managed"));
            Paths.Init(_dir, "instance");
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void ACopyWithoutTheMarkerIsRevisionOne()
        {
            Assert.Equal(1, Builds.InstancePatchRevision("2017sep"));
        }

        [Fact]
        public void TheRevisionIsReadFromTheMarkerThePatcherWrites()
        {
            File.WriteAllText(Path.Combine(_dir, "instance-2017sep", "HMM_Data", "Managed", "hmmrevive-patch.txt"), "2\n");
            Assert.Equal(2, Builds.InstancePatchRevision("2017sep"));
            File.WriteAllText(Path.Combine(_dir, "instance-2017sep", "HMM_Data", "Managed", "hmmrevive-patch.txt"), "junk");
            Assert.Equal(1, Builds.InstancePatchRevision("2017sep"));
        }

        [Theory]
        [InlineData("2017sep", true, "1.8", "1.8", 1, true)]   // same version, old patch: set it up again
        [InlineData("2017sep", true, "1.8", "1.8", 2, false)]  // current patch
        [InlineData("2017sep", true, "1.8", "1.7", 2, true)]   // older mod version, as before
        [InlineData("2017", true, "1.8", "1.8", 1, true)]
        [InlineData("steam", true, "1.8", "1.8", 1, false)]    // the Steam copy has no revision: the version decides
        [InlineData("2017sep", false, "1.8", "1.8", 1, false)] // no copy, nothing to update
        [InlineData("2017sep", true, null, "1.8", 1, false)]   // no mod in the kit to update with
        public void ACopyNeedsUpdatingWhenItsVersionOrItsPatchIsOld(string build, bool ready, string kit, string inst, int rev, bool expected)
        {
            Assert.Equal(expected, Builds.NeedsUpdate(build, ready, kit, inst, rev));
        }
    }
}
