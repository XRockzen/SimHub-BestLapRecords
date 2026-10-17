using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace User.TeamPace
{
    // ─── Lightweight DTO ─────────────────────────────────────────────────────

    public class ActivityLap
    {
        public string   TrackName { get; set; }
        public string   Game      { get; set; }   // display name, e.g. "iRacing"
        public DateTime Date      { get; set; }
        public TimeSpan LapTime   { get; set; }
    }

    /// <summary>
    /// Global stacked weekly activity chart: all circuits, all games.
    /// X = real calendar timeline week by week (gaps visible, always ends today).
    /// Y = total laps per week.
    /// One colour per circuit (cycling palette), stacked bars.
    /// Click a bar or legend item to highlight a circuit; hover legend for transient highlight.
    /// </summary>
    internal class GlobalActivityChartRenderer
    {
        // ─── Layout ───────────────────────────────────────────────────────────

        private const double ML = 32;
        private const double MR = 4;
        private const double MT = 6;
        private const double MB = 20;

        // ─── 12-colour palette ────────────────────────────────────────────────

        private static readonly Color[] Palette = new Color[]
        {
            Color.FromRgb(100, 200, 255),   // blue
            Color.FromRgb(100, 220, 100),   // green
            Color.FromRgb(255, 160,  60),   // orange
            Color.FromRgb(220, 100, 220),   // purple
            Color.FromRgb(255, 220,  60),   // yellow
            Color.FromRgb(255, 100, 100),   // red
            Color.FromRgb( 80, 220, 190),   // teal
            Color.FromRgb(255, 140, 200),   // pink
            Color.FromRgb(180, 220,  80),   // lime
            Color.FromRgb(140, 100, 255),   // violet
            Color.FromRgb(255, 190,  80),   // amber
            Color.FromRgb(100, 200, 170),   // mint
        };

        // ─── Tooltip hit zone (per-week column) ───────────────────────────────

        private struct HitZone
        {
            public double   X1, X2;
            public DateTime Week;
            public Dictionary<string, double> LapsPerTrack;
            public double Total;
        }

        // ─── Bar hit zone (per-segment, for click) ────────────────────────────

        private struct BarHitZone
        {
            public double X1, X2, Y1, Y2;
            public string TrackName;
        }

        private List<HitZone>    _hitZones    = new List<HitZone>();
        private List<BarHitZone> _barHitZones = new List<BarHitZone>();

        // ─── Per-track bar references (for dynamic colour updates) ────────────

        private Dictionary<string, List<Rectangle>> _barsByTrack =
            new Dictionary<string, List<Rectangle>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, Color> _trackColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        // ─── Highlight state ──────────────────────────────────────────────────

        private string    _selectedTrack;   // click-pinned (null = none)
        private string    _hoveredTrack;    // transient hover  (null = none)
        private WrapPanel _legendPanel;     // kept for dynamic legend updates

        // ─── Canvas / data state ─────────────────────────────────────────────

        private Canvas            _canvas;
        private List<ActivityLap> _laps;
        private List<string>      _tracks;   // ordered track names (assigned colours)

        public bool UseHours { get; private set; }

        /// <summary>A bar was clicked: its circuit, or null when the selection was cleared.</summary>
        public event Action<string> TrackClicked;

        // Shared scale so several drivers' charts can be compared side by side
        public DateTime? ScaleMinDate  { get; set; }
        public double?   ScaleMaxValue { get; set; }

        /// <summary>Highest weekly total for these laps, in the unit used by the chart.</summary>
        public static double MaxWeeklyValue(List<ActivityLap> laps, bool useHours)
        {
            var weeks = new Dictionary<DateTime, double>();
            foreach (var lap in laps)
            {
                if (lap.Date == DateTime.MinValue) continue;
                if (useHours && lap.LapTime <= TimeSpan.Zero) continue;
                var ws = WeekStart(lap.Date);
                double cur;
                weeks.TryGetValue(ws, out cur);
                weeks[ws] = cur + (useHours ? lap.LapTime.TotalHours : 1.0);
            }
            double max = 0;
            foreach (var v in weeks.Values) if (v > max) max = v;
            return max;
        }

        public void SetMode(bool useHours)
        {
            UseHours = useHours;
            Render();
        }

        /// <summary>Highlight a specific track (e.g. from an external selection).</summary>
        public void SelectTrack(string trackName)
        {
            _selectedTrack = trackName;
            UpdateHighlight();
            UpdateLegendHighlight(_hoveredTrack ?? _selectedTrack);
        }

        private Border    _tooltip;
        private TextBlock _tooltipBody;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Attach and render.
        /// <paramref name="tracks"/> is an ordered list of track names for the legend
        /// (colours are assigned in this order). Pass null to let the renderer auto-sort.
        /// </summary>
        public void Attach(Canvas canvas, List<ActivityLap> laps, List<string> tracks = null)
        {
            if (_canvas != null) Detach();

            _canvas = canvas;
            _laps   = laps ?? new List<ActivityLap>();
            _tracks = tracks;

            // Pre-compute tracks NOW (independent of canvas layout),
            // so RenderLegend() always finds _tracks != null even if
            // the canvas ActualWidth is still 0 at this point.
            if (_tracks == null)
                ComputeTracks();

            canvas.SizeChanged         += OnSizeChanged;
            canvas.MouseMove           += OnMouseMove;
            canvas.MouseLeave          += OnMouseLeave;
            canvas.MouseLeftButtonDown += OnMouseLeftButtonDown;

            Render();
        }

        public void Detach()
        {
            if (_canvas == null) return;
            _canvas.SizeChanged         -= OnSizeChanged;
            _canvas.MouseMove           -= OnMouseMove;
            _canvas.MouseLeave          -= OnMouseLeave;
            _canvas.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            _canvas = null;
        }

        /// <summary>
        /// Update the data (e.g. after a game filter change) and re-render
        /// without detaching/re-subscribing canvas events.
        /// </summary>
        public void SetData(List<ActivityLap> laps)
        {
            _laps          = laps ?? new List<ActivityLap>();
            _tracks        = null;
            _selectedTrack = null;
            _hoveredTrack  = null;
            ComputeTracks();   // pre-compute before canvas check in Render()
            Render();
        }

        /// <summary>Render the colour legend into a WrapPanel with hover + click highlight.</summary>
        public void RenderLegend(WrapPanel panel)
        {
            _legendPanel = panel;
            panel.Children.Clear();
            if (_tracks == null) ComputeTracks();
            if (_tracks == null || _tracks.Count == 0) return;

            for (int i = 0; i < _tracks.Count; i++)
            {
                var col       = Palette[i % Palette.Length];
                var trackName = _tracks[i];   // capture for closures

                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin      = new Thickness(0, 0, 8, 2),
                    Cursor      = Cursors.Hand
                };

                var square = new Rectangle
                {
                    Width             = 9,
                    Height            = 9,
                    Fill              = new SolidColorBrush(col),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 3, 0)
                };

                var lbl = new TextBlock
                {
                    Text              = trackName,
                    FontSize          = 9,
                    Foreground        = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming      = TextTrimming.CharacterEllipsis,
                    MaxWidth          = 120
                };

                sp.Children.Add(square);
                sp.Children.Add(lbl);

                // Hover → transient highlight
                sp.MouseEnter += (s, e) =>
                {
                    _hoveredTrack = trackName;
                    UpdateHighlight();
                };
                sp.MouseLeave += (s, e) =>
                {
                    _hoveredTrack = null;
                    UpdateHighlight();
                };

                // Click → toggle pin
                sp.MouseLeftButtonDown += (s, e) =>
                {
                    if (string.Equals(_selectedTrack, trackName, StringComparison.OrdinalIgnoreCase))
                        _selectedTrack = null;
                    else
                        _selectedTrack = trackName;
                    UpdateHighlight();
                    e.Handled = true;
                };

                panel.Children.Add(sp);
            }

            // Apply current highlight state (in case a track is already selected/hovered)
            string currentActive = _hoveredTrack ?? _selectedTrack;
            if (currentActive != null)
                UpdateLegendHighlight(currentActive);
        }

        // ─── Events ───────────────────────────────────────────────────────────

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_canvas != null && e.NewSize.Width > 10) Render();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_tooltip == null || _hitZones.Count == 0) return;

            var pos = e.GetPosition(_canvas);
            HitZone? found = null;
            foreach (var hz in _hitZones)
                if (pos.X >= hz.X1 && pos.X < hz.X2) { found = hz; break; }

            if (found == null) { _tooltip.Visibility = Visibility.Collapsed; return; }

            var z  = found.Value;
            var sb = new StringBuilder();
            sb.AppendFormat("Sem. du {0}", z.Week.ToString("dd MMM yyyy"));

            var sorted = z.LapsPerTrack
                          .OrderByDescending(kv => kv.Value)
                          .ToList();

            int shown = 0;
            foreach (var kv in sorted)
            {
                if (shown >= 6) { sb.Append("\n…"); break; }
                if (UseHours)
                    sb.AppendFormat("\n{0} · {1}", kv.Key, FormatHours(kv.Value));
                else
                    sb.AppendFormat("\n{0} · {1}", kv.Key, (int)kv.Value);
                shown++;
            }
            if (UseHours)
                sb.AppendFormat("\nTotal · {0}", FormatHours(z.Total));
            else
                sb.AppendFormat("\nTotal · {0} tour{1}", (int)z.Total, z.Total > 1 ? "s" : "");

            _tooltipBody.Text = sb.ToString();

            double cw = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : _canvas.Width;

            const double TW = 180, TH = 110;
            double tx = (z.X1 + z.X2) * 0.5 - TW * 0.5;
            double ty = MT - TH - 4;
            if (ty < 0) ty = MT + 4;
            if (tx + TW > cw - 2) tx = cw - TW - 2;
            if (tx < ML) tx = ML;

            Canvas.SetLeft(_tooltip, tx);
            Canvas.SetTop(_tooltip,  ty);
            _tooltip.Visibility = Visibility.Visible;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_tooltip != null) _tooltip.Visibility = Visibility.Collapsed;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos     = e.GetPosition(_canvas);
            string hit  = null;

            foreach (var bz in _barHitZones)
            {
                if (pos.X >= bz.X1 && pos.X < bz.X2 &&
                    pos.Y >= bz.Y1 && pos.Y < bz.Y2)
                {
                    hit = bz.TrackName;
                    break;
                }
            }

            if (hit == null)
            {
                // Clicked on empty area → clear selection
                _selectedTrack = null;
            }
            else if (string.Equals(_selectedTrack, hit, StringComparison.OrdinalIgnoreCase))
            {
                // Same track clicked again → toggle off
                _selectedTrack = null;
            }
            else
            {
                _selectedTrack = hit;
            }

            UpdateHighlight();
            var h = TrackClicked;
            if (h != null) h(_selectedTrack);
        }

        // ─── Highlight engine ─────────────────────────────────────────────────

        /// <summary>
        /// Recolour bars and legend items according to the current
        /// hover / selected state — no full re-render required.
        /// </summary>
        private void UpdateHighlight()
        {
            string active = _hoveredTrack ?? _selectedTrack;   // hover wins over pin

            // ── Bars ─────────────────────────────────────────────────────────
            foreach (var kv in _barsByTrack)
            {
                Color baseCol;
                bool  hasCol   = _trackColors.TryGetValue(kv.Key, out baseCol);
                bool  isActive = active == null ||
                                 string.Equals(kv.Key, active, StringComparison.OrdinalIgnoreCase);

                foreach (var rect in kv.Value)
                {
                    if (isActive)
                    {
                        if (hasCol)
                            rect.Fill = new SolidColorBrush(Color.FromArgb(200, baseCol.R, baseCol.G, baseCol.B));
                    }
                    else
                    {
                        rect.Fill = new SolidColorBrush(Color.FromArgb(50, 140, 140, 140));
                    }
                }
            }

            // ── Legend ───────────────────────────────────────────────────────
            UpdateLegendHighlight(active);
        }

        private void UpdateLegendHighlight(string activeTrack)
        {
            if (_legendPanel == null || _tracks == null) return;

            for (int i = 0; i < _legendPanel.Children.Count && i < _tracks.Count; i++)
            {
                var sp = _legendPanel.Children[i] as StackPanel;
                if (sp == null) continue;

                bool isActive = activeTrack == null ||
                                string.Equals(_tracks[i], activeTrack, StringComparison.OrdinalIgnoreCase);
                sp.Opacity = isActive ? 1.0 : 0.3;
            }
        }

        // ─── Rendering ────────────────────────────────────────────────────────

        private void Render()
        {
            _canvas.Children.Clear();
            _hitZones.Clear();
            _barHitZones.Clear();
            _barsByTrack.Clear();
            _trackColors.Clear();
            _tooltip     = null;
            _tooltipBody = null;

            if (_laps == null || _laps.Count == 0) return;

            double cw = _canvas.ActualWidth > 0
                      ? _canvas.ActualWidth
                      : (!double.IsNaN(_canvas.Width) ? _canvas.Width : 0);
            double ch = !double.IsNaN(_canvas.Height)
                      ? _canvas.Height
                      : _canvas.ActualHeight;

            if (cw < 30 || ch < 20) return;

            double chartW = cw - ML - MR;
            double chartH = ch - MT - MB;

            // ── Group by week × track ─────────────────────────────────────────
            var weekData = new Dictionary<DateTime, Dictionary<string, double>>();

            foreach (var lap in _laps)
            {
                if (lap.Date == DateTime.MinValue) continue;
                if (UseHours && lap.LapTime <= TimeSpan.Zero) continue;

                var ws = WeekStart(lap.Date);

                if (!weekData.ContainsKey(ws))
                    weekData[ws] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                double cur;
                weekData[ws].TryGetValue(lap.TrackName, out cur);
                weekData[ws][lap.TrackName] = cur + (UseHours ? lap.LapTime.TotalHours : 1.0);
            }

            if (weekData.Count == 0) return;

            // ── Track order (pre-computed in Attach/SetData, always non-null here) ──
            if (_tracks == null || _tracks.Count == 0)
                ComputeTracks();
            if (_tracks == null || _tracks.Count == 0) return;

            // ── Full timeline minWeek → today ─────────────────────────────────
            var sortedKeys = weekData.Keys.OrderBy(d => d).ToList();
            DateTime minW  = sortedKeys[0];
            if (ScaleMinDate.HasValue && WeekStart(ScaleMinDate.Value) < minW)
                minW = WeekStart(ScaleMinDate.Value);
            DateTime maxW  = WeekStart(DateTime.Today);
            if (maxW < minW) maxW = sortedKeys[sortedKeys.Count - 1];

            var allWeeks = new List<DateTime>();
            for (DateTime w = minW; w <= maxW; w = w.AddDays(7))
                allWeeks.Add(w);

            int totalWeeks = allWeeks.Count;
            if (totalWeeks == 0) return;

            // ── Y scale ───────────────────────────────────────────────────────
            double maxVal = 0;
            foreach (var ws in allWeeks)
            {
                if (!weekData.ContainsKey(ws)) continue;
                double total = 0;
                foreach (var v in weekData[ws].Values) total += v;
                if (total > maxVal) maxVal = total;
            }
            if (ScaleMaxValue.HasValue && ScaleMaxValue.Value > maxVal) maxVal = ScaleMaxValue.Value;
            if (maxVal <= 0) return;

            // ── Y grid ────────────────────────────────────────────────────────
            int ySteps = UseHours ? 4 : Math.Min((int)maxVal, 4);
            for (int step = 0; step <= ySteps; step++)
            {
                double yVal = maxVal * step / ySteps;
                double y    = MT + chartH * (1.0 - (double)step / ySteps);

                _canvas.Children.Add(new Line
                {
                    X1 = ML, X2 = ML + chartW, Y1 = y, Y2 = y,
                    Stroke          = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                    StrokeThickness = 1
                });

                if (step == 0 || step == ySteps)
                {
                    string lblText = UseHours ? FormatHours(yVal) : ((int)Math.Round(yVal)).ToString();
                    var lbl = new TextBlock
                    {
                        Text       = lblText,
                        FontSize   = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255))
                    };
                    Canvas.SetLeft(lbl, 0);
                    Canvas.SetTop(lbl,  y - 6);
                    _canvas.Children.Add(lbl);
                }
            }

            _canvas.Children.Add(new Line
            {
                X1 = ML, X2 = ML + chartW,
                Y1 = MT + chartH, Y2 = MT + chartH,
                Stroke          = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1
            });

            // ── Bars + X labels ───────────────────────────────────────────────
            double slotW = chartW / totalWeeks;
            double barW  = Math.Max(Math.Min(slotW * 0.85, 28.0), 1.5);

            int labelEveryN = 1;
            while (slotW * labelEveryN < 30) labelEveryN++;

            int prevMonth = -1, prevYear = -1;

            for (int wi = 0; wi < totalWeeks; wi++)
            {
                DateTime week   = allWeeks[wi];
                double   slotCX = ML + slotW * wi + slotW * 0.5;
                double   slotX1 = ML + slotW * wi;
                double   slotX2 = slotX1 + slotW;

                // Month label
                if ((week.Month != prevMonth || week.Year != prevYear) && wi % labelEveryN == 0)
                {
                    prevMonth = week.Month;
                    prevYear  = week.Year;

                    var lbl = new TextBlock
                    {
                        Text       = week.ToString("MMM yy"),
                        FontSize   = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255))
                    };
                    double lblX = slotCX - 14;
                    if (lblX < ML)      lblX = ML;
                    if (lblX + 30 > cw) lblX = cw - 30;
                    Canvas.SetLeft(lbl, lblX);
                    Canvas.SetTop(lbl,  MT + chartH + 4);
                    _canvas.Children.Add(lbl);
                }

                // Tooltip hit zone (whole column)
                if (weekData.ContainsKey(week))
                {
                    double total = 0;
                    foreach (var v in weekData[week].Values) total += v;
                    _hitZones.Add(new HitZone
                    {
                        X1           = slotX1,
                        X2           = slotX2,
                        Week         = week,
                        LapsPerTrack = weekData[week],
                        Total        = total
                    });
                }

                if (!weekData.ContainsKey(week)) continue;

                double yBottom = MT + chartH;
                double barX    = slotCX - barW * 0.5;

                // Stack tracks bottom-to-top (most-ridden = index 0 = drawn last = bottom)
                for (int ti = _tracks.Count - 1; ti >= 0; ti--)
                {
                    string track = _tracks[ti];
                    double count;
                    if (!weekData[week].TryGetValue(track, out count) || count <= 0) continue;

                    double barH    = chartH * count / maxVal;
                    Color  col     = Palette[ti % Palette.Length];
                    double rectTop = yBottom - Math.Max(barH, 1);

                    // Determine initial fill based on current highlight state
                    string active = _hoveredTrack ?? _selectedTrack;
                    bool   isActive = active == null ||
                                     string.Equals(track, active, StringComparison.OrdinalIgnoreCase);

                    var rect = new Rectangle
                    {
                        Width  = barW,
                        Height = Math.Max(barH, 1),
                        Fill   = isActive
                                    ? new SolidColorBrush(Color.FromArgb(200, col.R, col.G, col.B))
                                    : new SolidColorBrush(Color.FromArgb(50, 140, 140, 140))
                    };
                    Canvas.SetLeft(rect, barX);
                    Canvas.SetTop(rect,  rectTop);
                    _canvas.Children.Add(rect);

                    // Store for later highlight updates
                    if (!_barsByTrack.ContainsKey(track))
                        _barsByTrack[track] = new List<Rectangle>();
                    _barsByTrack[track].Add(rect);

                    if (!_trackColors.ContainsKey(track))
                        _trackColors[track] = col;

                    // Bar-segment hit zone for click detection
                    _barHitZones.Add(new BarHitZone
                    {
                        X1        = barX,
                        X2        = barX + barW,
                        Y1        = rectTop,
                        Y2        = yBottom,
                        TrackName = track
                    });

                    yBottom -= barH;
                }
            }

            // ── Tooltip overlay (must be last so it renders on top) ───────────
            _tooltipBody = new TextBlock
            {
                FontSize     = 9,
                Foreground   = Brushes.White,
                Margin       = new Thickness(7, 5, 7, 5),
                LineHeight   = 14,
                TextWrapping = TextWrapping.NoWrap
            };
            _tooltip = new Border
            {
                Background       = new SolidColorBrush(Color.FromArgb(230, 16, 18, 28)),
                BorderBrush      = new SolidColorBrush(Color.FromArgb(160, 120, 180, 255)),
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(4),
                IsHitTestVisible = false,
                Visibility       = Visibility.Collapsed,
                Child            = _tooltipBody
            };
            _canvas.Children.Add(_tooltip);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Compute the ordered track list from <see cref="_laps"/> (by total laps descending).
        /// Safe to call before the canvas has been laid out.
        /// </summary>
        private void ComputeTracks()
        {
            if (_laps == null || _laps.Count == 0)
            {
                _tracks = new List<string>();
                return;
            }

            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var lap in _laps)
            {
                if (lap.Date == DateTime.MinValue || string.IsNullOrEmpty(lap.TrackName)) continue;
                int cur;
                totals.TryGetValue(lap.TrackName, out cur);
                totals[lap.TrackName] = cur + 1;
            }

            _tracks = totals.OrderByDescending(kv => kv.Value)
                            .Select(kv => kv.Key)
                            .ToList();
        }

        private static DateTime WeekStart(DateTime d)
        {
            int diff = (int)d.DayOfWeek - (int)DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return d.Date.AddDays(-diff);
        }

        private static string FormatHours(double hours)
        {
            int h = (int)hours;
            int m = (int)Math.Round((hours - h) * 60);
            if (m == 60) { h++; m = 0; }
            if (h > 0 && m > 0) return string.Format("{0}h{1:D2}m", h, m);
            if (h > 0) return string.Format("{0}h", h);
            return string.Format("{0}m", Math.Max(m, 1));
        }
    }
}
