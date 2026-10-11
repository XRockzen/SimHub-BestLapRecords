using System;

namespace User.BestLapRecords
{
    /// <summary>Level 3 display: one row per individual lap.</summary>
    public class LapTimeEntry
    {
        public int      LapNumber  { get; set; }
        public TimeSpan LapTime    { get; set; }
        public string   Category   { get; set; }   // category (GT3, LMP2, …)
        public string   CarModel   { get; set; }   // resolved model name
        public string   TeamEntry  { get; set; }   // raw CarId = team/livery name
        public DateTime RecordDate { get; set; }
        public string   SessionId  { get; set; }

        // ─── Weather / track conditions (may be null if not recorded) ─────────
        public double? TrackTemp      { get; set; }   // °C
        public double? AirTemp        { get; set; }   // °C
        public string  Weather        { get; set; }   // e.g. "Clear", "Overcast"
        public string  TrackCondition { get; set; }   // e.g. "Dry", "Damp", "Wet"

        // ─── Validity ─────────────────────────────────────────────────────────
        public bool IsValid { get; set; }

        // ─── Computed after load ──────────────────────────────────────────────
        public string DeltaFormatted { get; set; }

        public string LapTimeFormatted
        {
            get { return LapTime.Ticks <= 0 ? "—" : BestLapRecord.FormatLapTime(LapTime); }
        }

        public string RecordDateFormatted
        {
            get { return RecordDate == DateTime.MinValue ? "-" : RecordDate.ToString("dd/MM/yyyy HH:mm"); }
        }
    }
}
