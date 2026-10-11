using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.BestLapRecords
{
    /// <summary>Lap-by-lap analysis of one driver's session, with stats on the checked laps.</summary>
    public partial class AnalysisColumn : UserControl
    {
        /// <summary>Raised when the checked laps change (average lap may have moved).</summary>
        public event Action<AnalysisColumn> StatsChanged;

        public DriverView       Driver  { get; private set; }
        public LmuSessionRecord Session { get; private set; }
        public long AverageTicks { get; private set; }   // average of checked valid laps, 0 if none

        private readonly ObservableCollection<AnalysisLapRow> _rows = new ObservableCollection<AnalysisLapRow>();
        private long _referenceAvg;
        private string _referenceName;

        public IList<AnalysisLapRow> Rows { get { return _rows; } }

        public AnalysisColumn(DriverView driver, LmuSessionRecord session)
        {
            Driver  = driver;
            Session = session;
            InitializeComponent();
            Build();
        }

        // ─── Build ────────────────────────────────────────────────────────────

        private void Build()
        {
            var r = Session;
            var cls = r.CarClass ?? "";
            BadgeClass.Background  = Brush(LmuCategoryStyle.GetBg(cls));
            BadgeClass.BorderBrush = Brush(LmuCategoryStyle.GetBorder(cls));
            BadgeClass.BorderThickness = LmuCategoryStyle.GetBorderThickness(cls) == "1" ? new Thickness(1) : new Thickness(0);
            TxtClass.Text = LmuCategoryStyle.GetShortName(cls);
            TxtCar.Text   = r.CarType ?? "";
            TxtTeam.Text  = r.TeamName ?? "";
            TxtResult.Text = BuildResultLine(r);

            SimBadges.Children.Clear();
            AddBadge(r.IsFullSim ? "SIM COMPLÈTE" : "ARCADE", r.IsFullSim ? "#CC1565B0" : "#CC666666");
            AddBadge(r.Setting == "Multiplayer" ? "MULTI" : "SOLO", r.Setting == "Multiplayer" ? "#CC8B5523" : "#CC2E7D32");

            var laps = LmuSessionStore.GetLapsForSession(r.FileId, Driver.DbPath);
            int liveMatched = MergeLiveData(r, laps, Driver.DbPath);

            float theo = 0f, top = 0f;
            float b1 = float.MaxValue, b2 = float.MaxValue, b3 = float.MaxValue;
            foreach (var l in laps)
            {
                if (l.IsValid && l.S1 > 0 && l.S1 < b1) b1 = l.S1;
                if (l.IsValid && l.S2 > 0 && l.S2 < b2) b2 = l.S2;
                if (l.IsValid && l.S3 > 0 && l.S3 < b3) b3 = l.S3;
                if (l.TopSpeedKph > top) top = l.TopSpeedKph;
            }
            if (b1 < float.MaxValue && b2 < float.MaxValue && b3 < float.MaxValue) theo = b1 + b2 + b3;
            TxtBestLap.Text  = r.BestLapFormatted;
            TxtTheo.Text     = theo > 0 ? "Théorique " + Fmt.LapSec(theo) : "";
            TxtTopSpeed.Text = top > 0 ? string.Format("Vmax {0:0} km/h", top) : "";

            bool hasVe = false, hasTire = false;
            string prevCompound = null;
            LmuLapRecord prevLap = null;
            foreach (var l in laps)
            {
                bool tireChanged = (prevCompound != null && !string.IsNullOrEmpty(l.FrontCompound) &&
                                    l.FrontCompound != prevCompound) ||
                                   (l.LivePit != null && l.LivePit.TyresChanged);
                bool rhythm = l.IsValid && !l.IsPitLap && l.LapNum > 1 &&
                              (prevLap == null || !prevLap.IsPitLap);
                var row = new AnalysisLapRow
                {
                    Lap         = l,
                    TireChanged = tireChanged,
                    IsRhythmLap = rhythm,
                    IsChecked   = rhythm,
                };
                if (l.HasLive)
                {
                    row.WearLapFL = l.LiveWearFL; row.WearLapFR = l.LiveWearFR;
                    row.WearLapRL = l.LiveWearRL; row.WearLapRR = l.LiveWearRR;
                }
                else if (prevLap != null)
                {
                    // XML stores remaining fraction at lap end: wear = drop vs. previous lap
                    row.WearLapFL = WearDrop(prevLap.TireWearFL, l.TireWearFL);
                    row.WearLapFR = WearDrop(prevLap.TireWearFR, l.TireWearFR);
                    row.WearLapRL = WearDrop(prevLap.TireWearRL, l.TireWearRL);
                    row.WearLapRR = WearDrop(prevLap.TireWearRR, l.TireWearRR);
                }
                prevLap = l;
                if (row.HasVe) hasVe = true;
                if (row.HasTireData) hasTire = true;
                row.PropertyChanged += (s, ev) => { if (ev.PropertyName == "IsChecked") Recalc(); };
                _rows.Add(row);
                if (!string.IsNullOrEmpty(l.FrontCompound)) prevCompound = l.FrontCompound;
            }

            LapGrid.ItemsSource       = _rows;
            ColVe.Visibility          = hasVe   ? Visibility.Visible : Visibility.Collapsed;
            StatsVePanel.Visibility   = hasVe   ? Visibility.Visible : Visibility.Collapsed;
            StatsTirePanel.Visibility = hasTire ? Visibility.Visible : Visibility.Collapsed;
            TxtLive.Text = liveMatched > 0
                ? string.Format("● direct : {0} tour(s)", liveMatched)
                : "○ pas de données direct";

            Recalc();
        }

        private static string BuildResultLine(LmuSessionRecord r)
        {
            var parts = new List<string>();
            parts.Add(r.SessionDate != DateTime.MinValue
                ? r.SessionDate.ToString("ddd d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR")) : r.SessionTypeLong);
            if (r.IsRace)
            {
                parts.Add("départ " + Pos(r.ClassGridPos, r.GridPos));
                parts.Add("arrivée " + (r.IsFinished ? Pos(r.ClassPosition, r.Position) : (r.TotalLaps > 0 ? "DNF" : "—")));
                parts.Add(string.Format("{0} arrêt(s)", r.Pitstops));
            }
            parts.Add(string.Format("{0} tour(s)", r.TotalLaps));
            parts.Add(string.Format("{0} incident(s)", r.IncidentCount));
            if (r.PenaltyCount > 0) parts.Add(string.Format("{0} pénalité(s)", r.PenaltyCount));
            return string.Join("  ·  ", parts);
        }

        private static string Pos(int cls, int overall)
        {
            if (cls > 0 && overall > 0 && cls != overall) return string.Format("P{0} (P{1} gén.)", cls, overall);
            if (cls > 0) return "P" + cls;
            if (overall > 0) return "P" + overall;
            return "—";
        }

        private void AddBadge(string text, string bg)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding      = new Thickness(5, 1, 5, 1),
                Margin       = new Thickness(0, 0, 4, 0),
                Background   = Brush(bg),
            };
            b.Child = new TextBlock { Text = text, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            SimBadges.Children.Add(b);
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        // ─── Multi-driver comparison ──────────────────────────────────────────

        /// <summary>Gap of each lap to the fastest displayed driver on the same lap number.</summary>
        public void SetTeamDeltas(Dictionary<int, long> bestByLap)
        {
            ColTeamDelta.Visibility = bestByLap != null ? Visibility.Visible : Visibility.Collapsed;
            if (bestByLap == null) return;
            foreach (var row in _rows)
            {
                long best;
                if (!row.IsValid || row.Lap.LapTimeTicks <= 0 || !bestByLap.TryGetValue(row.LapNum, out best))
                {
                    row.TeamDelta = ""; continue;
                }
                long gap = row.Lap.LapTimeTicks - best;
                row.TeamDelta   = gap == 0 ? "★" : "+" + Fmt.Seconds(TimeSpan.FromTicks(gap));
                row.TeamDeltaFg = gap == 0 ? "#FF4CAF50" : (gap < TimeSpan.TicksPerSecond / 2 ? "#FFFFB74D" : "#FFEF5350");
            }
            LapGrid.Items.Refresh();
        }

        /// <summary>Best average among the displayed drivers, to show this driver's gap to it.</summary>
        public void SetAverageReference(long bestAvgTicks, string bestName)
        {
            _referenceAvg  = bestAvgTicks;
            _referenceName = bestName;
            UpdateVsBest();
        }

        private void UpdateVsBest()
        {
            if (_referenceAvg <= 0 || AverageTicks <= 0) { TxtStVsBest.Text = ""; return; }
            long gap = AverageTicks - _referenceAvg;
            if (gap <= 0)
            {
                TxtStVsBest.Text = "meilleure moyenne";
                TxtStVsBest.Foreground = Brushes.LightGreen;
            }
            else
            {
                TxtStVsBest.Text = string.Format("+{0} vs {1}", Fmt.Seconds(TimeSpan.FromTicks(gap)), _referenceName);
                TxtStVsBest.Foreground = Brush("#FFEF9A9A");
            }
        }

        // ─── Selection ────────────────────────────────────────────────────────

        private void BtnValid_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsChecked = row.IsRhythmLap;
        }

        private void BtnAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsChecked = true;
        }

        private void BtnNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsChecked = false;
        }

        // ─── Stats ────────────────────────────────────────────────────────────

        private void Recalc()
        {
            var sel = new List<AnalysisLapRow>();
            foreach (var row in _rows) if (row.IsChecked) sel.Add(row);

            TxtSelSummary.Text = string.Format("{0} / {1} tour(s) sélectionné(s)", sel.Count, _rows.Count);
            UpdateHighlights(sel);

            // Lap times
            long best = long.MaxValue, worst = 0, sum = 0; int cnt = 0;
            foreach (var row in sel)
            {
                if (!row.IsValid) continue;
                var t = row.Lap.LapTimeTicks;
                if (t <= 0) continue;
                sum += t; cnt++;
                if (t < best) best = t;
                if (t > worst) worst = t;
            }
            AverageTicks = cnt > 0 ? sum / cnt : 0;
            if (cnt > 0)
            {
                TxtStAvg.Text   = Fmt.Lap(TimeSpan.FromTicks(AverageTicks));
                TxtStBest.Text  = "Meilleur " + Fmt.Lap(TimeSpan.FromTicks(best));
                TxtStWorst.Text = "Pire " + Fmt.Lap(TimeSpan.FromTicks(worst));
                TxtStDelta.Text = "Écart meilleur/pire " + Fmt.Seconds(TimeSpan.FromTicks(worst - best));
                var d = TimeSpan.FromTicks(sum);
                TxtStDuree.Text = string.Format("{0} tour(s) · {1}", cnt,
                    d.TotalHours >= 1 ? string.Format("{0}h {1:00}m", (int)d.TotalHours, d.Minutes)
                                      : string.Format("{0}m {1:00}s", d.Minutes, d.Seconds));
            }
            else
            {
                TxtStAvg.Text = "—";
                TxtStBest.Text = TxtStWorst.Text = TxtStDelta.Text = TxtStDuree.Text = "";
            }
            UpdateVsBest();

            UpdateFuel(sel);
            UpdateEnergy(sel);
            TxtStTireFL.Text = "AVG  " + WearPerLap(sel, x => x.WearLapFL);
            TxtStTireFR.Text = "AVD  " + WearPerLap(sel, x => x.WearLapFR);
            TxtStTireRL.Text = "ARG  " + WearPerLap(sel, x => x.WearLapRL);
            TxtStTireRR.Text = "ARD  " + WearPerLap(sel, x => x.WearLapRR);
            UpdatePits(sel);

            if (StatsChanged != null) StatsChanged(this);
        }

        private void UpdateHighlights(List<AnalysisLapRow> sel)
        {
            long best = long.MaxValue, worst = 0;
            float b1 = float.MaxValue, b2 = float.MaxValue, b3 = float.MaxValue;
            int valid = 0;
            foreach (var row in sel)
            {
                if (!row.IsValid) continue;
                var l = row.Lap;
                if (l.LapTimeTicks > 0)
                {
                    valid++;
                    if (l.LapTimeTicks < best)  best  = l.LapTimeTicks;
                    if (l.LapTimeTicks > worst) worst = l.LapTimeTicks;
                }
                if (l.S1 > 0 && l.S1 < b1) b1 = l.S1;
                if (l.S2 > 0 && l.S2 < b2) b2 = l.S2;
                if (l.S3 > 0 && l.S3 < b3) b3 = l.S3;
            }
            foreach (var row in _rows)
            {
                var l = row.Lap;
                bool on = row.IsChecked && row.IsValid;
                row.SetHighlights(
                    on && l.LapTimeTicks == best,
                    on && valid > 1 && l.LapTimeTicks == worst,
                    on && l.S1 > 0 && l.S1 == b1,
                    on && l.S2 > 0 && l.S2 == b2,
                    on && l.S3 > 0 && l.S3 == b3);
            }
        }

        // Litres when live data exists, otherwise % of tank from the XML.
        // Pit laps are excluded: refuelling makes their consumption meaningless.
        private void UpdateFuel(List<AnalysisLapRow> sel)
        {
            bool litres = false;
            foreach (var row in sel) if (row.HasLiveFuel) { litres = true; break; }

            float tot = 0f, left = 0f; int cnt = 0;
            foreach (var row in sel)
            {
                if (row.IsPitLap) continue;
                float used = litres ? row.Lap.LiveFuelUsedL : row.Lap.FuelUsed * 100f;
                if (used <= 0) continue;
                tot += used; cnt++;
                float l = litres ? row.Lap.LiveFuelLevelL : row.Lap.FuelRemaining * 100f;
                if (l > 0) left = l;
            }
            string unit = litres ? "L" : "% rés.";
            if (cnt == 0)
            {
                TxtStFuelAvg.Text = "—"; TxtStFuelTot.Text = TxtStFuelLaps.Text = "";
                return;
            }
            float avg = tot / cnt;
            TxtStFuelAvg.Text  = string.Format("{0:F2} {1}/tour", avg, unit);
            TxtStFuelTot.Text  = string.Format("Total {0:F1} {1} sur {2} tour(s)", tot, unit, cnt);
            TxtStFuelLaps.Text = left > 0 ? string.Format("Autonomie ≈ {0:F1} tours", left / avg) : "";
            if (!litres)
                TxtStFuelLaps.Text += (TxtStFuelLaps.Text.Length > 0 ? "\n" : "") +
                                      string.Format("Plein ≈ {0:F1} tours", 100f / avg);
        }

        private void UpdateEnergy(List<AnalysisLapRow> sel)
        {
            float tot = 0f; int cnt = 0;
            foreach (var row in sel)
                if (!row.IsPitLap && row.Lap.VirtualEnergyUsed > 0) { tot += row.Lap.VirtualEnergyUsed * 100f; cnt++; }
            if (cnt == 0) { TxtStVeAvg.Text = "—"; TxtStVeTot.Text = ""; return; }
            float avg = tot / cnt;
            TxtStVeAvg.Text = string.Format("{0:F2} %/tour", avg);
            TxtStVeTot.Text = string.Format("Total {0:F1} %  ·  100 % ≈ {1:F1} tours", tot, 100f / avg);
        }

        private static string WearPerLap(List<AnalysisLapRow> rows, Func<AnalysisLapRow, float> wear)
        {
            float sum = 0f; int cnt = 0;
            foreach (var row in rows) { float w = wear(row); if (w > 0) { sum += w; cnt++; } }
            return cnt > 0 ? string.Format("{0:F2} %/tour", sum / cnt) : "—";
        }

        // Measured when captured live, otherwise estimated from lap times:
        // (in-lap + out-lap) minus twice the average green-flag lap.
        private void UpdatePits(List<AnalysisLapRow> sel)
        {
            long refSum = 0; int refCnt = 0;
            foreach (var row in sel)
                if (row.IsValid && !row.IsPitLap && row.Lap.LapTimeTicks > 0) { refSum += row.Lap.LapTimeTicks; refCnt++; }
            double refSec = refCnt > 0 ? TimeSpan.FromTicks(refSum / refCnt).TotalSeconds : 0;

            var items = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (!row.IsPitLap) continue;
                var parts = new List<string> { "Tour " + row.LapNum };
                var p = row.Lap.LivePit;
                if (p != null)
                {
                    parts.Add(string.Format("voie {0:F1} s", p.LaneSec));
                    parts.Add(string.Format("arrêt {0:F1} s", p.StoppedSec));
                    if (p.LimiterSec > 0)   parts.Add(string.Format("limiteur {0:F1} s", p.LimiterSec));
                    if (p.FuelAddedL > 0.5) parts.Add(string.Format("+{0:F1} L", p.FuelAddedL));
                    if (p.TyresChanged)     parts.Add("pneus");
                }
                if (refSec > 0 && row.Lap.LapTimeTicks > 0)
                {
                    double loss = row.Lap.LapTime.TotalSeconds - refSec;
                    if (i + 1 < _rows.Count && _rows[i + 1].Lap.LapTimeTicks > 0 && !_rows[i + 1].IsPitLap)
                        loss += _rows[i + 1].Lap.LapTime.TotalSeconds - refSec;
                    parts.Add(string.Format("perte ≈ {0:+0.0;-0.0} s", loss));
                }
                items.Add(string.Join("  ·  ", parts));
            }
            StatsPitPanel.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatsPitList.ItemsSource = items;
        }

        // ─── Live data merge ──────────────────────────────────────────────────

        private static float WearDrop(float prev, float cur)
        {
            if (prev <= 0 || cur <= 0 || cur > prev) return 0f;
            return (prev - cur) * 100f;
        }

        // Laps are matched on absolute start time; if the clocks disagree (server
        // time online), fall back to lap numbers within the session window.
        private static int MergeLiveData(LmuSessionRecord r, List<LmuLapRecord> laps, string dbPath)
        {
            if (r.BlockStartUtc == DateTime.MinValue || laps.Count == 0) return 0;

            float lastEt = 0f;
            foreach (var l in laps) if (l.ElapsedTime > lastEt) lastEt = l.ElapsedTime;
            var from = r.BlockStartUtc.AddMinutes(-15);
            var to   = r.BlockStartUtc.AddSeconds(lastEt).AddMinutes(15);

            var live = LmuSessionStore.GetLiveLaps(from, to, dbPath);
            var pits = LmuSessionStore.GetLivePits(from, to, dbPath);
            if (live.Count == 0 && pits.Count == 0) return 0;

            long tolerance = TimeSpan.FromSeconds(10).Ticks;
            int matched = 0;
            foreach (var l in laps)
            {
                long start = r.BlockStartUtc.AddSeconds(l.ElapsedTime).Ticks;
                LmuLiveLap bestMatch = null;
                long bestGap = tolerance;
                foreach (var s in live)
                {
                    long gap = Math.Abs(s.StartUtcTicks - start);
                    if (gap <= bestGap) { bestGap = gap; bestMatch = s; }
                }
                if (bestMatch != null) { ApplyLive(l, bestMatch); matched++; }
            }

            bool byLapNum = matched == 0;
            if (byLapNum)
                foreach (var l in laps)
                    foreach (var s in live)
                        if (s.LapNum == l.LapNum) { ApplyLive(l, s); matched++; break; }

            foreach (var p in pits)
            {
                LmuLapRecord target = null;
                foreach (var l in laps)
                {
                    if (byLapNum) { if (l.LapNum == p.LapNum) { target = l; break; } }
                    else if (r.BlockStartUtc.AddSeconds(l.ElapsedTime).Ticks <= p.EntryUtcTicks) target = l;
                }
                if (target != null && target.LivePit == null) target.LivePit = p;
            }
            return matched;
        }

        private static void ApplyLive(LmuLapRecord l, LmuLiveLap s)
        {
            l.HasLive        = true;
            l.LiveFuelUsedL  = (float)s.FuelUsedL;
            l.LiveFuelLevelL = (float)s.FuelLevelL;
            l.LiveWearFL     = (float)s.WearFL;
            l.LiveWearFR     = (float)s.WearFR;
            l.LiveWearRL     = (float)s.WearRL;
            l.LiveWearRR     = (float)s.WearRR;
            if (s.InPitLane) l.IsPitLap = true;
        }
    }
}
