using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>Lap-by-lap analysis of one driver's session, with stats on the checked laps.</summary>
    public partial class AnalysisColumn : UserControl
    {
        // Decimal point everywhere, like lap times (1:58.335), whatever the Windows locale
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
            TxtDate.Text  = r.SessionDate != DateTime.MinValue
                ? r.SessionDate.ToString("ddd d MMM yyyy · HH:mm", CultureInfo.GetCultureInfo("fr-FR")) : "";

            KpiFinishBox.Visibility = r.IsRace ? Visibility.Visible : Visibility.Collapsed;
            KpiGridBox.Visibility   = r.IsRace ? Visibility.Visible : Visibility.Collapsed;
            KpiFinish.Text = r.IsFinished ? Pos(r.ClassPosition, r.Position) : (r.TotalLaps > 0 ? "Abandon" : "—");
            KpiGrid.Text   = Pos(r.ClassGridPos, r.GridPos);
            KpiBest.Text   = r.BestLapFormatted;
            KpiLaps.Text   = r.TotalLaps.ToString();
            KpiPits.Text   = r.Pitstops.ToString();
            KpiIncidents.Text = r.PenaltyCount > 0
                ? string.Format(Inv, "{0} · {1} pén.", r.IncidentCount, r.PenaltyCount)
                : r.IncidentCount.ToString();

            SimBadges.Children.Clear();
            AddBadge(r.IsFullSim ? "SIM COMPLÈTE" : "ARCADE", r.IsFullSim ? "#CC1565B0" : "#CC666666");
            AddBadge(r.Setting == "Multiplayer" ? "MULTI" : "SOLO", r.Setting == "Multiplayer" ? "#CC8B5523" : "#CC2E7D32");

            var laps = LmuSessionStore.GetLapsForSession(r.FileId, Driver.DbPath);
            int liveMatched = MergeLiveData(r, laps, Driver.DbPath);
            ReconstructInvalidTimes(laps);

            // Theoretical best = best S1 + best S2 + best S3 of valid laps, each with its lap
            float theo = 0f, top = 0f;
            float b1 = float.MaxValue, b2 = float.MaxValue, b3 = float.MaxValue;
            int l1 = 0, l2 = 0, l3 = 0, invalid = 0;
            foreach (var l in laps)
            {
                if (!l.IsValid) invalid++;
                if (l.IsValid && l.S1 > 0 && l.S1 < b1) { b1 = l.S1; l1 = l.LapNum; }
                if (l.IsValid && l.S2 > 0 && l.S2 < b2) { b2 = l.S2; l2 = l.LapNum; }
                if (l.IsValid && l.S3 > 0 && l.S3 < b3) { b3 = l.S3; l3 = l.LapNum; }
                if (l.TopSpeedKph > top) top = l.TopSpeedKph;
            }
            bool hasTheo = b1 < float.MaxValue && b2 < float.MaxValue && b3 < float.MaxValue;
            if (hasTheo) theo = b1 + b2 + b3;
            KpiTheo.Text = theo > 0 ? Fmt.LapSec(theo) : "—";
            KpiTop.Text  = top > 0 ? string.Format(Inv, "{0:0} km/h", top) : "—";
            KpiInvalid.Text = invalid.ToString();
            TxtTheoDetail.Text = hasTheo
                ? string.Format(Inv, "Théorique = S1 {0} (tour {1})  +  S2 {2} (tour {3})  +  S3 {4} (tour {5})",
                                Fmt.LapSec(b1), l1, Fmt.LapSec(b2), l2, Fmt.LapSec(b3), l3)
                : "";
            TxtTheoDetail.Visibility = hasTheo ? Visibility.Visible : Visibility.Collapsed;

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

            LapGrid.ItemsSource = _rows;
            HasVe    = hasVe;
            HasTyres = hasTire;
            foreach (var row in _rows)
            {
                if (row.FuelUsed != "—") HasFuel = true;
                if (row.IsPitLap) HasPits = true;
            }
            TxtLive.Text = liveMatched > 0
                ? string.Format(Inv, "● capture directe : {0} tour(s)", liveMatched)
                : "";
            SetOptionalBlocks(HasFuel, HasVe, HasTyres, HasPits);

            Recalc();
        }

        // One row of key figures, two when the column is too narrow (3-4 drivers)
        private void KpiGridPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            int visible = 0;
            foreach (UIElement c in KpiGridPanel.Children) if (c.Visibility == Visibility.Visible) visible++;
            if (visible == 0) return;
            int rows = e.NewSize.Width / visible < 74 ? 2 : 1;
            if (KpiGridPanel.Rows != rows) KpiGridPanel.Rows = rows;
        }

        public bool HasFuel  { get; private set; }
        public bool HasVe    { get; private set; }
        public bool HasTyres { get; private set; }
        public bool HasPits  { get; private set; }

        /// <summary>
        /// Shows the optional blocks and columns. In a comparison the host passes the union over
        /// all drivers, so every column keeps the same layout and the lap tables stay aligned.
        /// </summary>
        public void SetOptionalBlocks(bool fuel, bool ve, bool tyres, bool pits)
        {
            StatsFuelPanel.Visibility = fuel  ? Visibility.Visible : Visibility.Collapsed;
            StatsVePanel.Visibility   = ve    ? Visibility.Visible : Visibility.Collapsed;
            StatsTirePanel.Visibility = tyres ? Visibility.Visible : Visibility.Collapsed;
            StatsPitPanel.Visibility  = pits  ? Visibility.Visible : Visibility.Collapsed;
            ColFuel.Visibility        = fuel  ? Visibility.Visible : Visibility.Collapsed;
            ColFuelLeft.Visibility    = fuel  ? Visibility.Visible : Visibility.Collapsed;
            ColVe.Visibility          = ve    ? Visibility.Visible : Visibility.Collapsed;
            ColWear.Visibility        = tyres ? Visibility.Visible : Visibility.Collapsed;
            TxtNoLiveHint.Visibility  = fuel && tyres ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string Pos(int cls, int overall)
        {
            if (cls > 0 && overall > 0 && cls != overall) return string.Format(Inv, "P{0} (P{1})", cls, overall);
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
                TxtStVsBest.Text = "★ meilleure moyenne";
                TxtStVsBest.Foreground = Brushes.LightGreen;
            }
            else
            {
                TxtStVsBest.Text = string.Format(Inv, "+{0} s vs {1}", Fmt.Seconds(TimeSpan.FromTicks(gap)), _referenceName);
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

            TxtSelSummary.Text = string.Format(Inv, "{0} tour(s) coché(s) sur {1}", sel.Count, _rows.Count);
            UpdateHighlights(sel);

            // Lap times
            long best = long.MaxValue, worst = 0, sum = 0; int cnt = 0;
            var secs = new List<double>();
            foreach (var row in sel)
            {
                if (!row.IsValid) continue;
                var t = row.Lap.LapTimeTicks;
                if (t <= 0) continue;
                sum += t; cnt++;
                secs.Add(TimeSpan.FromTicks(t).TotalSeconds);
                if (t < best) best = t;
                if (t > worst) worst = t;
            }
            AverageTicks = cnt > 0 ? sum / cnt : 0;
            if (cnt > 0)
            {
                double mean = TimeSpan.FromTicks(AverageTicks).TotalSeconds, sq = 0;
                foreach (var s in secs) sq += (s - mean) * (s - mean);
                double sigma = cnt > 1 ? Math.Sqrt(sq / (cnt - 1)) : 0;

                TxtStAvg.Text       = Fmt.Lap(TimeSpan.FromTicks(AverageTicks));
                TxtStBestWorst.Text = string.Format(Inv, "Meilleur {0}  ·  Pire {1}",
                    Fmt.Lap(TimeSpan.FromTicks(best)), Fmt.Lap(TimeSpan.FromTicks(worst)));
                TxtStSigma.Text  = cnt > 1 ? string.Format(Inv, "± {0:0.000} s", sigma) : "—";
                TxtStSpread.Text = "Écart meilleur / pire : " + Fmt.Seconds(TimeSpan.FromTicks(worst - best));
                var d = TimeSpan.FromTicks(sum);
                TxtStDuree.Text = string.Format(Inv, "{0} tour(s) · {1}", cnt,
                    d.TotalHours >= 1 ? string.Format(Inv, "{0}h {1:00}m", (int)d.TotalHours, d.Minutes)
                                      : string.Format(Inv, "{0}m {1:00}s", d.Minutes, d.Seconds));
            }
            else
            {
                TxtStAvg.Text = TxtStSigma.Text = "—";
                TxtStBestWorst.Text = TxtStSpread.Text = "";
                TxtStDuree.Text = "Cochez des tours dans le tableau";
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
            TxtStFuelAvg.Text  = string.Format(Inv, "{0:F2} {1}/tour", avg, unit);
            TxtStFuelTot.Text  = string.Format(Inv, "Total {0:F1} {1} sur {2} tour(s)", tot, unit, cnt);
            TxtStFuelLaps.Text = left > 0 ? string.Format(Inv, "Autonomie ≈ {0:F1} tours", left / avg) : "";
            if (!litres)
                TxtStFuelLaps.Text += (TxtStFuelLaps.Text.Length > 0 ? "\n" : "") +
                                      string.Format(Inv, "Plein ≈ {0:F1} tours", 100f / avg);
        }

        private void UpdateEnergy(List<AnalysisLapRow> sel)
        {
            float tot = 0f; int cnt = 0;
            foreach (var row in sel)
                if (!row.IsPitLap && row.Lap.VirtualEnergyUsed > 0) { tot += row.Lap.VirtualEnergyUsed * 100f; cnt++; }
            if (cnt == 0) { TxtStVeAvg.Text = "—"; TxtStVeTot.Text = ""; return; }
            float avg = tot / cnt;
            TxtStVeAvg.Text = string.Format(Inv, "{0:F2} %/tour", avg);
            TxtStVeTot.Text = string.Format(Inv, "Total {0:F1} %  ·  100 % ≈ {1:F1} tours", tot, 100f / avg);
        }

        private static string WearPerLap(List<AnalysisLapRow> rows, Func<AnalysisLapRow, float> wear)
        {
            float sum = 0f; int cnt = 0;
            foreach (var row in rows) { float w = wear(row); if (w > 0) { sum += w; cnt++; } }
            return cnt > 0 ? string.Format(Inv, "{0:F2} %/tour", sum / cnt) : "—";
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
                    parts.Add(string.Format(Inv, "voie {0:F1} s", p.LaneSec));
                    parts.Add(string.Format(Inv, "arrêt {0:F1} s", p.StoppedSec));
                    if (p.LimiterSec > 0)   parts.Add(string.Format(Inv, "limiteur {0:F1} s", p.LimiterSec));
                    if (p.FuelAddedL > 0.5) parts.Add(string.Format(Inv, "+{0:F1} L", p.FuelAddedL));
                    if (p.TyresChanged)     parts.Add("pneus");
                }
                if (refSec > 0 && row.Lap.LapTimeTicks > 0)
                {
                    double loss = row.Lap.LapTime.TotalSeconds - refSec;
                    if (i + 1 < _rows.Count && _rows[i + 1].Lap.LapTimeTicks > 0 && !_rows[i + 1].IsPitLap)
                        loss += _rows[i + 1].Lap.LapTime.TotalSeconds - refSec;
                    parts.Add(string.Format(Inv, "perte ≈ {0:+0.0;-0.0} s", loss));
                }
                items.Add(string.Join("  ·  ", parts));
            }
            if (items.Count == 0) items.Add("Aucun arrêt pour ce pilote");
            StatsPitList.ItemsSource = items;
        }

        // ─── Live data merge ──────────────────────────────────────────────────

        private static float WearDrop(float prev, float cur)
        {
            if (prev <= 0 || cur <= 0 || cur > prev) return 0f;
            return (prev - cur) * 100f;
        }

        // The session start written by LMU is not the real green flag: it is off by a few
        // minutes, differently for each driver. Laps are therefore matched on lap number, and
        // when several captured laps share a number (another session in the window), the one
        // closest to the session's typical clock offset wins.
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

            Func<LmuLapRecord, long> xmlStart = l => r.BlockStartUtc.AddSeconds(l.ElapsedTime).Ticks;

            var offsets = new List<long>();
            foreach (var l in laps)
                foreach (var s in live)
                    if (s.LapNum == l.LapNum) offsets.Add(s.StartUtcTicks - xmlStart(l));
            if (offsets.Count == 0) return 0;
            offsets.Sort();
            long median = offsets[offsets.Count / 2];

            int matched = 0;
            foreach (var l in laps)
            {
                LmuLiveLap best = null;
                long bestGap = long.MaxValue;
                foreach (var s in live)
                {
                    if (s.LapNum != l.LapNum) continue;
                    long gap = Math.Abs(s.StartUtcTicks - xmlStart(l) - median);
                    if (gap < bestGap) { bestGap = gap; best = s; }
                }
                if (best != null) { ApplyLive(l, best); matched++; }
            }

            long maxPitGap = TimeSpan.FromMinutes(10).Ticks;
            foreach (var p in pits)
                foreach (var l in laps)
                {
                    if (l.LapNum != p.LapNum || l.LivePit != null) continue;
                    if (Math.Abs(p.EntryUtcTicks - xmlStart(l) - median) > maxPitGap) continue;
                    l.LivePit = p;
                    break;
                }
            return matched;
        }

        /// <summary>
        /// LMU gives no time for invalidated laps, but each lap carries its start time:
        /// an invalid lap lasted until the next lap started.
        /// </summary>
        private static void ReconstructInvalidTimes(List<LmuLapRecord> laps)
        {
            for (int i = 0; i + 1 < laps.Count; i++)
            {
                var l = laps[i];
                if (l.IsValid) continue;
                // A lap starting at session opening includes the time spent in the garage
                if (l.ElapsedTime < 1f) continue;
                double sec = laps[i + 1].ElapsedTime - l.ElapsedTime;
                if (sec > 10 && sec < 3600) l.ReconstructedTicks = TimeSpan.FromSeconds(sec).Ticks;
            }
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
