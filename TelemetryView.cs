using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace User.TeamPace
{
    /// <summary>
    /// Lap telemetry comparison on one circuit: up to four laps (any driver, any session),
    /// overlaid on lap distance, with the time gap to the first lap, a track map and the values
    /// under the cursor.
    /// </summary>
    internal class TelemetryView : UserControl
    {
        public const int MaxSlots = 4;

        /// <summary>What a slot shows: a driver, optionally a session, optionally a lap (0 = best).</summary>
        public class Preset
        {
            public string DriverKey;
            public string SessionFileId;   // null = every session on the circuit
            public int    LapNum;
        }

        private class Slot
        {
            public DriverView Driver;
            public string     SessionKey;   // null = all sessions
            public TeleLapRef Lap;
            public Color      Color;
            public TeleTrace  Trace;
            public string     Note;
        }

        private class Item
        {
            public object Value;
            public string Text;
            public override string ToString() { return Text; }
        }

        private static readonly string[] ExtraColors = { "#FFFFC857", "#FFEC6FAD", "#FF4DD0E1", "#FFB0BEC5" };
        private static readonly HashSet<TeleChannel> Visible = new HashSet<TeleChannel>(TelemetryChart.Channels);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly List<DriverView> _drivers;
        private readonly string _circuit;
        private readonly List<Preset> _presets;   // kept in sync with the slots (owned by the nav state)
        private readonly Dictionary<string, List<TeleLapRef>> _laps = new Dictionary<string, List<TeleLapRef>>();
        private readonly List<Slot> _slots = new List<Slot>();

        private readonly StackPanel _slotPanel = new StackPanel();
        private readonly WrapPanel _toggles = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        private readonly TelemetryChart _chart = new TelemetryChart();
        private readonly TrackMap _map = new TrackMap { Height = 230, Cursor = Cursors.Hand };
        private readonly StackPanel _readout = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock _empty = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560,
            TextAlignment = TextAlignment.Center,
        };
        private bool _building;

        public TelemetryView(List<DriverView> drivers, string circuit, List<Preset> presets)
        {
            _drivers = drivers;
            _circuit = circuit;
            _presets = presets;

            foreach (var d in drivers) _laps[d.Key] = TelemetryService.GetLaps(d, circuit);
            foreach (var p in presets)
            {
                if (_slots.Count >= MaxSlots) break;
                var d = drivers.Find(x => x.Key == p.DriverKey);
                if (d != null) _slots.Add(CreateSlot(d, p));
            }
            if (_slots.Count == 0 && drivers.Count > 0) _slots.Add(CreateSlot(drivers[0], new Preset()));

            Content = BuildLayout();
            _chart.CursorMoved += d => { _map.SetCursor(d); UpdateReadout(d); };
            _chart.ViewChanged += () => _map.SetView(_chart.ViewStart, _chart.ViewEnd);
            _map.DistanceClicked += d => _chart.SetView(d - 250, d + 250);
            Reload(false);
        }

        // ─── Slots ────────────────────────────────────────────────────────────

        private Slot CreateSlot(DriverView d, Preset p)
        {
            var slot = new Slot { Driver = d };
            var laps = _laps[d.Key];
            if (!string.IsNullOrEmpty(p.SessionFileId))
            {
                slot.SessionKey = "S|" + p.SessionFileId;
                if (!laps.Exists(l => l.SessionKey == slot.SessionKey))
                {
                    slot.SessionKey = null;
                    slot.Note = "séance sans télémétrie : meilleur tour du circuit";
                }
            }
            slot.Lap = p.LapNum > 0 && slot.SessionKey != null
                ? laps.Find(l => l.SessionKey == slot.SessionKey && l.LapNum == p.LapNum)
                : null;
            if (slot.Lap == null) slot.Lap = BestLap(laps, slot.SessionKey);
            return slot;
        }

        private static TeleLapRef BestLap(List<TeleLapRef> laps, string sessionKey)
        {
            TeleLapRef best = null, fallback = null;
            foreach (var l in laps)
            {
                if (sessionKey != null && l.SessionKey != sessionKey) continue;
                if (l.IsClean && (best == null || l.LapSec < best.LapSec)) best = l;
                if (l.Lap.IsComplete && (fallback == null || l.LapSec < fallback.LapSec)) fallback = l;
            }
            return best ?? fallback;
        }

        private void AssignColors()
        {
            var used = new HashSet<string>();
            foreach (var s in _slots)
            {
                string hex = s.Driver.ColorHex;
                if (used.Contains(hex))
                    foreach (var c in ExtraColors) if (!used.Contains(c)) { hex = c; break; }
                used.Add(hex);
                s.Color = (Color)ColorConverter.ConvertFromString(hex);
            }
        }

        private void SyncPresets()
        {
            _presets.Clear();
            foreach (var s in _slots)
                _presets.Add(new Preset
                {
                    DriverKey     = s.Driver.Key,
                    SessionFileId = s.Lap != null && s.SessionKey != null && s.Lap.Session != null ? s.Lap.Session.FileId : null,
                    LapNum        = s.Lap != null && s.SessionKey != null ? s.Lap.LapNum : 0,
                });
        }

        // ─── Layout ───────────────────────────────────────────────────────────

        private UIElement BuildLayout()
        {
            var root = new DockPanel();

            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(_slotPanel);
            top.Children.Add(_toggles);
            root.Children.Add(top);

            var side = new StackPanel { Width = 300, Margin = new Thickness(12, 0, 0, 0) };
            var mapBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6), Child = _map,
                ToolTip = "Clic sur le tracé : zoom sur ce virage",
            };
            side.Children.Add(mapBox);
            side.Children.Add(_readout);
            DockPanel.SetDock(side, Dock.Right);
            root.Children.Add(side);

            var center = new Grid();
            center.Children.Add(_chart);
            center.Children.Add(_empty);
            root.Children.Add(center);

            BuildToggles();
            return root;
        }

        private void BuildToggles()
        {
            _toggles.Children.Clear();
            _toggles.Children.Add(new TextBlock { Text = "Courbes", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)),
                                                  VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var c in TelemetryChart.Channels)
            {
                var channel = c;
                var cb = new CheckBox
                {
                    Content = TelemetryChart.Title(c).Split(' ')[0], IsChecked = Visible.Contains(c), FontSize = 11,
                    Foreground = Brushes.White, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                cb.Checked   += (s, e) => { Visible.Add(channel);    _chart.SetVisible(VisibleList()); };
                cb.Unchecked += (s, e) => { Visible.Remove(channel); _chart.SetVisible(VisibleList()); };
                _toggles.Children.Add(cb);
            }
            _toggles.Children.Add(new TextBlock
            {
                Text = "molette : zoom  ·  glisser : déplacer  ·  double-clic : tour complet",
                FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            });
        }

        private static List<TeleChannel> VisibleList()
        {
            var list = new List<TeleChannel>();
            foreach (var c in TelemetryChart.Channels) if (Visible.Contains(c)) list.Add(c);
            return list;
        }

        private void BuildSlotRows()
        {
            _building = true;
            _slotPanel.Children.Clear();
            for (int i = 0; i < _slots.Count; i++) _slotPanel.Children.Add(BuildSlotRow(_slots[i], i));

            if (_slots.Count < MaxSlots)
            {
                var add = new Button { Content = "+ Ajouter un tour", HorizontalAlignment = HorizontalAlignment.Left,
                                       Margin = new Thickness(0, 2, 0, 0) };
                add.SetResourceReference(StyleProperty, "SmallBtn");   // resolved once in the page
                add.Click += (s, e) =>
                {
                    var d = _slots.Count > 0 ? _slots[0].Driver : _drivers[0];
                    _slots.Add(CreateSlot(d, new Preset()));
                    Reload(true);
                };
                _slotPanel.Children.Add(add);
            }
            _building = false;
        }

        private UIElement BuildSlotRow(Slot slot, int index)
        {
            var brush = new SolidColorBrush(slot.Color);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            foreach (var w in new[] { 5.0, 170, 300, 250, -1, 30 })
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w),
                });

            var bar = new Border { Background = brush, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1, 0, 1) };
            grid.Children.Add(bar);

            // Driver
            var cbDriver = Combo(1);
            foreach (var d in _drivers) cbDriver.Items.Add(new Item { Value = d, Text = d.DbPath == null ? d.Name + " (moi)" : d.Name });
            cbDriver.SelectedIndex = _drivers.IndexOf(slot.Driver);
            cbDriver.SelectionChanged += (s, e) =>
            {
                if (_building) return;
                slot.Driver = (DriverView)((Item)cbDriver.SelectedItem).Value;
                slot.SessionKey = null; slot.Note = null;
                slot.Lap = BestLap(_laps[slot.Driver.Key], null);
                Reload(true);
            };
            grid.Children.Add(cbDriver);

            var laps = _laps[slot.Driver.Key];

            // Session
            var cbSession = Combo(2);
            cbSession.Items.Add(new Item { Value = null, Text = "Toutes les séances  ·  meilleurs tours" });
            var sessions = new List<TeleLapRef>();
            var seen = new HashSet<string>();
            foreach (var l in laps) if (seen.Add(l.SessionKey)) sessions.Add(l);
            sessions.Sort((a, b) => b.Date.CompareTo(a.Date));
            int selSession = 0;
            foreach (var s in sessions)
            {
                var best = BestLap(laps, s.SessionKey);
                int count = laps.FindAll(l => l.SessionKey == s.SessionKey).Count;
                cbSession.Items.Add(new Item
                {
                    Value = s.SessionKey,
                    Text = string.Format("{0}  ·  {1} tours{2}", s.SessionLabel, count,
                                         best != null ? "  ·  " + Fmt.LapSec(best.LapSec) : ""),
                });
                if (s.SessionKey == slot.SessionKey) selSession = cbSession.Items.Count - 1;
            }
            cbSession.SelectedIndex = selSession;
            cbSession.SelectionChanged += (s, e) =>
            {
                if (_building || cbSession.SelectedItem == null) return;
                slot.SessionKey = (string)((Item)cbSession.SelectedItem).Value;
                slot.Note = null;
                slot.Lap = BestLap(_laps[slot.Driver.Key], slot.SessionKey);
                Reload(true);
            };
            grid.Children.Add(cbSession);

            // Lap
            var cbLap = Combo(3);
            var lapItems = LapItems(laps, slot.SessionKey);
            int selLap = -1;
            foreach (var it in lapItems)
            {
                cbLap.Items.Add(it);
                if (it.Value == slot.Lap) selLap = cbLap.Items.Count - 1;
            }
            cbLap.SelectedIndex = selLap;
            cbLap.IsEnabled = lapItems.Count > 0;
            cbLap.SelectionChanged += (s, e) =>
            {
                if (_building || cbLap.SelectedItem == null) return;
                slot.Lap = (TeleLapRef)((Item)cbLap.SelectedItem).Value;
                slot.Note = null;
                Reload(true);
            };
            grid.Children.Add(cbLap);

            // Summary
            var info = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                                       TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(info, 4);
            if (slot.Lap == null)
                info.Inlines.Add(new System.Windows.Documents.Run(laps.Count == 0
                    ? "aucune télémétrie sur ce circuit" : "aucun tour") { Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), FontStyle = FontStyles.Italic });
            else
            {
                info.Inlines.Add(new System.Windows.Documents.Run(Fmt.LapSec(slot.Lap.LapSec)) { Foreground = brush, FontWeight = FontWeights.SemiBold });
                if (index == 0)
                    info.Inlines.Add(new System.Windows.Documents.Run("   référence") { Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) });
                else if (_slots[0].Lap != null)
                {
                    double gap = slot.Lap.LapSec - _slots[0].Lap.LapSec;
                    info.Inlines.Add(new System.Windows.Documents.Run(string.Format(Inv, "   {0}{1:0.000} s", gap >= 0 ? "+" : "−", Math.Abs(gap)))
                    {
                        Foreground = new SolidColorBrush(gap <= 0 ? Color.FromRgb(0x66, 0xBB, 0x6A) : Color.FromRgb(0xEF, 0x9A, 0x9A)),
                    });
                }
                if (!string.IsNullOrEmpty(slot.Note))
                    info.Inlines.Add(new System.Windows.Documents.Run("   (" + slot.Note + ")") { Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), FontStyle = FontStyles.Italic });
            }
            grid.Children.Add(info);

            if (_slots.Count > 1)
            {
                var remove = new TextBlock { Text = "✕", FontSize = 12, Opacity = 0.6, Cursor = Cursors.Hand, ToolTip = "Retirer ce tour",
                                             VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                remove.MouseLeftButtonDown += (s, e) => { _slots.Remove(slot); Reload(true); e.Handled = true; };
                Grid.SetColumn(remove, 5);
                grid.Children.Add(remove);
            }
            return grid;
        }

        private static ComboBox Combo(int column)
        {
            var cb = new ComboBox { Height = 24, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                                    MaxDropDownHeight = 420 };
            Grid.SetColumn(cb, column);
            return cb;
        }

        /// <summary>
        /// One session: every lap in order, flagged. All sessions: clean laps from the fastest
        /// (a full history would be thousands of laps).
        /// </summary>
        private static List<Item> LapItems(List<TeleLapRef> laps, string sessionKey)
        {
            var items = new List<Item>();
            if (sessionKey == null)
            {
                var clean = laps.FindAll(l => l.IsClean);
                clean.Sort((a, b) => a.LapSec.CompareTo(b.LapSec));
                for (int i = 0; i < clean.Count && i < 300; i++)
                {
                    var l = clean[i];
                    items.Add(new Item
                    {
                        Value = l,
                        Text = string.Format("{0}{1}   T{2} · {3}", Fmt.LapSec(l.LapSec), i == 0 ? " ★" : "", l.LapNum, l.SessionLabel),
                    });
                }
                return items;
            }
            var list = laps.FindAll(l => l.SessionKey == sessionKey);
            list.Sort((a, b) => a.LapNum.CompareTo(b.LapNum));
            var best = BestLap(laps, sessionKey);
            foreach (var l in list)
            {
                var remark = l.Remark;
                string time = remark == "invalide" ? "(" + Fmt.LapSec(l.LapSec) + ")" : Fmt.LapSec(l.LapSec);
                items.Add(new Item
                {
                    Value = l,
                    Text = string.Format("T{0,-3}  {1}{2}{3}", l.LapNum, time, l == best ? "  ★" : "",
                                         remark != null ? "   " + remark : ""),
                });
            }
            return items;
        }

        // ─── Data ─────────────────────────────────────────────────────────────

        private void Reload(bool keepView)
        {
            AssignColors();
            SyncPresets();
            BuildSlotRows();

            var traces = new List<TelemetryChart.Trace>();
            foreach (var s in _slots)
            {
                s.Trace = null;
                if (s.Lap == null) continue;
                try { s.Trace = TelemetryService.LoadTrace(s.Lap); }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("[TeamPace] Trace load: " + ex.Message);
                    continue;
                }
                traces.Add(new TelemetryChart.Trace { Data = s.Trace, Color = s.Color });
            }

            // Time gap to the first lap, at each of its points
            if (traces.Count > 0)
            {
                var reference = traces[0].Data;
                for (int k = 1; k < traces.Count; k++)
                {
                    var other = traces[k].Data;
                    var delta = new float[reference.N];
                    for (int i = 0; i < reference.N; i++)
                    {
                        double t = other.TimeAt(reference.DistAt(i));
                        delta[i] = double.IsNaN(t) ? float.NaN : (float)(t - reference.Time[i]);
                    }
                    traces[k].Delta = delta;
                }
            }

            bool any = false;
            foreach (var kv in _laps) any |= kv.Value.Count > 0;
            _empty.Visibility = traces.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _empty.Text = any
                ? "Choisissez un tour ci-dessus."
                : "Aucune télémétrie sur ce circuit pour les pilotes affichés.\n\nLMU enregistre la télémétrie de chaque relais : cliquez sur « ⟳ Tout mettre à jour » " +
                  "pour l'extraire et récupérer celle de vos coéquipiers. Elle est aussi extraite automatiquement à la fin de chaque session.";
            _chart.Visibility = traces.Count == 0 ? Visibility.Hidden : Visibility.Visible;

            _chart.SetData(traces, VisibleList(), keepView);
            var refSlot = _slots.Find(s => s.Trace != null);
            _map.SetTrace(refSlot != null ? refSlot.Trace : null, refSlot != null ? refSlot.Color : Colors.White);
            _map.SetView(_chart.ViewStart, _chart.ViewEnd);
            UpdateReadout(double.NaN);
        }

        /// <summary>Values under the cursor, or each lap's summary when the mouse is elsewhere.</summary>
        private void UpdateReadout(double dist)
        {
            _readout.Children.Clear();
            bool cursor = !double.IsNaN(dist);
            _readout.Children.Add(new TextBlock
            {
                Text = cursor ? string.Format(Inv, "À {0:0} m", dist) : "Survolez les courbes pour lire les valeurs",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 6),
            });

            Slot refSlot = _slots.Count > 0 && _slots[0].Trace != null ? _slots[0] : null;
            foreach (var s in _slots)
            {
                if (s.Trace == null) continue;
                var t = s.Trace;
                var box = new Border
                {
                    BorderBrush = new SolidColorBrush(s.Color), BorderThickness = new Thickness(3, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)),
                    Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 5),
                };
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}  ·  T{1}  ·  {2}", s.Driver.Name, s.Lap.LapNum, Fmt.LapSec(s.Lap.LapSec)),
                    FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(s.Color),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                string line;
                if (cursor && dist >= t.FirstDist && dist <= t.DistAt(t.N - 1))
                {
                    int i = t.IndexAt(dist);
                    string gap = "";
                    if (refSlot != null && s != refSlot)
                    {
                        double tr = refSlot.Trace.TimeAt(dist), ts = t.TimeAt(dist);
                        if (!double.IsNaN(tr) && !double.IsNaN(ts))
                            gap = string.Format(Inv, "Δ {0}{1:0.000} s   ", ts - tr >= 0 ? "+" : "−", Math.Abs(ts - tr));
                    }
                    line = string.Format(Inv, "{0}{1:0} km/h   rapport {2}\naccél. {3:0} %   frein {4:0} %   volant {5:+0;-0;0} %   {6:0} tr/min",
                                         gap, t.Speed[i], t.Gear[i] < 0 ? "R" : t.Gear[i] == 0 ? "N" : t.Gear[i].ToString(Inv),
                                         t.Throttle[i], t.Brake[i], t.Steer[i], t.Rpm[i]);
                }
                else if (cursor) line = "hors de ce tour";
                else
                {
                    float top = 0;
                    for (int i = 0; i < t.N; i++) top = Math.Max(top, t.Speed[i]);
                    line = string.Format(Inv, "{0}\nV. max {1:0} km/h{2}", s.Lap.SessionLabel, top,
                                         s.Lap.Remark != null ? "   ·   " + s.Lap.Remark : "");
                }
                panel.Children.Add(new TextBlock { Text = line, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                                                   Margin = new Thickness(0, 2, 0, 0) });
                box.Child = panel;
                _readout.Children.Add(box);
            }
        }
    }
}
