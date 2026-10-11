using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace User.BestLapRecords
{
    /// <summary>Level 2 display: one row per Category for a given Circuit.</summary>
    public class Level2Row
    {
        public string       Category       { get; set; }
        public string       Track          { get; set; }
        public string       Game           { get; set; }
        public string       GameFolderName { get; set; }

        /// <summary>All CarIds that belong to this category on this circuit.</summary>
        public List<string> CarIds { get; set; }

        public TimeSpan  BestLap    { get; set; }
        public TimeSpan? BestRhythm { get; set; }
        public int       TotalLaps  { get; set; }
        public int       CarCount   { get; set; }

        // ─── Formatted ────────────────────────────────────────────────────────

        public string BestLapFormatted
        {
            get { return BestLapRecord.FormatLapTime(BestLap); }
        }

        public string BestRhythmFormatted
        {
            get { return BestRhythm.HasValue ? BestLapRecord.FormatLapTime(BestRhythm.Value) : "-"; }
        }

        // ─── Category badge ───────────────────────────────────────────────────

        private CategoryBadge _badge;
        private CategoryBadge Badge
        {
            get
            {
                if (_badge == null) _badge = CategoryStyleHelper.GetBadge(Category);
                return _badge;
            }
        }

        public string    CategoryShort       { get { return Badge.ShortName; } }
        public Brush     CategoryBg          { get { return Badge.Background; } }
        public Brush     CategoryBorderBrush { get { return Badge.BorderBrush; } }
        public Thickness CategoryBorderThick { get { return Badge.BorderThickness; } }

        public Level2Row()
        {
            CarIds = new List<string>();
        }
    }
}
