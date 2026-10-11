using GameReaderCommon;
using System;
using System.Threading.Tasks;

namespace User.BestLapRecords
{
    public class LmuLiveLap
    {
        public int    Id             { get; set; }
        public long   StartUtcTicks  { get; set; }
        public long   EndUtcTicks    { get; set; }
        public int    LapNum         { get; set; }
        public string Track          { get; set; }
        public double FuelUsedL      { get; set; }
        public double FuelLevelL     { get; set; }
        public double WearFL         { get; set; }   // % of tyre worn during this lap
        public double WearFR         { get; set; }
        public double WearRL         { get; set; }
        public double WearRR         { get; set; }
        public bool   InPitLane      { get; set; }
    }

    public class LmuLivePit
    {
        public int    Id             { get; set; }
        public long   EntryUtcTicks  { get; set; }
        public long   ExitUtcTicks   { get; set; }
        public int    LapNum         { get; set; }   // lap being driven at pit entry
        public string Track          { get; set; }
        public double LaneSec        { get; set; }   // pit entry → pit exit
        public double StoppedSec     { get; set; }   // speed ~0 inside the pit lane
        public double LimiterSec     { get; set; }   // pit limiter engaged inside the lane
        public double FuelAddedL     { get; set; }
        public bool   TyresChanged   { get; set; }
    }

    /// <summary>Records per-lap fuel/tyre usage and pit stop timings from SimHub telemetry.</summary>
    public class LmuLiveTracker
    {
        private const double MaxTickSec     = 1.0;   // ignore gaps (pause, alt-tab)
        private const double MaxWearStepPct = 2.0;   // a bigger jump in one tick = new tyres
        private const double StoppedKmh     = 1.0;

        private string   _track;
        private int      _lastCompleted = -1;
        private DateTime _lastTick;

        // Current lap accumulators
        private DateTime _lapStart;
        private bool     _lapStarted;
        private double   _fuelUsed;
        private double[] _wear = new double[4];
        private bool     _lapInPit;

        private double   _prevFuel = -1;
        private double[] _prevTyre;

        // Current pit stop
        private bool     _inLane;
        private DateTime _pitEntry;
        private int      _pitLap;
        private double   _pitStopped, _pitLimiter, _pitFuelAdded;
        private bool     _pitTyres;

        public void Update(GameData data)
        {
            var d   = data.NewData;
            var now = DateTime.UtcNow;
            if (d == null || data.GamePaused || data.GameReplay || data.GameInMenu)
            {
                _lastTick = DateTime.MinValue;
                return;
            }

            double dt = _lastTick == DateTime.MinValue ? 0 : (now - _lastTick).TotalSeconds;
            if (dt < 0 || dt > MaxTickSec) dt = 0;
            _lastTick = now;

            var track     = d.TrackName ?? "";
            int completed = d.CompletedLaps;

            if (track != _track || completed < _lastCompleted)
                ResetSession(track);

            AccumulateFuel(d.Fuel, _inLane);
            AccumulateWear(new[] { d.TyreWearFrontLeft, d.TyreWearFrontRight,
                                   d.TyreWearRearLeft,  d.TyreWearRearRight });
            TrackPit(d, now, dt, completed);

            if (completed < 0) return;

            if (_lastCompleted >= 0 && completed > _lastCompleted && _lapStarted)
                SaveLap(now, completed, d.Fuel);

            if (_lastCompleted < 0 || completed > _lastCompleted)
                StartLap(now);

            _lastCompleted = completed;
        }

        private void ResetSession(string track)
        {
            _track         = track;
            _lastCompleted = -1;
            _lapStarted    = false;
            _inLane        = false;
            _prevFuel      = -1;
            _prevTyre      = null;
        }

        private void StartLap(DateTime now)
        {
            _lapStart   = now;
            _lapStarted = true;
            _fuelUsed   = 0;
            _wear       = new double[4];
            _lapInPit   = _inLane;
        }

        private void AccumulateFuel(double fuel, bool inLane)
        {
            if (_prevFuel >= 0)
            {
                double delta = _prevFuel - fuel;
                if (delta > 0) _fuelUsed += delta;
                else if (delta < 0 && inLane) _pitFuelAdded += -delta;
            }
            _prevFuel = fuel;
        }

        // SimHub's wear convention differs per game (worn % or remaining %), so only
        // the magnitude of the change is used; large steps are tyre changes.
        private void AccumulateWear(double[] tyre)
        {
            if (_prevTyre != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    double step = Math.Abs(tyre[i] - _prevTyre[i]);
                    if (step > MaxWearStepPct) { if (_inLane) _pitTyres = true; }
                    else _wear[i] += step;
                }
            }
            _prevTyre = tyre;
        }

        private void TrackPit(StatusDataBase d, DateTime now, double dt, int completed)
        {
            bool inLane = d.IsInPitLane != 0;

            if (inLane && !_inLane)
            {
                _pitEntry     = now;
                _pitLap       = completed + 1;
                _pitStopped   = 0;
                _pitLimiter   = 0;
                _pitFuelAdded = 0;
                _pitTyres     = false;
                _lapInPit     = true;
            }
            else if (inLane)
            {
                if (d.SpeedKmh < StoppedKmh) _pitStopped += dt;
                if (d.PitLimiterOn != 0)     _pitLimiter += dt;
            }
            else if (_inLane)
            {
                var pit = new LmuLivePit
                {
                    EntryUtcTicks = _pitEntry.Ticks,
                    ExitUtcTicks  = now.Ticks,
                    LapNum        = _pitLap,
                    Track         = _track,
                    LaneSec       = (now - _pitEntry).TotalSeconds,
                    StoppedSec    = _pitStopped,
                    LimiterSec    = _pitLimiter,
                    FuelAddedL    = _pitFuelAdded,
                    TyresChanged  = _pitTyres,
                };
                Task.Run(() => LmuSessionStore.SaveLivePit(pit));
            }

            _inLane = inLane;
        }

        private void SaveLap(DateTime now, int lapNum, double fuel)
        {
            var lap = new LmuLiveLap
            {
                StartUtcTicks = _lapStart.Ticks,
                EndUtcTicks   = now.Ticks,
                LapNum        = lapNum,
                Track         = _track,
                FuelUsedL     = _fuelUsed,
                FuelLevelL    = fuel,
                WearFL        = _wear[0],
                WearFR        = _wear[1],
                WearRL        = _wear[2],
                WearRR        = _wear[3],
                InPitLane     = _lapInPit,
            };
            Task.Run(() => LmuSessionStore.SaveLiveLap(lap));
        }
    }
}
