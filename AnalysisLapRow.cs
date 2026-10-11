using System;
using System.ComponentModel;

namespace User.BestLapRecords
{
    /// <summary>Row model for the analysis lap DataGrid (view 7). Supports INotifyPropertyChanged for live stats recalc.</summary>
    public class AnalysisLapRow : INotifyPropertyChanged
    {
        public LmuLapRecord Lap { get; set; }

        // ── Checkbox ──────────────────────────────────────────────────────────

        private bool _isChecked;
        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged("IsChecked");
                }
            }
        }

        // ── Lap identity ──────────────────────────────────────────────────────

        public int  LapNum   { get { return Lap.LapNum; } }
        public bool IsValid  { get { return Lap.IsValid; } }
        public bool IsPitLap { get { return Lap.IsPitLap; } }

        // ── Time ──────────────────────────────────────────────────────────────

        public string LapTime
        {
            get
            {
                if (!Lap.IsValid) return "--:--.---";
                var t = Lap.LapTime;
                return string.Format("{0}:{1:00}.{2:000}",
                    (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
            }
        }

        private static string Sec(float s)
        {
            if (s <= 0) return "—";
            var t = TimeSpan.FromSeconds(s);
            return string.Format("{0}:{1:00}.{2:000}",
                (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
        }

        public string S1 { get { return Sec(Lap.S1); } }
        public string S2 { get { return Sec(Lap.S2); } }
        public string S3 { get { return Sec(Lap.S3); } }

        // ── Fuel ──────────────────────────────────────────────────────────────

        // Live capture gives litres; LMU's XML only gives fractions of the tank.
        public bool HasLiveFuel { get { return Lap.HasLive && Lap.LiveFuelUsedL > 0; } }

        public string FuelUsed
        {
            get
            {
                if (HasLiveFuel)      return Lap.LiveFuelUsedL.ToString("F2") + " L";
                if (Lap.FuelUsed > 0) return (Lap.FuelUsed * 100).ToString("F1") + " %";
                return "—";
            }
        }

        public string FuelRemaining
        {
            get
            {
                if (Lap.HasLive && Lap.LiveFuelLevelL > 0) return Lap.LiveFuelLevelL.ToString("F1") + " L";
                if (Lap.FuelRemaining > 0)                 return (Lap.FuelRemaining * 100).ToString("F0") + " %";
                return "—";
            }
        }

        public bool   HasVe  { get { return Lap.VirtualEnergyUsed > 0; } }
        public string VeUsed { get { return HasVe ? (Lap.VirtualEnergyUsed * 100).ToString("F1") + " %" : "—"; } }

        // ── Tyre wear during this lap (% of tyre, set by the builder) ─────────

        public float WearLapFL { get; set; }
        public float WearLapFR { get; set; }
        public float WearLapRL { get; set; }
        public float WearLapRR { get; set; }

        public bool HasTireData { get { return WearLapFL > 0 || WearLapFR > 0 || WearLapRL > 0 || WearLapRR > 0; } }

        public string TireWearAvg
        {
            get
            {
                if (!HasTireData) return "—";
                return ((WearLapFL + WearLapFR + WearLapRL + WearLapRR) / 4f).ToString("F2") + " %";
            }
        }

        // ── Compound ─────────────────────────────────────────────────────────

        public string Compound { get { return Lap.FrontCompound ?? ""; } }

        // ── Visual markers ────────────────────────────────────────────────────

        public bool TireChanged { get; set; }  // compound changed vs. previous lap

        // Representative of race pace: valid, not lap 1, not a pit lap, not the lap after one
        public bool IsRhythmLap { get; set; }

        // Highlights are recomputed over the checked laps, so they need notifications.
        private bool _isBest, _isWorst, _isS1Best, _isS2Best, _isS3Best;

        public void SetHighlights(bool best, bool worst, bool s1, bool s2, bool s3)
        {
            if (best != _isBest || worst != _isWorst)
            {
                _isBest = best; _isWorst = worst;
                OnPropertyChanged("LapTimeFg");
            }
            if (s1 != _isS1Best) { _isS1Best = s1; OnPropertyChanged("S1Fg"); }
            if (s2 != _isS2Best) { _isS2Best = s2; OnPropertyChanged("S2Fg"); }
            if (s3 != _isS3Best) { _isS3Best = s3; OnPropertyChanged("S3Fg"); }
        }

        // ── Colors ───────────────────────────────────────────────────────────

        private const string ColNormal = "#DDFFFFFF";
        private const string ColBest   = "#FF4CAF50";
        private const string ColWorst  = "#FFEF5350";
        private const string ColSector = "#FFBA68C8";

        public string LapTimeFg
        {
            get
            {
                if (!IsValid) return "#66FFFFFF";
                if (_isBest)  return ColBest;
                if (_isWorst) return ColWorst;
                return ColNormal;
            }
        }

        public string S1Fg { get { return _isS1Best ? ColSector : ColNormal; } }
        public string S2Fg { get { return _isS2Best ? ColSector : ColNormal; } }
        public string S3Fg { get { return _isS3Best ? ColSector : ColNormal; } }

        public string RowBg
        {
            get
            {
                if (!IsValid)  return "#08FF4444";
                if (IsPitLap)  return "#0A1565B0";
                return "Transparent";
            }
        }

        // ── Marker text (pit / tire / invalid) ───────────────────────────────

        // ── Gap to the fastest displayed driver on the same lap number ───────

        public string TeamDelta   { get; set; }
        public string TeamDeltaFg { get; set; }

        public string FlagPit     { get { return IsPitLap   ? "PIT"  : ""; } }
        public string FlagTire    { get { return TireChanged ? "PNEU" : ""; } }
        public string FlagInvalid { get { return !IsValid    ? "INV"  : ""; } }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
