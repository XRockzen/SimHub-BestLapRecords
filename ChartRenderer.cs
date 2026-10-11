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
    /// Interactive progress-chart renderer attached to a WPF Canvas.
    /// Features: outlier filtering · initial zoom · mouse-wheel zoom ·
    ///           average line · trend line · hover tooltip with weather.
    /// </summary>
    internal class ChartRenderer
    {
        // ─── Colour palette (models) ──────────────────────────────────────────

        internal static readonly Color[] Palette = new Color[]
        {
            Color.FromRgb(100, 200, 255),   // blue
            Color.FromRgb(100, 220, 100),   // green
            Color.FromRgb(255, 160,  60),   // orange
            Color.FromRgb(220, 100, 220),   // purple
            Color.FromRgb(255, 220,  60),   // yellow
            Color.FromRgb(255, 100, 100),   // red
        };

        // ─── Layout constants ─────────────────────────────────────────────────

        private const double ML = 54;   // left margin (Y labels)
        private const double MR = 10;   // right margin
        private const double MT = 12;   // top margin
        private const double MB = 28;   // bottom margin (X labels)

        // ─── State ────────────────────────────────────────────────────────────

        private Canvas              _canvas;
        private List<LapTimeEntry>  _filtered;  // outliers removed
        private List<string>        _models;

        private double _cw, _ch;           // canvas size in pixels
        private double _minSec, _maxSec;   // current Y axis range (adjusted by zoom)
        private double _avgSec, _bestSec;

        // Hit-testing data (rebuilt on each Render)
        private struct ChartPoint { public double X, Y; public LapTimeEntry Lap; }
        private List<ChartPoint> _pts = new List<ChartPoint>();

        // Tooltip overlay
        private Border    _tooltip;
        private TextBlock _tooltipBody;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Attach the renderer to <paramref name="canvas"/> and render immediately.
        /// Applies outlier filter (laps > bestLap + 5 s are excluded).
        /// </summary>
        public void Attach(Canvas canvas, List<LapTimeEntry> laps)
        {
            if (_canvas != null) Detach();

            _canvas = canvas;
            _cw     = !double.IsNaN(canvas.Width)  ? canvas.Width  : canvas.ActualWidth;
            _ch     = !double.IsNaN(canvas.Height) ? canvas.Height : canvas.ActualHeight;

            // ── Outlier filter ──────────────────────────────────────────────
            if (laps == null || laps.Count == 0) { _filtered = new List<LapTimeEntry>(); return; }

            _bestSec = laps.Min(l => l.LapTime.TotalSeconds);
            _filtered = laps.Where(l => l.LapTime.TotalSeconds <= _bestSec + 5.0).ToList();
            if (_filtered.Count == 0) _filtered = laps.ToList();

            _avgSec = _filtered.Average(l => l.LapTime.TotalSeconds);

            // ── Initial Y range: best-1s … avg+3s ──────────────────────────
            _minSec = _bestSec - 1.0;
            _maxSec = _avgSec  + 3.0;
            if (_maxSec - _minSec < 1.5) _maxSec = _minSec + 1.5;

            _models = _filtered.Select(l => l.CarModel ?? "")
                               .Distinct()
                               .OrderBy(m => m)
                               .ToList();

            // ── Tooltip overlay ─────────────────────────────────────────────
            _tooltipBody = new TextBlock();
            _tooltipBody.FontSize    = 10;
            _tooltipBody.Foreground  = Brushes.White;
            _tooltipBody.Margin      = new Thickness(7, 5, 7, 5);
            _tooltipBody.MaxWidth    = 180;
            _tooltipBody.TextWrapping = TextWrapping.Wrap;
            _tooltipBody.LineHeight  = 15;

            _tooltip = new Border();
            _tooltip.Background       = new SolidColorBrush(Color.FromArgb(230, 16, 18, 28));
            _tooltip.BorderBrush      = new SolidColorBrush(Color.FromArgb(160, 120, 180, 255));
            _tooltip.BorderThickness  = new Thickness(1);
            _tooltip.CornerRadius     = new CornerRadius(4);
            _tooltip.IsHitTestVisible = false;
            _tooltip.Visibility       = Visibility.Collapsed;
            _tooltip.Child            = _tooltipBody;

            // ── Subscribe events ────────────────────────────────────────────
            canvas.MouseWheel  += OnWheel;
            canvas.MouseMove   += OnMove;
            canvas.MouseLeave  += OnLeave;
            canvas.SizeChanged += OnSizeChanged;

            Render();
        }

        /// <summary>Unsubscribe all events and release the canvas reference.</summary>
        public void Detach()
        {
            if (_canvas == null) return;
            _canvas.MouseWheel  -= OnWheel;
            _canvas.MouseMove   -= OnMove;
            _canvas.MouseLeave  -= OnLeave;
            _canvas.SizeChanged -= OnSizeChanged;
            _canvas = null;
        }

        /// <summary>Render a coloured legend (one entry per car model) into a WrapPanel.</summary>
        public void RenderLegend(WrapPanel panel)
        {
            panel.Children.Clear();
            if (_models == null || _models.Count == 0) return;

            for (int mi = 0; mi < _models.Count; mi++)
            {
                var color = Palette[mi % Palette.Length];

                var sp = new StackPanel { Orientation = Orientation.Horizontal,
                                          Margin      = new Thickness(0, 0, 8, 3) };

                var rect = new Rectangle
                {
                    Width             = 10,
                    Height            = 10,
                    Fill              = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 4, 0)
                };

                var lbl = new TextBlock
                {
                    Text              = _models[mi],
                    FontSize          = 10,
                    Foreground        = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping      = TextWrapping.Wrap
                };

                sp.Children.Add(rect);
                sp.Children.Add(lbl);
                panel.Children.Add(sp);
            }
        }

        // ─── Mouse events ─────────────────────────────────────────────────────

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            // Scroll up = zoom in (tighter Y range), scroll down = zoom out
            double factor = e.Delta > 0 ? 0.82 : 1.22;
            double center = (_minSec + _maxSec) * 0.5;
            double half   = (_maxSec - _minSec) * 0.5 * factor;

            if (half < 0.5)  half = 0.5;   // minimum 1 s range
            if (half > 60.0) half = 60.0;   // maximum 120 s range

            _minSec = center - half;
            _maxSec = center + half;
            Render();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_pts == null || _pts.Count == 0 || _tooltip == null) return;

            var pos      = e.GetPosition(_canvas);
            double best  = 12.0;    // pixel radius for hit detection
            int    found = -1;

            for (int i = 0; i < _pts.Count; i++)
            {
                double dx   = pos.X - _pts[i].X;
                double dy   = pos.Y - _pts[i].Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < best) { best = dist; found = i; }
            }

            if (found < 0) { _tooltip.Visibility = Visibility.Collapsed; return; }

            var lap = _pts[found].Lap;
            var sb  = new StringBuilder();

            sb.Append(BestLapRecord.FormatLapTime(lap.LapTime));
            if (lap.RecordDate != DateTime.MinValue)
                sb.Append("  ").Append(lap.RecordDate.ToString("dd/MM/yy HH:mm"));

            string modelLine = !string.IsNullOrEmpty(lap.CarModel) ? lap.CarModel
                             : (!string.IsNullOrEmpty(lap.TeamEntry) ? lap.TeamEntry : null);
            if (modelLine != null) { sb.Append("\n"); sb.Append(modelLine); }

            // Weather / conditions
            bool hasWeather = !string.IsNullOrEmpty(lap.Weather) ||
                              !string.IsNullOrEmpty(lap.TrackCondition);
            bool hasTemp    = lap.TrackTemp.HasValue || lap.AirTemp.HasValue;

            if (hasWeather || hasTemp)
            {
                sb.Append("\n");
                if (!string.IsNullOrEmpty(lap.Weather))       sb.Append(lap.Weather);
                if (!string.IsNullOrEmpty(lap.TrackCondition))
                {
                    if (!string.IsNullOrEmpty(lap.Weather)) sb.Append(" · ");
                    sb.Append(lap.TrackCondition);
                }
                if (hasTemp)
                {
                    if (hasWeather) sb.Append("\n");
                    if (lap.TrackTemp.HasValue)
                        sb.AppendFormat("Piste {0:F0}°C", lap.TrackTemp.Value);
                    if (lap.AirTemp.HasValue)
                    {
                        if (lap.TrackTemp.HasValue) sb.Append("  ");
                        sb.AppendFormat("Air {0:F0}°C", lap.AirTemp.Value);
                    }
                }
            }

            _tooltipBody.Text = sb.ToString();

            // Position: prefer right of point, fall back to left
            const double TW = 165, TH = 70;
            double tx = _pts[found].X + 12;
            double ty = _pts[found].Y - TH * 0.5;
            if (tx + TW > _cw - 4) tx = _pts[found].X - TW - 8;
            if (ty + TH > _ch - 2) ty = _ch - TH - 2;
            if (ty < 2) ty = 2;

            Canvas.SetLeft(_tooltip, tx);
            Canvas.SetTop(_tooltip,  ty);
            _tooltip.Visibility = Visibility.Visible;
        }

        private void OnLeave(object sender, MouseEventArgs e)
        {
            if (_tooltip != null) _tooltip.Visibility = Visibility.Collapsed;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                _cw = e.NewSize.Width;
                _ch = e.NewSize.Height;
                Render();
            }
        }

        // ─── Rendering ────────────────────────────────────────────────────────

        private void Render()
        {
            _canvas.Children.Clear();
            _pts = new List<ChartPoint>();

            if (_filtered == null || _filtered.Count == 0) return;

            int    n      = _filtered.Count;
            double chartW = _cw - ML - MR;
            double chartH = _ch - MT - MB;
            double span   = _maxSec - _minSec;
            if (span < 0.001) span = 0.001;

            // ── Pre-compute canvas positions (needed for session bands) ────────
            double[] xArr   = new double[n];
            double[] yArr   = new double[n];
            bool[]   inView = new bool[n];

            for (int i = 0; i < n; i++)
            {
                xArr[i]   = ML + (n > 1 ? chartW * i / (n - 1) : chartW * 0.5);
                double s  = _filtered[i].LapTime.TotalSeconds;
                yArr[i]   = SecToY(s, chartH, span);
                inView[i] = (s >= _minSec - 0.001 && s <= _maxSec + 0.001);
            }

            // ── Session background bands ──────────────────────────────────────
            DrawSessionBands(xArr, n, chartW, chartH);

            // ── Y grid lines + labels ─────────────────────────────────────────
            DrawYGrid(chartW, chartH, span);

            // ── Axes ──────────────────────────────────────────────────────────
            Line(ML, MT + chartH, ML + chartW, MT + chartH, Color.FromArgb(90, 255, 255, 255), 1);
            Line(ML, MT,          ML,           MT + chartH, Color.FromArgb(90, 255, 255, 255), 1);

            // ── X date labels ─────────────────────────────────────────────────
            DrawXLabels(chartW, chartH, n);

            // ── New-PB detection ──────────────────────────────────────────────
            bool[] isPB = new bool[n];
            double runBest = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double s = _filtered[i].LapTime.TotalSeconds;
                if (s < runBest) { runBest = s; if (i > 0) isPB[i] = true; }
            }

            // ── Polylines per model ───────────────────────────────────────────
            for (int mi = 0; mi < _models.Count; mi++)
            {
                string model = _models[mi];
                var col      = Palette[mi % Palette.Length];
                var pts      = new PointCollection();

                for (int i = 0; i < n; i++)
                {
                    if ((_filtered[i].CarModel ?? "") == model && inView[i])
                        pts.Add(new Point(xArr[i], yArr[i]));
                }

                if (pts.Count > 1)
                {
                    var poly = new Polyline
                    {
                        Points          = pts,
                        Stroke          = new SolidColorBrush(Color.FromArgb(140, col.R, col.G, col.B)),
                        StrokeThickness = 1.5,
                        StrokeLineJoin  = PenLineJoin.Round
                    };
                    _canvas.Children.Add(poly);
                }
            }

            // ── Dots + PB markers + hit-test registration ─────────────────────
            for (int mi = 0; mi < _models.Count; mi++)
            {
                string model = _models[mi];
                var col      = Palette[mi % Palette.Length];

                for (int i = 0; i < n; i++)
                {
                    if ((_filtered[i].CarModel ?? "") != model || !inView[i]) continue;

                    bool isBest = Math.Abs(_filtered[i].LapTime.TotalSeconds - _bestSec) < 0.001;

                    if (isBest)
                    {
                        // ★ Yellow star for the overall best lap
                        _canvas.Children.Add(CreateStar(xArr[i], yArr[i], 7.0, 3.0));
                    }
                    else
                    {
                        const double R = 3.0;
                        var dot = new Ellipse
                        {
                            Width  = R * 2,
                            Height = R * 2,
                            Fill   = new SolidColorBrush(col)
                        };
                        Canvas.SetLeft(dot, xArr[i] - R);
                        Canvas.SetTop(dot,  yArr[i] - R);
                        _canvas.Children.Add(dot);
                    }

                    // ▼ Green triangle at the bottom for each new PB
                    if (isPB[i])
                        _canvas.Children.Add(CreatePBMarker(xArr[i], MT + chartH + 2));

                    var cp = new ChartPoint { X = xArr[i], Y = yArr[i], Lap = _filtered[i] };
                    _pts.Add(cp);
                }
            }

            // ── Tooltip on top ────────────────────────────────────────────────
            if (_tooltip != null)
            {
                _tooltip.Visibility = Visibility.Collapsed;
                _canvas.Children.Add(_tooltip);
            }
        }

        // ─── Chart sub-elements ───────────────────────────────────────────────

        /// <summary>Alternating background bands, one per session.</summary>
        private void DrawSessionBands(double[] xArr, int n, double chartW, double chartH)
        {
            // Count distinct sessions first — no point drawing if only 1
            int sessCount = 0;
            string prev = null;
            for (int i = 0; i < n; i++)
            {
                string s = _filtered[i].SessionId ?? "";
                if (s != prev) { sessCount++; prev = s; }
            }
            if (sessCount < 2) return;

            bool shade = false;
            int i2 = 0;
            while (i2 < n)
            {
                string sessId = _filtered[i2].SessionId ?? "";
                int start = i2;
                while (i2 < n && (_filtered[i2].SessionId ?? "") == sessId) i2++;
                int end = i2 - 1;

                shade = !shade;
                if (!shade) continue;   // draw every other band

                double x1 = start == 0 ? ML
                           : (xArr[start] + xArr[start - 1]) * 0.5;
                double x2 = end == n - 1 ? ML + chartW
                           : (xArr[end] + xArr[end + 1]) * 0.5;

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width  = Math.Max(x2 - x1, 1),
                    Height = chartH,
                    Fill   = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
                };
                Canvas.SetLeft(rect, x1);
                Canvas.SetTop(rect,  MT);
                _canvas.Children.Add(rect);
            }
        }

        private void DrawYGrid(double chartW, double chartH, double span)
        {
            int steps = 5;
            for (int i = 0; i <= steps; i++)
            {
                double frac = (double)i / steps;
                double y    = MT + chartH * frac;
                double sec  = _maxSec - span * frac;

                var grid = new Line
                {
                    X1 = ML, X2 = ML + chartW,
                    Y1 = y,  Y2 = y,
                    Stroke          = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
                    StrokeThickness = 1
                };
                _canvas.Children.Add(grid);

                var lbl = new TextBlock
                {
                    Text      = BestLapRecord.FormatLapTime(TimeSpan.FromSeconds(sec)),
                    FontSize  = 8.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255))
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl,  y - 6);
                _canvas.Children.Add(lbl);
            }
        }

        private void DrawXLabels(double chartW, double chartH, int n)
        {
            if (n == 0) return;
            double yLbl   = MT + chartH + 5;
            bool hasDates = _filtered[0].RecordDate != DateTime.MinValue;

            var l1 = new TextBlock
            {
                Text      = hasDates ? _filtered[0].RecordDate.ToString("dd/MM/yy") : "1",
                FontSize  = 8.5,
                Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255))
            };
            Canvas.SetLeft(l1, ML);
            Canvas.SetTop(l1, yLbl);
            _canvas.Children.Add(l1);

            if (n > 1)
            {
                var l2 = new TextBlock
                {
                    Text      = hasDates
                        ? _filtered[n - 1].RecordDate.ToString("dd/MM/yy")
                        : n.ToString(),
                    FontSize  = 8.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255))
                };
                Canvas.SetLeft(l2, ML + chartW - 40);
                Canvas.SetTop(l2, yLbl);
                _canvas.Children.Add(l2);
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Small downward green triangle indicating a new PB at that point.</summary>
        private static Polygon CreatePBMarker(double cx, double topY)
        {
            var pts = new PointCollection();
            pts.Add(new Point(cx - 4.5, topY));
            pts.Add(new Point(cx + 4.5, topY));
            pts.Add(new Point(cx,       topY + 7));
            return new Polygon
            {
                Points          = pts,
                Fill            = new SolidColorBrush(Color.FromRgb(80, 220, 80)),
                Stroke          = new SolidColorBrush(Color.FromRgb(30, 140, 30)),
                StrokeThickness = 0.5
            };
        }

        /// <summary>Creates a 5-pointed yellow star centred on (cx, cy).</summary>
        private static Polygon CreateStar(double cx, double cy, double outerR, double innerR)
        {
            var pts = new PointCollection();
            for (int i = 0; i < 10; i++)
            {
                double angle = Math.PI * i / 5.0 - Math.PI / 2.0; // start from top
                double r     = (i % 2 == 0) ? outerR : innerR;
                pts.Add(new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }
            return new Polygon
            {
                Points          = pts,
                Fill            = new SolidColorBrush(Color.FromRgb(255, 220, 0)),
                Stroke          = new SolidColorBrush(Color.FromRgb(200, 130, 0)),
                StrokeThickness = 0.6
            };
        }

        private double SecToY(double sec, double chartH, double span)
        {
            return MT + chartH * (1.0 - (sec - _minSec) / span);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private void Line(double x1, double y1, double x2, double y2, Color c, double thickness)
        {
            var l = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke          = new SolidColorBrush(c),
                StrokeThickness = thickness
            };
            _canvas.Children.Add(l);
        }
    }
}
