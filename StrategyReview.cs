using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace User.TeamPace
{
    /// <summary>
    /// A plan against the race that was run: the real stints and stops of our car (from LMU's
    /// results for every car, plus what SimHub captured at our stops), matched stop by stop with
    /// the plan, and what explains the laps won or lost.
    /// </summary>
    internal class StrategyReview
    {
        public class RealStint
        {
            public string Driver;
            public int FromLap, ToLap;
            public double Start, End;   // race time, s (0 = start of lap 1)
            public int Laps { get { return ToLap - FromLap + 1; } }
        }

        public class RealStop
        {
            public int Lap;
            public double At;           // race time
            public double Loss;         // s lost against the car's pace
            public string From, To;     // driver change, or null
            public LmuLivePit Pit;      // captured live, when SimHub was running
        }

        public class Row
        {
            public StrategyEngine.StopPlan Planned;
            public int PlannedIndex = -1;
            public RealStop Real;
            public double Delta;        // real loss − planned loss
            public string Verdict;
            public int Mood;            // +1 fine, 0 neutral, −1 to review
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public CarRun Run;
        public List<RealStint> Stints = new List<RealStint>();
        public List<RealStop> Stops = new List<RealStop>();
        public List<Row> Rows = new List<Row>();
        public Dictionary<string, int> LapsByDriver = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public double Finish;         // race time at the flag
        public double UnplannedLoss, OverrunLoss;

        public static StrategyReview Build(RaceField field, StrategyEngine.Simulation plan, Dictionary<int, LmuLivePit> pits)
        {
            var car = field == null ? null : field.Cars.FirstOrDefault(c => c.IsUs);
            if (car == null || car.Starts.Count < 2) return null;
            var r = new StrategyReview { Run = RaceFieldAnalysis.Run(car) };
            double t0 = r.Run.Start;
            r.Finish = r.Run.End - t0;

            // Driver at a given session time: the last swap before it, else the first driver
            string first = car.Swaps.Count > 0 ? car.Swaps[0].From : car.Drivers.FirstOrDefault();
            Func<double, string> driverAt = et =>
            {
                string d = first;
                foreach (var s in car.Swaps) if (s.Et <= et) d = s.To;
                return d;
            };

            foreach (var s in r.Run.Stops)
            {
                var stop = new RealStop { Lap = s.Lap, At = s.At - t0, Loss = s.Loss };
                if (s.Swap != null) { stop.From = s.Swap.From; stop.To = s.Swap.To; }
                LmuLivePit pit;
                if (pits != null && (pits.TryGetValue(s.Lap, out pit) || pits.TryGetValue(s.Lap + 1, out pit) || pits.TryGetValue(s.Lap - 1, out pit))) stop.Pit = pit;
                r.Stops.Add(stop);
            }

            // Stints between stops
            int from = 1;
            for (int i = 0; i <= r.Stops.Count; i++)
            {
                int to = i < r.Stops.Count ? r.Stops[i].Lap : r.Run.Laps;
                if (to < from) continue;
                double start = car.Starts[from - 1] - t0;
                double end = i < r.Stops.Count ? r.Stops[i].At : r.Finish;
                var st = new RealStint { Driver = driverAt(car.Starts[from - 1] + 1), FromLap = from, ToLap = to, Start = start, End = end };
                r.Stints.Add(st);
                from = to + 1;
            }
            // Laps per driver, lap by lap (a stint may hold a swap LMU logged mid-stint)
            for (int i = 0; i < car.Starts.Count; i++)
            {
                var d = driverAt(car.Starts[i] + 1) ?? "?";
                int c; r.LapsByDriver.TryGetValue(d, out c); r.LapsByDriver[d] = c + 1;
            }

            if (plan != null) r.Match(plan);
            return r;
        }

        /// <summary>Each planned stop with the real one closest in laps (6 laps apart at most).</summary>
        private void Match(StrategyEngine.Simulation plan)
        {
            var used = new HashSet<RealStop>();
            var rows = new List<Row>();
            for (int i = 0; i < plan.Stops.Count; i++)
            {
                var p = plan.Stops[i];
                RealStop best = null;
                foreach (var s in Stops)
                    if (!used.Contains(s) && Math.Abs(s.Lap - p.Lap) <= 6 && (best == null || Math.Abs(s.Lap - p.Lap) < Math.Abs(best.Lap - p.Lap))) best = s;
                var row = new Row { Planned = p, PlannedIndex = i, Real = best };
                if (best != null)
                {
                    used.Add(best);
                    row.Delta = best.Loss - p.Lost;
                    int lapGap = best.Lap - p.Lap;
                    string when = lapGap == 0 ? "" : string.Format(", {0} tour{1} plus {2}", Math.Abs(lapGap), Math.Abs(lapGap) > 1 ? "s" : "", lapGap > 0 ? "tard" : "tôt");
                    if (row.Delta > 20)
                    {
                        row.Mood = -1;
                        OverrunLoss += row.Delta;
                        row.Verdict = "Plus long que prévu" + when;
                        var pit = best.Pit;
                        double usualLane = UsualLane();
                        if (pit != null && usualLane > 0 && pit.LaneSec > usualLane + 10)
                            row.Verdict = string.Format(Inv, "{0:0} s de plus dans la voie que d'habitude ({1:0} s)", pit.LaneSec - usualLane, usualLane);
                    }
                    else if (row.Delta < -20) { row.Mood = 1; row.Verdict = "Plus rapide que prévu" + when; }
                    else { row.Mood = 1; row.Verdict = "Conforme" + when; }
                }
                else { row.Mood = 0; row.Verdict = "Pas fait"; row.Delta = -p.Lost; }
                rows.Add(row);
            }
            foreach (var s in Stops)
            {
                if (used.Contains(s)) continue;
                bool noFuel = s.Pit != null && s.Pit.FuelAddedL < 1;
                var row = new Row
                {
                    Real = s, Delta = s.Loss, Mood = -1,
                    Verdict = s.From != null && (noFuel || s.Pit == null) ? "Arrêt imprévu : changement de pilote" + (noFuel ? " seul" : "") : "Arrêt imprévu",
                };
                UnplannedLoss += s.Loss;
                rows.Add(row);
            }
            rows.Sort((a, b) => Lap(a).CompareTo(Lap(b)));
            Rows = rows;
        }

        private static int Lap(Row r) { return r.Real != null ? r.Real.Lap : r.Planned.Lap; }

        /// <summary>The usual pit-lane time of our stops, when SimHub captured them.</summary>
        public double UsualLane()
        {
            var lanes = Stops.Where(s => s.Pit != null && s.Pit.LaneSec > 0).Select(s => s.Pit.LaneSec).ToList();
            return lanes.Count == 0 ? 0 : RaceFieldAnalysis.Median(lanes);
        }

        /// <summary>A stop as a short line: "T29 · 0h51 · +36.9 L · 4 pneus · +100 s · A→P".</summary>
        public static string Describe(RealStop s, Func<double, string> clock)
        {
            var parts = new List<string> { "T" + s.Lap, clock(s.At) };
            if (s.Pit != null)
            {
                if (s.Pit.FuelAddedL >= 1) parts.Add(string.Format(Inv, "+{0:0.0} L", s.Pit.FuelAddedL)); else parts.Add("sans carburant");
                if (s.Pit.TyresCount > 0) parts.Add(s.Pit.TyresCount == 1 ? "1 pneu" : s.Pit.TyresCount + " pneus");
                else if (s.Pit.TyresChanged) parts.Add("pneus changés");
            }
            parts.Add(string.Format(Inv, "+{0:0} s", s.Loss));
            if (s.From != null) parts.Add(Initial(s.From) + "→" + Initial(s.To));
            return string.Join(" · ", parts);
        }

        public static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Trim().Substring(0, 1).ToUpperInvariant();
        }
    }
}
