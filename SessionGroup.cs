using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace User.BestLapRecords
{
    public class SessionGroup : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public string           Label    { get; set; }
        public List<LapTimeEntry> Laps   { get; set; }

        public int LapCount { get { return Laps != null ? Laps.Count : 0; } }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged("IsExpanded");
                OnPropertyChanged("BodyVisibility");
                OnPropertyChanged("ToggleGlyph");
            }
        }

        public Visibility BodyVisibility
        {
            get { return _isExpanded ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string ToggleGlyph { get { return _isExpanded ? "▾" : "▸"; } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        // ── Build session groups from a flat sorted list of laps ──────────────

        public static List<SessionGroup> BuildGroups(List<LapTimeEntry> laps)
        {
            var bySession = laps
                .GroupBy(l => string.IsNullOrEmpty(l.SessionId) ? "__none__" : l.SessionId)
                .OrderBy(g =>
                {
                    var dates = g.Where(l => l.RecordDate != DateTime.MinValue).Select(l => l.RecordDate).ToList();
                    return dates.Count > 0 ? dates.Min() : DateTime.MaxValue;
                });

            var result = new List<SessionGroup>();
            foreach (var g in bySession)
            {
                var sessionLaps = g.OrderBy(l => l.LapNumber).ToList();

                var car = sessionLaps
                    .Select(l => l.CarModel).Where(m => !string.IsNullOrEmpty(m))
                    .GroupBy(m => m).OrderByDescending(x => x.Count())
                    .Select(x => x.Key).FirstOrDefault() ?? "";

                var dates = sessionLaps
                    .Where(l => l.RecordDate != DateTime.MinValue)
                    .Select(l => l.RecordDate).ToList();
                var startTime = dates.Count > 0 ? dates.Min() : DateTime.MinValue;

                string dateStr = startTime != DateTime.MinValue
                    ? startTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                    : "?";

                string sessionType = InferSessionType(g.Key);

                var sg = new SessionGroup();
                sg.Laps  = sessionLaps;
                sg.Label = string.Format("{0}   {1}   {2}   ({3} tour{4})",
                    car, dateStr, sessionType, sessionLaps.Count, sessionLaps.Count > 1 ? "s" : "");
                result.Add(sg);
            }

            return result;
        }

        // SessionId from XML files ends with P1/Q1/R1 (Practice/Qualify/Race)
        private static string InferSessionType(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return "Session";
            if (sessionId.Length >= 2)
            {
                char last = sessionId[sessionId.Length - 1];
                char prev = sessionId[sessionId.Length - 2];
                if (char.IsDigit(last))
                {
                    if (prev == 'R') return "Course";
                    if (prev == 'Q') return "Qualif.";
                    if (prev == 'P') return "Essais";
                }
            }
            return "Session";
        }
    }
}
