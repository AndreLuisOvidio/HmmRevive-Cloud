using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    // The lobby running the draft by positions (spec .specs/features/lol-draft): /lobby/draft, the clock, the match.
    public class LobbyDraftTests : IDisposable
    {
        private readonly List<Lobby> _lobbies = new List<Lobby>();
        private readonly List<Game.ServerOptions> _started = new List<Game.ServerOptions>();
        private readonly Dictionary<string, Lobby.Member> _m = new Dictionary<string, Lobby.Member>(); // by name

        public LobbyDraftTests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hmmrevive-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Paths.Init(dir, "instance");
            Settings.Data = new Dictionary<string, object>();
            Catalog.Load();
        }

        public void Dispose() { foreach (var l in _lobbies) l.Close(); }

        private static int FreePort()
        {
            var t = new TcpListener(IPAddress.Loopback, 0);
            t.Start();
            int port = ((IPEndPoint)t.LocalEndpoint).Port;
            t.Stop();
            return port;
        }

        private static Dictionary<string, object> Obj(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        // A drafting lobby: the host watches (no team), each name joins the given team; bots per team.
        private Lobby NewLobby(string[] blue, string[] red, int blueBots = 0, int redBots = 0, int plan = 0, bool spectators = false, int final = 20)
        {
            var l = new Lobby(FreePort(), FreePort(), Builds.Steam) { CountdownSeconds = 0, AutoStart = false, DraftPlanSeconds = plan, DraftFinalSeconds = final };
            l.BuildReady = _ => true;
            l.StartServerHook = o => { _started.Add(o); return null; };
            _lobbies.Add(l);
            var host = _m["Host"] = l.Join("Host", Program.Version, new[] { Builds.Steam }, true, out _);
            l.Update(host.Token, Obj("team", Lobby.NoTeam));
            l.Configure(Obj("draft", true, "spectators", spectators, "autoStart", false,
                "blue", Obj("bots", blueBots), "red", Obj("bots", redBots)));
            foreach (var (team, names) in new[] { ("blue", blue), ("red", red) })
                foreach (string n in names)
                {
                    var m = l.Join(n, Program.Version, new[] { Builds.Steam }, false, out string error);
                    Assert.True(m != null, error);
                    Assert.Null(l.Update(m.Token, Obj("team", team)));
                    _m[n] = m;
                }
            return l;
        }

        private Lobby.Member M(Lobby l, string name) => _m[name];

        private Response Act(Lobby l, string name, string action, string car = null, int? target = null, int? request = null)
        {
            var body = Obj("token", M(l, name).Token, "action", action);
            if (car != null) body["car"] = car;
            if (target != null) body["target"] = target.Value;
            if (request != null) body["request"] = request.Value;
            return l.Handle(new Request { Method = "POST", Path = "/lobby/draft", Body = Js.Write(body), Remote = IPAddress.Loopback });
        }

        private static string Text(Response r) => System.Text.Encoding.UTF8.GetString(r.Body);
        private static void Ok(Response r) => Assert.True(r.Status == 200, r.Status + " " + Text(r));
        private static void Refused(Response r, string why) { Assert.Equal(400, r.Status); Assert.Equal(why, Js.Read(Text(r)).Str("error")); }

        private Dictionary<string, object> Draft(Lobby l, string viewer) => (Dictionary<string, object>)l.Snapshot(M(l, viewer).Token)["draft"];

        private static object[] SeatsOf(Dictionary<string, object> d, string team) => (object[])((Dictionary<string, object>)d["seats"])[team];

        private Dictionary<string, object> SeatOf(Lobby l, string name)
        {
            var m = M(l, name);
            return SeatsOf(Draft(l, name), m.Team).Cast<Dictionary<string, object>>().Single(s => (string)s["id"] == m.Id);
        }

        private int Pos(Lobby l, string name) => (int)SeatOf(l, name)["pos"];

        private string Free(Lobby l, string viewer)
        {
            var d = Draft(l, viewer);
            var used = new HashSet<string>(((object[])d["bans"]).Concat((object[])d["picks"]).Cast<Dictionary<string, object>>().Select(c => (string)c["car"]));
            return Catalog.Cars.Select(c => c.Id.ToString()).First(c => !used.Contains(c));
        }

        // Every acting human marks a free car and locks it, until the draft leaves the turns.
        private void PlayAll(Lobby l, params string[] players)
        {
            for (int guard = 0; guard < 20 && (string)Draft(l, players[0])["stage"] == "turns"; guard++)
                foreach (string p in players.Where(p => (bool)SeatOf(l, p)["acting"]))
                {
                    Ok(Act(l, p, "mark", Free(l, p)));
                    Ok(Act(l, p, "lock"));
                }
        }

        // ---- DRAFT-01: the draw ----

        [Fact]
        public void StartingDrawsTeamOneAndPutsHumansBeforeBots()
        {
            var l = NewLobby(new[] { "Ana", "Bia" }, new[] { "Edu" }, blueBots: 2, redBots: 3, plan: 15);
            var firsts = new HashSet<string>();
            var blueOrders = new HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                Assert.Null(l.Start());
                Assert.Equal("draft", l.Phase);
                var d = Draft(l, "Ana");
                firsts.Add((string)d["first"]);
                var blue = SeatsOf(d, "blue").Cast<Dictionary<string, object>>().ToList();
                var red = SeatsOf(d, "red").Cast<Dictionary<string, object>>().ToList();
                Assert.Equal(new[] { false, false, true, true }, blue.Select(s => (bool)s["bot"]));
                Assert.Equal(new[] { false, true, true, true }, red.Select(s => (bool)s["bot"]));
                Assert.Equal(new[] { M(l, "Ana").Id, M(l, "Bia").Id }.OrderBy(x => x), blue.Take(2).Select(s => (string)s["id"]).OrderBy(x => x));
                Assert.Equal(M(l, "Edu").Id, red[0]["id"]);
                blueOrders.Add(string.Join(",", blue.Take(2).Select(s => (string)s["id"])));
                l.ResetDraft();
            }
            Assert.Equal(new[] { "blue", "red" }, firsts.OrderBy(x => x));
            Assert.Equal(2, blueOrders.Count);
        }

        // ---- the /lobby/draft route ----

        [Fact]
        public void PlayersMarkAndLockThroughTheRoute()
        {
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" });
            l.Start();
            string first = (string)Draft(l, "Ana")["first"];
            string banner = first == "blue" ? "Ana" : "Edu", other = first == "blue" ? "Edu" : "Ana";
            Refused(Act(l, other, "lock"), "It's not your turn.");
            Refused(Act(l, banner, "lock"), "Choose a car first.");
            Ok(Act(l, banner, "mark", "7"));
            Ok(Act(l, banner, "lock"));
            Assert.Equal("7", SeatOf(l, banner)["ban"]);
            Refused(Act(l, other, "mark", "7"), "That car is already banned or picked.");
        }

        [Fact]
        public void UnknownActionsAndNonPlayersAreRefused()
        {
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" }, spectators: true);
            var spec = _m["Sam"] = l.Join("Sam", Program.Version, new[] { Builds.Steam }, false, out _);
            Assert.Null(l.Update(spec.Token, Obj("team", Lobby.Spectator)));
            Refused(Act(l, "Ana", "lock"), "No draft is running.");
            l.Start();
            Refused(Act(l, "Ana", "dance"), "Unknown draft action.");
            Refused(Act(l, "Sam", "mark", "3"), "Only players draft.");
            Refused(Act(l, "Host", "mark", "3"), "Only players draft.");
        }

        [Fact]
        public void PlanningRefusesLockingThroughTheRoute()
        {
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" }, plan: 15);
            l.Start();
            Assert.Equal("plan", Draft(l, "Ana")["stage"]);
            Refused(Act(l, "Ana", "lock"), "The draft starts in a moment.");
        }

        // ---- DRAFT-07: bots and players who left ----

        [Fact]
        public void BotsPlayAtOnceAndAPlayerWhoLeftBecomesABot()
        {
            var l = NewLobby(new[] { "Ana", "Bia" }, new string[0], redBots: 2);
            l.Start();
            var d = Draft(l, "Ana");
            Assert.Equal(2, SeatsOf(d, "blue").Cast<Dictionary<string, object>>().Count(s => !(bool)s["bot"]));
            l.Leave(M(l, "Bia").Token);
            l.Step(DateTime.UtcNow);
            PlayAll(l, "Ana");
            var blue = SeatsOf(Draft(l, "Ana"), "blue").Cast<Dictionary<string, object>>().ToList();
            Assert.All(blue, s => Assert.NotEqual("", s["car"])); // Bia's position picked by itself
            Assert.All(SeatsOf(Draft(l, "Ana"), "red").Cast<Dictionary<string, object>>(), s => Assert.NotEqual("", s["car"]));
        }

        // ---- DRAFT-10: finalization and the match ----

        [Fact]
        public void AfterTheLastPickPlayersDriveTheirPicksAndReadyStartsTheMatch()
        {
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" }, blueBots: 1, redBots: 1);
            l.Start();
            PlayAll(l, "Ana", "Edu");
            var d = Draft(l, "Ana");
            Assert.Equal("final", d["stage"]);
            Assert.Equal(SeatOf(l, "Ana")["car"], M(l, "Ana").Car);
            Assert.Equal(SeatOf(l, "Edu")["car"], M(l, "Edu").Car);
            Assert.Empty(_started);
            l.Update(M(l, "Ana").Token, Obj("ready", true));
            Assert.Empty(_started);
            l.Update(M(l, "Edu").Token, Obj("ready", true));
            var o = Assert.Single(_started);
            Assert.Equal("starting", l.Phase);
            string BotCar(string team) => (string)SeatsOf(Draft(l, "Ana"), team).Cast<Dictionary<string, object>>().Single(s => (bool)s["bot"])["car"];
            Assert.Equal(BotCar("blue"), o.BluBotCars);
            Assert.Equal(BotCar("red"), o.RedBotCars);
        }

        [Fact]
        public void FinalizationEndsByItselfWhenItsTimeIsUp()
        {
            // The 20 s themselves are SeatDraft's (DraftTests); past 20 s the lobby would also drop the silent test players.
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" }, final: 5);
            l.Start();
            PlayAll(l, "Ana", "Edu");
            l.Step(DateTime.UtcNow.AddSeconds(4));
            Assert.Empty(_started);
            l.Step(DateTime.UtcNow.AddSeconds(6));
            Assert.Single(_started);
        }

        [Fact]
        public void WhileDraftingACarChangeIsIgnoredButASkinIsKept()
        {
            var l = NewLobby(new[] { "Ana" }, new[] { "Edu" });
            l.Start();
            PlayAll(l, "Ana", "Edu");
            string car = M(l, "Ana").Car;
            string other = Catalog.Cars.Select(c => c.Id.ToString()).First(c => c != car);
            Assert.Null(l.Update(M(l, "Ana").Token, Obj("car", other, "skin", "3")));
            Assert.Equal(car, M(l, "Ana").Car);
            Assert.Equal("3", M(l, "Ana").Skin);
        }

        // ---- DRAFT-11 / DRAFT-12 through the route ----

        [Fact]
        public void TeammatesSwapPositionsAndTradeCarsThroughTheRoute()
        {
            var l = NewLobby(new[] { "Ana", "Bia" }, new[] { "Edu" }, plan: 15);
            l.Start();
            int ana = Pos(l, "Ana"), bia = Pos(l, "Bia");
            Ok(Act(l, "Ana", "swap", target: bia));
            var req = (Dictionary<string, object>)Assert.Single((object[])Draft(l, "Bia")["requests"]);
            Assert.Empty((object[])Draft(l, "Edu")["requests"]);
            Ok(Act(l, "Bia", "accept", request: (int)req["id"]));
            Assert.Equal((bia, ana), (Pos(l, "Ana"), Pos(l, "Bia")));

            l.Step(DateTime.UtcNow.AddSeconds(15)); // planning ends
            PlayAll(l, "Ana", "Bia", "Edu");
            string a = M(l, "Ana").Car, b = M(l, "Bia").Car;
            Ok(Act(l, "Ana", "trade", target: Pos(l, "Bia")));
            req = (Dictionary<string, object>)Assert.Single((object[])Draft(l, "Bia")["requests"]);
            Ok(Act(l, "Bia", "accept", request: (int)req["id"]));
            Assert.Equal((b, a), (M(l, "Ana").Car, M(l, "Bia").Car));
        }

        // ---- DRAFT-14: host settings ----

        [Fact]
        public void TheHostSetupHasNoOrderAndTurnsDefaultToThirtySeconds()
        {
            var l = new Lobby(FreePort(), FreePort(), Builds.Steam);
            _lobbies.Add(l);
            Assert.Equal(30, l.DraftTime);
            l.Configure(Obj("draft", true, "draftBans", "A3 B3", "draftPicks", "A4 B4"));
            var setup = l.SetupJson();
            Assert.False(setup.ContainsKey("draftBans"));
            Assert.False(setup.ContainsKey("draftPicks"));
            Assert.True((bool)setup["draft"]);
            Assert.Equal(30, setup["draftTime"]);
        }
    }
}
