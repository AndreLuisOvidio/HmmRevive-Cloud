using System;
using System.Collections.Generic;
using System.Linq;
using HmmRevive.Launcher;
using Xunit;

namespace HmmRevive.Launcher.Tests
{
    // The draft by positions (spec .specs/features/lol-draft): order, marks and locks, time outs, bots, requests, views.
    public class DraftTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        private static readonly List<string> Cars = Enumerable.Range(1, 18).Select(i => i.ToString()).ToList();
        private static readonly Func<string, bool> NoneGone = _ => false;

        private static SeatDraft New(string[] blue, string[] red, string first = "blue", int turn = 30, int plan = 0, int final = 20) =>
            new SeatDraft(first, new Dictionary<string, List<string>> { ["blue"] = blue.ToList(), ["red"] = red.ToList() }, Cars, turn, T0, plan, final);

        private static SeatDraft Four(string first = "blue", int turn = 30, int plan = 0) =>
            New(new[] { "b1", "b2", "b3", "b4" }, new[] { "r1", "r2", "r3", "r4" }, first, turn, plan);

        private static string Shape(IEnumerable<SeatDraft.Turn> turns) =>
            string.Join(" ", turns.Select(t => $"{t.Team[0]}{(t.Pick ? "P" : "B")}{string.Join("", t.Positions)}"));

        // Marks then locks every acting position of the current turn with the given cars, in order.
        private static void Play(SeatDraft d, params string[] cars)
        {
            var t = d.Current;
            for (int i = 0; i < t.Positions.Count; i++)
            {
                Assert.Null(d.Mark(t.Team, t.Positions[i], cars[i]));
                Assert.Null(d.Lock(t.Team, t.Positions[i], T0));
            }
        }

        private static SeatDraft.Seat Seat(SeatDraft d, string team, int pos) => d.Seats[team][pos - 1];

        // ---- DRAFT-02: the fixed order ----

        [Fact]
        public void TheOrderIsOneBanEachThenPicksOneTwoTwoOneOneOne()
        {
            Assert.Equal("bB1 rB1 bP1 rP12 bP23 rP3 bP4 rP4", Shape(SeatDraft.TurnsFor("blue", "red", 4, 4)));
            Assert.Equal("rB1 bB1 rP1 bP12 rP23 bP3 rP4 bP4", Shape(SeatDraft.TurnsFor("red", "blue", 4, 4)));
        }

        [Fact]
        public void ADraftStartsWithTheTeamOneBanOfPositionOne()
        {
            var d = Four("red");
            Assert.Equal("turns", d.Stage);
            Assert.Equal("red", d.Current.Team);
            Assert.False(d.Current.Pick);
            Assert.Equal(new[] { 1 }, d.Current.Positions);
        }

        // ---- DRAFT-03: positions a team doesn't have ----

        [Fact]
        public void PositionsATeamDoesNotHaveAreSkipped()
        {
            Assert.Equal("bB1 rB1 bP1 rP12 bP2 rP3", Shape(SeatDraft.TurnsFor("blue", "red", 2, 3)));
            Assert.Equal("bB1 rB1 bP1 rP1", Shape(SeatDraft.TurnsFor("blue", "red", 1, 1)));
        }

        // ---- DRAFT-04: planning ----

        [Fact]
        public void PlanningRefusesLockingAndKeepsTheMarkAsIntent()
        {
            var d = Four(plan: 15);
            Assert.Equal("plan", d.Stage);
            Assert.Null(d.Mark("blue", 3, "7"));
            Assert.Equal("7", Seat(d, "blue", 3).Intent);
            Assert.Equal("The draft starts in a moment.", d.Lock("blue", 1, T0));
            Assert.False(d.Tick(T0.AddSeconds(14), new Random(1), NoneGone));
            Assert.Equal("plan", d.Stage);
            Assert.True(d.Tick(T0.AddSeconds(15), new Random(1), NoneGone));
            Assert.Equal("turns", d.Stage);
            Assert.Equal(T0.AddSeconds(15 + 30), d.StageEnds);
        }

        [Fact]
        public void AnIntentBecomesTheMarkWhenThePickTurnStartsButNotForABan()
        {
            var d = Four(plan: 15);
            d.Mark("blue", 1, "5");
            d.Tick(T0.AddSeconds(15), new Random(1), NoneGone);
            Assert.Null(Seat(d, "blue", 1).Mark); // the ban turn starts empty
            Assert.Null(d.Mark("blue", 1, "9"));
            Assert.Null(d.Lock("blue", 1, T0));
            Play(d, "10"); // red bans
            Assert.Equal("5", Seat(d, "blue", 1).Mark); // blue's first pick starts on the planned car
        }

        // ---- DRAFT-05: marking and locking ----

        [Fact]
        public void AnActingPlayerMarksAndTheLastMarkWins()
        {
            var d = Four();
            Assert.Null(d.Mark("blue", 1, "3"));
            Assert.Null(d.Mark("blue", 1, "4"));
            Assert.Equal("4", Seat(d, "blue", 1).Mark);
        }

        [Fact]
        public void LockingOutsideYourTurnIsRefused()
        {
            var d = Four();
            Assert.Equal("It's not your turn.", d.Lock("blue", 2, T0));
            Assert.Equal("It's not your turn.", d.Lock("red", 1, T0));
        }

        [Fact]
        public void MarkingABannedOrPickedCarIsRefused()
        {
            var d = Four();
            Play(d, "3"); // blue bans 3
            Assert.Equal("That car is already banned or picked.", d.Mark("red", 1, "3"));
            Play(d, "4"); // red bans 4
            Play(d, "5"); // blue picks 5
            Assert.Equal("That car is already banned or picked.", d.Mark("red", 1, "5"));
        }

        [Fact]
        public void LockingWithoutAMarkIsRefused()
        {
            Assert.Equal("Choose a car first.", Four().Lock("blue", 1, T0));
        }

        [Fact]
        public void LockingTwiceInOneTurnIsRefused()
        {
            var d = Four();
            Play(d, "1"); Play(d, "2"); Play(d, "3");
            d.Mark("red", 1, "4");
            Assert.Null(d.Lock("red", 1, T0));
            Assert.Equal("You already locked in.", d.Lock("red", 1, T0));
        }

        [Fact]
        public void ALockRecordsTheBanOrThePickAndTheTurnWaitsForEveryActingPosition()
        {
            var d = Four();
            Play(d, "1");
            Assert.Equal("1", Seat(d, "blue", 1).Ban);
            Play(d, "2");
            Play(d, "3");
            Assert.Equal("3", Seat(d, "blue", 1).Car);
            int step = d.Step;
            d.Mark("red", 1, "4");
            d.Lock("red", 1, T0);
            Assert.Equal(step, d.Step); // red position 2 still has to lock
            d.Mark("red", 2, "5");
            d.Lock("red", 2, T0);
            Assert.Equal(step + 1, d.Step);
            Assert.Equal("4", Seat(d, "red", 1).Car);
            Assert.Equal("5", Seat(d, "red", 2).Car);
        }

        [Fact]
        public void TwoTeammatesMarkingTheSameCarOnlyTheFirstLockGetsIt()
        {
            var d = Four();
            Play(d, "1"); Play(d, "2"); Play(d, "3");
            Assert.Null(d.Mark("red", 1, "9"));
            Assert.Null(d.Mark("red", 2, "9"));
            Assert.Null(d.Lock("red", 1, T0));
            Assert.Equal("That car is already banned or picked.", d.Lock("red", 2, T0));
        }

        // ---- DRAFT-06: time out ----

        [Fact]
        public void ATimedOutPickTakesTheMarkOrElseAFreeCar()
        {
            var d = Four();
            Play(d, "1"); Play(d, "2"); Play(d, "3");
            DateTime start = d.StageEnds.Value.AddSeconds(-30);
            d.Mark("red", 1, "8");
            Assert.True(d.Tick(start.AddSeconds(30), new Random(1), NoneGone));
            Assert.Equal("8", Seat(d, "red", 1).Car);
            string other = Seat(d, "red", 2).Car;
            Assert.Contains(other, Cars);
            Assert.DoesNotContain(other, new[] { "1", "2", "3", "8" });
            Assert.Equal("blue", d.Current.Team);
        }

        [Fact]
        public void ATimedOutBanTakesTheMarkOrElseBansNothing()
        {
            var d = Four();
            d.Mark("blue", 1, "6");
            d.Tick(T0.AddSeconds(30), new Random(1), NoneGone);
            Assert.Equal("6", Seat(d, "blue", 1).Ban);
            d.Tick(T0.AddSeconds(60), new Random(1), NoneGone);
            Assert.Null(Seat(d, "red", 1).Ban);
            Assert.True(d.Current.Pick); // the turn moved on to blue's first pick
        }

        [Fact]
        public void WithoutATimeLimitATurnNeverTimesOut()
        {
            var d = Four(turn: 0);
            Assert.Null(d.StageEnds);
            Assert.False(d.Tick(T0.AddHours(1), new Random(1), NoneGone));
            Assert.Equal(0, d.Step);
        }

        // ---- DRAFT-07: bots and players who left ----

        [Fact]
        public void ABotBansNothingAndPicksAFreeCarAtOnce()
        {
            var d = New(new[] { "b1" }, new string[] { null, null }, "red");
            Assert.True(d.Tick(T0, new Random(1), NoneGone));
            Assert.Null(Seat(d, "red", 1).Ban);
            Assert.Equal("blue", d.Current.Team); // the bot's ban was skipped
            Play(d, "2"); // blue bans
            d.Tick(T0, new Random(1), NoneGone);
            Assert.NotNull(Seat(d, "red", 1).Car);
            Assert.Equal("blue", d.Current.Team);
        }

        [Fact]
        public void APlayerWhoLeftPlaysLikeABot()
        {
            var d = Four();
            Assert.True(d.Tick(T0, new Random(1), id => id == "b1"));
            Assert.Null(Seat(d, "blue", 1).Ban);
            Assert.Equal("red", d.Current.Team);
        }

        [Fact]
        public void ATeamOfBotsNeverMakesTheDraftWait()
        {
            var d = New(new[] { "b1" }, new string[] { null, null, null, null });
            Play(d, "1");
            d.Tick(T0, new Random(1), NoneGone);
            Play(d, "2"); // blue picks; red's two bots follow at once
            d.Tick(T0, new Random(1), NoneGone);
            Assert.Equal("final", d.Stage); // blue has one seat: nothing left for it
            Assert.All(d.Seats["red"], s => Assert.NotNull(s.Car));
        }

        // ---- DRAFT-08: integrity ----

        [Fact]
        public void AFullDraftGivesEverySeatOneCarAndNoCarTwice()
        {
            var d = New(new string[] { null, null, null, null }, new string[] { null, null, null, null });
            d.Tick(T0, new Random(7), NoneGone);
            Assert.Equal("final", d.Stage);
            var picks = d.Seats.Values.SelectMany(s => s).Select(s => s.Car).ToList();
            Assert.Equal(8, picks.Count);
            Assert.All(picks, Assert.NotNull);
            Assert.Equal(8, picks.Distinct().Count());
        }

        [Fact]
        public void APickNeverTakesABannedCar()
        {
            var d = Four();
            Play(d, "1"); Play(d, "2");
            d.Tick(T0.AddSeconds(1000), new Random(3), id => true); // everyone else plays as a bot from here
            var picks = d.Seats.Values.SelectMany(s => s).Select(s => s.Car).ToList();
            Assert.DoesNotContain("1", picks);
            Assert.DoesNotContain("2", picks);
        }

        // ---- finalization (DRAFT-10, engine side) ----

        [Fact]
        public void AfterTheLastPickFinalizationLastsTwentySecondsThenTheDraftIsDone()
        {
            var d = New(new[] { "b1" }, new[] { "r1" });
            Play(d, "1"); Play(d, "2"); Play(d, "3");
            DateTime last = T0.AddSeconds(5);
            d.Mark("red", 1, "4");
            Assert.Null(d.Lock("red", 1, T0));
            Assert.Equal("final", d.Stage);
            DateTime ends = d.StageEnds.Value;
            Assert.False(d.Tick(ends.AddSeconds(-1), new Random(1), NoneGone));
            Assert.True(d.Tick(ends, new Random(1), NoneGone));
            Assert.Equal("done", d.Stage);
            Assert.Equal("The draft is over.", d.Mark("blue", 1, "9"));
        }

        // ---- DRAFT-11: swap positions ----

        [Fact]
        public void ASwapRequestWaitsTenSecondsForTheTeammate()
        {
            var d = Four(plan: 15);
            Assert.Null(d.AskSwap("blue", 2, 3, T0));
            var r = Assert.Single(d.Requests);
            Assert.Equal(("swap", "blue", 2, 3, T0.AddSeconds(10)), (r.Kind, r.Team, r.From, r.To, r.Expires));
        }

        [Fact]
        public void AnAcceptedSwapExchangesThePositions()
        {
            var d = Four(plan: 15);
            d.Mark("blue", 3, "7"); // b3 plans to pick 7
            d.AskSwap("blue", 2, 3, T0);
            Assert.Null(d.Answer("blue", 3, d.Requests[0].Id, true));
            Assert.Equal("b3", Seat(d, "blue", 2).Member);
            Assert.Equal("b2", Seat(d, "blue", 3).Member);
            Assert.Equal("7", Seat(d, "blue", 2).Intent); // the plan goes with the player
            Assert.Empty(d.Requests);
        }

        [Fact]
        public void ADeclinedOrExpiredSwapIsDropped()
        {
            var d = Four(plan: 15);
            d.AskSwap("blue", 2, 3, T0);
            Assert.Null(d.Answer("blue", 3, d.Requests[0].Id, false));
            Assert.Empty(d.Requests);
            Assert.Equal("b2", Seat(d, "blue", 2).Member);

            d.AskSwap("blue", 2, 3, T0);
            d.Tick(T0.AddSeconds(9), new Random(1), NoneGone);
            Assert.Single(d.Requests);
            Assert.True(d.Tick(T0.AddSeconds(10), new Random(1), NoneGone));
            Assert.Empty(d.Requests);
        }

        [Fact]
        public void NoSwapForAPlayerWithACarOrActingNow()
        {
            var d = Four();
            Assert.Equal("You can't swap positions now.", d.AskSwap("blue", 1, 2, T0)); // position 1 bans now
            Play(d, "1"); Play(d, "2"); Play(d, "3"); // blue position 1 has a car
            Assert.Equal("You can't swap positions now.", d.AskSwap("blue", 2, 1, T0));

            var p = Four(plan: 5);
            p.AskSwap("blue", 2, 1, T0);
            p.Tick(T0.AddSeconds(5), new Random(1), NoneGone); // planning ends: position 1 is acting
            Assert.Equal("You can't swap positions now.", p.Answer("blue", 1, p.Requests[0].Id, true));
            Assert.Equal("b1", Seat(p, "blue", 1).Member);
        }

        [Fact]
        public void ASwapNeedsAHumanTeammate()
        {
            var d = New(new[] { "b1", "b2", null }, new[] { "r1" }, plan: 15);
            Assert.Equal("Pick a teammate.", d.AskSwap("blue", 2, 3, T0));
            Assert.Equal("Pick a teammate.", d.AskSwap("blue", 2, 2, T0));
            Assert.Equal("Pick a teammate.", d.AskSwap("blue", 2, 4, T0));
        }

        // ---- DRAFT-12: trade cars ----

        private static SeatDraft TwoPicked(string[] blue)
        {
            var d = New(blue, new[] { "r1", "r2" });
            Play(d, "1"); Play(d, "2"); Play(d, "3"); Play(d, "4", "5");
            d.Tick(T0, new Random(1), NoneGone); // a bot in position 2 picks now
            if (d.Current != null) Play(d, "6");
            return d;
        }

        [Fact]
        public void AnAcceptedTradeExchangesTheCars()
        {
            var d = TwoPicked(new[] { "b1", "b2" });
            Assert.Equal("final", d.Stage);
            Assert.Null(d.AskTrade("blue", 1, 2, T0));
            var r = Assert.Single(d.Requests);
            Assert.Equal(("trade", 1, 2, T0.AddSeconds(10)), (r.Kind, r.From, r.To, r.Expires));
            Assert.Null(d.Answer("blue", 2, r.Id, true));
            Assert.Equal("6", Seat(d, "blue", 1).Car);
            Assert.Equal("3", Seat(d, "blue", 2).Car);
        }

        [Fact]
        public void TradingWithABotNeedsNoConsent()
        {
            var d = TwoPicked(new[] { "b1", null });
            string botCar = Seat(d, "blue", 2).Car;
            Assert.Null(d.AskTrade("blue", 1, 2, T0));
            Assert.Empty(d.Requests);
            Assert.Equal(botCar, Seat(d, "blue", 1).Car);
            Assert.Equal("3", Seat(d, "blue", 2).Car);
        }

        [Fact]
        public void ATradeNeedsACarOnBothSides()
        {
            var d = Four();
            Play(d, "1"); Play(d, "2"); Play(d, "3"); // only blue position 1 has a car
            Assert.Equal("Both of you need a car first.", d.AskTrade("blue", 1, 2, T0));
            Assert.Equal("Both of you need a car first.", d.AskTrade("blue", 2, 1, T0));
        }

        // ---- DRAFT-09: what each side sees ----

        private static Dictionary<string, object> SeatJson(Dictionary<string, object> j, string team, int pos) =>
            (Dictionary<string, object>)((object[])((Dictionary<string, object>)j["seats"])[team])[pos - 1];

        private static List<(string car, string team)> Choices(Dictionary<string, object> j, string key) =>
            ((object[])j[key]).Cast<Dictionary<string, object>>().Select(c => ((string)c["car"], (string)c["team"])).ToList();

        [Fact]
        public void MarksShowOnlyToTheMarkingTeam()
        {
            var d = Four(plan: 15);
            d.Mark("blue", 3, "7"); // a plan
            d.Tick(T0.AddSeconds(15), new Random(1), NoneGone);
            d.Mark("blue", 1, "5"); // a ban mark
            Assert.Equal("5", SeatJson(d.Json("blue", T0), "blue", 1)["mark"]);
            Assert.Equal("7", SeatJson(d.Json("blue", T0), "blue", 3)["intent"]);
            foreach (string viewer in new[] { "red", "spec", null })
            {
                Assert.Equal("", SeatJson(d.Json(viewer, T0), "blue", 1)["mark"]);
                Assert.Equal("", SeatJson(d.Json(viewer, T0), "blue", 3)["intent"]);
            }
        }

        [Fact]
        public void EveryoneSeesLockedChoicesTheOrderAndTheClock()
        {
            var d = Four();
            Play(d, "5"); Play(d, "6"); Play(d, "7");
            foreach (string viewer in new[] { "blue", "red", "spec" })
            {
                var j = d.Json(viewer, T0.AddSeconds(10));
                Assert.Equal(new[] { ("5", "blue"), ("6", "red") }, Choices(j, "bans"));
                Assert.Equal(new[] { ("7", "blue") }, Choices(j, "picks"));
                Assert.Equal("5", SeatJson(j, "blue", 1)["ban"]);
                Assert.Equal("7", SeatJson(j, "blue", 1)["car"]);
                Assert.Equal(("blue", "turns", 3, 8, 20), ((string)j["first"], (string)j["stage"], (int)j["step"], ((object[])j["turns"]).Length, (int)j["secondsLeft"]));
                var turn = (Dictionary<string, object>)((object[])j["turns"])[3];
                Assert.Equal(("red", true), ((string)turn["team"], (bool)turn["pick"]));
                Assert.Equal(new[] { 1, 2 }, (int[])turn["positions"]);
            }
        }

        [Fact]
        public void RequestsShowOnlyToTheirTeam()
        {
            var d = Four(plan: 15);
            d.AskSwap("blue", 2, 3, T0);
            var r = (Dictionary<string, object>)Assert.Single((object[])d.Json("blue", T0.AddSeconds(3))["requests"]);
            Assert.Equal(("swap", 2, 3, 7), ((string)r["kind"], (int)r["from"], (int)r["to"], (int)r["secondsLeft"]));
            Assert.Empty((object[])d.Json("red", T0)["requests"]);
            Assert.Empty((object[])d.Json("spec", T0)["requests"]);
        }

        [Fact]
        public void WithoutATimeLimitFinalizationHasNoEnd()
        {
            var d = New(new[] { "b1" }, new[] { "r1" }, turn: 0);
            Play(d, "1"); Play(d, "2"); Play(d, "3"); Play(d, "4");
            Assert.Equal("final", d.Stage);
            Assert.Null(d.StageEnds);
        }
    }
}
