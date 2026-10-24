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
        /// <summary>A teammate's request to swap positions or trade cars, waiting for position To to answer.</summary>
        public class Request { public int Id; public string Kind, Team; public int From, To; public DateTime Expires; }
        public const int RequestSeconds = 10;

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
        public readonly List<Request> Requests = new List<Request>();
        private int _requestId;
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
            bool changed = Requests.RemoveAll(r => now >= r.Expires) > 0;
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

        // ---- swaps and trades between teammates ----

        // Positions change hands only before either player has a car, and never while one of them is choosing.
        private bool CanSwap(string team, int a, int b) =>
            (Stage == "plan" || Stage == "turns") && SeatAt(team, a).Car == null && SeatAt(team, b).Car == null && !Acting(team, a) && !Acting(team, b);

        /// <summary>The player in position <paramref name="from"/> asks the one in <paramref name="to"/> to swap positions.</summary>
        public string AskSwap(string team, int from, int to, DateTime now)
        {
            Seat a = SeatAt(team, from), b = SeatAt(team, to);
            if (a == null) return "Only players draft.";
            if (b == null || b == a || b.Member == null) return "Pick a teammate.";
            if (!CanSwap(team, from, to)) return "You can't swap positions now.";
            Ask("swap", team, from, to, now);
            return null;
        }

        /// <summary>Asks a teammate to trade picked cars; with a bot of the team the trade happens at once.</summary>
        public string AskTrade(string team, int from, int to, DateTime now)
        {
            Seat a = SeatAt(team, from), b = SeatAt(team, to);
            if (a == null) return "Only players draft.";
            if (b == null || b == a) return "Pick a teammate.";
            if (a.Car == null || b.Car == null) return "Both of you need a car first.";
            if (Stage == "done") return "The draft is over.";
            if (b.Member == null) (a.Car, b.Car) = (b.Car, a.Car);
            else Ask("trade", team, from, to, now);
            return null;
        }

        private void Ask(string kind, string team, int from, int to, DateTime now)
        {
            Requests.RemoveAll(r => r.Team == team && r.From == from); // one request per player at a time
            Requests.Add(new Request { Id = ++_requestId, Kind = kind, Team = team, From = from, To = to, Expires = now.AddSeconds(RequestSeconds) });
        }

        /// <summary>The player in position <paramref name="pos"/> accepts or declines a request made to them.</summary>
        public string Answer(string team, int pos, int id, bool accept)
        {
            Request r = Requests.FirstOrDefault(x => x.Id == id && x.Team == team && x.To == pos);
            if (r == null) return "That request is gone.";
            Requests.Remove(r);
            if (!accept) return null;
            Seat a = SeatAt(team, r.From), b = SeatAt(team, r.To);
            if (r.Kind == "trade")
            {
                if (a.Car == null || b.Car == null) return "Both of you need a car first.";
                (a.Car, b.Car) = (b.Car, a.Car);
                return null;
            }
            if (!CanSwap(team, r.From, r.To)) return "You can't swap positions now.";
            (a.Member, b.Member) = (b.Member, a.Member);
            (a.Intent, b.Intent) = (b.Intent, a.Intent);
            // Other requests between these positions were meant for the players who just moved.
            Requests.RemoveAll(x => x.Team == team && new[] { x.From, x.To }.Intersect(new[] { r.From, r.To }).Any());
            return null;
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
