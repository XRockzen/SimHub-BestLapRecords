using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>
    /// Endurance planner, in three tabs. Préparation: the race, then the plan as a timeline of
    /// stints (drag a stop to move it, drop a driver on a stint, click a stop to set it up) with
    /// one card per stop. Analyse de course: a race that was run, against the plan, stop by stop,
    /// and against the other teams. Données mesurées: pace, consumption, wear and pit times
    /// measured on the team's sessions, each replaceable by hand.
    /// </summary>
    internal class StrategyView : UserControl
    {
        /// <summary>Every car of a race, to show in full (the race analysis screen).</summary>
        public event Action<RaceField> FieldRequested;

        private static StrategyPlan _plan;   // the plan being edited, kept while browsing
        private static string _tab = "prep";
        private static string _raceId;       // race shown in the analysis tab
        private static int _selectedStop = -1;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Brush StopBrush = Ui.Frozen(Color.FromRgb(0xFF, 0xC8, 0x57));
        private static readonly Brush Late = Ui.Frozen(Color.FromRgb(0xEF, 0x53, 0x50));

        private readonly List<DriverView> _drivers;
        private StrategyEngine.Measures _measures;
        private readonly StackPanel _header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        private readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        private readonly StackPanel _body = new StackPanel();
        private bool _building;

        /// <summary>Opens on the analysis of a race (from a session's analysis).</summary>
        public static void ShowRace(LmuSessionRecord race)
        {
            _tab = "race";
            _raceId = race != null ? race.FileId : null;
        }

        public StrategyView(List<DriverView> drivers, string circuit, string classLabel)
        {
            _drivers = drivers;
            if (_plan == null) _plan = StrategyPlan.Default();
            _plan.Normalize();
            if (circuit != null && (circuit != _plan.Circuit || classLabel != _plan.ClassLabel))
            {
                // Another circuit: the team's latest plan for it, else a fresh one
                var saved = StrategyEngine.SavedPlans().Where(p => p.Circuit == circuit && p.ClassLabel == classLabel).OrderByDescending(p => p.UpdatedUtc).FirstOrDefault();
                if (saved != null) { _plan = saved; _plan.Normalize(); }
                else { _plan = StrategyPlan.Default(); _plan.Circuit = circuit; _plan.ClassLabel = classLabel; }
            }
            if (_plan.Circuit == null) PickDefaultCircuit();
            if (_plan.Drivers.Count == 0) foreach (var d in drivers) _plan.Drivers.Add(d.Name);

            var root = new DockPanel();
            DockPanel.SetDock(_header, Dock.Top);
            root.Children.Add(_header);
            DockPanel.SetDock(_tabs, Dock.Top);
            root.Children.Add(_tabs);
            root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
            Content = root;

            BuildHeader();
            Remeasure();
        }

        private void PickDefaultCircuit()
        {
            LmuSessionRecord last = null;
            foreach (var d in _drivers) foreach (var s in d.Sessions) if (last == null || s.SessionDate > last.SessionDate) last = s;
            if (last != null) { _plan.Circuit = last.TrackDisplayName; _plan.ClassLabel = LmuCategoryStyle.GetShortName(last.CarClass); }
        }

        private void Remeasure()
        {
            _measures = _plan.Circuit == null ? null : StrategyEngine.Measure(_drivers, _plan.Circuit, _plan.ClassLabel, _plan.TankL);
            Rebuild();
        }

        private void Rebuild()
        {
            _building = true;
            BuildTabs();
            _body.Children.Clear();
            if (_measures == null) _body.Children.Add(Ui.T("Choisissez un circuit.", 13, Ui.Muted));
            else
            {
                StrategyEngine.ApplyOverrides(_measures, _plan);
                if (_tab == "race") BuildRace();
                else if (_tab == "data") _body.Children.Add(Card(MeasuresTable()));
                else BuildPreparation();
            }
            _building = false;
        }

        private void BuildTabs()
        {
            _tabs.Children.Clear();
            foreach (var t in new[] { new[] { "prep", "Préparation" }, new[] { "race", "Analyse de course" }, new[] { "data", "Données mesurées" } })
            {
                var key = t[0];
                var b = new Button { Content = t[1], FontSize = 14.5, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand };
                b.SetResourceReference(StyleProperty, "TabBtn");
                b.BorderThickness = new Thickness(0, 0, 0, 2);
                b.BorderBrush = _tab == key ? Ui.Frozen(Color.FromRgb(0x21, 0x96, 0xF3)) : Brushes.Transparent;
                b.Foreground = _tab == key ? Brushes.White : Ui.Muted;
                b.FontWeight = _tab == key ? FontWeights.SemiBold : FontWeights.Normal;
                b.Background = Brushes.Transparent;
                b.Click += (s, e) => { _tab = key; Rebuild(); };
                _tabs.Children.Add(b);
            }
        }

        // ─── Header: saved plans ──────────────────────────────────────────────

        private void BuildHeader()
        {
            _header.Children.Clear();
            var plans = StrategyEngine.SavedPlans();
            var cb = new ComboBox { Width = 340, Height = 26, FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0), MaxDropDownHeight = 420,
                                    ToolTip = "Plans de l'équipe, enregistrés dans le dossier d'équipe" };
            cb.Items.Add(plans.Count > 0 ? string.Format("Plans de l'équipe ({0})…", plans.Count) : "Aucun plan enregistré");
            foreach (var p in plans)
                cb.Items.Add(new ComboBoxItem
                {
                    Tag = p,
                    Content = string.Format("{0}   ·   {1} le {2:dd/MM HH:mm}", p.Id,
                        string.IsNullOrEmpty(p.Author) ? "?" : p.Author, p.UpdatedUtc == DateTime.MinValue ? DateTime.Now : p.UpdatedUtc.ToLocalTime()),
                });
            cb.SelectedIndex = 0;
            cb.SelectionChanged += (s, e) =>
            {
                var item = cb.SelectedItem as ComboBoxItem;
                if (item == null) return;
                _plan = (StrategyPlan)item.Tag;
                _plan.Normalize();
                _selectedStop = -1;
                BuildHeader();
                Remeasure();
            };
            _header.Children.Add(cb);

            var name = new TextBox { Width = 240, Height = 26, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Text = _plan.Id ?? "", ToolTip = "Nom du plan" };
            _header.Children.Add(name);
            var save = Ui.Button("Enregistrer", "Enregistre le plan dans le dossier d'équipe : chaque coéquipier le retrouve dans la liste");
            save.Margin = new Thickness(8, 0, 0, 0);
            save.Click += (s, e) =>
            {
                var id = name.Text.Trim();
                if (id.Length == 0) id = string.Format("{0} · {1} h", _plan.Circuit, _plan.DurationMin / 60.0);
                _plan.Id = id;
                bool shared = StrategyEngine.Save(_plan);
                BuildHeader();
                var t = Ui.T(shared ? "✔ enregistré et partagé avec l'équipe" : "⚠ enregistré sur ce PC seulement : dossier d'équipe introuvable", 12, shared ? Ui.Green : Ui.Amber);
                t.Margin = new Thickness(10, 0, 0, 0);
                _header.Children.Add(t);
            };
            _header.Children.Add(save);
            if (!string.IsNullOrEmpty(_plan.Id) && plans.Exists(p => p.Id == _plan.Id))
            {
                var del = Ui.Button("Supprimer", "Supprime ce plan pour toute l'équipe");
                del.Margin = new Thickness(6, 0, 0, 0);
                del.Click += (s, e) => { StrategyEngine.Delete(_plan.Id); _plan.Id = null; BuildHeader(); };
                _header.Children.Add(del);
            }
            var fresh = Ui.Button("Nouveau plan");
            fresh.Margin = new Thickness(6, 0, 0, 0);
            fresh.Click += (s, e) =>
            {
                var c = _plan.Circuit; var k = _plan.ClassLabel;
                _plan = StrategyPlan.Default();
                _plan.Circuit = c; _plan.ClassLabel = k;
                foreach (var d in _drivers) _plan.Drivers.Add(d.Name);
                _selectedStop = -1;
                BuildHeader();
                Remeasure();
            };
            _header.Children.Add(fresh);
            if (!string.IsNullOrEmpty(_plan.Author) && _plan.Author != LmuSessionStore.PlayerName)
            {
                var by = Ui.T(string.Format("plan de {0} : vos changements l'enregistrent à votre nom pour toute l'équipe", _plan.Author), 12, Ui.Muted);
                by.Margin = new Thickness(12, 0, 0, 0);
                _header.Children.Add(by);
            }
        }

        // ═══ Préparation ══════════════════════════════════════════════════════

        private void BuildPreparation()
        {
            _body.Children.Add(RaceInputs());
            foreach (var name in _plan.Drivers)
                if (!_drivers.Exists(d => d.Name == name))
                    _body.Children.Add(Warning(string.Format("{0} fait partie de ce plan mais n'est pas affiché : ajoutez-le avec « + Ajouter un pilote… » pour l'inclure.", name)));

            var sim = StrategyEngine.Simulate(_plan, _measures, false);
            if (sim.Stints.Count == 0)
            {
                foreach (var x in sim.Warnings) _body.Children.Add(Warning(x));
                return;
            }
            if (_selectedStop >= sim.Stops.Count) _selectedStop = -1;

            _body.Children.Add(Kpis(sim));
            _body.Children.Add(Timeline(sim));
            foreach (var x in sim.Warnings) _body.Children.Add(Warning(x));
            _body.Children.Add(StopCards(sim));
        }

        private UIElement RaceInputs()
        {
            var wrap = new WrapPanel();
            var circuits = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _drivers)
                foreach (var s in d.Sessions) circuits.Add(s.TrackDisplayName + "  ·  " + LmuCategoryStyle.GetShortName(s.CarClass));
            var cb = new ComboBox { Height = 26, FontSize = 12.5, MaxDropDownHeight = 420, Width = 330 };
            foreach (var c in circuits) cb.Items.Add(c);
            cb.SelectedItem = _plan.Circuit + "  ·  " + _plan.ClassLabel;
            cb.SelectionChanged += (s, e) =>
            {
                if (_building || cb.SelectedItem == null) return;
                var parts = ((string)cb.SelectedItem).Split(new[] { "  ·  " }, StringSplitOptions.None);
                _plan.Circuit = parts[0]; _plan.ClassLabel = parts[1];
                _plan.Overrides.Clear();
                Dispatcher.BeginInvoke(new Action(Remeasure));
            };
            wrap.Children.Add(Field("Circuit · catégorie", cb));
            wrap.Children.Add(Field("Durée (h:mm)", Box(string.Format("{0}:{1:00}", _plan.DurationMin / 60, _plan.DurationMin % 60), v =>
            {
                var p = v.Split(':');
                int h, m = 0;
                if (!int.TryParse(p[0], out h) || (p.Length > 1 && !int.TryParse(p[1], out m))) return false;
                _plan.DurationMin = h * 60 + m;
                return _plan.DurationMin > 0;
            }, null, 70)));
            wrap.Children.Add(Field("Départ", Box(_plan.StartClock ?? "", v => { _plan.StartClock = v.Trim(); return true; }, "ex. 20:00, pour afficher l'heure de chaque arrêt", 70)));
            wrap.Children.Add(Field("Pneus pour la course", Box(_plan.TyresAvailable.ToString(), v => { int n; if (!int.TryParse(v, out n) || n < 4) return false; _plan.TyresAvailable = n; return true; }, "Nombre de pneus, ceux du départ compris (20 = 5 trains)", 60)));
            wrap.Children.Add(Field("Réservoir (L)", Box(_plan.TankL.ToString(Inv), v => { double n; if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out n) || n < 1) return false; _plan.TankL = n; return true; }, null, 60)));
            wrap.Children.Add(Field("Relais maxi (min)", Box(_plan.MaxStintMin > 0 ? _plan.MaxStintMin.ToString() : "", v => { int n = 0; if (v.Trim().Length > 0 && !int.TryParse(v, out n)) return false; _plan.MaxStintMin = n; return true; }, "vide = selon le réservoir et l'énergie", 60)));
            wrap.Children.Add(Field("Conduite mini / pilote (min)", Box(_plan.MinDriveMin > 0 ? _plan.MinDriveMin.ToString() : "", v => { int n = 0; if (v.Trim().Length > 0 && !int.TryParse(v, out n)) return false; _plan.MinDriveMin = n; return true; }, "vide = pas de règle", 60)));

            var team = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 6) };
            foreach (var d in _drivers)
            {
                var key = d.Name;
                var check = new CheckBox { IsChecked = _plan.Drivers.Contains(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Pilote de la course" };
                check.Checked += (s, e) => { if (_building) return; if (!_plan.Drivers.Contains(key)) _plan.Drivers.Add(key); Rebuild(); };
                check.Unchecked += (s, e) => { if (_building) return; _plan.Drivers.Remove(key); Rebuild(); };
                team.Children.Add(check);
                team.Children.Add(Ui.T(d.Name + "   ", 13, Ui.Light(d.Brush), FontWeights.SemiBold));
            }
            wrap.Children.Add(Field("Pilotes", team));
            var c2 = Card(wrap);
            c2.Margin = new Thickness(0, 0, 0, 10);
            return c2;
        }

        private UIElement Kpis(StrategyEngine.Simulation sim)
        {
            var kpis = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 10) };
            kpis.Children.Add(Kpi("TOURS PRÉVUS", sim.Laps.ToString()));
            kpis.Children.Add(Kpi("ARRÊTS", sim.Stops.Count.ToString()));
            kpis.Children.Add(Kpi("TEMPS AUX STANDS", Duration(sim.PitLoss)));
            kpis.Children.Add(Kpi("PNEUS UTILISÉS", string.Format("{0} / {1}", sim.TyresUsed, _plan.TyresAvailable)));
            var driving = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var k in _plan.Drivers)
            {
                var d = _drivers.Find(x => x.Name == k);
                double t; sim.Driving.TryGetValue(k, out t);
                if (d == null) continue;
                var v = Ui.T(Fmt.TotalTime(TimeSpan.FromSeconds(t)) + "  ", 17, Ui.Light(d.Brush), FontWeights.SemiBold);
                v.ToolTip = d.Name;
                driving.Children.Add(v);
            }
            var dk = new StackPanel();
            dk.Children.Add(Ui.T("CONDUITE", 11, Ui.Muted));
            dk.Children.Add(driving);
            var dkc = Ui.Card(dk); dkc.Margin = new Thickness(0, 0, 8, 0);
            kpis.Children.Add(dkc);
            int longest = sim.Stints.Max(s => s.Laps);
            kpis.Children.Add(Kpi("RELAIS LE PLUS LONG", longest + " tours"));
            return kpis;
        }

        // ─── Timeline: stints and stops, edited in place ──────────────────────

        private class Block { public Border Box; public StrategyEngine.StintPlan Stint; public int FirstLap; }

        private UIElement Timeline(StrategyEngine.Simulation sim)
        {
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var chips = new StackPanel { Orientation = Orientation.Horizontal };
            chips.Children.Add(Ui.T("Glisser sur un relais : ", 12, Ui.Muted));
            foreach (var k in _plan.Drivers)
            {
                var d = _drivers.Find(x => x.Name == k);
                if (d == null) continue;
                var chip = new Border
                {
                    BorderBrush = d.Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 2, 10, 2),
                    Margin = new Thickness(0, 0, 6, 0), Background = Ui.Tint(d.Brush, 40), Cursor = Cursors.SizeAll,
                    Child = Ui.T(d.Name, 12.5, Brushes.White, FontWeights.SemiBold), ToolTip = "Glisser ce pilote sur un relais pour qu'il le prenne",
                };
                var driverName = d.Name;
                chip.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragDrop.DoDragDrop(chip, driverName, DragDropEffects.Copy); };
                chips.Children.Add(chip);
            }
            DockPanel.SetDock(chips, Dock.Right);
            head.Children.Add(chips);
            head.Children.Add(Title("Déroulé de la course", null));
            panel.Children.Add(head);

            // Hour ticks
            var ticks = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            double total = Math.Max(1, sim.Finish);
            int hours = (int)Math.Floor(total / 3600);
            double used = 0;
            for (int h = 0; h <= hours; h++)
            {
                double w = h < hours ? 3600 : total - hours * 3600;
                ticks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, w), GridUnitType.Star) });
                var t = Ui.T(h + " h", 11.5, Ui.Faint);
                Grid.SetColumn(t, h);
                ticks.Children.Add(t);
                used += w;
            }
            panel.Children.Add(ticks);

            var line = new Grid { Height = 66 };
            var blocks = new List<Block>();
            int firstLap = 1;
            for (int i = 0; i < sim.Stints.Count; i++)
            {
                var st = sim.Stints[i];
                var d = _drivers.Find(x => x.Name == st.Driver);
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, st.End - st.Start), GridUnitType.Star) });
                var col = line.ColumnDefinitions.Count - 1;
                bool fresh = i == 0 || sim.Stops[i - 1].Tyres == 4;
                string tyres = i == 0 ? "pneus neufs" : sim.Stops[i - 1].Tyres == 4 ? "4 pneus neufs" : sim.Stops[i - 1].Tyres > 0 ? sim.Stops[i - 1].Tyres + " pneus neufs" : "mêmes pneus";
                var text = new StackPanel { Margin = new Thickness(9, 6, 6, 6) };
                text.Children.Add(Ui.T(d != null ? d.Name : st.Driver, 13.5, Brushes.White, FontWeights.Bold));
                text.Children.Add(Ui.T(string.Format("T{0}–T{1} · {2}", firstLap, firstLap + st.Laps - 1, Fmt.TotalTime(TimeSpan.FromSeconds(st.End - st.Start))), 12, Brushes.White));
                text.Children.Add(Ui.T(tyres, 11.5, Ui.Frozen(Color.FromArgb(220, 255, 255, 255))));
                var box = new Border
                {
                    Background = d != null ? d.Brush : Brushes.Gray, Child = text, AllowDrop = true, ClipToBounds = true,
                    CornerRadius = new CornerRadius(i == 0 ? 6 : 0, i == sim.Stints.Count - 1 ? 6 : 0, i == sim.Stints.Count - 1 ? 6 : 0, i == 0 ? 6 : 0),
                    ToolTip = string.Format("Relais {0} : {1} tours, de {2} à {3}\nClic : choisir le pilote · ou glisser un pilote dessus", i + 1, st.Laps, Clock(st.Start), Clock(st.End)),
                    Cursor = Cursors.Hand,
                };
                int stint = i;
                box.Drop += (s, e) =>
                {
                    var name = e.Data.GetData(typeof(string)) as string;
                    if (name != null) SetDriver(sim, stint, name);
                };
                box.MouseLeftButtonUp += (s, e) =>
                {
                    var menu = new ContextMenu();
                    foreach (var k in _plan.Drivers)
                    {
                        var dv = _drivers.Find(x => x.Name == k);
                        if (dv == null) continue;
                        var item = new MenuItem { Header = dv.Name, IsChecked = dv.Name == st.Driver };
                        var nm = dv.Name;
                        item.Click += (a, b) => SetDriver(sim, stint, nm);
                        menu.Items.Add(item);
                    }
                    menu.PlacementTarget = box;
                    menu.IsOpen = true;
                };
                Grid.SetColumn(box, col);
                line.Children.Add(box);
                blocks.Add(new Block { Box = box, Stint = st, FirstLap = firstLap });
                firstLap += st.Laps;

                if (i < sim.Stops.Count)
                {
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                    line.Children.Add(StopMarker(sim, i, blocks, line, line.ColumnDefinitions.Count - 1));
                }
            }
            panel.Children.Add(line);
            var c = Card(panel);
            c.Margin = new Thickness(0, 0, 0, 10);
            return c;
        }

        private void SetDriver(StrategyEngine.Simulation sim, int stint, string name)
        {
            var list = new List<string>();
            for (int k = 0; k < sim.Stints.Count; k++) list.Add(sim.Stints[k].Driver);
            list[stint] = name;
            _plan.StintDrivers = list;
            Dispatcher.BeginInvoke(new Action(Rebuild));
        }

        /// <summary>A stop between two stints: click to select it, drag it to move it to another lap.</summary>
        private UIElement StopMarker(StrategyEngine.Simulation sim, int index, List<Block> blocks, Grid line, int col)
        {
            var stop = sim.Stops[index];
            bool selected = index == _selectedStop;
            var label = new TextBlock { Text = (index + 1).ToString(), FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                        Foreground = selected ? Ui.Frozen(Color.FromRgb(0x22, 0x23, 0x28)) : StopBrush };
            var marker = new Border
            {
                Background = selected ? StopBrush : Ui.Frozen(Color.FromRgb(0x1B, 0x1C, 0x20)), BorderBrush = StopBrush,
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Child = label, Cursor = Cursors.SizeWE,
                ToolTip = string.Format(Inv, "Arrêt {0} · T{1} · {2}{3}\nGlisser pour le déplacer · clic pour le régler", index + 1, stop.Lap, Clock(stop.At), stop.LapChosen ? " · déplacé à la main" : ""),
            };
            Grid.SetColumn(marker, col);

            Point origin = new Point();
            bool dragging = false;
            int target = stop.Lap;
            marker.MouseLeftButtonDown += (s, e) => { origin = e.GetPosition(line); dragging = false; marker.CaptureMouse(); e.Handled = true; };
            marker.MouseMove += (s, e) =>
            {
                if (!marker.IsMouseCaptured) return;
                var pos = e.GetPosition(line);
                if (!dragging && Math.Abs(pos.X - origin.X) < 4) return;
                dragging = true;
                target = LapAt(blocks, line, pos.X);
                label.Text = "T" + target;
                label.FontSize = 10;
            };
            marker.MouseLeftButtonUp += (s, e) =>
            {
                if (!marker.IsMouseCaptured) return;
                marker.ReleaseMouseCapture();
                if (dragging && target > 0 && target != stop.Lap)
                {
                    while (_plan.StopLaps.Count <= index) _plan.StopLaps.Add(-1);
                    _plan.StopLaps[index] = target;
                    // The following stops find their place again from there
                    for (int k = index + 1; k < _plan.StopLaps.Count; k++) if (_plan.StopLaps[k] > 0 && _plan.StopLaps[k] <= target) _plan.StopLaps[k] = -1;
                }
                _selectedStop = index;
                Dispatcher.BeginInvoke(new Action(Rebuild));
                e.Handled = true;
            };
            return marker;
        }

        /// <summary>The lap under a horizontal position of the timeline.</summary>
        private static int LapAt(List<Block> blocks, Grid line, double x)
        {
            Block last = null;
            foreach (var b in blocks)
            {
                var left = b.Box.TranslatePoint(new Point(0, 0), line).X;
                if (x < left && last != null) return last.FirstLap + last.Stint.Laps - 1;
                if (x >= left && x <= left + b.Box.ActualWidth)
                {
                    double frac = (x - left) / Math.Max(1, b.Box.ActualWidth);
                    return Math.Max(1, b.FirstLap + (int)Math.Round(frac * b.Stint.Laps) - 1);
                }
                last = b;
            }
            return last != null ? last.FirstLap + last.Stint.Laps - 1 : 0;
        }

        // ─── One card per stop ────────────────────────────────────────────────

        private UIElement StopCards(StrategyEngine.Simulation sim)
        {
            if (sim.Stops.Count == 0) return Ui.T("Aucun arrêt nécessaire.", 13, Ui.Muted);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Min(4, sim.Stops.Count) };
            for (int i = 0; i < sim.Stops.Count; i++) grid.Children.Add(StopCard(sim, i));
            return grid;
        }

        private UIElement StopCard(StrategyEngine.Simulation sim, int i)
        {
            var s = sim.Stops[i];
            bool last = i == sim.Stops.Count - 1;
            var p = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var when = Ui.T(Clock(s.At), 12.5, Ui.Muted);
            DockPanel.SetDock(when, Dock.Right);
            head.Children.Add(when);
            head.Children.Add(Ui.T(string.Format("Arrêt {0} · T{1}", i + 1, s.Lap), 15, Ui.Text, FontWeights.Bold));
            p.Children.Add(head);

            p.Children.Add(Line(sim.EnergyMode ? "Énergie" : "Essence", LevelBox(i, s, last, sim.EnergyMode)));
            p.Children.Add(Line("Pneus", TyreButtons(i, s)));
            var prev = i < sim.Stints.Count ? sim.Stints[i].Driver : "";
            var next = _drivers.Find(x => x.Name == s.NextDriver);
            var relay = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            if (prev == s.NextDriver) relay.Inlines.Add(new System.Windows.Documents.Run("même pilote (" + prev + ")") { Foreground = Ui.Muted });
            else
            {
                relay.Inlines.Add(new System.Windows.Documents.Run(prev + " → ") { Foreground = Ui.Text });
                relay.Inlines.Add(new System.Windows.Documents.Run(s.NextDriver) { Foreground = next != null ? Ui.Light(next.Brush) : Ui.Text, FontWeight = FontWeights.Bold });
            }
            p.Children.Add(Line("Relais", relay));
            if (s.LapChosen)
            {
                var reset = Ui.T("déplacé à la main · remettre en automatique", 11.5, Ui.Link);
                reset.Cursor = Cursors.Hand;
                reset.MouseLeftButtonDown += (snd, e) =>
                {
                    if (i < _plan.StopLaps.Count) _plan.StopLaps[i] = -1;
                    Dispatcher.BeginInvoke(new Action(Rebuild));
                    e.Handled = true;
                };
                p.Children.Add(reset);
            }
            var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var lost = Ui.T(string.Format(Inv, "perdu +{0:0} s", s.Lost), 13, Ui.Red);
            lost.ToolTip = "Voie des stands comprise";
            DockPanel.SetDock(lost, Dock.Right);
            foot.Children.Add(lost);
            foot.Children.Add(Ui.T(string.Format(Inv, "arrêt {0:0} s", s.Stopped), 13, Ui.Text));
            p.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = foot, Margin = new Thickness(0, 4, 0, 0) });

            var card = new Border
            {
                Background = Ui.Panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 10, 10), Child = p,
                BorderBrush = i == _selectedStop ? StopBrush : Brushes.Transparent, BorderThickness = new Thickness(1),
            };
            card.MouseLeftButtonDown += (snd, e) => { if (_selectedStop != i) { _selectedStop = i; Dispatcher.BeginInvoke(new Action(Rebuild)); } };
            return card;
        }

        private static UIElement Line(string label, FrameworkElement value)
        {
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2), MinHeight = 26 };
            var l = Ui.T(label, 12.5, Ui.Muted);
            l.Width = 76;
            DockPanel.SetDock(l, Dock.Left);
            d.Children.Add(l);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            d.Children.Add(value);
            return d;
        }

        /// <summary>Tyres changed at a stop: auto, or 0, 2, 4 (1 and 3 from the menu).</summary>
        private FrameworkElement TyreButtons(int stop, StrategyEngine.StopPlan s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            Action<int> set = n =>
            {
                while (_plan.StopTyres.Count <= stop) _plan.StopTyres.Add(-1);
                _plan.StopTyres[stop] = n;
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };
            foreach (var n in new[] { -1, 0, 2, 4 })
            {
                int value = n;
                bool on = n == -1 ? !s.TyresChosen : s.TyresChosen && s.Tyres == n;
                var b = Ui.SmallButton(n == -1 ? (s.TyresChosen ? "auto" : "auto · " + s.Tyres) : n.ToString(),
                    n == -1 ? "Changés quand le train arrive en fin de vie" : n == 0 ? "Aucun pneu changé" : n + " pneus neufs");
                b.Margin = new Thickness(3, 0, 0, 0);
                b.MinWidth = 30;
                Ui.SetOn(b, on);
                b.Click += (snd, e) => set(value);
                row.Children.Add(b);
            }
            if (s.TyresChosen && (s.Tyres == 1 || s.Tyres == 3))
                row.Children.Add(Ui.T("  " + s.Tyres + " pneus", 12, Ui.Amber));
            return row;
        }

        /// <summary>
        /// The level asked for when leaving the pits (virtual energy %, litres without it), as in
        /// LMU's pit menu. Empty = automatic: full, or just what the last stint needs.
        /// </summary>
        private FrameworkElement LevelBox(int stop, StrategyEngine.StopPlan s, bool last, bool energy)
        {
            string unit = energy ? "%" : "L";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var added = Ui.T(string.Format(Inv, "+{0:0} {1}  ", energy ? s.EnergyPct : s.FuelL, unit), 12, Ui.Faint);
            added.ToolTip = energy ? string.Format(Inv, "Remis à l'arrêt : +{0:0} % d'énergie, environ {1:0} L d'essence", s.EnergyPct, s.FuelL) : "Remis à l'arrêt";
            row.Children.Add(added);
            var box = new TextBox
            {
                Width = 52, Height = 24, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right,
                Text = string.Format(Inv, "{0:0}", s.Level), FontWeight = s.FuelChosen ? FontWeights.Bold : FontWeights.Normal,
                BorderBrush = s.FuelChosen ? Ui.Amber : Ui.Frozen(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(s.FuelChosen ? 2 : 1),
                ToolTip = string.Format("Niveau à la sortie des stands, comme dans le menu des stands de LMU.\nVide = automatique : {0}.", last ? "juste ce qu'il faut jusqu'au drapeau" : "le plein"),
            };
            Action apply = () =>
            {
                if (_building) return;
                var text = (box.Text ?? "").Replace("%", "").Replace("L", "").Replace(',', '.').Trim();
                double v = -1;
                if (text.Length > 0 && (!double.TryParse(text, NumberStyles.Float, Inv, out v) || v < 0)) { box.BorderBrush = Ui.Red; return; }
                if (text.Length > 0 && !s.FuelChosen && Math.Abs(v - s.Level) < 0.5) return;   // the automatic value, untouched
                while (_plan.StopLevel.Count <= stop) _plan.StopLevel.Add(-1);
                _plan.StopLevel[stop] = v;
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };
            box.LostFocus += (snd, e) => apply();
            box.KeyDown += (snd, e) => { if (e.Key == Key.Enter) { apply(); Keyboard.ClearFocus(); } };
            row.Children.Add(box);
            row.Children.Add(Ui.T(" " + unit, 13, Ui.Muted));
            if (s.FuelChosen)
            {
                var reset = Ui.T("  auto", 12, Ui.Link);
                reset.Cursor = Cursors.Hand;
                reset.ToolTip = "Revenir au niveau automatique";
                reset.MouseLeftButtonDown += (snd, e) =>
                {
                    if (stop < _plan.StopLevel.Count) _plan.StopLevel[stop] = -1;
                    Dispatcher.BeginInvoke(new Action(Rebuild));
                    e.Handled = true;
                };
                row.Children.Add(reset);
            }
            return row;
        }


        /// <summary>Races run on this circuit and category by the displayed drivers, one per race.</summary>
        private List<LmuSessionRecord> Races()
        {
            var list = new List<LmuSessionRecord>();
            foreach (var d in _drivers)
                foreach (var s in d.Sessions)
                {
                    if (!s.IsRace || s.TrackDisplayName != _plan.Circuit || LmuCategoryStyle.GetShortName(s.CarClass) != _plan.ClassLabel) continue;
                    if (s.TotalLaps == 0 && s.CarLaps == 0) continue;   // joined but never drove
                    if (list.Exists(x => SessionMatch.Same(x, s))) continue;
                    list.Add(s);
                }
            list.Sort((a, b) => b.SessionDate.CompareTo(a.SessionDate));
            return list;
        }

        private static string RaceLabel(LmuSessionRecord r)
        {
            var row = new LmuSessionRow { Record = r };
            return string.Format("{0:dd/MM · HH:mm} · {1} · {2} tours", r.SessionDate, row.Position, r.CarLaps > 0 ? r.CarLaps : r.TotalLaps);
        }

        // ═══ Analyse de course ════════════════════════════════════════════════

        private void BuildRace()
        {
            var races = Races();
            if (races.Count == 0)
            {
                _body.Children.Add(Ui.T("Aucune course sur ce circuit dans cette catégorie pour les pilotes affichés.", 13, Ui.Muted));
                return;
            }
            var race = races.FirstOrDefault(r => r.FileId == _raceId) ?? races.FirstOrDefault(r => _drivers.Exists(d => { var m = d.FindMatch(r); return m != null && m.FileId == _raceId; })) ?? races[0];
            _raceId = race.FileId;

            var pick = new ComboBox { Width = 380, Height = 26, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var r in races) pick.Items.Add(new ComboBoxItem { Content = "Course du " + RaceLabel(r), Tag = r });
            foreach (ComboBoxItem it in pick.Items) if (it.Tag == race) pick.SelectedItem = it;
            pick.SelectionChanged += (s, e) =>
            {
                if (_building) return;
                _raceId = ((LmuSessionRecord)((ComboBoxItem)pick.SelectedItem).Tag).FileId;
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };
            _body.Children.Add(pick);

            // Every car of the race, from whichever driver's data has it
            RaceField field = null;
            var pits = new Dictionary<int, LmuLivePit>();
            foreach (var d in _drivers)
            {
                var mine = d.FindMatch(race);
                if (mine == null) continue;
                if (field == null) field = LmuSessionStore.GetField(mine.FileId, d.DbPath) ?? TeamRaceStore.FieldFor(mine);
                try
                {
                    var laps = LmuSessionStore.GetLapsForSession(mine.FileId, d.DbPath);
                    AnalysisColumn.MergeLiveData(mine, laps, d.DbPath);
                    foreach (var l in laps) if (l.LivePit != null && !l.LivePit.FromGarage && !pits.ContainsKey(l.LapNum)) pits[l.LapNum] = l.LivePit;
                }
                catch { }
            }
            if (field == null)
            {
                _body.Children.Add(Ui.T("Les données de toutes les voitures manquent pour cette course : cliquez sur « ⟳ Tout mettre à jour » (courses importées avant la version 3.5).", 13, Ui.Muted));
                return;
            }

            var sim = _plan.Drivers.Count > 0 ? StrategyEngine.Simulate(_plan, _measures, false) : null;
            if (sim != null && sim.Stints.Count == 0) sim = null;
            var review = StrategyReview.Build(field, sim, pits);
            if (review == null)
            {
                _body.Children.Add(Ui.T("Notre voiture n'est pas dans le résultat de cette course.", 13, Ui.Muted));
                return;
            }

            _body.Children.Add(RaceKpis(sim, review));
            _body.Children.Add(PlannedVsReal(sim, review));
            if (sim != null) _body.Children.Add(StopsReview(review));

            var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            bottom.Children.Add(Verdict(sim, review));
            var others = FieldSummary(field);
            Grid.SetColumn(others, 2);
            bottom.Children.Add(others);
            _body.Children.Add(bottom);
        }

        private UIElement RaceKpis(StrategyEngine.Simulation sim, StrategyReview r)
        {
            var kpis = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 10) };
            // delta = real − planned; worse when it grows for everything but the laps
            Func<string, string, string, double, UIElement> pair = (label, planned, real, delta) =>
            {
                var p = new StackPanel();
                p.Children.Add(Ui.T(label + (sim != null ? " · PRÉVU / RÉEL" : ""), 11, Ui.Muted));
                var v = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text };
                v.Inlines.Add(new System.Windows.Documents.Run(sim != null ? planned + " / " + real : real));
                bool worse = label == "TOURS" ? delta < 0 : delta > 0;
                if (sim != null && Math.Abs(delta) > 0.001)
                    v.Inlines.Add(new System.Windows.Documents.Run("  " + (delta > 0 ? "+" : "−") + DeltaText(label, Math.Abs(delta))) { FontSize = 13, Foreground = worse ? Ui.Red : Ui.Green });
                p.Children.Add(v);
                var c = Ui.Card(p); c.Margin = new Thickness(0, 0, 8, 0);
                return c;
            };
            int planLaps = sim != null ? sim.Laps : 0;
            kpis.Children.Add(pair("TOURS", planLaps.ToString(), r.Run.Laps.ToString(), sim != null ? r.Run.Laps - planLaps : 0));
            kpis.Children.Add(pair("ARRÊTS", sim != null ? sim.Stops.Count.ToString() : "", r.Stops.Count.ToString(), sim != null ? r.Stops.Count - sim.Stops.Count : 0));
            kpis.Children.Add(pair("TEMPS AUX STANDS", sim != null ? Duration(sim.PitLoss) : "", Duration(r.Run.PitLoss), sim != null ? r.Run.PitLoss - sim.PitLoss : 0));
            double planPace = sim != null && sim.Laps > 0 ? (sim.Finish - sim.PitLoss) / sim.Laps : 0;
            kpis.Children.Add(pair("RYTHME", sim != null ? Fmt.LapSec(planPace) : "", Fmt.LapSec(r.Run.Pace), sim != null ? r.Run.Pace - planPace : 0));

            var drv = new StackPanel();
            drv.Children.Add(Ui.T("TOURS PAR PILOTE", 11, Ui.Muted));
            var line = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) };
            foreach (var kv in r.LapsByDriver)
            {
                var d = DriverByName(kv.Key);
                line.Inlines.Add(new System.Windows.Documents.Run(kv.Key + " " + kv.Value + "   ") { Foreground = d != null ? Ui.Light(d.Brush) : Ui.Text });
            }
            drv.Children.Add(line);
            if (sim != null)
            {
                var planned = new Dictionary<string, int>();
                foreach (var st in sim.Stints) { int c; planned.TryGetValue(st.Driver, out c); planned[st.Driver] = c + st.Laps; }
                drv.Children.Add(Ui.T("prévu " + string.Join(" / ", planned.Select(kv => kv.Value.ToString())), 11.5, Ui.Faint));
            }
            var dc = Ui.Card(drv); dc.Margin = new Thickness(0, 0, 8, 0);
            kpis.Children.Add(dc);
            return kpis;
        }

        private static string DeltaText(string label, double v)
        {
            if (label == "TEMPS AUX STANDS") return Duration(v);
            if (label == "RYTHME") return v.ToString("0.00", Inv);
            return v.ToString("0", Inv);
        }

        private DriverView DriverByName(string name)
        {
            return _drivers.Find(d => TeamRace.SameDriver(d.Name, name));
        }

        /// <summary>Planned and real stints on the same time scale; a real stop is as wide as its cost.</summary>
        private UIElement PlannedVsReal(StrategyEngine.Simulation sim, StrategyReview r)
        {
            var p = new StackPanel();
            p.Children.Add(Title(sim != null ? "Prévu / réel" : "Déroulé réel", null));
            double total = Math.Max(r.Finish, sim != null ? sim.Finish : 0);

            Func<string, Grid, UIElement> row = (label, bar) =>
            {
                var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var l = Ui.T(label, 12.5, Ui.Muted);
                l.Width = 60;
                DockPanel.SetDock(l, Dock.Left);
                d.Children.Add(l);
                d.Children.Add(bar);
                return d;
            };
            Action<Grid, double, Brush, string, string> seg = (g, width, brush, text, tip) =>
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, width), GridUnitType.Star) });
                var b = new Border { Background = brush, ToolTip = tip, ClipToBounds = true };
                if (text != null) b.Child = Ui.T(text, 12, Brushes.White, FontWeights.SemiBold);
                if (text != null) ((TextBlock)b.Child).Margin = new Thickness(7, 0, 4, 0);
                Grid.SetColumn(b, g.ColumnDefinitions.Count - 1);
                g.Children.Add(b);
            };

            if (sim != null)
            {
                var g = new Grid { Height = 34 };
                double t = 0;
                for (int i = 0; i < sim.Stints.Count; i++)
                {
                    var st = sim.Stints[i];
                    var d = DriverByName(st.Driver);
                    seg(g, st.End - t, d != null ? d.Brush : Brushes.Gray, (d != null ? d.Name.Split(' ')[0] : st.Driver) + " · " + st.Laps + " t",
                        string.Format("Relais prévu {0} : {1} tours", i + 1, st.Laps));
                    t = st.End;
                    if (i < sim.Stops.Count)
                    {
                        seg(g, sim.Stops[i].Lost, StopBrush, null, string.Format(Inv, "Arrêt prévu {0} · T{1} · +{2:0} s", i + 1, sim.Stops[i].Lap, sim.Stops[i].Lost));
                        t += sim.Stops[i].Lost;
                    }
                }
                if (total > t) seg(g, total - t, Brushes.Transparent, null, null);
                p.Children.Add(row("Prévu", g));
            }
            {
                var g = new Grid { Height = 34 };
                double t = 0;
                var bad = new HashSet<StrategyReview.RealStop>(r.Rows.Where(x => x.Mood < 0 && x.Real != null).Select(x => x.Real));
                for (int i = 0; i < r.Stints.Count; i++)
                {
                    var st = r.Stints[i];
                    var d = DriverByName(st.Driver);
                    seg(g, st.End - t, d != null ? d.Brush : Brushes.Gray, (st.Driver ?? "?").Split(' ')[0] + " · " + st.Laps + " t",
                        string.Format("Relais {0} : {1}, T{2}–T{3}", i + 1, st.Driver, st.FromLap, st.ToLap));
                    t = st.End;
                    if (i < r.Stops.Count)
                    {
                        var stop = r.Stops[i];
                        seg(g, stop.Loss, bad.Contains(stop) ? Late : StopBrush, null, StrategyReview.Describe(stop, Clock));
                        t += stop.Loss;
                    }
                }
                if (total > t) seg(g, total - t, Brushes.Transparent, null, null);
                p.Children.Add(row("Réel", g));
            }
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(60, 4, 0, 0) };
            legend.Children.Add(new Border { Width = 10, Height = 10, Background = StopBrush, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 5, 0) });
            legend.Children.Add(Ui.T(sim != null ? "arrêt dans le plan    " : "arrêt    ", 12, Ui.Muted));
            if (sim != null)
            {
                legend.Children.Add(new Border { Width = 10, Height = 10, Background = Late, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 5, 0) });
                legend.Children.Add(Ui.T("arrêt hors plan ou trop long", 12, Ui.Muted));
            }
            p.Children.Add(legend);
            var c = Card(p);
            c.Margin = new Thickness(0, 0, 0, 10);
            return c;
        }

        private UIElement StopsReview(StrategyReview r)
        {
            var table = new StackPanel();
            var widths = new[] { 54.0, 2.2, 2.6, 80, 2.6 };
            Func<bool, Grid> grid = head =>
            {
                var g = new Grid { MinHeight = head ? 26 : 30 };
                foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 10 ? new GridLength(w) : new GridLength(w, GridUnitType.Star) });
                return g;
            };
            var h = grid(true);
            var names = new[] { "Arrêt", "Prévu", "Réel", "Écart", "Constat" };
            for (int i = 0; i < names.Length; i++)
            {
                var t = Ui.T(names[i], 12, Ui.Faint);
                t.Margin = new Thickness(10, 0, 10, 0);
                if (i == 3) t.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(t, i);
                h.Children.Add(t);
            }
            table.Children.Add(new Border { Child = h, Background = Ui.HeaderBg });
            foreach (var row in r.Rows)
            {
                var g = grid(false);
                var planned = row.Planned != null
                    ? string.Format(Inv, "T{0} · {1} · {2} · {3} · +{4:0} s", row.Planned.Lap, Clock(row.Planned.At),
                        row.Planned.Full ? "plein" : string.Format(Inv, "+{0:0} {1}", row.Planned.EnergyPct > 0 ? row.Planned.EnergyPct : row.Planned.FuelL, row.Planned.EnergyPct > 0 ? "%" : "L"),
                        row.Planned.Tyres == 0 ? "0 pneu" : row.Planned.Tyres + " pneus", row.Planned.Lost)
                    : "pas d'arrêt prévu";
                var real = row.Real != null ? StrategyReview.Describe(row.Real, Clock) : "pas d'arrêt";
                if (row.Real != null && row.Real.Pit != null && row.Real.Pit.LaneSec > 0)
                    real += string.Format(Inv, " · voie {0:0} s", row.Real.Pit.LaneSec);
                Brush mood = row.Mood > 0 ? Ui.Green : row.Mood < 0 ? Ui.Red : Ui.Muted;
                var cells = new[]
                {
                    row.PlannedIndex >= 0 ? (row.PlannedIndex + 1).ToString() : "+",
                    planned, real,
                    string.Format(Inv, "{0}{1:0} s", row.Delta >= 0 ? "+" : "−", Math.Abs(row.Delta)),
                    row.Verdict,
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = Ui.T(cells[i], 13, i == 1 && row.Planned == null ? Ui.Faint : i >= 3 ? mood : Ui.Text, i == 0 ? FontWeights.Bold : FontWeights.Normal);
                    t.Margin = new Thickness(10, 0, 10, 0);
                    t.ToolTip = cells[i];
                    if (i == 3) t.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(t, i);
                    g.Children.Add(t);
                }
                table.Children.Add(new Border
                {
                    Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = row.Mood < 0 ? Ui.Frozen(Color.FromArgb(26, 0xEF, 0x53, 0x50)) : Brushes.Transparent,
                });
            }
            var c = new Border { Background = Ui.Panel, CornerRadius = new CornerRadius(8), Child = table, ClipToBounds = true };
            return c;
        }

        private UIElement Verdict(StrategyEngine.Simulation sim, StrategyReview r)
        {
            var p = new StackPanel();
            p.Children.Add(Title("Bilan", null));
            Action<string> say = text => p.Children.Add(new TextBlock { Text = text, FontSize = 13.5, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            if (sim == null)
            {
                say(string.Format(Inv, "{0} tours, {1} arrêts, {2} aux stands. Préparez un plan pour comparer.", r.Run.Laps, r.Stops.Count, Duration(r.Run.PitLoss)));
                return Card(p);
            }
            double pace = r.Run.Pace > 0 ? r.Run.Pace : 100;
            int missing = sim.Laps - r.Run.Laps;
            var causes = new List<string>();
            var unplanned = r.Rows.Where(x => x.PlannedIndex < 0 && x.Real != null).ToList();
            foreach (var u in unplanned) causes.Add(string.Format(Inv, "l'arrêt imprévu du tour {0} (≈ {1:0.0} tour{2})", u.Real.Lap, u.Real.Loss / pace, u.Real.Loss / pace >= 2 ? "s" : ""));
            foreach (var o in r.Rows.Where(x => x.PlannedIndex >= 0 && x.Real != null && x.Delta > 20))
                causes.Add(string.Format(Inv, "l'arrêt {0} trop long (≈ {1:0.0} tour)", o.PlannedIndex + 1, o.Delta / pace));
            if (missing > 0)
                say(string.Format("{0} tour{1} de moins que prévu{2}.", missing, missing > 1 ? "s" : "", causes.Count > 0 ? " : " + string.Join(", ", causes) : ""));
            else if (missing < 0) say(string.Format("{0} tour{1} de plus que prévu.", -missing, missing < -1 ? "s" : ""));
            else say("Le nombre de tours prévu est tenu.");
            var ok = r.Rows.Where(x => x.PlannedIndex >= 0 && x.Mood > 0).Select(x => (x.PlannedIndex + 1).ToString()).ToList();
            if (ok.Count > 0) say(string.Format("Arrêt{0} {1} dans le plan.", ok.Count > 1 ? "s" : "", string.Join(", ", ok)));
            double planPace = sim.Laps > 0 ? (sim.Finish - sim.PitLoss) / sim.Laps : 0;
            if (planPace > 0)
            {
                double d = r.Run.Pace - planPace;
                say(string.Format(Inv, "Rythme : {0}{1:0.00} s au tour sur le prévu, soit ≈ {2:0} s sur la course.", d >= 0 ? "+" : "−", Math.Abs(d), Math.Abs(d) * r.Run.Laps));
            }
            return Card(p);
        }

        /// <summary>What our car lost against the class winner, with the way to every car.</summary>
        private UIElement FieldSummary(RaceField field)
        {
            var p = new StackPanel();
            var head = new DockPanel();
            var all = Ui.T("Toutes les voitures →", 13, Ui.Link);
            all.Cursor = Cursors.Hand;
            all.MouseLeftButtonDown += (s, e) => { var h = FieldRequested; if (h != null) h(field); };
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
            var runs = RaceFieldAnalysis.RunAll(field);
            var us = runs.FirstOrDefault(x => x.Car.IsUs);
            var cls = us != null ? us.Car.CarClass : null;
            head.Children.Add(Title("Face aux autres équipes", LmuCategoryStyle.GetShortName(cls)));
            p.Children.Add(head);
            var winner = runs.Where(x => x.Car.CarClass == cls && x != us && x.ClassPosition > 0).OrderBy(x => x.ClassPosition).FirstOrDefault();
            if (us == null || winner == null) { p.Children.Add(Ui.T("Pas de comparaison possible.", 12.5, Ui.Muted)); return Card(p); }

            var items = new[]
            {
                Tuple.Create("Rythme", (us.Pace - winner.Pace) * us.Laps),
                Tuple.Create("Arrêts", us.PitLoss - winner.PitLoss),
                Tuple.Create("Hors arrêts", us.OtherLoss - winner.OtherLoss),
            };
            double max = Math.Max(30, items.Max(i => Math.Abs(i.Item2)));
            foreach (var it in items)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.Children.Add(Ui.T(it.Item1, 13));
                var track = new Grid { Height = 12, Margin = new Thickness(4, 0, 8, 0) };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Abs(it.Item2) / max, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Abs(it.Item2) / max, GridUnitType.Star) });
                track.Children.Add(new Border { Background = it.Item2 > 0 ? Ui.Red : Ui.Green, CornerRadius = new CornerRadius(3) });
                Grid.SetColumn(track, 1);
                g.Children.Add(track);
                var v = Ui.T((it.Item2 > 0 ? "+" : "−") + Duration(Math.Abs(it.Item2)), 13, it.Item2 > 0 ? Ui.Red : Ui.Green, FontWeights.SemiBold);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(v, 2);
                g.Children.Add(v);
                p.Children.Add(g);
            }
            var top = runs.Where(x => x.Car.CarClass == cls && x != us && x.ClassPosition > 0 && x.ClassPosition <= 5).ToList();
            if (top.Count > 0)
            {
                var laps = top.SelectMany(x => x.Stops).Select(x => x.Lap).OrderBy(x => x).ToList();
                p.Children.Add(new TextBlock
                {
                    Text = string.Format("face au vainqueur ({0}) · les 5 premiers : {1} arrêts en moyenne, {2:0} s aux stands",
                        winner.Label, top.Average(x => x.Stops.Count).ToString("0.#", Inv), top.Average(x => x.PitLoss)),
                    FontSize = 12, Foreground = Ui.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                });
            }
            return Card(p);
        }

        // ═══ Données mesurées ═════════════════════════════════════════════════

        private UIElement MeasuresTable()
        {
            var panel = new StackPanel();
            panel.Children.Add(Title("Données de l'équipe", "mesurées sur vos sessions · cliquez une valeur pour la remplacer"));
            var drivers = _measures.Drivers.FindAll(d => _plan.Drivers.Contains(d.Driver.Name));

            Func<Grid> newRow = () =>
            {
                var g = new Grid { MinHeight = 30 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                foreach (var d in drivers) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                return g;
            };
            var header = newRow();
            header.Children.Add(Ui.T("Mesure", 12, Ui.Faint));
            for (int i = 0; i < drivers.Count; i++)
            {
                var t = Ui.T(drivers[i].Driver.Name, 12, Ui.Light(drivers[i].Driver.Brush), FontWeights.SemiBold);
                Grid.SetColumn(t, i + 1);
                header.Children.Add(t);
            }
            var src = Ui.T("Source", 12, Ui.Faint);
            Grid.SetColumn(src, drivers.Count + 1);
            header.Children.Add(src);
            panel.Children.Add(header);

            foreach (var p in StrategyEngine.DriverParams)
            {
                var g = newRow();
                g.Children.Add(Ui.T(StrategyEngine.Label(p), 13));
                string source = null;
                for (int i = 0; i < drivers.Count; i++)
                {
                    var v = drivers[i].Values[p];
                    var cell = ValueBox(p, drivers[i].Driver.Name, v);
                    Grid.SetColumn(cell, i + 1);
                    g.Children.Add(cell);
                    if (source == null) source = v.Overridden ? "modifié à la main" : v.Source;
                }
                var s = Ui.T(source ?? "", 11.5, Ui.Faint);
                s.ToolTip = source;
                Grid.SetColumn(s, drivers.Count + 1);
                g.Children.Add(s);
                panel.Children.Add(new Border { Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            foreach (var p in StrategyEngine.TeamParams)
            {
                var g = newRow();
                g.Children.Add(Ui.T(StrategyEngine.Label(p), 13));
                var v = _measures.Team[p];
                var cell = ValueBox(p, "", v);
                Grid.SetColumn(cell, 1);
                Grid.SetColumnSpan(cell, Math.Max(1, drivers.Count));
                g.Children.Add(cell);
                var source = v.Overridden ? "modifié à la main" : "équipe · " + v.Source;
                if (p == StrategyEngine.FuelRate && drivers.Count > 0)
                {
                    var dv = drivers[0].Values;
                    double ve = dv[StrategyEngine.Energy].Value, fuel = dv[StrategyEngine.Fuel].Value;
                    if (ve > 0 && fuel > 0)
                        source = string.Format(Inv, "≈ {0:0.0} % d'énergie par seconde ({1} : {2:0.00} L par %)  ·  ", v.Value / (fuel / ve), drivers[0].Driver.Name, fuel / ve) + source;
                }
                var s = Ui.T(source, 11.5, Ui.Faint);
                Grid.SetColumn(s, drivers.Count + 1);
                g.Children.Add(s);
                panel.Children.Add(new Border { Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            return panel;
        }

        /// <summary>A measured value shown as text; typing replaces it, emptying the box restores the measure.</summary>
        private FrameworkElement ValueBox(string param, string driverKey, StrategyValue v)
        {
            var box = new TextBox
            {
                Text = Editable(param, v.Value), Width = 110, Height = 24, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center, Background = Ui.Frozen(Color.FromArgb(40, 0, 0, 0)),
                Foreground = v.Overridden ? Ui.Amber : (v.Measured ? Ui.Text : Ui.Muted),
                BorderBrush = v.Overridden ? Ui.Amber : Ui.Frozen(Color.FromArgb(50, 255, 255, 255)),
                ToolTip = (v.Measured ? "Mesuré : " : "Valeur par défaut : ") + StrategyEngine.Format(param, v.Value) + "\nEffacez pour revenir à la mesure.",
            };
            box.LostFocus += (s, e) =>
            {
                var text = box.Text.Trim();
                _plan.Overrides.RemoveAll(o => o.Param == param && o.DriverName == driverKey);
                if (text.Length > 0 && text != Editable(param, v.Value))
                {
                    double value;
                    if (TryParse(param, text, out value)) _plan.Overrides.Add(new StrategyOverride { Param = param, DriverName = driverKey, Value = value });
                }
                else if (text.Length > 0 && v.Overridden)
                    _plan.Overrides.Add(new StrategyOverride { Param = param, DriverName = driverKey, Value = v.Value });
                Remeasure();
            };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            return box;
        }

        private static string Editable(string param, double v)
        {
            if (param == StrategyEngine.Pace) return Fmt.LapSec(v);
            if (param == StrategyEngine.TyreLife || param == StrategyEngine.LaneLoss || param == StrategyEngine.Tyres4) return v.ToString("0", Inv);
            return v.ToString(param == StrategyEngine.Degradation ? "0.000" : "0.00", Inv);
        }

        private static bool TryParse(string param, string text, out double v)
        {
            text = text.Replace(',', '.');
            if (param == StrategyEngine.Pace && text.Contains(":"))
            {
                var p = text.Split(':');
                double m, s;
                v = 0;
                if (!double.TryParse(p[0], NumberStyles.Float, Inv, out m) || !double.TryParse(p[1], NumberStyles.Float, Inv, out s)) return false;
                v = m * 60 + s;
                return v > 0;
            }
            return double.TryParse(text, NumberStyles.Float, Inv, out v) && v >= 0;
        }

        // ─── Small widgets ────────────────────────────────────────────────────

        /// <summary>Race time, or the clock time when a start time is given.</summary>
        private string Clock(double raceSec)
        {
            TimeSpan start;
            if (!string.IsNullOrEmpty(_plan.StartClock) && TimeSpan.TryParse(_plan.StartClock, Inv, out start))
                return (start + TimeSpan.FromSeconds(raceSec)).ToString(@"hh\:mm");
            var t = TimeSpan.FromSeconds(Math.Max(0, raceSec));
            return string.Format("{0}h{1:00}", (int)t.TotalHours, t.Minutes);
        }

        private static string Duration(double sec)
        {
            var t = TimeSpan.FromSeconds(Math.Abs(sec));
            return t.TotalMinutes >= 1 ? string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds) : string.Format("{0} s", t.Seconds);
        }

        private static UIElement Title(string title, string sub)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            p.Children.Add(Ui.T(title, 14.5, Ui.Text, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(sub)) p.Children.Add(Ui.T("   " + sub, 12, Ui.Muted));
            return p;
        }

        private static UIElement Warning(string text)
        {
            return new TextBlock { Text = "⚠ " + text, Foreground = Ui.Amber, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        }

        private static Border Card(UIElement child) { return Ui.Card(child, new Thickness(14, 12, 14, 12)); }

        private static UIElement Kpi(string label, string value)
        {
            var p = new StackPanel();
            p.Children.Add(Ui.T(label, 11, Ui.Muted));
            p.Children.Add(Ui.T(value, 20, Ui.Text, FontWeights.SemiBold));
            var c = Ui.Card(p); c.Margin = new Thickness(0, 0, 8, 0);
            return c;
        }

        private static UIElement Field(string label, FrameworkElement input)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 18, 6) };
            p.Children.Add(Ui.T(label, 11.5, Ui.Muted));
            input.Margin = new Thickness(0, 3, 0, 0);
            p.Children.Add(input);
            return p;
        }

        private TextBox Box(string text, Func<string, bool> apply, string tooltip, double width)
        {
            var b = new TextBox { Text = text, Height = 26, Width = width, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tooltip };
            b.LostFocus += (s, e) =>
            {
                if (apply(b.Text)) { b.BorderBrush = Ui.Frozen(Color.FromArgb(50, 255, 255, 255)); Dispatcher.BeginInvoke(new Action(Rebuild)); }
                else b.BorderBrush = Ui.Red;
            };
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            return b;
        }
    }
}
