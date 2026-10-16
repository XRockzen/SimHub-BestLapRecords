using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace User.TeamPace
{
    // ── Converter : "" → Collapsed, non-empty → Visible ─────────────────────
    public class VisibleWhenNotEmptyConverter : IValueConverter
    {
        public static readonly VisibleWhenNotEmptyConverter Instance = new VisibleWhenNotEmptyConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        { throw new NotSupportedException(); }
    }

    /// <summary>
    /// Main screen: every view is rendered as one column per displayed driver,
    /// with shared filters and a back stack (career → circuit → session analysis).
    /// </summary>
    public partial class TeamPaceControl : UserControl
    {
        private enum Screen { Career, History, Progress, Analysis, Telemetry }

        private class NavState
        {
            public Screen Screen;
            public string Circuit;
            public string ClassLabel;
            public Dictionary<string, LmuSessionRecord> Sessions;   // driver key → session
            public string Title;      // analysis: circuit name
            public string Subtitle;   // analysis: "Course du 20/09/26 · 18:50"
            public string Crumb;      // short label in the breadcrumb
            public string BadgeClass; // raw car class for the title badge
            public List<TelemetryView.Preset> Presets;   // telemetry: laps shown, kept up to date by the view
        }

        private ProgressionChart _progressChart;

        // Kept across control re-creations (SimHub rebuilds the settings page)
        private static readonly List<string> SelectedKeys = new List<string> { "" };

        private readonly TeamPacePlugin _plugin;
        private readonly DispatcherTimer      _loadTimer;
        private bool _lmuSettingsDirty;

        private List<DriverView> _drivers = new List<DriverView>();
        private NavState _state;
        private readonly Stack<NavState> _back = new Stack<NavState>();

        // Shared filters
        private string _typeFilter = "ALL";
        private bool   _simOnly, _commonOnly;
        private readonly HashSet<string> _activeClasses = new HashSet<string>();

        // Columns currently shown
        private readonly List<CareerColumn>      _careerCols   = new List<CareerColumn>();
        private readonly List<SessionListColumn> _listCols     = new List<SessionListColumn>();
        private readonly List<AnalysisColumn>    _analysisCols = new List<AnalysisColumn>();
        private bool _populatingDrivers;

        public TeamPaceControl(TeamPacePlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();

            TxtLmuPlayer.Text = LmuSessionStore.PlayerName  ?? "";
            TxtLmuPath.Text   = LmuSessionStore.ResultsPath ?? "";
            if (string.IsNullOrEmpty(TxtLmuPath.Text))
                TxtLmuPath.Text = LmuXmlParser.AutoDiscoverResultsPath();
            TxtTeamFolder.Text = LmuSessionStore.TeamFolder;
            _lmuSettingsDirty = false;

            // Settings stay open until the player name is known, then fold away
            SetSettingsOpen(string.IsNullOrWhiteSpace(LmuSessionStore.PlayerName));

            // Mouse "back" button and Alt+Left go back, like in a browser
            PreviewMouseDown += (s, e) => { if (e.ChangedButton == MouseButton.XButton1) { GoBack(); e.Handled = true; } };
            PreviewKeyDown   += (s, e) =>
            {
                if (e.Key == Key.System && e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
                { GoBack(); e.Handled = true; }
            };

            RebuildDrivers();
            Navigate(new NavState { Screen = Screen.Career, Crumb = "Carrière" }, false);

            TxtVersion.Text = "TeamPace " + PluginUpdater.Format(PluginUpdater.CurrentVersion);
            Loaded   += (s, e) =>
            {
                PluginUpdater.Changed += OnUpdateChanged;
                TelemetryService.StatusChanged += OnTelemetryStatus;
                RefreshUpdateBanner();
            };
            Unloaded += (s, e) =>
            {
                PluginUpdater.Changed -= OnUpdateChanged;
                TelemetryService.StatusChanged -= OnTelemetryStatus;
            };

            _loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _loadTimer.Tick += LoadTimer_Tick;
            if (_plugin.IsImporting) _loadTimer.Start();
        }

        private void LoadTimer_Tick(object sender, EventArgs e)
        {
            if (_plugin.IsImporting) return;
            _loadTimer.Stop();
            ReloadData();
        }

        private void ReloadData()
        {
            RebuildDrivers();
            Render();
        }

        // ═══ Drivers ══════════════════════════════════════════════════════════

        private void RebuildDrivers()
        {
            var available = LmuTeamSync.GetDrivers();
            var byKey = new Dictionary<string, TeamDriver>();
            foreach (var d in available) byKey[d.DbPath ?? ""] = d;

            SelectedKeys.RemoveAll(k => !byKey.ContainsKey(k));
            if (SelectedKeys.Count == 0) SelectedKeys.Add("");

            _drivers = new List<DriverView>();
            for (int i = 0; i < SelectedKeys.Count; i++)
                _drivers.Add(new DriverView(byKey[SelectedKeys[i]], DriverView.Palette[i % DriverView.Palette.Length]));

            DriverChips.Children.Clear();
            foreach (var d in _drivers) DriverChips.Children.Add(BuildChip(d));

            _populatingDrivers = true;
            var choices = new List<object>();
            choices.Add(SelectedKeys.Count >= DriverView.MaxDrivers ? "4 pilotes maximum" : "+ Ajouter un pilote…");
            foreach (var d in available)
                if (!SelectedKeys.Contains(d.DbPath ?? "")) choices.Add(d);
            CmbAddDriver.ItemsSource = choices;
            CmbAddDriver.SelectedIndex = 0;
            CmbAddDriver.IsEnabled = choices.Count > 1 && SelectedKeys.Count < DriverView.MaxDrivers;
            _populatingDrivers = false;
        }

        private UIElement BuildChip(DriverView d)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = d.Brush, Margin = new Thickness(0, 0, 5, 0),
                                             VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = d.DbPath == null ? d.Name + " (moi)" : d.Name,
                                               FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            if (_drivers.Count > 1)
            {
                var close = new TextBlock { Text = "  ✕", FontSize = 10, Opacity = 0.6, Cursor = Cursors.Hand,
                                            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Retirer ce pilote" };
                close.MouseLeftButtonDown += (s, e) =>
                {
                    SelectedKeys.Remove(d.Key);
                    RebuildDrivers();
                    RefreshForDriverChange();
                    e.Handled = true;
                };
                panel.Children.Add(close);
            }
            return new Border
            {
                Child = panel,
                BorderBrush = d.Brush, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(30, d.Brush.Color.R, d.Brush.Color.G, d.Brush.Color.B)),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0),
            };
        }

        private void CmbAddDriver_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populatingDrivers) return;
            var d = CmbAddDriver.SelectedItem as TeamDriver;
            if (d == null || SelectedKeys.Count >= DriverView.MaxDrivers) return;
            SelectedKeys.Add(d.DbPath ?? "");
            RebuildDrivers();
            RefreshForDriverChange();
        }

        /// <summary>In an analysis, newly added drivers get their matching session if they have one.</summary>
        private void RefreshForDriverChange()
        {
            if (_state != null && _state.Screen == Screen.Analysis)
            {
                LmuSessionRecord any = null;
                foreach (var s in _state.Sessions.Values) { any = s; break; }
                if (any != null)
                    foreach (var d in _drivers)
                        if (!_state.Sessions.ContainsKey(d.Key))
                        {
                            var m = d.FindMatch(any);
                            if (m != null) _state.Sessions[d.Key] = m;
                        }
            }
            else if (_state != null && _state.Screen == Screen.Telemetry)
            {
                // Removed drivers lose their laps, added ones get their best lap on the circuit
                _state.Presets.RemoveAll(p => !_drivers.Exists(d => d.Key == p.DriverKey));
                foreach (var d in _drivers)
                    if (!_state.Presets.Exists(p => p.DriverKey == d.Key) && _state.Presets.Count < TelemetryView.MaxSlots)
                        _state.Presets.Add(new TelemetryView.Preset { DriverKey = d.Key });
            }
            Render();
        }

        // ═══ Navigation ═══════════════════════════════════════════════════════

        private void Navigate(NavState state, bool push)
        {
            if (push && _state != null) _back.Push(_state);
            _state = state;
            Render();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) { GoBack(); }

        private void GoBack()
        {
            if (_back.Count == 0) return;
            _state = _back.Pop();
            Render();
        }

        /// <summary>Jumps back to the i-th breadcrumb level (0 = root of the stack).</summary>
        private void GoToCrumb(int index)
        {
            var path = new List<NavState>(_back);
            path.Reverse();                       // oldest first
            if (index < 0 || index >= path.Count) return;
            _back.Clear();
            for (int i = 0; i < index; i++) _back.Push(path[i]);
            _state = path[index];
            Render();
        }

        private void BtnTabCareer_Click(object sender, RoutedEventArgs e)
        {
            _back.Clear();
            Navigate(new NavState { Screen = Screen.Career, Crumb = "Carrière" }, false);
        }

        private void BtnTabHistory_Click(object sender, RoutedEventArgs e)
        {
            _back.Clear();
            Navigate(new NavState { Screen = Screen.History, Crumb = "Historique" }, false);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsOpen(SettingsPanel.Visibility != Visibility.Visible);
        }

        private void SetSettingsOpen(bool open)
        {
            SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            BtnSettings.Content = open ? "⚙ Masquer les réglages" : "⚙ Réglages";
        }

        /// <summary>Page title, subtitle, optional category badge and clickable breadcrumb.</summary>
        private void SetHeader(string title, string subtitle, string carClass)
        {
            TxtPageTitle.Text    = title;
            TxtPageSubtitle.Text = subtitle ?? "";
            if (string.IsNullOrEmpty(carClass))
                PageBadge.Visibility = Visibility.Collapsed;
            else
            {
                PageBadge.Visibility = Visibility.Visible;
                PageBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(LmuCategoryStyle.GetBg(carClass)));
                TxtPageBadge.Text    = LmuCategoryStyle.GetShortName(carClass);
            }

            BreadcrumbPanel.Children.Clear();
            var path = new List<NavState>(_back);
            path.Reverse();
            BreadcrumbPanel.Visibility = path.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < path.Count; i++)
            {
                int index = i;
                var link = new TextBlock { Text = path[i].Crumb ?? "…", Style = (Style)FindResource("CrumbLink") };
                link.MouseLeftButtonDown += (s, e) => { GoToCrumb(index); e.Handled = true; };
                BreadcrumbPanel.Children.Add(link);
                BreadcrumbPanel.Children.Add(new TextBlock { Text = "  ›  ", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)) });
            }
            BreadcrumbPanel.Children.Add(new TextBlock { Text = _state.Crumb ?? title, FontSize = 12,
                                                         Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)) });
        }

        private void SetTopStatus(string text)
        {
            TxtTopStatus.Text = text ?? "";
        }

        private void OpenAnalysis(DriverView driver, LmuSessionRecord session)
        {
            var map = new Dictionary<string, LmuSessionRecord>();
            map[driver.Key] = session;
            foreach (var d in _drivers)
            {
                if (d.Key == driver.Key) continue;
                var m = d.FindMatch(session);
                if (m != null) map[d.Key] = m;
            }
            var when = session.SessionDate != DateTime.MinValue ? session.SessionDate.ToString("dd/MM/yy · HH:mm") : "?";
            Navigate(new NavState
            {
                Screen     = Screen.Analysis,
                Sessions   = map,
                Title      = session.TrackDisplayName,
                Subtitle   = string.Format("{0} du {1}{2}", session.SessionTypeLong, when,
                                           map.Count > 1 ? string.Format("  ·  session commune à {0} pilotes", map.Count) : ""),
                Crumb      = string.Format("{0} {1}", session.SessionTypeLong,
                                           session.SessionDate != DateTime.MinValue ? session.SessionDate.ToString("dd/MM") : ""),
                BadgeClass = session.CarClass,
            }, true);
        }

        // ═══ Rendering ════════════════════════════════════════════════════════

        private void Render()
        {
            if (_state == null) return;
            foreach (var d in _drivers) d.Invalidate();

            ClearColumns();
            BtnBack.Visibility = _back.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // The tab the current path started from stays highlighted
            var rootScreen = _back.Count > 0 ? new List<NavState>(_back)[_back.Count - 1].Screen : _state.Screen;
            SetTabActive(BtnTabCareer,  rootScreen == Screen.Career);
            SetTabActive(BtnTabHistory, rootScreen == Screen.History);

            ProgressChartPanel.Visibility = _state.Screen == Screen.Progress ? Visibility.Visible : Visibility.Collapsed;
            if (_progressChart != null) { _progressChart.Detach(); _progressChart = null; }
            ClassFilterBar.Visibility = _state.Screen == Screen.Career   ? Visibility.Visible : Visibility.Collapsed;
            TypeFilterBar.Visibility  = _state.Screen == Screen.History  ? Visibility.Visible : Visibility.Collapsed;
            CommonLegend.Visibility   = (_state.Screen == Screen.History || _state.Screen == Screen.Progress) && _drivers.Count > 1
                                        ? Visibility.Visible : Visibility.Collapsed;
            AnalysisLegend.Visibility = _state.Screen == Screen.Analysis ? Visibility.Visible : Visibility.Collapsed;
            BtnTelemetry.Visibility   = _state.Screen == Screen.Analysis || _state.Screen == Screen.Progress
                                        ? Visibility.Visible : Visibility.Collapsed;
            TxtLegendTeam.Visibility  = _drivers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            switch (_state.Screen)
            {
                case Screen.Career:   RenderCareer();   break;
                case Screen.History:  RenderHistory();  break;
                case Screen.Progress: RenderProgress(); break;
                case Screen.Analysis: RenderAnalysis(); break;
                case Screen.Telemetry: RenderTelemetry(); break;
            }
        }

        private static void SetTabActive(Button b, bool active)
        {
            b.BorderBrush = active
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2196F3"))
                : Brushes.Transparent;
            b.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(166, 255, 255, 255));
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private void ClearColumns()
        {
            ColumnsHost.Children.Clear();
            ColumnsHost.ColumnDefinitions.Clear();
            _careerCols.Clear();
            _listCols.Clear();
            _analysisCols.Clear();
        }

        /// <summary>Adds an equal-width column with the driver's colour bar and name above the content.</summary>
        private void AddColumn(DriverView d, UIElement content, string subtitle)
        {
            int index = ColumnsHost.ColumnDefinitions.Count;
            if (index > 0)
                ColumnsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            ColumnsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            header.Children.Add(new TextBlock { Text = d.DbPath == null ? d.Name + "  (moi)" : d.Name,
                                                FontSize = 17, FontWeight = FontWeights.Bold, Foreground = d.Brush });
            if (!string.IsNullOrEmpty(subtitle))
                header.Children.Add(new TextBlock { Text = "   " + subtitle, FontSize = 12, Opacity = 0.6,
                                                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });

            var dock = new DockPanel();
            var bar = new Border { Height = 3, Background = d.Brush, CornerRadius = new CornerRadius(1.5) };
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(header);
            dock.Children.Add(content);

            Grid.SetColumn(dock, ColumnsHost.ColumnDefinitions.Count - 1);
            ColumnsHost.Children.Add(dock);
        }

        // ─── Career ───────────────────────────────────────────────────────────

        private void RenderCareer()
        {
            SetHeader("Carrière",
                _drivers.Count > 1 ? string.Format("Comparaison de {0} pilotes", _drivers.Count)
                                   : "Vos statistiques sur Le Mans Ultimate",
                null);

            // Shared activity scale so the charts compare at a glance
            DateTime? scaleMin = null;
            double scaleMax = 0;
            foreach (var d in _drivers)
            {
                var act = CareerColumn.BuildActivity(d.Sessions);
                foreach (var a in act) if (!scaleMin.HasValue || a.Date < scaleMin.Value) scaleMin = a.Date;
                scaleMax = Math.Max(scaleMax, GlobalActivityChartRenderer.MaxWeeklyValue(act, true));
            }

            foreach (var d in _drivers)
            {
                var col = new CareerColumn(d);
                col.Load(scaleMin, scaleMax > 0 ? (double?)scaleMax : null);
                col.CircuitSelected += key => { foreach (var c in _careerCols) if (c != col) c.SelectCircuit(key); };
                col.CircuitOpened   += row => Navigate(new NavState
                {
                    Screen = Screen.Progress, Circuit = row.Circuit, ClassLabel = row.CarClassLabel,
                    Crumb = ShortCircuit(row.Circuit) + " · " + row.CarClassLabel,
                }, true);
                _careerCols.Add(col);
                AddColumn(d, col, string.Format("{0} sessions", d.Sessions.Count));
            }

            // Gaps between drivers on each circuit × class
            if (_drivers.Count > 1)
            {
                var best  = new Dictionary<string, long>();
                var count = new Dictionary<string, int>();
                foreach (var c in _careerCols)
                    foreach (var r in c.AllRows)
                    {
                        long b;
                        if (!best.TryGetValue(r.Key, out b) || r.BestTicks < b) best[r.Key] = r.BestTicks;
                        int n; count.TryGetValue(r.Key, out n); count[r.Key] = n + 1;
                    }
                foreach (var c in _careerCols) c.SetGaps(best, count);
            }
            else foreach (var c in _careerCols) c.SetGaps(null, null);

            PopulateClassFilter();
            foreach (var c in _careerCols) c.ApplyClassFilter(_activeClasses);
            TxtStatus.Text = "Double-clic sur un circuit : progression de chaque pilote sur ce circuit";
        }

        private void PopulateClassFilter()
        {
            ClassFilterBar.Children.Clear();
            var seen = new List<LmuCircuitBestRow>();
            var labels = new HashSet<string>();
            foreach (var c in _careerCols)
                foreach (var r in c.AllRows)
                    if (labels.Add(r.CarClassLabel)) seen.Add(r);
            seen.Sort((a, b) => string.Compare(a.CarClassLabel, b.CarClassLabel, StringComparison.OrdinalIgnoreCase));
            _activeClasses.IntersectWith(labels);

            if (seen.Count <= 1) return;
            ClassFilterBar.Children.Add(new TextBlock { Text = "Catégories :", FontSize = 10, Opacity = 0.5,
                                                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
            foreach (var r in seen)
            {
                var label = r.CarClassLabel;
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(r.CarClassBg)),
                    BorderBrush = r.CarClassThick == "1"
                        ? (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(r.CarClassBorder))
                        : Brushes.Transparent,
                    BorderThickness = r.CarClassThick == "1" ? new Thickness(1) : new Thickness(0),
                    Cursor = Cursors.Hand,
                    Opacity = _activeClasses.Count == 0 || _activeClasses.Contains(label) ? 1.0 : 0.28,
                    Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
                };
                tag.MouseLeftButtonDown += (s, e) =>
                {
                    if (!_activeClasses.Remove(label)) _activeClasses.Add(label);
                    foreach (UIElement child in ClassFilterBar.Children)
                    {
                        var b = child as Border;
                        if (b == null) continue;
                        var l = ((TextBlock)b.Child).Text;
                        b.Opacity = _activeClasses.Count == 0 || _activeClasses.Contains(l) ? 1.0 : 0.28;
                    }
                    foreach (var c in _careerCols) c.ApplyClassFilter(_activeClasses);
                };
                ClassFilterBar.Children.Add(tag);
            }
        }

        // ─── History ──────────────────────────────────────────────────────────

        private void RenderHistory()
        {
            SetHeader("Historique", "Toutes les sessions, de la plus récente à la plus ancienne", null);
            UpdateTypeFilterButtons();
            int total = 0;
            foreach (var d in _drivers)
            {
                var rows = new List<LmuSessionRow>();
                foreach (var s in d.Sessions)
                {
                    if (_typeFilter != "ALL" && !(s.SessionType ?? "").StartsWith(_typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_simOnly && !s.IsFullSim) continue;
                    var row = new LmuSessionRow { Record = s, CommonColors = CommonColorsFor(d, s) };
                    if (_commonOnly && !row.IsCommon) continue;
                    rows.Add(row);
                }
                total += rows.Count;
                AddListColumn(d, rows, false, null);
            }
            TxtStatus.Text = total == 0 && string.IsNullOrEmpty(LmuSessionStore.PlayerName)
                ? "Configurez votre nom de joueur et cliquez sur ⟳ Importer"
                : "Double-clic sur une session : analyse (côte à côte si elle est commune)";
        }

        private void UpdateTypeFilterButtons()
        {
            var on  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2196F3"));
            var off = new SolidColorBrush(Color.FromArgb(68, 255, 255, 255));
            BtnFAll.BorderBrush    = _typeFilter == "ALL" ? on : off;
            BtnFRace.BorderBrush   = _typeFilter == "R"   ? on : off;
            BtnFQuali.BorderBrush  = _typeFilter == "Q"   ? on : off;
            BtnFPrac.BorderBrush   = _typeFilter == "P"   ? on : off;
            BtnFSim.BorderBrush    = _simOnly    ? on : off;
            BtnFCommon.BorderBrush = _commonOnly ? on : off;
            BtnFCommon.Visibility  = _drivers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TypeFilter_Click(object sender, RoutedEventArgs e)
        {
            _typeFilter = (string)((Button)sender).Tag;
            Render();
        }

        private void SimFilter_Click(object sender, RoutedEventArgs e)    { _simOnly = !_simOnly; Render(); }
        private void CommonFilter_Click(object sender, RoutedEventArgs e) { _commonOnly = !_commonOnly; Render(); }

        private List<Brush> CommonColorsFor(DriverView owner, LmuSessionRecord s)
        {
            var list = new List<Brush>();
            foreach (var other in _drivers)
                if (other != owner && other.FindMatch(s) != null) list.Add(other.Brush);
            return list;
        }

        private void AddListColumn(DriverView d, List<LmuSessionRow> rows, bool progressMode, string subtitle)
        {
            var col = new SessionListColumn(d);
            col.Load(rows, progressMode);
            col.SessionSelected += (drv, rec) =>
            {
                if (rec == null) return;
                foreach (var c in _listCols) if (c != col) c.SelectMatching(rec);
                if (_progressChart != null) _progressChart.Highlight(rec);
            };
            col.SessionOpened += OpenAnalysis;
            _listCols.Add(col);
            AddColumn(d, col, subtitle);
        }

        // ─── Circuit progression ──────────────────────────────────────────────

        private void RenderProgress()
        {
            // Sessions of each driver on this circuit × class, oldest first
            var perDriver = new List<List<LmuSessionRecord>>();
            string rawClass = null;
            foreach (var d in _drivers)
            {
                var matching = new List<LmuSessionRecord>();
                foreach (var s in d.Sessions)
                {
                    if (s.BestLapTime.Ticks <= 0) continue;
                    if (!string.Equals(s.TrackDisplayName, _state.Circuit, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(LmuCategoryStyle.GetShortName(s.CarClass), _state.ClassLabel, StringComparison.OrdinalIgnoreCase)) continue;
                    matching.Add(s);
                    if (rawClass == null) rawClass = s.CarClass;
                }
                matching.Sort((a, b) => a.SessionDate.CompareTo(b.SessionDate));
                perDriver.Add(matching);
            }
            _state.BadgeClass = rawClass;
            SetHeader(_state.Circuit, "Progression du meilleur tour, session après session", rawClass);

            long teamBest = long.MaxValue;
            foreach (var list in perDriver)
                foreach (var s in list) if (s.BestLapTime.Ticks < teamBest) teamBest = s.BestLapTime.Ticks;

            var series = new List<ProgressionChart.Series>();
            for (int i = 0; i < _drivers.Count; i++)
            {
                var d = _drivers[i];
                var matching = perDriver[i];
                var common = new HashSet<LmuSessionRecord>();

                long best = long.MaxValue;
                foreach (var s in matching) if (s.BestLapTime.Ticks < best) best = s.BestLapTime.Ticks;

                var rows = new List<LmuSessionRow>();
                LmuSessionRecord prev = null;
                foreach (var s in matching)
                {
                    string delta = "—", fg = "#DDFFFFFF";
                    if (prev != null)
                    {
                        var diff = s.BestLapTime - prev.BestLapTime;
                        if (Math.Abs(diff.TotalSeconds) < 0.001) delta = "=";
                        else { delta = Fmt.Signed(diff); fg = diff.Ticks < 0 ? "#FF4CAF50" : "#FFEF5350"; }
                    }
                    var row = new LmuSessionRow
                    {
                        Record = s, IsPersonalBest = s.BestLapTime.Ticks == best,
                        Delta = delta, DeltaFg = fg, CommonColors = CommonColorsFor(d, s),
                    };
                    if (row.IsCommon) common.Add(s);
                    rows.Add(row);
                    prev = s;
                }
                rows.Reverse();   // newest first, delta still against the previous outing

                AddListColumn(d, rows, true, null);
                _listCols[_listCols.Count - 1].SetSummary(BuildProgressSummary(matching, best, teamBest));
                series.Add(new ProgressionChart.Series { Driver = d, Sessions = matching, IsCommon = common.Contains });
            }

            _progressChart = new ProgressionChart();
            _progressChart.PointSelected += (drv, rec) =>
            {
                foreach (var c in _listCols)
                {
                    if (c.Driver == drv) c.SelectSession(rec);
                    else c.SelectMatching(rec);
                }
                _progressChart.Highlight(rec);
            };
            _progressChart.PointOpened += OpenAnalysis;
            _progressChart.Attach(ProgressCanvas, series);

            TxtStatus.Text = "Δ précédente : écart avec la session précédente du même pilote";
        }

        private List<SessionListColumn.SummaryItem> BuildProgressSummary(List<LmuSessionRecord> sessions, long best, long teamBest)
        {
            var items = new List<SessionListColumn.SummaryItem>();
            if (sessions.Count == 0)
            {
                items.Add(new SessionListColumn.SummaryItem { Label = "SESSIONS", Value = "0", Detail = "jamais roulé ici", Color = "#F2FFFFFF" });
                return items;
            }
            LmuSessionRecord recordSession = sessions[0];
            foreach (var s in sessions) if (s.BestLapTime.Ticks == best) recordSession = s;
            var first = sessions[0];
            var last  = sessions[sessions.Count - 1];

            items.Add(new SessionListColumn.SummaryItem
            {
                Label = "RECORD", Value = Fmt.Lap(TimeSpan.FromTicks(best)), Color = "#FF66BB6A",
                Detail = string.Format("{0} du {1}", recordSession.SessionTypeLong, recordSession.SessionDate.ToString("dd/MM/yy")),
            });
            if (_drivers.Count > 1)
                items.Add(new SessionListColumn.SummaryItem
                {
                    Label = "ÉCART ÉQUIPE",
                    Value = best == teamBest ? "★ meilleur" : "+" + Fmt.Seconds(TimeSpan.FromTicks(best - teamBest)),
                    Color = best == teamBest ? "#FF66BB6A" : "#FFEF9A9A",
                    Detail = "sur le record du circuit",
                });
            var gain = TimeSpan.FromTicks(best - first.BestLapTime.Ticks);
            items.Add(new SessionListColumn.SummaryItem
            {
                Label = "PROGRESSION",
                Value = sessions.Count > 1 && gain.Ticks < 0 ? Fmt.Signed(gain) : "—",
                Color = "#F2FFFFFF",
                Detail = sessions.Count > 1 ? "depuis le " + first.SessionDate.ToString("dd/MM/yy") : "une seule session",
            });
            items.Add(new SessionListColumn.SummaryItem
            {
                Label = "DERNIÈRE SÉANCE", Value = last.BestLapFormatted, Color = "#F2FFFFFF",
                Detail = last.BestLapTime.Ticks == best ? "★ record"
                       : "+" + Fmt.Seconds(TimeSpan.FromTicks(last.BestLapTime.Ticks - best)) + " du record",
            });
            var driving = TimeSpan.Zero; int laps = 0;
            foreach (var s in sessions) { driving = driving + CareerColumn.SessionDuration(s); laps += s.TotalLaps; }
            items.Add(new SessionListColumn.SummaryItem
            {
                Label = "ROULAGE", Value = Fmt.TotalTime(driving), Color = "#FF80DEEA",
                Detail = string.Format("{0} sessions · {1} tours", sessions.Count, laps),
            });
            return items;
        }

        private static string ShortCircuit(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Circuit";
            int dash = name.IndexOf(" – ", StringComparison.Ordinal);
            var s = dash > 0 ? name.Substring(0, dash) : name;
            return s.Length > 28 ? s.Substring(0, 27) + "…" : s;
        }

        // ─── Analysis ─────────────────────────────────────────────────────────

        private void RenderAnalysis()
        {
            SetHeader(_state.Title, _state.Subtitle, _state.BadgeClass);
            foreach (var d in _drivers)
            {
                LmuSessionRecord s;
                if (_state.Sessions.TryGetValue(d.Key, out s))
                {
                    var col = new AnalysisColumn(d, s);
                    col.StatsChanged += c => UpdateAverageReferences();
                    col.LapOpened    += (c, lapNum) => OpenTelemetryFromAnalysis(c.Driver.Key, lapNum);
                    _analysisCols.Add(col);
                    AddColumn(d, col, s.SessionTypeLong);
                }
                else
                {
                    AddColumn(d, new TextBlock
                    {
                        Text = "Ce pilote n'a pas participé à cette session.",
                        Opacity = 0.4, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 30, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    }, null);
                }
            }

            bool fuel = false, ve = false, tyres = false, pits = false;
            foreach (var c in _analysisCols)
            {
                fuel |= c.HasFuel; ve |= c.HasVe; tyres |= c.HasTyres; pits |= c.HasPits;
            }
            foreach (var c in _analysisCols) c.SetOptionalBlocks(fuel, ve, tyres, pits);
            int maxStops = 0;
            foreach (var c in _analysisCols) maxStops = Math.Max(maxStops, c.PitCount);
            foreach (var c in _analysisCols) c.SetPitBlockStops(maxStops);

            if (_analysisCols.Count > 1)
            {
                var bestByLap = new Dictionary<int, long>();
                foreach (var c in _analysisCols)
                    foreach (var r in c.Rows)
                    {
                        if (!r.IsValid || r.Lap.LapTimeTicks <= 0) continue;
                        long b;
                        if (!bestByLap.TryGetValue(r.LapNum, out b) || r.Lap.LapTimeTicks < b) bestByLap[r.LapNum] = r.Lap.LapTimeTicks;
                    }
                foreach (var c in _analysisCols) c.SetTeamDeltas(bestByLap);
            }
            else foreach (var c in _analysisCols) c.SetTeamDeltas(null);

            UpdateAverageReferences();
            TxtStatus.Text = "Cochez / décochez des tours : les stats et les couleurs se recalculent par pilote  ·  double-clic sur un tour : sa télémétrie";
        }

        private void UpdateAverageReferences()
        {
            AnalysisColumn best = null;
            if (_analysisCols.Count > 1)
                foreach (var c in _analysisCols)
                    if (c.AverageTicks > 0 && (best == null || c.AverageTicks < best.AverageTicks)) best = c;
            foreach (var c in _analysisCols)
                c.SetAverageReference(best != null ? best.AverageTicks : 0, best != null ? best.Driver.Name : null);
        }

        // ─── Telemetry ────────────────────────────────────────────────────────

        private void BtnTelemetry_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Screen == Screen.Analysis) OpenTelemetryFromAnalysis(null, 0);
            else if (_state.Screen == Screen.Progress)
            {
                var presets = new List<TelemetryView.Preset>();
                foreach (var d in _drivers) presets.Add(new TelemetryView.Preset { DriverKey = d.Key });
                OpenTelemetry(_state.Circuit, _state.BadgeClass, presets);
            }
        }

        /// <summary>From a session: each driver's best lap of that session, or the given lap for one driver.</summary>
        private void OpenTelemetryFromAnalysis(string driverKey, int lapNum)
        {
            var presets = new List<TelemetryView.Preset>();
            string circuit = _state.Title, carClass = _state.BadgeClass;
            foreach (var d in _drivers)
            {
                LmuSessionRecord s;
                if (!_state.Sessions.TryGetValue(d.Key, out s)) { presets.Add(new TelemetryView.Preset { DriverKey = d.Key }); continue; }
                circuit = s.TrackDisplayName;
                var p = new TelemetryView.Preset { DriverKey = d.Key, SessionFileId = s.FileId, LapNum = d.Key == driverKey ? lapNum : 0 };
                if (d.Key == driverKey) presets.Insert(0, p);   // the double-clicked lap is the reference
                else presets.Add(p);
            }
            OpenTelemetry(circuit, carClass, presets);
        }

        private void OpenTelemetry(string circuit, string carClass, List<TelemetryView.Preset> presets)
        {
            Navigate(new NavState
            {
                Screen = Screen.Telemetry, Circuit = circuit, BadgeClass = carClass, Presets = presets,
                Crumb = "Télémétrie",
            }, true);
        }

        private void RenderTelemetry()
        {
            SetHeader(_state.Circuit, "Télémétrie  ·  tours superposés sur la distance parcourue", _state.BadgeClass);
            ColumnsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnsHost.Children.Add(new TelemetryView(_drivers, _state.Circuit, _state.Presets));
            TxtStatus.Text = "Le premier tour sert de référence pour l'écart  ·  clic sur le tracé : zoom sur le virage";
        }

        private void OnTelemetryStatus()
        {
            Dispatcher.BeginInvoke(new Action(() => SetTopStatus(TelemetryService.Status)));
        }

        // ═══ Mise à jour ══════════════════════════════════════════════════════

        private bool _manualCheck;

        private void OnUpdateChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshUpdateBanner();
                if (!_manualCheck) return;
                _manualCheck = false;
                if (PluginUpdater.LastError != null)
                    TxtStatus.Text = "Vérification impossible : " + PluginUpdater.LastError;
                else if (PluginUpdater.Available == null)
                    TxtStatus.Text = "Vous avez la dernière version (" + PluginUpdater.Format(PluginUpdater.CurrentVersion) + ")";
            }));
        }

        private void RefreshUpdateBanner()
        {
            if (PluginUpdater.PendingRestartVersion != null)
            {
                UpdateBanner.Visibility = Visibility.Visible;
                UpdateButtons.Visibility = Visibility.Collapsed;
                TxtUpdateTitle.Text = "✔ Mise à jour " + PluginUpdater.Format(PluginUpdater.PendingRestartVersion) + " installée";
                TxtUpdateNotes.Text = "Redémarrez SimHub pour l'activer.";
                return;
            }
            var info = PluginUpdater.Available;
            if (info == null || PluginUpdater.Dismissed)
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
                return;
            }
            UpdateBanner.Visibility  = Visibility.Visible;
            UpdateButtons.Visibility = Visibility.Visible;
            BtnUpdateInstall.IsEnabled = true;
            TxtUpdateTitle.Text = string.Format("⬆ Mise à jour {0} disponible  (installée : {1})",
                PluginUpdater.Format(info.Version), PluginUpdater.Format(PluginUpdater.CurrentVersion));
            TxtUpdateNotes.Text = string.IsNullOrEmpty(info.Notes) ? "" : info.Notes.Replace("\r", "");
        }

        private void BtnUpdateInstall_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateInstall.IsEnabled = false;
            TxtUpdateTitle.Text = "Téléchargement et vérification…";
            PluginUpdater.InstallInBackground(error => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (error == null) { RefreshUpdateBanner(); return; }
                BtnUpdateInstall.IsEnabled = true;
                TxtUpdateTitle.Text = "Échec de la mise à jour";
                TxtUpdateNotes.Text = error;
            })));
        }

        private void BtnUpdateNotes_Click(object sender, RoutedEventArgs e)
        {
            var info = PluginUpdater.Available;
            if (info != null && !string.IsNullOrEmpty(info.HtmlUrl))
                try { System.Diagnostics.Process.Start(info.HtmlUrl); } catch { }
        }

        private void BtnUpdateLater_Click(object sender, RoutedEventArgs e)
        {
            PluginUpdater.Dismissed = true;
            RefreshUpdateBanner();
        }

        private void LnkCheckUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            if (!PluginUpdater.IsConfigured)
            {
                TxtStatus.Text = "Mises à jour automatiques non configurées dans cette version";
                return;
            }
            _manualCheck = true;
            PluginUpdater.Dismissed = false;
            TxtStatus.Text = "Recherche de mise à jour…";
            PluginUpdater.CheckInBackground();
        }

        // ═══ LMU config ═══════════════════════════════════════════════════════

        private void TxtLmuPlayer_TextChanged(object sender, TextChangedEventArgs e) { _lmuSettingsDirty = true; }
        private void TxtLmuPath_TextChanged(object sender, TextChangedEventArgs e)   { _lmuSettingsDirty = true; }

        /// <summary>
        /// The one refresh button: new LMU results, telemetry extraction, then team sharing
        /// (own data out, teammates' data in).
        /// </summary>
        private void BtnUpdateAll_Click(object sender, RoutedEventArgs e) { UpdateAll(); }

        private void UpdateAll()
        {
            if (_lmuSettingsDirty)
            {
                LmuSessionStore.SaveSettings(TxtLmuPlayer.Text.Trim(), TxtLmuPath.Text.Trim());
                _lmuSettingsDirty = false;
            }
            if (string.IsNullOrWhiteSpace(LmuSessionStore.PlayerName))
            {
                SetSettingsOpen(true);
                SetTopStatus("Renseignez d'abord votre nom de joueur LMU");
                return;
            }

            BtnUpdateAll.IsEnabled = false;
            Action<string> report = text => Dispatcher.BeginInvoke(new Action(() => SetTopStatus(text)));
            Task.Run(() =>
            {
                string teamText = null;
                TelemetryService.Result tele = null;
                try
                {
                    report("Import des résultats LMU…");
                    _plugin.RunLmuImport();

                    tele = TelemetryService.Extract();

                    if (LmuTeamSync.IsConfigured)
                    {
                        report("Synchronisation avec l'équipe…");
                        var error   = LmuTeamSync.Export();
                        int updated = LmuTeamSync.PullTeammates();
                        int traces  = TelemetryService.SyncTeam();
                        int mates   = LmuTeamSync.GetDrivers().Count - 1;
                        teamText = error != null
                            ? "Échec de l'envoi : " + error
                            : string.Format("Envoyé à {0:HH:mm}  ·  {1} coéquipier(s){2}{3}", DateTime.Now, mates,
                                updated > 0 ? string.Format(", {0} mis à jour", updated) : "",
                                traces > 0 ? string.Format(", {0} relais de télémétrie reçus", traces) : "");
                    }
                    TelemetryService.Invalidate();
                }
                catch (Exception ex)
                {
                    teamText = "Erreur : " + ex.Message;
                    SimHub.Logging.Current.Warn("[TeamPace] Update all: " + ex);
                }

                Dispatcher.Invoke(() =>
                {
                    TxtLmuStatus.Text = string.Format("{0} session(s)", LmuSessionStore.GetSessionCount());
                    if (teamText != null) TxtTeamStatus.Text = teamText;
                    var parts = new List<string> { TxtLmuStatus.Text };
                    if (tele != null)
                        parts.Add(tele.Error != null ? "télémétrie : " + tele.Error
                                : tele.Laps > 0 ? string.Format("{0} tour(s) de télémétrie extrait(s)", tele.Laps)
                                : "télémétrie à jour");
                    if (LmuTeamSync.IsConfigured) parts.Add("équipe synchronisée");
                    SetTopStatus("À jour : " + string.Join("  ·  ", parts));
                    BtnUpdateAll.IsEnabled = true;
                    ReloadData();
                });
            });
        }

        // ═══ Équipe (synchro dossier partagé) ═════════════════════════════════

        private void BtnTeamBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description  = "Dossier Google Drive partagé avec l'équipe";
                dlg.SelectedPath = LmuSessionStore.TeamFolder;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                LmuSessionStore.SaveTeamFolder(dlg.SelectedPath);
                TxtTeamFolder.Text = dlg.SelectedPath;
            }
            UpdateAll();
        }
    }
}
