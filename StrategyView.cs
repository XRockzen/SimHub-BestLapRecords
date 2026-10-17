using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>
    /// Endurance planner. The race and the team are entered; pace, consumption, wear and pit
    /// times come from the team's sessions on that circuit and category (each one can be replaced
    /// by hand); the plan is simulated stint by stint and each stint's driver can be changed.
    /// </summary>
    internal class StrategyView : UserControl
    {
        private static StrategyPlan _plan;   // the plan being edited, kept while browsing
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly List<DriverView> _drivers;
        private StrategyEngine.Measures _measures;
        private readonly StackPanel _left = new StackPanel { Width = 330 };
        private readonly StackPanel _right = new StackPanel();
        private readonly StackPanel _header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        private bool _building;

        public StrategyView(List<DriverView> drivers, string circuit, string classLabel)
        {
            _drivers = drivers;
            if (_plan == null) _plan = StrategyPlan.Default();
            if (circuit != null) { _plan.Circuit = circuit; _plan.ClassLabel = classLabel; }
            if (_plan.Circuit == null) PickDefaultCircuit();
            if (_plan.Drivers.Count == 0) foreach (var d in drivers) _plan.Drivers.Add(d.Name);

            var root = new DockPanel();
            DockPanel.SetDock(_header, Dock.Top);
            root.Children.Add(_header);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(344) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var leftScroll = new ScrollViewer { Content = _left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            grid.Children.Add(leftScroll);
            var rightScroll = new ScrollViewer { Content = _right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(rightScroll, 1);
            grid.Children.Add(rightScroll);
            root.Children.Add(grid);
            Content = root;

            BuildHeader();
            Remeasure();
        }

        private void PickDefaultCircuit()
        {
            // The most recent circuit driven by the displayed drivers
            LmuSessionRecord last = null;
            foreach (var d in _drivers) foreach (var s in d.Sessions) if (last == null || s.SessionDate > last.SessionDate) last = s;
            if (last != null) { _plan.Circuit = last.TrackDisplayName; _plan.ClassLabel = LmuCategoryStyle.GetShortName(last.CarClass); }
        }

        private void Remeasure()
        {
            _measures = _plan.Circuit == null ? null
                : StrategyEngine.Measure(_drivers, _plan.Circuit, _plan.ClassLabel, _plan.TankL);
            Rebuild();
        }

        private void Rebuild()
        {
            _building = true;
            BuildLeft();
            BuildRight();
            _building = false;
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
                BuildHeader();
                Remeasure();
            };
            _header.Children.Add(cb);

            var name = new TextBox { Width = 240, Height = 26, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, Text = _plan.Id ?? "", ToolTip = "Nom du plan" };
            _header.Children.Add(name);
            var save = Ui.Button("Enregistrer le plan", "Enregistre le plan dans le dossier d'équipe : chaque coéquipier le retrouve dans la liste");
            save.Margin = new Thickness(8, 0, 0, 0);
            var status = Ui.T("", 12, Ui.Muted);
            status.Margin = new Thickness(10, 0, 0, 0);
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

        // ─── Left: the race and the team ──────────────────────────────────────

        private void BuildLeft()
        {
            _left.Children.Clear();
            var race = new StackPanel();
            race.Children.Add(Title("La course", "à saisir"));

            var circuits = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _drivers)
                foreach (var s in d.Sessions) circuits.Add(s.TrackDisplayName + "  ·  " + LmuCategoryStyle.GetShortName(s.CarClass));
            var cb = new ComboBox { Height = 26, FontSize = 12.5, MaxDropDownHeight = 420 };
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
            race.Children.Add(Field("Circuit · catégorie", cb));
            race.Children.Add(Field("Durée (h:mm)", Box(string.Format("{0}:{1:00}", _plan.DurationMin / 60, _plan.DurationMin % 60), v =>
            {
                var p = v.Split(':');
                int h, m = 0;
                if (!int.TryParse(p[0], out h) || (p.Length > 1 && !int.TryParse(p[1], out m))) return false;
                _plan.DurationMin = h * 60 + m;
                return _plan.DurationMin > 0;
            })));
            race.Children.Add(Field("Heure de départ", Box(_plan.StartClock ?? "", v => { _plan.StartClock = v.Trim(); return true; }, "ex. 20:00, pour afficher l'heure de chaque arrêt")));
            race.Children.Add(Field("Jeux de pneus", Box(_plan.TyreSets.ToString(), v => { int n; if (!int.TryParse(v, out n) || n < 1) return false; _plan.TyreSets = n; return true; })));
            race.Children.Add(Field("Réservoir (L)", Box(_plan.TankL.ToString(Inv), v => { double n; if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out n) || n < 1) return false; _plan.TankL = n; return true; })));
            race.Children.Add(Field("Relais maxi (min)", Box(_plan.MaxStintMin > 0 ? _plan.MaxStintMin.ToString() : "", v => { int n = 0; if (v.Trim().Length > 0 && !int.TryParse(v, out n)) return false; _plan.MaxStintMin = n; return true; }, "vide = selon le réservoir")));
            race.Children.Add(Field("Conduite mini / pilote (min)", Box(_plan.MinDriveMin > 0 ? _plan.MinDriveMin.ToString() : "", v => { int n = 0; if (v.Trim().Length > 0 && !int.TryParse(v, out n)) return false; _plan.MinDriveMin = n; return true; }, "vide = pas de règle")));
            _left.Children.Add(Card(race));

            var team = new StackPanel();
            team.Children.Add(Title("L'équipe", "pilotes de la course"));
            foreach (var d in _drivers)
            {
                var key = d.Name;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var check = new CheckBox { IsChecked = _plan.Drivers.Contains(key), VerticalAlignment = VerticalAlignment.Center };
                check.Checked += (s, e) => { if (!_plan.Drivers.Contains(key)) _plan.Drivers.Add(key); Rebuild(); };
                check.Unchecked += (s, e) => { _plan.Drivers.Remove(key); Rebuild(); };
                row.Children.Add(check);
                row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = d.Brush, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(Ui.T(d.Name, 13));
                team.Children.Add(row);
            }
            team.Children.Add(new TextBlock
            {
                Text = "Ajoutez un pilote avec « + Ajouter un pilote… » en haut de l'écran.", FontSize = 11.5, Foreground = Ui.Faint,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            });
            var card = Card(team);
            card.Margin = new Thickness(0, 10, 0, 0);
            _left.Children.Add(card);
        }

        // ─── Right: measures, plan, stops ─────────────────────────────────────

        private void BuildRight()
        {
            _right.Children.Clear();
            _right.Margin = new Thickness(14, 0, 0, 0);
            if (_measures == null)
            {
                _right.Children.Add(Ui.T("Choisissez un circuit.", 13, Ui.Muted));
                return;
            }
            StrategyEngine.ApplyOverrides(_measures, _plan);
            foreach (var name in _plan.Drivers)
                if (!_drivers.Exists(d => d.Name == name))
                    _right.Children.Add(new TextBlock
                    {
                        Text = string.Format("⚠ {0} fait partie de ce plan mais n'est pas affiché : ajoutez-le avec « + Ajouter un pilote… » pour l'inclure.", name),
                        Foreground = Ui.Amber, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                    });
            _right.Children.Add(Card(MeasuresTable()));

            var sim = StrategyEngine.Simulate(_plan, _measures, false);
            if (sim.Stints.Count == 0)
            {
                var w = new StackPanel();
                foreach (var x in sim.Warnings) w.Children.Add(new TextBlock { Text = "⚠ " + x, Foreground = Ui.Amber, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
                var wc = Card(w); wc.Margin = new Thickness(0, 10, 0, 0);
                _right.Children.Add(wc);
                return;
            }
            var simB = StrategyEngine.Simulate(_plan, _measures, true);

            var kpis = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 10, 0, 0) };
            kpis.Children.Add(Kpi("TOURS PRÉVUS", sim.Laps.ToString()));
            kpis.Children.Add(Kpi("ARRÊTS", sim.Stops.Count.ToString()));
            kpis.Children.Add(Kpi("TEMPS PERDU AUX STANDS", Fmt.TotalTime(TimeSpan.FromSeconds(sim.PitLoss)).Replace("min", " min")));
            kpis.Children.Add(Kpi("PNEUS UTILISÉS", string.Format("{0} / {1} jeux", sim.SetsUsed, _plan.TyreSets)));
            var driving = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var k in _plan.Drivers)
            {
                var d = _drivers.Find(x => x.Name == k);
                double t; sim.Driving.TryGetValue(k, out t);
                if (d == null) continue;
                driving.Children.Add(Ui.T(Fmt.TotalTime(TimeSpan.FromSeconds(t)) + "  ", 15, Ui.Light(d.Brush), FontWeights.SemiBold));
            }
            var dk = new StackPanel();
            dk.Children.Add(Ui.T("CONDUITE", 11, Ui.Muted));
            dk.Children.Add(driving);
            var dkc = Ui.Card(dk); dkc.Margin = new Thickness(0, 0, 8, 0);
            kpis.Children.Add(dkc);
            _right.Children.Add(kpis);

            var plan = new StackPanel();
            var head = new DockPanel();
            double diff = (simB.Finish - sim.Finish) - (simB.Laps - sim.Laps) * Pace(_plan.Drivers[0]);
            var alt = Ui.T(string.Format(Inv, "Variante : 4 pneus à chaque arrêt  {0}{1:0} s", diff >= 0 ? "+" : "−", Math.Abs(diff)), 12.5, Ui.Muted);
            alt.ToolTip = "Même plan en changeant les 4 pneus à chaque arrêt : temps gagné ou perdu sur la course";
            DockPanel.SetDock(alt, Dock.Right);
            head.Children.Add(alt);
            head.Children.Add(Title("Plan proposé", "clic sur un pilote pour changer qui prend le relais"));
            plan.Children.Add(head);
            plan.Children.Add(StintBar(sim));
            plan.Children.Add(StopsTable(sim));
            foreach (var x in sim.Warnings)
                plan.Children.Add(new TextBlock { Text = "⚠ " + x, Foreground = Ui.Amber, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var pc = Card(plan);
            pc.Margin = new Thickness(0, 10, 0, 0);
            _right.Children.Add(pc);
        }

        private double Pace(string key)
        {
            var dd = _measures.Drivers.Find(d => d.Driver.Name == key);
            return dd != null ? dd.Values[StrategyEngine.Pace].Value : 100;
        }

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
                var s = Ui.T(v.Overridden ? "modifié à la main" : "équipe · " + v.Source, 11.5, Ui.Faint);
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
            box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) System.Windows.Input.Keyboard.ClearFocus(); };
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

        /// <summary>Stints as proportional blocks; each block's header picks its driver.</summary>
        private UIElement StintBar(StrategyEngine.Simulation sim)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var grid = new Grid();
            double total = sim.Finish > 0 ? sim.Finish : 1;
            var keys = _plan.Drivers;
            for (int i = 0; i < sim.Stints.Count; i++)
            {
                var s = sim.Stints[i];
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.5, s.End - s.Start), GridUnitType.Star) });
                var d = _drivers.Find(x => x.Name == s.Driver);
                var cell = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
                var pick = new ComboBox { Height = 26, FontSize = 12, ToolTip = "Pilote de ce relais", Background = d != null ? d.Brush : Brushes.Gray };
                foreach (var k in keys)
                {
                    var dv = _drivers.Find(x => x.Name == k);
                    if (dv != null) pick.Items.Add(new ComboBoxItem { Content = dv.Name, Tag = dv.Name });
                }
                foreach (ComboBoxItem it in pick.Items) if ((string)it.Tag == s.Driver) pick.SelectedItem = it;
                int stint = i;
                pick.SelectionChanged += (snd, e) =>
                {
                    if (_building || pick.SelectedItem == null) return;
                    // Freeze the drivers up to this stint, then this one changes
                    var list = new List<string>();
                    for (int k = 0; k < sim.Stints.Count; k++) list.Add(sim.Stints[k].Driver);
                    list[stint] = (string)((ComboBoxItem)pick.SelectedItem).Tag;
                    _plan.StintDrivers = list;
                    Dispatcher.BeginInvoke(new Action(Rebuild));
                };
                cell.Children.Add(pick);
                cell.Children.Add(new Border
                {
                    Height = 26, Background = d != null ? d.Brush : Brushes.Gray, CornerRadius = new CornerRadius(0, 0, 4, 4),
                    Child = new TextBlock { Text = string.Format("{0} t · {1}", s.Laps, Fmt.TotalTime(TimeSpan.FromSeconds(s.End - s.Start))), Foreground = Brushes.White,
                                            FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                            TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = string.Format("Relais {0} : {1} tours, de {2} à {3}", i + 1, s.Laps, Clock(s.Start), Clock(s.End)),
                });
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            outer.Children.Add(grid);

            var ticks = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            int hours = (int)Math.Ceiling(_plan.DurationMin / 60.0);
            for (int h = 0; h <= hours; h++) ticks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int h = 0; h <= hours; h++)
            {
                var t = Ui.T(h + " h", 11, Ui.Faint);
                Grid.SetColumn(t, h);
                ticks.Children.Add(t);
            }
            outer.Children.Add(ticks);
            return outer;
        }

        private UIElement StopsTable(StrategyEngine.Simulation sim)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var widths = new[] { 60.0, 64, 96, 210, 170, 90, 90, 160 };
            Func<string[], Brush, bool, Grid> row = (cells, nextColor, head) =>
            {
                var g = new Grid { MinHeight = 29 };
                foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] == null) continue;
                    var t = Ui.T(cells[i], head ? 12 : 13, head ? Ui.Faint : (i == cells.Length - 1 ? nextColor : Ui.Text), i == cells.Length - 1 && !head ? FontWeights.SemiBold : FontWeights.Normal);
                    t.Margin = new Thickness(6, 0, 6, 0);
                    Grid.SetColumn(t, i);
                    g.Children.Add(t);
                }
                return g;
            };
            Func<Grid, bool, UIElement> wrap = (g, head) =>
                new Border { Child = g, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = head ? Ui.HeaderBg : Brushes.Transparent };
            var header = row(new[] { "Arrêt", "Tour", "Heure", "Carburant", "Pneus", "Arrêté", "Perdu", "Repart" }, Ui.Text, true);
            ((TextBlock)header.Children[4]).ToolTip = "Nombre de pneus changés à cet arrêt : « auto » les change quand le train arrive en fin de vie";
            panel.Children.Add(wrap(header, true));
            for (int i = 0; i < sim.Stops.Count; i++)
            {
                var s = sim.Stops[i];
                var d = _drivers.Find(x => x.Name == s.NextDriver);
                bool last = i == sim.Stops.Count - 1;
                var g = row(new[]
                {
                    (i + 1).ToString(), s.Lap.ToString(), Clock(s.At),
                    string.Format(Inv, "+{0:0} L{1}", s.FuelL, last ? " (jusqu'à l'arrivée)" : ""),
                    null,
                    string.Format(Inv, "{0:0} s", s.Stopped), string.Format(Inv, "+{0:0} s", s.Lost),
                    d != null ? d.Name : "",
                }, d != null ? Ui.Light(d.Brush) : Ui.Text, false);
                g.Children.Add(TyreChoice(i, s));
                panel.Children.Add(wrap(g, false));
            }
            if (sim.Stops.Count == 0) panel.Children.Add(Ui.T("Aucun arrêt nécessaire.", 13, Ui.Muted));
            return panel;
        }

        /// <summary>How many tyres to change at a stop: automatic, or 0 to 4 chosen by hand.</summary>
        private ComboBox TyreChoice(int stop, StrategyEngine.StopPlan s)
        {
            var cb = new ComboBox { Height = 24, FontSize = 12.5, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            string auto = s.TyresChosen ? "auto" : (s.Tyres == 0 ? "auto · aucun" : string.Format("auto · {0} neufs", s.Tyres));
            cb.Items.Add(auto);
            cb.Items.Add("aucun");
            for (int n = 1; n <= 4; n++) cb.Items.Add(n == 1 ? "1 pneu" : n + " pneus");
            cb.SelectedIndex = s.TyresChosen ? s.Tyres + 1 : 0;
            if (s.TyresChosen) cb.Foreground = Ui.Amber;
            cb.ToolTip = "Changé à la main : le temps d'arrêt suit (2 pneus et 4 pneus mesurés, 1 et 3 entre les deux)";
            Grid.SetColumn(cb, 4);
            cb.SelectionChanged += (snd, e) =>
            {
                if (_building) return;
                while (_plan.StopTyres.Count <= stop) _plan.StopTyres.Add(-1);
                _plan.StopTyres[stop] = cb.SelectedIndex <= 0 ? -1 : cb.SelectedIndex - 1;
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };
            return cb;
        }

        /// <summary>Race time, or the clock time when a start time is given.</summary>
        private string Clock(double raceSec)
        {
            TimeSpan start;
            if (!string.IsNullOrEmpty(_plan.StartClock) && TimeSpan.TryParse(_plan.StartClock, Inv, out start))
                return (start + TimeSpan.FromSeconds(raceSec)).ToString(@"hh\:mm");
            var t = TimeSpan.FromSeconds(raceSec);
            return string.Format("{0}h{1:00}", (int)t.TotalHours, t.Minutes);
        }

        // ─── Small widgets ────────────────────────────────────────────────────

        private static UIElement Title(string title, string sub)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            p.Children.Add(Ui.T(title, 14.5, Ui.Text, FontWeights.SemiBold));
            p.Children.Add(Ui.T("   " + sub, 12, Ui.Muted));
            return p;
        }

        private static Border Card(UIElement child) { return Ui.Card(child, new Thickness(14, 12, 14, 12)); }

        private static UIElement Kpi(string label, string value)
        {
            var p = new StackPanel();
            p.Children.Add(Ui.T(label, 11, Ui.Muted));
            p.Children.Add(Ui.T(value, 18, Ui.Text, FontWeights.SemiBold));
            var c = Ui.Card(p); c.Margin = new Thickness(0, 0, 8, 0);
            return c;
        }

        private static UIElement Field(string label, FrameworkElement input)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Ui.T(label, 12.5, Ui.Muted));
            Grid.SetColumn(input, 1);
            g.Children.Add(input);
            return g;
        }

        private TextBox Box(string text, Func<string, bool> apply, string tooltip = null)
        {
            var b = new TextBox { Text = text, Height = 26, FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tooltip };
            b.LostFocus += (s, e) =>
            {
                if (apply(b.Text)) { b.BorderBrush = Ui.Frozen(Color.FromArgb(50, 255, 255, 255)); Dispatcher.BeginInvoke(new Action(Rebuild)); }
                else b.BorderBrush = Ui.Red;
            };
            b.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) System.Windows.Input.Keyboard.ClearFocus(); };
            return b;
        }
    }
}
