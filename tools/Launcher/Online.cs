using System;
using System.Diagnostics;
using System.IO;

namespace HmmRevive.Launcher
{
    /// <summary>
    /// "Play on HMM Private Club" (Lonny's server, with the game's online menus and matchmaking), as Lonny asked for it:
    /// his HMM-Connect connects the Steam copy of the game (it writes user.config and installs fixes his server checks
    /// at login, and his server needs a Steam login), so a HMM Revive copy can't be pointed at it and we ship no address.
    /// The button just starts the Steam game; a Steam copy HMM-Connect hasn't set up yet gets the club's Discord instead.
    /// Both live side by side: HMM-Connect only replaces files in the Steam folder (it never writes into them, so our
    /// hardlinked instances keep theirs), and the patcher uses its .hmm-backup of the original firstpass (tools/Patcher).
    /// </summary>
    public static class Online
    {
        public const string DiscordUrl = "https://discord.gg/hmmclub";

        /// <summary>Whether HMM-Connect has connected the Steam copy (its user.config, which it signs with its name).</summary>
        public static bool Connected
        {
            get
            {
                try
                {
                    string dir = Game.FindGame();
                    string config = dir == null ? null : Path.Combine(dir, "user.config");
                    return config != null && File.Exists(config) && File.ReadAllText(config).Contains("HMM-Connect");
                }
                catch { return false; }
            }
        }

        /// <summary>Starts the Steam game, which HMM-Connect pointed at the club.</summary>
        public static string Play()
        {
            if (Game.Running(Game.Client)) return "Close the HMM Revive game first.";
            if (Game.TestBackground) Program.Log("test: would start steam://rungameid/331360");
            else Process.Start(new ProcessStartInfo("steam://rungameid/331360") { UseShellExecute = true });
            return null;
        }

        public static void OpenDiscord() => Process.Start(new ProcessStartInfo(DiscordUrl) { UseShellExecute = true });
    }
}
