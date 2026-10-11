using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace User.TeamPace
{
    /// <summary>One driver's session list — history (all circuits) or circuit progression.</summary>
    public partial class SessionListColumn : UserControl
    {
        public event Action<DriverView, LmuSessionRecord> SessionSelected;
        public event Action<DriverView, LmuSessionRecord> SessionOpened;

        public DriverView Driver { get; private set; }
        private bool _syncing;

        public SessionListColumn(DriverView driver)
        {
            Driver = driver;
            InitializeComponent();
        }

        public void Load(List<LmuSessionRow> rows, bool progressMode)
        {
            ColCircuit.Visibility = progressMode ? Visibility.Collapsed : Visibility.Visible;
            ColCar.Visibility     = progressMode ? Visibility.Collapsed : Visibility.Visible;
            ColClass.Visibility   = progressMode ? Visibility.Collapsed : Visibility.Visible;
            ColDelta.Visibility   = progressMode ? Visibility.Visible   : Visibility.Collapsed;

            int common = 0;
            foreach (var r in rows) if (r.IsCommon) common++;
            TxtSummary.Text = string.Format("{0} session{1}{2}", rows.Count, rows.Count > 1 ? "s" : "",
                common > 0 ? string.Format("  ·  {0} commune{1}", common, common > 1 ? "s" : "") : "");

            SessionGrid.ItemsSource = rows;
        }

        public class SummaryItem
        {
            public string Label  { get; set; }
            public string Value  { get; set; }
            public string Detail { get; set; }
            public string Color  { get; set; }
        }

        public void SetSummary(List<SummaryItem> items)
        {
            SummaryCard.Visibility = items != null && items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SummaryItems.ItemsSource = items;
        }

        /// <summary>Selects this driver's own session (e.g. clicked on the progression chart).</summary>
        public void SelectSession(LmuSessionRecord session)
        {
            _syncing = true;
            foreach (LmuSessionRow r in SessionGrid.Items)
                if (r.Record == session) { SessionGrid.SelectedItem = r; SessionGrid.ScrollIntoView(r); break; }
            _syncing = false;
        }

        /// <summary>Selects the row matching another driver's session, without re-raising the event.</summary>
        public void SelectMatching(LmuSessionRecord other)
        {
            _syncing = true;
            LmuSessionRow match = null;
            if (other != null)
                foreach (LmuSessionRow r in SessionGrid.Items)
                    if (SessionMatch.Same(r.Record, other)) { match = r; break; }
            SessionGrid.SelectedItem = match;
            if (match != null) SessionGrid.ScrollIntoView(match);
            _syncing = false;
        }

        private void SessionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            var row = SessionGrid.SelectedItem as LmuSessionRow;
            if (SessionSelected != null) SessionSelected(Driver, row != null ? row.Record : null);
        }

        private void SessionGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = SessionGrid.SelectedItem as LmuSessionRow;
            if (row != null && SessionOpened != null) SessionOpened(Driver, row.Record);
        }
    }
}
