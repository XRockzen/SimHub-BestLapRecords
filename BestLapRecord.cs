using System;

namespace User.TeamPace
{
    public class BestLapRecord
    {
        public string Game           { get; set; }
        public string GameFolderName { get; set; }
        public string Track          { get; set; }
        public string Category       { get; set; }
        public string CarId          { get; set; }   // raw ID from DB (= team/entry name in LMU)
        public string CarDisplayName { get; set; }   // car model name

        // Alias so XAML bindings using "Car" still work
        public string Car
        {
            get { return CarDisplayName ?? CarId; }
        }

        public TimeSpan  BestLap    { get; set; }
        public TimeSpan? BestRhythm { get; set; }
        public DateTime  RecordDate { get; set; }
        public int       TotalLaps  { get; set; }
        public TimeSpan  TotalTime  { get; set; }

        public string BestLapFormatted
        {
            get { return FormatLapTime(BestLap); }
        }

        public string BestRhythmFormatted
        {
            get { return BestRhythm.HasValue ? FormatLapTime(BestRhythm.Value) : "-"; }
        }

        public static string FormatLapTime(TimeSpan t)
        {
            if (t == TimeSpan.Zero || t == TimeSpan.MaxValue) return "-";
            int minutes = (int)t.TotalMinutes;
            return string.Format("{0}:{1:D2}.{2:D3}", minutes, t.Seconds, t.Milliseconds);
        }

        public static string FormatDelta(TimeSpan delta)
        {
            int minutes = (int)delta.TotalMinutes;
            if (minutes > 0)
                return string.Format("+{0}:{1:D2}.{2:D3}", minutes, delta.Seconds, delta.Milliseconds);
            return string.Format("+{0}.{1:D3}", delta.Seconds, delta.Milliseconds);
        }
    }
}
