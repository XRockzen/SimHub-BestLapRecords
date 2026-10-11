using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>Level 1 display: one row per Circuit (all categories combined).</summary>
    public class Level1Row
    {
        public string   Track          { get; set; }
        public string   Game           { get; set; }
        public string   GameFolderName { get; set; }
        public int      TotalLaps      { get; set; }
        public TimeSpan TotalTime      { get; set; }

        public string TotalTimeFormatted
        {
            get
            {
                int h = (int)TotalTime.TotalHours;
                int m = TotalTime.Minutes;
                if (h > 0 && m > 0) return string.Format("{0}h{1:D2}m", h, m);
                if (h > 0) return string.Format("{0}h", h);
                if (m > 0) return string.Format("{0}m", m);
                return "-";
            }
        }

        /// <summary>One badge per distinct category recorded on this circuit.</summary>
        public List<CategoryBadge> CategoryBadges { get; set; }

        // ─── Row alternation (one group per circuit name) ─────────────────────

        public bool IsAlternate { get; set; }

        public Brush RowBg
        {
            get
            {
                return IsAlternate
                    ? CategoryStyleHelper.RowBrushA
                    : CategoryStyleHelper.RowBrushB;
            }
        }
    }
}
