using System;
using System.Collections.Generic;
using System.Linq;

namespace HmmRevive.Launcher
{
    /// <summary>
    /// Tournament draft by positions, like League of Legends' champion select, run in the lobby before the match. Each
    /// team's players hold positions 1 to 4 (drawn by the lobby; bots after the humans). Team 1 (drawn) and Team 2 then
    /// follow one fixed order: position 1 of each team bans one car, then the picks go 1-2-2-1-1-1 starting with Team 1,
    /// each position picking its own car. Positions acting in the same turn choose at the same time, each with its own
    /// mark and lock. The draft opens with a short planning stage (marks only) and ends with a finalization stage before
    /// the match. A turn has a time limit; when it runs out a pick takes the mark or a free car, a ban the mark or nothing.
    /// Bots, and players who left the lobby, ban nothing and pick a free car at once. Time is passed in, so the lobby's
    /// clock (and tests) drive it.
    /// </summary>
    public class SeatDraft
    {
        public const int DefaultTurnSeconds = 30, PlanSeconds = 15, FinalSeconds = 20;

        /// <summary>A position. Member null = a bot. Mark = the car chosen for the current action; Intent = the car the
        /// player plans to pick, marked outside their turn (used as the mark when their pick turn starts).</summary>
        public class Seat { public string Member, Mark, Intent, Ban, Car; public bool Locked; }
        public class Turn { public string Team; public bool Pick; public List<int> Positions; }

        // The order, for Team 1 (true) and Team 2: ban, ban, then picks 1-2-2-1-1-1.
        private static readonly (bool first, bool pick, int[] positions)[] Order =
        {
            (true, false, new[] { 1 }), (false, false, new[] { 1 }),
            (true, true, new[] { 1 }), (false, true, new[] { 1, 2 }), (true, true, new[] { 2, 3 }),
            (false, true, new[] { 3 }), (true, true, new[] { 4 }), (false, true, new[] { 4 }),
        };

        public readonly string First, Second; // "blue"/"red": Team 1 and Team 2
        public readonly Dictionary<string, List<Seat>> Seats;
        public readonly List<Turn> Turns;
        public int Step { get; private set; }
        public string Stage { get; private set; } // plan, turns, final, done
        public DateTime? StageEnds { get; private set; } // null: no time limit
        private readonly List<string> _cars;
        private readonly int _turnSeconds, _finalSeconds;

        public Turn Current => Stage == "turns" && Step < Turns.Count ? Turns[Step] : null;

        /// <summary>The fixed order without the positions a team doesn't have.</summary>
        public static List<Turn> TurnsFor(string first, string second, int firstSeats, int secondSeats)
        {
            var turns = new List<Turn>();
            foreach (var (isFirst, pick, positions) in Order)
            {
                List<int> p = positions.Where(x => x <= (isFirst ? firstSeats : secondSeats)).ToList();
                if (p.Count > 0) turns.Add(new Turn { Team = isFirst ? first : second, Pick = pick, Positions = p });
            }
            return turns;
        }

        /// <param name="seats">per team, the member id of each position in order (null = bot)</param>
        /// <param name="cars">every car id</param>
        /// <param name="turnSeconds">time per turn, 0 = no limit (then finalization waits for the players too)</param>
        public SeatDraft(string first, Dictionary<string, List<string>> seats, IEnumerable<string> cars, int turnSeconds, DateTime now,
            int planSeconds = PlanSeconds, int finalSeconds = FinalSeconds)
        {
            First = first;
            Second = first == "blue" ? "red" : "blue";
            Seats = new[] { "blue", "red" }.ToDictionary(t => t, t => (seats.TryGetValue(t, out var ids) ? ids : new List<string>())
                .Select(id => new Seat { Member = id }).ToList());
            _cars = cars.ToList();
            _turnSeconds = Math.Max(0, turnSeconds);
            _finalSeconds = finalSeconds;
            Turns = TurnsFor(First, Second, Seats[First].Count, Seats[Second].Count);
            Stage = "plan";
            StageEnds = now.AddSeconds(planSeconds);
            if (planSeconds <= 0) StartTurns(now);
        }

        private Seat SeatAt(string team, int pos) => Seats.TryGetValue(team, out var s) && pos >= 1 && pos <= s.Count ? s[pos - 1] : null;

        private bool Acting(string team, int pos) => Current != null && Current.Team == team && Current.Positions.Contains(pos);

        public bool Available(string car) => car != null && _cars.Contains(car) && !Seats.Values.SelectMany(s => s).Any(s => s.Ban == car || s.Car == car);

        /// <summary>A player clicks a car: the mark for their current action, or else the car they plan to pick.</summary>
        public string Mark(string team, int pos, string car)
        {
            Seat s = SeatAt(team, pos);
            if (s == null) return "Only players draft.";
            if (Stage == "final" || Stage == "done") return "The draft is over.";
            if (!Available(car)) return "That car is already banned or picked.";
            if (Acting(team, pos) && !s.Locked) s.Mark = car;
            else if (s.Car == null) s.Intent = car;
            return null;
        }

        public string Lock(string team, int pos, DateTime now)
        {
            Seat s = SeatAt(team, pos);
            if (s == null) return "Only players draft.";
            if (Stage == "plan") return "The draft starts in a moment.";
            if (Stage != "turns") return "The draft is over.";
            if (!Acting(team, pos)) return "It's not your turn.";
            if (s.Locked) return "You already locked in.";
            if (s.Mark == null) return "Choose a car first.";
            if (!Available(s.Mark)) return "That car is already banned or picked.";
            Commit(s, Current.Pick, s.Mark);
            if (AllLocked()) NextTurn(now);
            return null;
        }

        /// <summary>
        /// The lobby's clock: ends planning, plays bots and players who left (<paramref name="gone"/>), completes a timed
        /// out turn and ends finalization. True when the draft changed.
        /// </summary>
        public bool Tick(DateTime now, Random rng, Func<string, bool> gone)
        {
            bool changed = false;
            if (Stage == "plan" && StageEnds != null && now >= StageEnds) { StartTurns(now); changed = true; }
            while (Stage == "turns")
            {
                Turn t = Current;
                bool timedOut = StageEnds != null && now >= StageEnds;
                foreach (Seat s in t.Positions.Select(p => SeatAt(t.Team, p)).Where(s => !s.Locked))
                {
                    bool bot = s.Member == null || gone(s.Member);
                    if (!bot && !timedOut) continue;
                    // A pick always gets a car; a ban only a marked one (a bot marks nothing).
                    string car = !bot && Available(s.Mark) ? s.Mark : t.Pick ? Free(rng) : null;
                    Commit(s, t.Pick, car);
                    changed = true;
                }
                if (!AllLocked()) break;
                NextTurn(now);
                changed = true;
            }
            if (Stage == "final" && StageEnds != null && now >= StageEnds) { Stage = "done"; changed = true; }
            return changed;
        }

        private string Free(Random rng) => _cars.Where(Available).OrderBy(_ => rng.Next()).FirstOrDefault();

        private bool AllLocked() => Current.Positions.All(p => SeatAt(Current.Team, p).Locked);

        private void Commit(Seat s, bool pick, string car)
        {
            if (pick) s.Car = car;
            else s.Ban = car;
            s.Locked = true;
            s.Mark = null;
        }

        private void StartTurns(DateTime now)
        {
            Stage = "turns";
            Step = 0;
            StartTurn(now);
        }

        private void NextTurn(DateTime now)
        {
            Step++;
            StartTurn(now);
        }

        // A pick turn starts on the car the player planned, when it's still free; a ban turn starts empty.
        private void StartTurn(DateTime now)
        {
            if (Step >= Turns.Count)
            {
                Stage = "final";
                StageEnds = _turnSeconds > 0 ? now.AddSeconds(_finalSeconds) : (DateTime?)null;
                return;
            }
            Turn t = Turns[Step];
            foreach (int p in t.Positions)
            {
                Seat s = SeatAt(t.Team, p);
                s.Locked = false;
                s.Mark = t.Pick && Available(s.Intent) ? s.Intent : null;
            }
            StageEnds = _turnSeconds > 0 ? now.AddSeconds(_turnSeconds) : (DateTime?)null;
        }
    }
}
