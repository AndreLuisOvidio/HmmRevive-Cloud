using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    // The 2017 game syncs the client clock with the server over a second UDP port, the game port + 5000. A host whose Windows
    // Firewall lets only the game port in leaves every joiner on their own clock: a time zone or a few seconds of PC clock
    // error then shows as a negative match clock and late inputs.
    public class FirewallPortsTests
    {
        [Fact]
        public void AnOldBuildAlsoOpensTheClockSyncPort()
        {
            Assert.Equal("9696,14696", Firewall.GamePorts("2017sep", 9696));
            Assert.Equal("9697,14697", Firewall.GamePorts("2017", 9697));
        }

        [Fact]
        public void TheSteamBuildKeepsOnlyTheGamePort()
        {
            Assert.Equal("9696", Firewall.GamePorts(Builds.Steam, 9696));
        }

        [Fact]
        public void TheRuleOfAnOldBuildHasANewNameSoOlderHostsAreAskedAgain()
        {
            Assert.Equal("HMM Revive game", Firewall.GameRuleOf(Builds.Steam));
            Assert.Equal("HMM Revive game 2017sep + clock", Firewall.GameRuleOf("2017sep"));
            Assert.Equal("HMM Revive game 2017sep", Firewall.OldGameRuleOf("2017sep")); // removed when the new one is made
        }
    }
}
