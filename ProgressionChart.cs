using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace User.TeamPace
{
    /// <summary>
    /// Best lap of every session over time, one coloured series per driver, drawn on a Canvas.
    /// Slow outliers are pinned to the top edge so they don't squash the interesting range.
    /// </summary>
    internal class ProgressionChart
    {
        public class Series
        {
            public DriverView Driver;
            public List<LmuSessionRecord> Sessions;   // sessions with a best lap
            public Func<LmuSessionRecord, bool> IsCommon;
        }

        public event Action<DriverView, LmuSessionRecord> PointSelected;
        public event Action<DriverView, LmuSessionRecord> PointOpened;

        private const double ML = 64, MR = 14, MT = 10, MB = 24;
        private static readonly Brush GridBrush  = Frozen(Color.FromArgb(28, 255, 255, 255));
        private static readonly Brush LabelBrush = Frozen(Color.FromArgb(150, 255, 255, 255));
        private static readonly Brush Amber      = Frozen(Color.FromRgb(0xFF, 0xC8, 0x57));

        private Canvas _canvas;
        private List<Series> _series = new List<Series>();
        private readonly Dictionary<LmuSessionRecord, Ellipse> _dots = new Dictionary<LmuSessionRecord, Ellipse>();
        private LmuSessionRecord _highlighted;

        public void Attach(Canvas canvas, List<Series> series)
        {
            Detach();
            _canvas = canvas;
            _series = series ?? new List<Series>();
            _canvas.SizeChanged += OnSizeChanged;
            Render();
        }

        public void Detach()
        {
            if (_canvas == null) return;
            _canvas.SizeChanged -= OnSizeChanged;
            _canvas.Children.Clear();
            _canvas = null;
        }

        /// <summary>Rings the point of a session (and of its counterparts) selected elsewhere.</summary>
        public void Highlight(LmuSessionRecord session)
        {
            _highlighted = session;
            foreach (var kv in _dots)
            {
                bool on = session != null && (kv.Key == session || SessionMatch.Same(kv.Key, session));
                kv.Value.StrokeThickness = on ? 3 : (kv.Value.Tag as string == "common" ? 2 : 0);
                kv.Value.Stroke = on ? Brushes.White : Amber;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 40) Render();
        }

        private void Render()
        {
            if (_canvas == null) return;
            _canvas.Children.Clear();
            _dots.Clear();

            double w = _canvas.ActualWidth, h = _canvas.Height;
            if (double.IsNaN(h) || h <= 0) h = _canvas.ActualHeight;
            var all = _series.SelectMany(s => s.Sessions).Where(s => s.BestLapTime.Ticks > 0 && s.SessionDate != DateTime.MinValue).ToList();
            if (w < 80 || h < 60 || all.Count == 0)
            {
                AddText("Aucune session chronométrée sur ce circuit.", ML, h / 2 - 8, LabelBrush, 12);
                return;
            }

            // ── Scales ───────────────────────────────────────────────────────
            var secs = all.Select(s => s.BestLapTime.TotalSeconds).OrderBy(v => v).ToList();
            double yMin = secs[0];
            double p80  = secs[(int)Math.Floor((secs.Count - 1) * 0.8)];
            double yMax = Math.Min(secs[secs.Count - 1], p80 + Math.Max(1.5, (p80 - yMin) * 0.6));
            if (yMax - yMin < 0.5) yMax = yMin + 0.5;
            double pad = (yMax - yMin) * 0.08;
            yMin -= pad; yMax += pad;

            // X = session order, not calendar time: months without driving would otherwise squash
            // the busy periods. Sessions a few minutes apart (shared sessions) share one slot.
            var slotStarts = new List<DateTime>();
            var slotOf = new Dictionary<LmuSessionRecord, int>();
            foreach (var s in all.OrderBy(x => x.SessionDate))
            {
                if (slotStarts.Count == 0 || (s.SessionDate - slotStarts[slotStarts.Count - 1]).TotalMinutes > 5)
                    slotStarts.Add(s.SessionDate);
                slotOf[s] = slotStarts.Count - 1;
            }
            int slots = slotStarts.Count;

            double cw = w - ML - MR, ch = h - MT - MB;
            Func<LmuSessionRecord, double> X = s => ML + (slotOf[s] + 0.5) / slots * cw;
            // Faster lap = smaller value = drawn higher; slower outliers are clamped to the bottom edge
            Func<double, double> Yt = v => MT + (Math.Min(v, yMax) - yMin) / (yMax - yMin) * ch;

            // ── Grid + labels ────────────────────────────────────────────────
            double step = NiceStep((yMax - yMin) / 4);
            for (double v = Math.Ceiling(yMin / step) * step; v <= yMax; v += step)
            {
                double y = Yt(v);
                AddLine(ML, y, w - MR, y, GridBrush, 1);
                AddText(Fmt.LapSec(v), 2, y - 8, LabelBrush, 11);
            }
            int ticks = Math.Max(1, Math.Min(slots, (int)(cw / 110)));
            int lastLabelled = -1;
            for (int i = 0; i < ticks; i++)
            {
                int slot = ticks == 1 ? 0 : (int)Math.Round((double)i * (slots - 1) / (ticks - 1));
                if (slot == lastLabelled) continue;
                lastLabelled = slot;
                double x = ML + (slot + 0.5) / slots * cw;
                AddLine(x, MT, x, MT + ch, GridBrush, 1);
                AddText(slotStarts[slot].ToString("dd/MM/yy", CultureInfo.InvariantCulture), x - 22, MT + ch + 5, LabelBrush, 11);
            }

            // ── Series ───────────────────────────────────────────────────────
            foreach (var s in _series)
            {
                var pts = s.Sessions.Where(x => x.BestLapTime.Ticks > 0 && x.SessionDate != DateTime.MinValue)
                                    .OrderBy(x => x.SessionDate).ToList();
                if (pts.Count == 0) continue;
                var color = s.Driver.Brush.Color;
                var lineBrush = Frozen(Color.FromArgb(120, color.R, color.G, color.B));

                // Running record: the line follows the best time achieved so far
                var poly = new Polyline { Stroke = lineBrush, StrokeThickness = 2, IsHitTestVisible = false };
                double best = double.MaxValue;
                foreach (var p in pts)
                {
                    best = Math.Min(best, p.BestLapTime.TotalSeconds);
                    poly.Points.Add(new Point(X(p), Yt(best)));
                }
                _canvas.Children.Add(poly);

                double record = pts.Min(p => p.BestLapTime.TotalSeconds);
                foreach (var p in pts)
                {
                    double v = p.BestLapTime.TotalSeconds;
                    bool clipped = v > yMax;
                    bool isRecord = Math.Abs(v - record) < 0.0005;
                    bool common = s.IsCommon != null && s.IsCommon(p);
                    double r = isRecord ? 7 : 5;
                    var dot = new Ellipse
                    {
                        Width = r * 2, Height = r * 2,
                        Fill = clipped ? Brushes.Transparent : s.Driver.Brush,
                        Stroke = common ? Amber : (clipped ? s.Driver.Brush : null),
                        StrokeThickness = common || clipped ? 2 : 0,
                        Cursor = Cursors.Hand,
                        Tag = common ? "common" : null,
                        ToolTip = string.Format("{0}\n{1} du {2}\nMeilleur tour {3}{4}{5}",
                            s.Driver.Name, p.SessionTypeLong, p.SessionDate.ToString("dd/MM/yy HH:mm"),
                            p.BestLapFormatted, isRecord ? "  ★ record" : "", common ? "\nSession commune" : ""),
                    };
                    Canvas.SetLeft(dot, X(p) - r);
                    Canvas.SetTop(dot, Yt(v) - r);
                    var driver = s.Driver; var session = p;
                    dot.MouseLeftButtonDown += (o, e) =>
                    {
                        if (e.ClickCount >= 2) { if (PointOpened != null) PointOpened(driver, session); }
                        else if (PointSelected != null) PointSelected(driver, session);
                        e.Handled = true;
                    };
                    _canvas.Children.Add(dot);
                    _dots[p] = dot;
                    if (isRecord)
                        AddText("★", X(p) - 5, Yt(v) - r - 17, s.Driver.Brush, 13);
                }
            }

            if (secs[secs.Count - 1] > yMax)
                AddText("○ en bas = session plus lente, hors échelle", w - MR - 250, MT + ch - 16, LabelBrush, 10);

            Highlight(_highlighted);
        }

        private void AddLine(double x1, double y1, double x2, double y2, Brush b, double t)
        {
            _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t, IsHitTestVisible = false });
        }

        private TextBlock AddText(string text, double x, double y, Brush b, double size)
        {
            var t = new TextBlock { Text = text, Foreground = b, FontSize = size, IsHitTestVisible = false };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            _canvas.Children.Add(t);
            return t;
        }

        /// <summary>Round tick spacing in seconds: 0.1, 0.2, 0.5, 1, 2, 5, 10…</summary>
        private static double NiceStep(double raw)
        {
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (var m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (raw <= m * mag) return m * mag;
            return 10 * mag;
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }
    }
}
