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
    /// <summary>
    /// Stacked weekly activity bar chart.
    /// X axis = real calendar timeline (oldest week → TODAY, gaps visible).
    /// Y axis = number of laps per week.
    /// Each week bar is stacked by category (colour-coded), with hover tooltip.
    /// </summary>
    internal class ActivityChartRenderer
    {
        // ─── Layout ───────────────────────────────────────────────────────────

        private const double ML = 30;   // left  (Y labels)
        private const double MR = 4;    // right
        private const double MT = 6;    // top
        private const double MB = 20;   // bottom (X labels)

        // ─── Hit-test data ────────────────────────────────────────────────────

        private struct HitZone
        {
            public double   X1, X2;
            public DateTime Week;
            public Dictionary<string, double> LapsPerCat;
            public double Total;
        }

        private List<HitZone> _hitZones = new List<HitZone>();

        // ─── State ────────────────────────────────────────────────────────────

        private Canvas             _canvas;
        private List<LapTimeEntry> _laps;
        private List<string>       _categories;   // ordered, e.g. ["HYPERCAR","LMP2",…]

        public bool UseHours { get; private set; }

        public void SetMode(bool useHours)
        {
            UseHours = useHours;
            Render();
        }

        // Tooltip overlay
        private Border    _tooltip;
        private TextBlock _tooltipBody;

        // ─── Public API ───────────────────────────────────────────────────────

        public void Attach(Canvas canvas, List<LapTimeEntry> laps, List<string> categories)
        {
            if (_canvas != null) Detach();

            _canvas     = canvas;
            _laps       = laps       ?? new List<LapTimeEntry>();
            _categories = categories ?? new List<string>();

            canvas.SizeChanged += OnSizeChanged;
            canvas.MouseMove   += OnMouseMove;
            canvas.MouseLeave  += OnMouseLeave;

            Render();
        }

        public void Detach()
        {
            if (_canvas == null) return;
            _canvas.SizeChanged -= OnSizeChanged;
            _canvas.MouseMove   -= OnMouseMove;
            _canvas.MouseLeave  -= OnMouseLeave;
            _canvas = null;
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
            {
                if (pos.X >= hz.X1 && pos.X < hz.X2)
                { found = hz; break; }
            }

            if (found == null)
            {
                _tooltip.Visibility = Visibility.Collapsed;
                return;
            }

            var hz2 = found.Value;

            // Build tooltip text
            var sb = new StringBuilder();
            sb.AppendFormat("Sem. du {0}", hz2.Week.ToString("dd MMM yyyy"));

            foreach (string cat in _categories)
            {
                double cnt;
                if (hz2.LapsPerCat.TryGetValue(cat, out cnt) && cnt > 0)
                {
                    string shortName = CategoryStyleHelper.GetBadge(cat).ShortName;
                    if (UseHours)
                        sb.AppendFormat("\n{0} · {1}", shortName, FormatHours(cnt));
                    else
                        sb.AppendFormat("\n{0} · {1} tour{2}", shortName, (int)cnt, cnt > 1 ? "s" : "");
                }
            }
            // Unknown cats
            foreach (var kvp in hz2.LapsPerCat)
            {
                bool known = false;
                foreach (string c in _categories)
                    if (string.Equals(c, kvp.Key, StringComparison.OrdinalIgnoreCase))
                    { known = true; break; }
                if (!known && kvp.Value > 0)
                {
                    if (UseHours)
                        sb.AppendFormat("\n? · {0}", FormatHours(kvp.Value));
                    else
                        sb.AppendFormat("\n? · {0} tour{1}", (int)kvp.Value, kvp.Value > 1 ? "s" : "");
                }
            }

            if (_categories.Count + hz2.LapsPerCat.Count > 1)
            {
                if (UseHours)
                    sb.AppendFormat("\nTotal · {0}", FormatHours(hz2.Total));
                else
                    sb.AppendFormat("\nTotal · {0} tour{1}", (int)hz2.Total, hz2.Total > 1 ? "s" : "");
            }

            _tooltipBody.Text = sb.ToString();

            double cw = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : _canvas.Width;
            double ch = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : _canvas.Height;

            const double TW = 150, TH = 80;
            double tx = (hz2.X1 + hz2.X2) * 0.5 - TW * 0.5;
            double ty = MT - TH - 4;   // above chart area by default

            if (ty < 0) ty = MT + 4;         // if no room above, show inside top
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

        // ─── Rendering ────────────────────────────────────────────────────────

        private void Render()
        {
            _canvas.Children.Clear();
            _hitZones.Clear();
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

            // ── Step 1: group laps by calendar week ───────────────────────────
            var weekData = new Dictionary<DateTime, Dictionary<string, double>>();

            foreach (var lap in _laps)
            {
                if (lap.RecordDate == DateTime.MinValue) continue;
                if (UseHours && lap.LapTime <= TimeSpan.Zero) continue;

                var ws = WeekStart(lap.RecordDate);

                if (!weekData.ContainsKey(ws))
                    weekData[ws] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                string cat = lap.Category ?? "";
                double cur;
                weekData[ws].TryGetValue(cat, out cur);
                weekData[ws][cat] = cur + (UseHours ? lap.LapTime.TotalHours : 1.0);
            }

            if (weekData.Count == 0) return;

            // ── Step 2: full timeline from minWeek → TODAY ────────────────────
            var sortedKeys = weekData.Keys.OrderBy(d => d).ToList();
            DateTime minW  = sortedKeys[0];
            DateTime maxW  = WeekStart(DateTime.Today);   // always extend to current week

            if (maxW < minW) maxW = sortedKeys[sortedKeys.Count - 1];

            var allWeeks = new List<DateTime>();
            for (DateTime w = minW; w <= maxW; w = w.AddDays(7))
                allWeeks.Add(w);

            int totalWeeks = allWeeks.Count;
            if (totalWeeks == 0) return;

            // ── Step 3: Y scale ───────────────────────────────────────────────
            double maxVal = 0;
            foreach (var ws in allWeeks)
            {
                if (!weekData.ContainsKey(ws)) continue;
                double total = 0;
                foreach (var v in weekData[ws].Values) total += v;
                if (total > maxVal) maxVal = total;
            }
            if (maxVal <= 0) return;

            // ── Step 4: Y grid + labels ───────────────────────────────────────
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

            // ── Step 5: bars + X labels ───────────────────────────────────────
            double slotW = chartW / totalWeeks;
            double barW  = Math.Max(Math.Min(slotW * 0.85, 28.0), 1.5);

            int labelEveryN = 1;
            while (slotW * labelEveryN < 30) labelEveryN++;

            int prevMonth = -1;
            int prevYear  = -1;

            for (int wi = 0; wi < totalWeeks; wi++)
            {
                DateTime week   = allWeeks[wi];
                double   slotCX = ML + slotW * wi + slotW * 0.5;
                double   slotX1 = ML + slotW * wi;
                double   slotX2 = slotX1 + slotW;

                // X label when month changes
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

                // Register hit zone (even for empty weeks — tooltip will show nothing)
                if (weekData.ContainsKey(week))
                {
                    double total = 0;
                    foreach (var v in weekData[week].Values) total += v;

                    _hitZones.Add(new HitZone
                    {
                        X1         = slotX1,
                        X2         = slotX2,
                        Week       = week,
                        LapsPerCat = weekData[week],
                        Total      = total
                    });
                }

                // Skip empty weeks for drawing
                if (!weekData.ContainsKey(week)) continue;

                double yBottom = MT + chartH;
                double barX    = slotCX - barW * 0.5;

                // Known categories first
                foreach (string cat in _categories)
                {
                    double count;
                    if (!weekData[week].TryGetValue(cat, out count) || count <= 0) continue;

                    double barH = chartH * count / maxVal;
                    Color  col  = CategoryStyleHelper.GetColor(cat);

                    var rect = new Rectangle
                    {
                        Width  = barW,
                        Height = Math.Max(barH, 1),
                        Fill   = new SolidColorBrush(Color.FromArgb(210, col.R, col.G, col.B))
                    };
                    Canvas.SetLeft(rect, barX);
                    Canvas.SetTop(rect,  yBottom - Math.Max(barH, 1));
                    _canvas.Children.Add(rect);
                    yBottom -= barH;
                }

                // Unknown categories
                foreach (var kvp in weekData[week])
                {
                    bool known = false;
                    foreach (string c in _categories)
                        if (string.Equals(c, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        { known = true; break; }
                    if (known || kvp.Value <= 0) continue;

                    double barH = chartH * kvp.Value / maxVal;
                    var rect = new Rectangle
                    {
                        Width  = barW,
                        Height = Math.Max(barH, 1),
                        Fill   = new SolidColorBrush(Color.FromArgb(150, 100, 110, 120))
                    };
                    Canvas.SetLeft(rect, barX);
                    Canvas.SetTop(rect,  yBottom - Math.Max(barH, 1));
                    _canvas.Children.Add(rect);
                    yBottom -= barH;
                }
            }

            // ── Tooltip overlay (on top of everything) ────────────────────────
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
