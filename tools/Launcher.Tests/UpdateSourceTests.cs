using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    /// <summary>HMM Revive Cloud updates from its own releases (HmmRevive-Cloud), chosen at build time.</summary>
    public class UpdateSourceTests
    {
        [Fact]
        public void ABuildUpdatesFromTheHmmReviveCloudReleasesByDefault()
        {
            // The repository id of AndreLuisOvidio/HmmRevive-Cloud: it stays right if the repository is renamed.
            Assert.Equal("https://api.github.com/repositories/1404819524/releases", Updater.ReleasesUrl);
        }

        [Fact]
        public void TheVersionIsTheCloudLine()
        {
            Assert.Equal("1.7", Program.Version);
        }
    }
}
