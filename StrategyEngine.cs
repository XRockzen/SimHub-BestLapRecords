using LiteDB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace User.TeamPace
{
    /// <summary>What the team enters for a race; saved as a named plan.</summary>
    public class StrategyPlan
    {
        public string Id           { get; set; }   // plan name
        public string Circuit      { get; set; }
        public string ClassLabel   { get; set; }
        public int    DurationMin  { get; set; }
        public string StartClock   { get; set; }   // "20:00", optional
        public int    TyreSets     { get; set; }
        public double TankL        { get; set; }
        public int    MaxStintMin  { get; set; }   // 0 = no limit
        public int    MinDriveMin  { get; set; }   // per driver, 0 = no rule
        // Drivers by name: the same on every teammate's computer, so a plan can be shared
        public List<string> Drivers     { get; set; }   // drivers taking part
        public List<string> StintDrivers { get; set; }  // driver of each stint, chosen by hand
        public List<int>    StopTyres   { get; set; }   // tyres changed at each stop, -1 = automatic
        public List<StrategyOverride> Overrides { get; set; }
        public string   Author     { get; set; }   // last saved by
        public DateTime UpdatedUtc { get; set; }

        public static StrategyPlan Default()
        {
            return new StrategyPlan
            {
                DurationMin = 360, StartClock = "", TyreSets = 6, TankL = 100, MaxStintMin = 0, MinDriveMin = 0,
                Drivers = new List<string>(), StintDrivers = new List<string>(), StopTyres = new List<int>(),
                Overrides = new List<StrategyOverride>(),
            };
        }

        /// <summary>Lists missing from a plan saved by an older version.</summary>
        public void Normalize()
        {
            if (Drivers == null) Drivers = new List<string>();
            if (StintDrivers == null) StintDrivers = new List<string>();
            if (StopTyres == null) StopTyres = new List<int>();
            if (Overrides == null) Overrides = new List<StrategyOverride>();
        }
    }

    public class StrategyOverride
    {
        public string Param     { get; set; }
        public string DriverName { get; set; }   // "" for team-wide values
        public double Value     { get; set; }
    }

    /// <summary>A value measured on the team's sessions, possibly replaced by hand.</summary>
    public class StrategyValue
    {
        public double Value;
        public string Source;
        public bool   Measured;     // false: default value
        public bool   Overridden;
    }

    /// <summary>
    /// Endurance planner: measures pace, degradation, consumption, wear and pit-stop times on the
    /// team's sessions for a circuit and category, then simulates the race stint by stint.
    /// </summary>
    internal static class StrategyEngine
    {
        public const string Pace = "pace", Degradation = "deg", Fuel = "fuel", Energy = "ve", Wear = "wear";
        public const string LaneLoss = "lane", FuelRate = "rate", Tyres2 = "tyres2", Tyres4 = "tyres4", TyreLife = "life";
        public static readonly string[] DriverParams = { Pace, Degradation, Fuel, Energy, Wear };
        public static readonly string[] TeamParams = { LaneLoss, FuelRate, Tyres2, Tyres4, TyreLife };

        public static string Label(string p)
        {
            switch (p)
            {
                case Pace:        return "Rythme de course";
                case Degradation: return "Dégradation des pneus";
                case Fuel:        return "Consommation";
                case Energy:      return "Énergie virtuelle";
                case Wear:        return "Usure pneus (le plus usé)";
                case LaneLoss:    return "Passage aux stands (sans arrêt)";
                case FuelRate:    return "Débit d'essence";
                case Tyres2:      return "Changement de 2 pneus";
                case Tyres4:      return "Changement des 4 pneus";
                default:          return "Durée d'un train de pneus";
            }
        }

        public static string Format(string p, double v)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (p)
            {
                case Pace:        return Fmt.LapSec(v);
                case Degradation: return string.Format(inv, "+{0:0.000} s/tour", v);
                case Fuel:        return string.Format(inv, "{0:0.00} L/tour", v);
                case Energy:      return v > 0 ? string.Format(inv, "{0:0.0} %/tour", v) : "—";
                case Wear:        return string.Format(inv, "{0:0.00} %/tour", v);
                case LaneLoss:    return string.Format(inv, "{0:0} s perdues", v);
                case FuelRate:    return string.Format(inv, "{0:0.0} L/s", v);
                case Tyres2:
                case Tyres4:      return string.Format(inv, "+{0:0} s", v);
                default:          return string.Format(inv, "{0:0} tours", v);
            }
        }

        // ─── Measures ─────────────────────────────────────────────────────────

        public class DriverData
        {
            public DriverView Driver;
            public Dictionary<string, StrategyValue> Values = new Dictionary<string, StrategyValue>();
        }

        public class Measures
        {
            public List<DriverData> Drivers = new List<DriverData>();
            public Dictionary<string, StrategyValue> Team = new Dictionary<string, StrategyValue>();
        }

        private class Stop { public double Stopped, Fuel, Lane, Loss; public int Tyres; }

        public static Measures Measure(List<DriverView> drivers, string circuit, string classLabel, double tankL)
        {
            var m = new Measures();
            var stops = new List<Stop>();
            foreach (var d in drivers)
            {
                var dd = new DriverData { Driver = d };
                var times = new List<double>(); var slopes = new List<double>(); var fuel = new List<double>();
                var ve = new List<double>(); var wear = new List<double>();
                int sessions = 0;

                var list = d.Sessions.FindAll(s => s.TrackDisplayName == circuit && LmuCategoryStyle.GetShortName(s.CarClass) == classLabel);
                list.Sort((a, b) => b.SessionDate.CompareTo(a.SessionDate));
                foreach (var s in list)
                {
                    if (sessions >= 8) break;
                    var laps = LmuSessionStore.GetLapsForSession(s.FileId, d.DbPath);
                    if (laps.Count < 3) continue;
                    sessions++;
                    laps.Sort((a, b) => a.LapNum.CompareTo(b.LapNum));
                    try { AnalysisColumn.MergeLiveData(s, laps, d.DbPath); } catch { }

                    long best = long.MaxValue;
                    foreach (var l in laps) if (l.IsValid && l.LapTimeTicks > 0) best = Math.Min(best, l.LapTimeTicks);
                    var run = new List<double>();
                    for (int i = 0; i < laps.Count; i++)
                    {
                        var l = laps[i];
                        bool clean = l.IsValid && !l.IsPitLap && l.LapNum > 1 && l.LapTimeTicks > 0 && l.LapTimeTicks < best * 1.04
                                     && (i == 0 || !laps[i - 1].IsPitLap);
                        if (clean) { times.Add(l.LapTime.TotalSeconds); run.Add(l.LapTime.TotalSeconds); }
                        if (l.IsPitLap || i == laps.Count - 1)
                        {
                            if (run.Count >= 12) slopes.Add(Slope(run));
                            run.Clear();
                        }
                        if (l.IsPitLap) continue;
                        if (l.HasLive && l.LiveFuelUsedL > 0.3) fuel.Add(l.LiveFuelUsedL);
                        else if (l.FuelUsed > 0.002) fuel.Add(l.FuelUsed * tankL);
                        if (l.VirtualEnergyUsed > 0.002) ve.Add(l.VirtualEnergyUsed * 100);
                        double w = Math.Max(Math.Max(l.LiveWearFL, l.LiveWearFR), Math.Max(l.LiveWearRL, l.LiveWearRR));
                        if (l.HasLive && w > 0.05) wear.Add(w);
                        else if (i > 0 && !laps[i - 1].IsPitLap)
                        {
                            var p = laps[i - 1];
                            double xw = Math.Max(Math.Max(p.TireWearFL - l.TireWearFL, p.TireWearFR - l.TireWearFR),
                                                 Math.Max(p.TireWearRL - l.TireWearRL, p.TireWearRR - l.TireWearRR)) * 100;
                            if (xw > 0.05 && xw < 5) wear.Add(xw);
                        }
                    }

                    // Pit stops measured live: what each part of a stop costs
                    double pace = Median(times);
                    for (int i = 1; i + 1 < laps.Count; i++)
                    {
                        var p = laps[i].LivePit;
                        if (p == null || p.FromGarage) continue;
                        double inLap = Dur(laps[i]), outLap = Dur(laps[i + 1]);
                        var stop = new Stop { Stopped = p.StoppedSec, Fuel = p.FuelAddedL, Tyres = p.TyresCount, Lane = p.LaneSec, Loss = double.NaN };
                        if (!double.IsNaN(pace) && inLap > 0 && outLap > pace * 0.85 && !laps[i + 1].IsPitLap)
                            stop.Loss = inLap + outLap - 2 * pace;
                        stops.Add(stop);
                    }
                }

                string basis = string.Format("{0} séance(s)", sessions);
                dd.Values[Pace] = Value(Median(times), double.NaN, string.Format("médiane de {0} tours propres · {1}", times.Count, basis));
                dd.Values[Degradation] = Value(slopes.Count > 0 ? Math.Max(0, Math.Min(0.3, Median(slopes))) : double.NaN, 0.03,
                    slopes.Count > 0 ? string.Format("pente du rythme sur {0} relais de 12 tours ou plus", slopes.Count) : "pas de relais assez long : valeur par défaut");
                dd.Values[Fuel] = Value(Median(fuel), double.NaN, string.Format("{0} tours mesurés", fuel.Count));
                dd.Values[Energy] = Value(Median(ve), 0, ve.Count > 0 ? string.Format("résultats LMU · {0} tours", ve.Count) : "pas d'énergie virtuelle sur cette voiture");
                dd.Values[Wear] = Value(Median(wear), double.NaN, string.Format("{0} tours mesurés", wear.Count));
                m.Drivers.Add(dd);
            }

            // Team-wide pit values
            var rates = new List<double>(); var tyres4 = new List<double>(); var tyres2 = new List<double>(); var lanes = new List<double>();
            foreach (var s in stops) if (s.Fuel >= 20 && s.Tyres == 0 && s.Stopped > 3) rates.Add(s.Fuel / s.Stopped);
            double rate = rates.Count > 0 ? Median(rates) : double.NaN;
            foreach (var s in stops)
            {
                if ((s.Tyres == 4 || s.Tyres == 2) && !double.IsNaN(rate))
                {
                    double t = s.Stopped - s.Fuel / rate;
                    if (t > 0) (s.Tyres == 4 ? tyres4 : tyres2).Add(t);
                }
                if (!double.IsNaN(s.Loss)) lanes.Add(s.Loss - s.Stopped);
            }
            m.Team[FuelRate] = Value(rate, 2.1, rates.Count > 0 ? string.Format("{0} ravitaillement(s) mesuré(s)", rates.Count) : "valeur par défaut");
            m.Team[Tyres2] = Value(Median(tyres2), 5, tyres2.Count > 0 ? string.Format("{0} changement(s) mesuré(s)", tyres2.Count) : "valeur par défaut");
            m.Team[Tyres4] = Value(Median(tyres4), 12, tyres4.Count > 0 ? string.Format("{0} changement(s) mesuré(s)", tyres4.Count) : "valeur par défaut");
            m.Team[LaneLoss] = Value(Median(lanes), 25, lanes.Count > 0 ? string.Format("{0} passage(s) mesuré(s)", lanes.Count) : "valeur par défaut");
            double maxWear = 0;
            foreach (var dd in m.Drivers) if (dd.Values[Wear].Measured) maxWear = Math.Max(maxWear, dd.Values[Wear].Value);
            m.Team[TyreLife] = Value(maxWear > 0 ? Math.Floor(60 / maxWear) : double.NaN, 40,
                maxWear > 0 ? "jusqu'à 60 % d'usure au rythme d'usure le plus élevé" : "valeur par défaut");
            return m;
        }

        private static StrategyValue Value(double measured, double fallback, string source)
        {
            bool ok = !double.IsNaN(measured) && measured > 0;
            return new StrategyValue { Value = ok ? measured : fallback, Measured = ok, Source = ok ? source : (double.IsNaN(fallback) ? "pas de mesure" : source) };
        }

        /// <summary>Applies hand-entered values on top of the measures.</summary>
        public static void ApplyOverrides(Measures m, StrategyPlan plan)
        {
            foreach (var o in plan.Overrides)
            {
                StrategyValue v = null;
                if (string.IsNullOrEmpty(o.DriverName)) m.Team.TryGetValue(o.Param, out v);
                else
                {
                    var dd = m.Drivers.Find(x => x.Driver.Name == o.DriverName);
                    if (dd != null) dd.Values.TryGetValue(o.Param, out v);
                }
                if (v != null) { v.Value = o.Value; v.Overridden = true; }
            }
        }

        private static double Dur(LmuLapRecord l)
        {
            if (l.LapTimeTicks > 0) return l.LapTime.TotalSeconds;
            return l.ReconstructedTicks > 0 ? TimeSpan.FromTicks(l.ReconstructedTicks).TotalSeconds : 0;
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var c = new List<double>(v); c.Sort();
            return c[c.Count / 2];
        }

        private static double Slope(List<double> y)
        {
            int n = y.Count;
            double mx = (n - 1) / 2.0, my = 0;
            foreach (var v in y) my += v;
            my /= n;
            double num = 0, den = 0;
            for (int i = 0; i < n; i++) { num += (i - mx) * (y[i] - my); den += (i - mx) * (i - mx); }
            return den > 0 ? num / den : 0;
        }

        // ─── Simulation ───────────────────────────────────────────────────────

        public class StintPlan
        {
            public string Driver;   // name
            public int Laps;
            public double Start, End;   // race time, s
        }

        public class StopPlan
        {
            public int Lap;
            public double At;           // race time when the car enters the pits
            public double FuelL;
            public int Tyres;          // tyres changed
            public bool TyresChosen;   // set by hand
            public double Stopped, Lost;
            public string NextDriver;
        }

        public class Simulation
        {
            public List<StintPlan> Stints = new List<StintPlan>();
            public List<StopPlan> Stops = new List<StopPlan>();
            public int Laps;
            public double Finish;         // race time when the last lap is completed
            public double PitLoss;
            public int SetsUsed;
            public int TyresUsed;      // single tyres, the starting set included
            public Dictionary<string, double> Driving = new Dictionary<string, double>();
            public List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// Timed race, stint by stint: a stint lasts as long as the tank (fuel or virtual energy)
        /// or the maximum stint time allows; tyres are changed when the next stint would take the
        /// set past its life (or at every stop when <paramref name="tyresEveryStop"/>).
        /// </summary>
        public static Simulation Simulate(StrategyPlan plan, Measures m, bool tyresEveryStop)
        {
            // First pass: stints as long as the tank allows. When that leaves a short last stint
            // (a splash), the same number of stints is spread evenly instead.
            var first = Run(plan, m, tyresEveryStop, 0);
            if (first.Stints.Count < 2) return first;
            int longest = 0;
            foreach (var st in first.Stints) longest = Math.Max(longest, st.Laps);
            var last = first.Stints[first.Stints.Count - 1];
            if (last.Laps >= longest * 0.6) return first;
            int even = (int)Math.Ceiling((double)first.Laps / first.Stints.Count);
            var balanced = Run(plan, m, tyresEveryStop, even);
            return balanced.Stints.Count <= first.Stints.Count ? balanced : first;
        }

        private static Simulation Run(StrategyPlan plan, Measures m, bool tyresEveryStop, int capLaps)
        {
            var sim = new Simulation();
            var keys = plan.Drivers.FindAll(k => m.Drivers.Exists(d => d.Driver.Name == k));
            if (keys.Count == 0) { sim.Warnings.Add("Choisissez au moins un pilote."); return sim; }
            foreach (var k in keys)
            {
                var dd = m.Drivers.Find(d => d.Driver.Name == k);
                if (!dd.Values[Pace].Measured && !dd.Values[Pace].Overridden) sim.Warnings.Add(dd.Driver.Name + " : pas de rythme mesuré ici, saisissez-le.");
                if (!(dd.Values[Fuel].Value > 0)) sim.Warnings.Add(dd.Driver.Name + " : pas de consommation mesurée ici, saisissez-la.");
            }
            if (sim.Warnings.Count > 0) return sim;

            double duration = plan.DurationMin * 60.0, tank = Math.Max(1, plan.TankL);
            double rate = m.Team[FuelRate].Value, lane = m.Team[LaneLoss].Value;
            double t2 = m.Team[Tyres2].Value, t4 = m.Team[Tyres4].Value;
            // 0 → 0, 2 → t2, 4 → t4, in between on a straight line
            Func<int, double> tyreTime = n => n <= 0 ? 0 : n <= 2 ? t2 * n / 2 : t2 + (t4 - t2) * (n - 2) / 2;
            int life = (int)Math.Max(1, m.Team[TyreLife].Value);
            double t = 0, fuelInTank = tank, energy = 100;
            double tyreAge = 0;
            int lap = 0, stint = 0;
            sim.TyresUsed = 4;

            while (t < duration && stint < 200)
            {
                var key = DriverOf(plan, keys, stint);
                var p = m.Drivers.Find(d => d.Driver.Name == key).Values;
                double pace = p[Pace].Value, fuel = p[Fuel].Value, ve = p[Energy].Value, deg = p[Degradation].Value;
                int n = Limit(fuelInTank, energy, fuel, ve, pace, plan.MaxStintMin);
                if (capLaps > 0) n = Math.Min(n, capLaps);
                var s = new StintPlan { Driver = key, Start = t };
                for (int k = 0; k < n && t < duration; k++)
                {
                    t += pace + deg * tyreAge;
                    lap++; tyreAge++; s.Laps++;
                    fuelInTank -= fuel; energy -= ve;
                }
                s.End = t;
                sim.Stints.Add(s);
                double dr; sim.Driving.TryGetValue(key, out dr); sim.Driving[key] = dr + (s.End - s.Start);
                if (t >= duration) break;

                // Stop: fuel for the next stint (or to the flag), tyres when the set is worn out
                var nextKey = DriverOf(plan, keys, stint + 1);
                var np = m.Drivers.Find(d => d.Driver.Name == nextKey).Values;
                double nPace = np[Pace].Value, nFuel = np[Fuel].Value, nVe = np[Energy].Value;
                int toFlag = (int)Math.Ceiling((duration - t - lane) / nPace) + 1;
                int next = Math.Min(Limit(tank, 100, nFuel, nVe, nPace, plan.MaxStintMin), Math.Max(1, toFlag));
                if (capLaps > 0) next = Math.Min(next, capLaps + 1);   // fuel for the balanced stint, one lap spare
                double add = Math.Max(0, Math.Min(tank - fuelInTank, next * nFuel + 0.5 * nFuel - fuelInTank));
                double veAdd = nVe > 0 ? Math.Max(0, Math.Min(100 - energy, next * nVe + 0.5 * nVe - energy)) : 0;
                int stopIndex = sim.Stops.Count;
                bool chosen = !tyresEveryStop && plan.StopTyres != null && stopIndex < plan.StopTyres.Count && plan.StopTyres[stopIndex] >= 0;
                int tyres = chosen ? Math.Min(4, plan.StopTyres[stopIndex]) : (tyresEveryStop || tyreAge + next > life ? 4 : 0);
                if (tyres > 0 && sim.TyresUsed + tyres > plan.TyreSets * 4)
                {
                    sim.Warnings.Add(string.Format("Arrêt {0} : {1} pneu(s) demandé(s) mais plus assez de pneus neufs ({2} jeux).", stopIndex + 1, tyres, plan.TyreSets));
                    if (!chosen) tyres = Math.Max(0, plan.TyreSets * 4 - sim.TyresUsed);
                }
                if (tyres > 0) { sim.TyresUsed += tyres; tyreAge = tyreAge * (4 - tyres) / 4; }   // a partial change leaves part of the wear
                // Virtual energy refills at the same pace as the fuel: the longer of the two counts
                double refuel = Math.Max(add, nVe > 0 ? veAdd / 100 * tank : 0) / rate;
                double stopped = refuel + tyreTime(tyres);
                double lost = lane + stopped;
                sim.Stops.Add(new StopPlan { Lap = lap, At = t, FuelL = add, Tyres = tyres, TyresChosen = chosen, Stopped = stopped, Lost = lost, NextDriver = nextKey });
                sim.PitLoss += lost;
                t += lost;
                fuelInTank += add; energy += veAdd;
                stint++;
            }
            sim.Laps = lap;
            sim.Finish = t;
            sim.SetsUsed = (int)Math.Ceiling(sim.TyresUsed / 4.0);

            if (plan.MinDriveMin > 0)
                foreach (var k in keys)
                {
                    double dr; sim.Driving.TryGetValue(k, out dr);
                    if (dr < plan.MinDriveMin * 60)
                        sim.Warnings.Add(string.Format("{0} ne roule que {1} : moins que le minimum de {2}.",
                            m.Drivers.Find(d => d.Driver.Name == k).Driver.Name, Fmt.TotalTime(TimeSpan.FromSeconds(dr)), Fmt.TotalTime(TimeSpan.FromMinutes(plan.MinDriveMin))));
                }
            return sim;
        }

        private static int Limit(double fuelInTank, double energy, double fuel, double ve, double pace, int maxStintMin)
        {
            int n = (int)Math.Floor((fuelInTank - 0.3 * fuel) / fuel);
            if (ve > 0) n = Math.Min(n, (int)Math.Floor((energy - 0.3 * ve) / ve));
            if (maxStintMin > 0) n = Math.Min(n, (int)Math.Floor(maxStintMin * 60 / pace));
            return Math.Max(1, n);
        }

        /// <summary>Driver of a stint: the one chosen by hand, otherwise the drivers in turn.</summary>
        public static string DriverOf(StrategyPlan plan, List<string> keys, int stint)
        {
            if (plan.StintDrivers != null && stint < plan.StintDrivers.Count && keys.Contains(plan.StintDrivers[stint]))
                return plan.StintDrivers[stint];
            return keys[stint % keys.Count];
        }

        // ─── Saved plans ──────────────────────────────────────────────────────
        //
        // Plans live in the team folder ("strategy\<name>.json"), so every teammate sees and edits
        // the same plans; a local copy keeps them when the team folder is unreachable.

        private static string _path;
        private static readonly object Lock = new object();

        public static void Init(string dataDir) { _path = Path.Combine(dataDir, "local.db"); }

        private static string SharedDir
        {
            get { return LmuTeamSync.IsConfigured ? Path.Combine(LmuSessionStore.TeamFolder, "strategy") : null; }
        }

        private static string FileName(string id)
        {
            var s = id.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s + ".json";
        }

        /// <summary>The team's plans (shared folder) and the local ones, newest version of each.</summary>
        public static List<StrategyPlan> SavedPlans()
        {
            var byId = new Dictionary<string, StrategyPlan>(StringComparer.OrdinalIgnoreCase);
            Action<StrategyPlan> keep = p =>
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) return;
                p.Normalize();
                StrategyPlan cur;
                if (!byId.TryGetValue(p.Id, out cur) || p.UpdatedUtc > cur.UpdatedUtc) byId[p.Id] = p;
            };
            if (_path != null)
                lock (Lock)
                {
                    try { using (var db = new LiteDatabase(string.Format("Filename={0}", _path))) foreach (var p in db.GetCollection<StrategyPlan>("strategy_plans").FindAll()) keep(p); }
                    catch (Exception ex) { SimHub.Logging.Current.Warn("[TeamPace] Plans: " + ex.Message); }
                }
            var dir = SharedDir;
            if (dir != null && Directory.Exists(dir))
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try { keep(Newtonsoft.Json.JsonConvert.DeserializeObject<StrategyPlan>(File.ReadAllText(file))); }
                    catch (Exception ex) { SimHub.Logging.Current.Warn("[TeamPace] Plan " + Path.GetFileName(file) + ": " + ex.Message); }
                }
            var list = new List<StrategyPlan>(byId.Values);
            list.Sort((a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            return list;
        }

        /// <summary>Saves locally and to the team folder. Returns false when only the local copy was written.</summary>
        public static bool Save(StrategyPlan plan)
        {
            if (string.IsNullOrWhiteSpace(plan.Id)) return false;
            plan.Author = LmuSessionStore.PlayerName;
            plan.UpdatedUtc = DateTime.UtcNow;
            if (_path != null)
                lock (Lock)
                {
                    try { using (var db = new LiteDatabase(string.Format("Filename={0}", _path))) db.GetCollection<StrategyPlan>("strategy_plans").Upsert(plan); }
                    catch (Exception ex) { SimHub.Logging.Current.Warn("[TeamPace] Plan save: " + ex.Message); }
                }
            var dir = SharedDir;
            if (dir == null) return false;
            try
            {
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, FileName(plan.Id));
                var tmp = target + ".tmp";
                File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(plan, Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
                return true;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[TeamPace] Plan share: " + ex.Message);
                return false;
            }
        }

        /// <summary>Deletes the plan for the whole team.</summary>
        public static void Delete(string id)
        {
            if (_path != null)
                lock (Lock)
                {
                    try { using (var db = new LiteDatabase(string.Format("Filename={0}", _path))) db.GetCollection<StrategyPlan>("strategy_plans").Delete(id); }
                    catch (Exception ex) { SimHub.Logging.Current.Warn("[TeamPace] Plan delete: " + ex.Message); }
                }
            var dir = SharedDir;
            if (dir == null) return;
            try
            {
                var file = Path.Combine(dir, FileName(id));
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[TeamPace] Plan delete: " + ex.Message); }
        }
    }
}
