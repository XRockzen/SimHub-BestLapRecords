using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>
    /// Work plan of a circuit: the three corners worth working on, each with the phase that
    /// costs time and the measure that explains it, against the driver's own best passage, a
    /// teammate or the best of the team.
    /// </summary>
    internal class CoachingView : UserControl
    {
        /// <summary>Open the telemetry on two laps, zoomed on a corner (from, to in metres).</summary>
        public event Action<List<TelemetryView.Preset>, double, double> TelemetryRequested;

        private static string _modeKey = "best";   // "best", "team" or a driver key

        private readonly List<DriverView> _drivers;
        private readonly string _circuit, _classLabel;
        private readonly DriverView _me;
        private readonly StackPanel _modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        private readonly Grid _body = new Grid();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public CoachingView(List<DriverView> drivers, string circuit, string classLabel)
        {
            _drivers = drivers; _circuit = circuit; _classLabel = classLabel;
            _me = drivers.Find(d => d.DbPath == null) ?? drivers[0];
            var root = new DockPanel();
            DockPanel.SetDock(_modes, Dock.Top);
            root.Children.Add(_modes);
            root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            BuildModes();
            Run();
        }

        private void BuildModes()
        {
            _modes.Children.Clear();
            _modes.Children.Add(Ui.T("Comparer à", 12.5, Ui.Muted));
            var options = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("best", "mon meilleur passage") };
            foreach (var d in _drivers) if (d != _me) options.Add(new KeyValuePair<string, string>(d.Key, d.Name));
            if (_drivers.Count > 2) options.Add(new KeyValuePair<string, string>("team", "le meilleur de l'équipe"));
            if (!options.Exists(o => o.Key == _modeKey)) _modeKey = "best";
            foreach (var o in options)
            {
                var key = o.Key;
                var b = Ui.Button(o.Value);
                b.Margin = new Thickness(8, 0, 0, 0);
                Ui.SetOn(b, _modeKey == key);
                b.Click += (s, e) => { _modeKey = key; BuildModes(); Run(); };
                _modes.Children.Add(b);
            }
        }

        // ─── Analysis ─────────────────────────────────────────────────────────

        private void Run()
        {
            _body.Children.Clear();
            _body.Children.Add(new TextBlock { Text = "Analyse des tours…", Foreground = Ui.Faint, FontStyle = FontStyles.Italic, FontSize = 13, Margin = new Thickness(4, 20, 0, 0) });
            var mode = _modeKey == "best" ? CoachingEngine.Mode.MyBest : _modeKey == "team" ? CoachingEngine.Mode.Team : CoachingEngine.Mode.Driver;
            var refDriver = _drivers.Find(d => d.Key == _modeKey);
            Task.Run(() =>
            {
                CoachingEngine.Result result = null;
                TrackModel model = null;
                try
                {
                    var mine = Laps(_me);
                    var others = new List<CoachingEngine.Lap>();
                    if (mode == CoachingEngine.Mode.Driver && refDriver != null) others = Laps(refDriver);
                    else if (mode == CoachingEngine.Mode.Team) foreach (var d in _drivers) if (d != _me) others.AddRange(Laps(d));
                    var known = new List<TeleLapRef>();
                    foreach (var d in _drivers) known.AddRange(TelemetryService.GetLaps(d, _circuit));
                    model = TelemetryService.TrackModelFor(_circuit, known);
                    if (model != null)
                    {
                        mine = Consistent(mine, model);
                        others = Consistent(others, model);
                    }
                    result = CoachingEngine.Analyze(model, mine, others, mode,
                        mode == CoachingEngine.Mode.Driver && refDriver != null ? refDriver.Name : "l'équipe");
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("[TeamPace] Work plan: " + ex);
                    result = new CoachingEngine.Result { Error = "Analyse impossible : " + ex.Message };
                }
                Dispatcher.BeginInvoke(new Action(() => Render(result, model, mode)));
            });
        }

        /// <summary>
        /// Clean laps with telemetry of a driver on this circuit and category, the 40 most recent.
        /// Only laps tied to a results file: their category and validity are known.
        /// </summary>
        private List<CoachingEngine.Lap> Laps(DriverView d)
        {
            var refs = TelemetryService.GetLaps(d, _circuit).FindAll(l =>
                l.IsClean && l.File.Version >= 2 && l.Session != null && l.XmlLap != null &&
                (_classLabel == null || LmuCategoryStyle.GetShortName(l.Session.CarClass) == _classLabel));
            refs.Sort((a, b) => b.Date.CompareTo(a.Date));
            if (refs.Count > 40) refs.RemoveRange(40, refs.Count - 40);
            var laps = new List<CoachingEngine.Lap>();
            foreach (var r in refs)
            {
                try { laps.Add(new CoachingEngine.Lap { Driver = d, Ref = r, Trace = TelemetryService.LoadTrace(r) }); }
                catch { }
            }
            return laps;
        }

        /// <summary>
        /// Laps whose recording matches the track: full length, and a recorded duration equal to
        /// the official time (a lap distance that jumped would shift every corner).
        /// </summary>
        private static List<CoachingEngine.Lap> Consistent(List<CoachingEngine.Lap> laps, TrackModel model)
        {
            return laps.FindAll(l =>
            {
                var t = l.Trace;
                return t.FirstDist < 10 && Math.Abs(t.DistAt(t.N - 1) - model.Length) < 40
                       && Math.Abs(t.Time[t.N - 1] - l.Ref.LapSec) < 0.6;
            });
        }

        // ─── Rendering ────────────────────────────────────────────────────────

        private void Render(CoachingEngine.Result r, TrackModel model, CoachingEngine.Mode mode)
        {
            _body.Children.Clear();
            _body.ColumnDefinitions.Clear();
            if (r.Error != null)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = r.Error + "\n\nLa télémétrie de LMU est extraite à la fin de chaque session et avec « ⟳ Tout mettre à jour ».",
                    Foreground = Ui.Muted, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 20, 0, 0), MaxWidth = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                });
                return;
            }
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

            var left = new StackPanel();
            left.Children.Add(Kpis(r, mode));

            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 8) };
            title.Children.Add(Ui.T(r.Priorities.Count > 0 ? "Tes " + r.Priorities.Count + " priorités" : "Aucune priorité nette", 16, Ui.Text, FontWeights.SemiBold));
            var hint = Ui.T("  ⓘ", 13, Ui.Faint);
            hint.ToolTip = "Classées par temps à gagner. Seuls les écarts qui se répètent sur plusieurs tours sont retenus : un tour raté ne compte pas.";
            title.Children.Add(hint);
            left.Children.Add(title);
            if (r.Priorities.Count == 0)
                left.Children.Add(Ui.T("Tes passages sont proches de la référence dans tous les virages.", 13, Ui.Muted));
            foreach (var p in r.Priorities) left.Children.Add(Card(p));

            left.Children.Add(AllCorners(r));
            Grid.SetColumn(left, 0);
            _body.Children.Add(left);

            var side = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var map = new TrackMap { Height = 300, ShowLegend = false, Cursor = Cursors.Arrow };
            if (model != null)
            {
                map.SetData(model, new List<TrackMap.Line>(), model.Length);
                map.SetView(0, model.Length);
                var marks = new List<KeyValuePair<double, string>>();
                foreach (var p in r.Priorities) marks.Add(new KeyValuePair<double, string>(p.Corner.Apex, p.Rank.ToString()));
                map.Highlights = marks;
            }
            side.Children.Add(Ui.Card(map, new Thickness(6)));
            var basis = new StackPanel();
            basis.Children.Add(Ui.T("Sur quoi c'est calculé", 13, Ui.Text, FontWeights.SemiBold));
            basis.Children.Add(new TextBlock
            {
                Text = string.Format("Tes {0} tours propres sur ce circuit{1}. Chaque virage est découpé en freinage, corde et sortie : on garde la phase qui coûte le plus et la mesure qui l'explique.",
                    r.MyLaps, mode == CoachingEngine.Mode.MyBest ? "" : string.Format(" et {0} tours de {1}", r.ReferenceLaps, r.ReferenceName)),
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Ui.Muted, Margin = new Thickness(0, 6, 0, 0),
            });
            var card = Ui.Card(basis);
            card.Margin = new Thickness(0, 10, 0, 0);
            side.Children.Add(card);
            Grid.SetColumn(side, 1);
            _body.Children.Add(side);
        }

        private UIElement Kpis(CoachingEngine.Result r, CoachingEngine.Mode mode)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            double gain = 0;
            foreach (var p in r.Priorities) gain += p.Gain;
            grid.Children.Add(Kpi("TON RECORD (télémétrie)", Fmt.LapSec(r.MyRecord), Ui.Green, "sur tes tours propres"));
            grid.Children.Add(Kpi("TON TOUR IDÉAL", double.IsNaN(r.Ideal) ? "—" : Fmt.LapSec(r.Ideal), Ui.Amber, "tes meilleurs virages mis bout à bout"));
            if (mode != CoachingEngine.Mode.MyBest)
                grid.Children.Add(Kpi(r.ReferenceName.ToUpperInvariant(), Fmt.LapSec(r.ReferenceRecord), Ui.Text,
                    r.ReferenceRecord < r.MyRecord ? "+" + Fmt.Seconds(TimeSpan.FromSeconds(r.MyRecord - r.ReferenceRecord)) + " devant toi"
                                                   : Fmt.Seconds(TimeSpan.FromSeconds(r.ReferenceRecord - r.MyRecord)) + " derrière toi"));
            grid.Children.Add(Kpi("À GAGNER (" + r.Priorities.Count + " PRIORITÉS)", string.Format(Inv, "−{0:0.00} s", gain), Ui.Text, "sur ton passage habituel"));
            return grid;
        }

        private static UIElement Kpi(string label, string value, Brush color, string sub)
        {
            var p = new StackPanel();
            p.Children.Add(Ui.T(label, 11, Ui.Muted));
            p.Children.Add(Ui.T(value, 19, color, FontWeights.SemiBold));
            p.Children.Add(Ui.T(sub, 11.5, Ui.Faint));
            var c = Ui.Card(p);
            c.Margin = new Thickness(0, 0, 8, 0);
            return c;
        }

        private UIElement Card(CoachingEngine.Priority p)
        {
            var panel = new StackPanel();
            var head = new DockPanel();
            var num = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Amber, Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = p.Rank.ToString(), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Ui.Frozen(Color.FromRgb(0x22, 0x23, 0x28)),
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(num, Dock.Left);
            head.Children.Add(num);
            var gain = Ui.T(string.Format(Inv, "−{0:0.00} s", p.Gain), 17, Ui.Amber, FontWeights.Bold);
            DockPanel.SetDock(gain, Dock.Right);
            head.Children.Add(gain);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Ui.T(string.Format(Inv, "{0} · {1} {2:0}°", p.Corner.Name, p.Corner.Left ? "gauche" : "droite", p.Corner.Angle), 16, Ui.Text, FontWeights.Bold));
            name.Children.Add(Ui.T("   " + p.Against, 12, Ui.Muted));
            head.Children.Add(name);
            panel.Children.Add(head);

            panel.Children.Add(new TextBlock { Text = p.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Ui.Text, Margin = new Thickness(0, 8, 0, 8) });

            var mid = new DockPanel();
            var phases = new StackPanel { Width = 270, Margin = new Thickness(0, 0, 16, 0) };
            var bars = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            var labels = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            double worst = Math.Max(0.01, Math.Max(p.PhaseLoss[0], Math.Max(p.PhaseLoss[1], p.PhaseLoss[2])));
            var names = new[] { "freinage", "corde", "sortie" };
            for (int i = 0; i < 3; i++)
            {
                double share = Math.Max(0, p.PhaseLoss[i]) / worst;
                var c = share > 0.66 ? Color.FromRgb(0xFF, 0x8A, 0x65) : share > 0.33 ? Color.FromRgb(0x8D, 0x6E, 0x63) : Color.FromRgb(0x3A, 0x3B, 0x42);
                bars.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(3), Background = Ui.Frozen(c), Margin = new Thickness(0, 0, 4, 0) });
                labels.Children.Add(Ui.T(string.Format(Inv, "{0} {1:0.00}", names[i], Math.Max(0, p.PhaseLoss[i])), 11, Ui.Muted));
            }
            phases.Children.Add(bars);
            phases.Children.Add(labels);
            phases.ToolTip = "Temps perdu dans chaque phase du virage";
            DockPanel.SetDock(phases, Dock.Left);
            mid.Children.Add(phases);
            var chips = new WrapPanel();
            foreach (var e in p.Evidence)
            {
                var bg = e.Verdict < 0 ? Color.FromArgb(46, 239, 138, 99) : e.Verdict > 0 ? Color.FromArgb(36, 102, 187, 106) : Color.FromArgb(30, 255, 255, 255);
                var fg = e.Verdict < 0 ? Color.FromRgb(0xF5, 0xB4, 0x9B) : e.Verdict > 0 ? Color.FromRgb(0x9F, 0xD3, 0xA2) : Color.FromRgb(0xD5, 0xD7, 0xDD);
                chips.Children.Add(new Border
                {
                    Background = Ui.Frozen(bg), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = e.Text, FontSize = 12, Foreground = Ui.Frozen(fg) },
                });
            }
            mid.Children.Add(chips);
            panel.Children.Add(mid);

            var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var go = Ui.Button("Voir dans la télémétrie →", "Ouvre les deux tours superposés, zoomés sur ce virage");
            go.Foreground = Ui.Link;
            DockPanel.SetDock(go, Dock.Right);
            go.Click += (s, e) => OpenTelemetry(p);
            foot.Children.Add(go);
            foot.Children.Add(Ui.T(p.Basis, 12, Ui.Faint));
            panel.Children.Add(foot);

            var card = Ui.Card(panel, new Thickness(14, 12, 14, 12));
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        private void OpenTelemetry(CoachingEngine.Priority p)
        {
            var presets = new List<TelemetryView.Preset>();
            foreach (var lap in new[] { p.Mine, p.Reference })
            {
                if (lap == null) continue;
                presets.Add(new TelemetryView.Preset
                {
                    DriverKey = lap.Driver.Key,
                    SessionFileId = lap.Ref.Session != null ? lap.Ref.Session.FileId : null,
                    LapNum = lap.Ref.Session != null ? lap.Ref.LapNum : 0,
                });
            }
            var h = TelemetryRequested;
            if (h != null) h(presets, p.Corner.Start - 120, p.Corner.End + 60);
        }

        private UIElement AllCorners(CoachingEngine.Result r)
        {
            var exp = new Expander
            {
                Header = Ui.T(string.Format("Tous les virages ({0})", r.Corners.Count), 13, Ui.Link),
                Margin = new Thickness(0, 4, 0, 0), Foreground = Ui.Text,
            };
            var table = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var widths = new[] { 150.0, 110, 110, 110, 100, 140 };
            Func<string[], Brush, bool, UIElement> row = (cells, color, head) =>
            {
                var g = new Grid { MinHeight = 26 };
                foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = Ui.T(cells[i], head ? 12 : 13, head ? Ui.Faint : (i == 4 ? color : Ui.Text));
                    t.Margin = new Thickness(6, 0, 6, 0);
                    Grid.SetColumn(t, i);
                    g.Children.Add(t);
                }
                return new Border { Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = head ? Ui.HeaderBg : Brushes.Transparent };
            };
            table.Children.Add(row(new[] { "Virage", "Ton meilleur", "Ton habituel", "Référence", "À gagner", "Phase" }, Ui.Text, true));
            foreach (var c in r.Corners)
                table.Children.Add(row(new[]
                {
                    string.Format(Inv, "{0} · {1} {2:0}°", c.Corner.Name, c.Corner.Left ? "G" : "D", c.Corner.Angle),
                    string.Format(Inv, "{0:0.000}", c.MyBest), string.Format(Inv, "{0:0.000}", c.MyMedian),
                    c.Reference > 0 ? string.Format(Inv, "{0:0.000}", c.Reference) : "—",
                    c.Gain > 0.005 ? string.Format(Inv, "−{0:0.00} s", c.Gain) : "—",
                    c.Phase ?? "",
                }, c.Gain > 0.1 ? Ui.Amber : Ui.Muted, false));
            exp.Content = table;
            return exp;
        }
    }
}
