using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace User.BestLapRecords
{
    /// <summary>Row of a session list column (history and circuit progression).</summary>
    public class LmuSessionRow
    {
        public LmuSessionRecord Record { get; set; }

        // ── Date / type ───────────────────────────────────────────────────────

        public string DateLabel
        {
            get
            {
                var d = Record.SessionDate;
                return d == DateTime.MinValue ? "—" : d.ToString("dd/MM/yy HH:mm");
            }
        }

        public long SessionDateTicks { get { return Record.SessionDate.Ticks; } }

        public string TypeLabel { get { return Record.SessionType ?? "?"; } }
        public string TypeBg
        {
            get
            {
                if (Record.IsRace)   return "#CC1565B0";
                if (Record.IsQuali)  return "#CC6A1E8E";
                return                       "#CC2E7D32";
            }
        }

        // ── Circuit / car ─────────────────────────────────────────────────────

        public string Circuit  { get { return Record.TrackDisplayName; } }

        public string CarType
        {
            get
            {
                var t = Record.CarType ?? "";
                return t.Length > 22 ? t.Substring(0, 21) + "…" : t;
            }
        }

        public string CarClassLabel   { get { return LmuCategoryStyle.GetShortName(Record.CarClass); } }
        public string CarClassBg      { get { return LmuCategoryStyle.GetBg(Record.CarClass); } }
        public string CarClassBorder  { get { return LmuCategoryStyle.GetBorder(Record.CarClass); } }
        public string CarClassThick   { get { return LmuCategoryStyle.GetBorderThickness(Record.CarClass); } }

        // ── Lap / result ──────────────────────────────────────────────────────

        public string BestLap    { get { return Record.BestLapFormatted; } }
        public long   BestTicks  { get { return Record.BestLapTime.Ticks; } }
        public int    TotalLaps  { get { return Record.TotalLaps; } }

        /// <summary>Class position, with overall in parentheses in multi-class races.</summary>
        public string Position
        {
            get
            {
                if (!Record.IsRace) return "—";
                if (!Record.IsFinished) return Record.TotalLaps > 0 ? "DNF" : "—";

                int overall = Record.Position;
                int cls     = Record.ClassPosition;
                if (cls > 0 && overall > 0 && cls != overall)
                    return string.Format("P{0} (P{1})", cls, overall);
                if (cls > 0)     return string.Format("P{0}", cls);
                if (overall > 0) return string.Format("P{0}", overall);
                return "—";
            }
        }

        public bool IsRace    { get { return Record.IsRace; } }
        public bool IsFullSim { get { return Record.IsFullSim; } }

        // ── Progression (delta vs. previous session on the same circuit) ─────

        public bool   IsPersonalBest { get; set; }
        public string Delta          { get; set; }
        public string DeltaFg        { get; set; }
        public string BestLapFg      { get { return IsPersonalBest ? "#FF4CAF50" : "#DDFFFFFF"; } }

        // ── Shared with the other displayed drivers ───────────────────────────

        public List<Brush> CommonColors { get; set; }
        public bool   IsCommon { get { return CommonColors != null && CommonColors.Count > 0; } }
        public string RowBg    { get { return IsCommon ? "#30FFC857" : "Transparent"; } }
    }

    /// <summary>Display row for the career circuit-bests DataGrid.</summary>
    public class LmuCircuitBestRow
    {
        public string Circuit        { get; set; }
        public string CarClassLabel  { get; set; }
        public string CarClassBg     { get; set; }
        public string CarClassBorder { get; set; }
        public string CarClassThick  { get; set; }
        public string BestLap        { get; set; }
        public long   BestTicks      { get; set; }
        public string Voiture        { get; set; }
        public string Date           { get; set; }
        public string TotalTime      { get; set; }
        public long   TotalTimeTicks { get; set; }

        public string Key { get { return Circuit + "|" + CarClassLabel; } }

        // Gap to the best of the displayed drivers on this circuit × class
        public string Gap   { get; set; }
        public string GapFg { get; set; }
        public long   GapTicks { get; set; }
    }
}
