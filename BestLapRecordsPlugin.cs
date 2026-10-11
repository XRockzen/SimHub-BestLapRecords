using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace User.BestLapRecords
{
    [PluginDescription("Hall of Records - Best laps and best rhythm per game / category / track / car")]
    [PluginAuthor("Amaury")]
    [PluginName("Best Lap Records")]
    public class BestLapRecordsPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        internal List<BestLapRecord> AllRecords;
        internal bool   IsLoading;
        internal bool   IsImporting;
        internal string LoadError;
        internal string PluginsDataPath;
        internal string LastGame;
        internal int    LmuSessionImportedCount;

        private BestLapRecord _currentRecord;
        private string        _lastKey;

        // ─── Invalid lap tracking ─────────────────────────────────────────────
        private int    _lastCompletedLaps  = -1;
        private string _lastTrackForSession = null;
        private string _sessionId           = null;   // our own session GUID

        private readonly LmuLiveTracker _liveTracker = new LmuLiveTracker();

        public BestLapRecordsPlugin()
        {
            AllRecords = new List<BestLapRecord>();
            IsLoading  = true;
        }

        public PluginManager PluginManager { get; set; }

        public ImageSource PictureIcon  { get { return null; } }
        public string      LeftMenuTitle{ get { return "Best Laps"; } }

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("[BestLapRecords] Starting plugin");

            this.AttachDelegate("CurrentBestLap",    () => _currentRecord != null ? _currentRecord.BestLapFormatted    : "-");
            this.AttachDelegate("CurrentBestRhythm", () => _currentRecord != null ? _currentRecord.BestRhythmFormatted : "-");
            this.AttachDelegate("CurrentTotalLaps",  () => _currentRecord != null ? _currentRecord.TotalLaps : 0);
            this.AttachDelegate("IsLoading",         () => IsLoading);

            var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            PluginsDataPath = Path.Combine(exeDir, "PluginsData");

            var commonPath = Path.Combine(PluginsDataPath, "Common");
            InvalidLapStore.Init(commonPath);
            LmuSessionStore.Init(commonPath);

            var dataPath = PluginsDataPath;
            Task.Run(() =>
            {
                try
                {
                    AllRecords = RecordsService.LoadAllRecords(dataPath);
                    var newest = AllRecords.OrderByDescending(r => r.RecordDate).FirstOrDefault();
                    LastGame = newest != null ? newest.Game : null;
                    SimHub.Logging.Current.Info(
                        string.Format("[BestLapRecords] Loaded {0} records — last game: {1}", AllRecords.Count, LastGame));
                }
                catch (Exception ex)
                {
                    LoadError = ex.Message;
                    SimHub.Logging.Current.Error(string.Format("[BestLapRecords] Load error: {0}", ex));
                }
                finally { IsLoading = false; }
            });

            // LMU XML import (background, does not block plugin startup)
            Task.Run(() => StartLmuImport());

            PluginUpdater.CleanupPrevious();
            PluginUpdater.CheckInBackground();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (data.GameRunning && data.GameName == "LMU")
                _liveTracker.Update(data);

            if (IsLoading || data.NewData == null) return;

            // ── Current-record tracking (sidebar delegate) ────────────────────
            var track = data.NewData.TrackName;
            var car   = data.NewData.CarModel;
            if (!string.IsNullOrEmpty(track) && !string.IsNullOrEmpty(car))
            {
                var key = string.Format("{0}|{1}|{2}", data.GameName, track, car);
                if (key != _lastKey)
                {
                    _lastKey = key;
                    _currentRecord = AllRecords.FirstOrDefault(r =>
                        string.Equals(r.Track, track, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(r.CarId,          car, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(r.CarDisplayName, car, StringComparison.OrdinalIgnoreCase)));
                }
            }

            // ── Invalid lap capture ───────────────────────────────────────────
            TrackInvalidLaps(data);
        }

        private void TrackInvalidLaps(GameData data)
        {
            var newData = data.NewData;
            if (newData == null) return;

            string lapTrack = newData.TrackName ?? "";
            int completedLaps = newData.CompletedLaps;

            // Detect session change: new track or lap counter reset
            bool sessionChanged = (lapTrack != _lastTrackForSession)
                               || (completedLaps >= 0 && _lastCompletedLaps >= 0 && completedLaps < _lastCompletedLaps);

            if (sessionChanged)
            {
                _sessionId           = Guid.NewGuid().ToString("N");
                _lastCompletedLaps   = -1;
                _lastTrackForSession = lapTrack;
            }

            // Negative means game not active / no data yet
            if (completedLaps < 0)
            {
                _lastCompletedLaps = -1;
                return;
            }

            // Detect lap completion (CompletedLaps increased by at least 1)
            if (_lastCompletedLaps >= 0 && completedLaps > _lastCompletedLaps)
            {
                TimeSpan lastLapTime = newData.LastLapTime;
                bool isInvalid = lastLapTime.Ticks <= 0;

                if (isInvalid)
                {
                    string carId = newData.CarId ?? newData.CarModel ?? "";
                    string game  = data.GameName ?? "";

                    if (!string.IsNullOrEmpty(lapTrack) && !string.IsNullOrEmpty(carId) && !string.IsNullOrEmpty(game))
                    {
                        InvalidLapStore.AddLap(game, lapTrack, carId,
                            completedLaps, _sessionId ?? "", DateTime.UtcNow);

                        SimHub.Logging.Current.Info(
                            string.Format("[BestLapRecords] Captured invalid lap #{0} — {1} / {2}",
                                completedLaps, lapTrack, carId));
                    }
                }
            }

            _lastCompletedLaps = completedLaps;
        }

        public void End(PluginManager pluginManager)
        {
            LmuXmlParser.StopWatcher();
            LmuTeamSync.Export();
        }

        // ─── LMU XML import ───────────────────────────────────────────────────

        private void StartLmuImport()
        {
            IsImporting = true;
            try
            {
                var resultsPath = LmuSessionStore.ResultsPath;
                var playerName  = LmuSessionStore.PlayerName;

                // Auto-discover path if not configured
                if (string.IsNullOrEmpty(resultsPath))
                    resultsPath = LmuXmlParser.AutoDiscoverResultsPath();

                if (string.IsNullOrEmpty(resultsPath))
                {
                    SimHub.Logging.Current.Info(
                        "[BestLapRecords] LMU results path not found — set it in plugin settings");
                    return;
                }

                if (string.IsNullOrEmpty(playerName))
                {
                    SimHub.Logging.Current.Info(
                        "[BestLapRecords] LMU player name not set — configure it in plugin settings");
                    return;
                }

                int imported = LmuXmlParser.ScanAndImport(resultsPath, playerName);
                LmuSessionImportedCount = LmuSessionStore.GetSessionCount();

                // Watch for new sessions after a race
                LmuXmlParser.StartWatcher(resultsPath, playerName);

                LmuTeamSync.PullTeammates();
                LmuTeamSync.Export();

                if (imported > 0)
                    SimHub.Logging.Current.Info(
                        string.Format("[BestLapRecords] LMU import done: {0} new sessions", imported));
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(
                    string.Format("[BestLapRecords] LMU import error: {0}", ex.Message));
            }
            finally
            {
                IsImporting = false;
            }
        }

        internal void TriggerLmuRescan()
        {
            Task.Run(() => StartLmuImport());
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new BestLapRecordsControl(this);
        }

        internal void Reload()
        {
            IsLoading = true;
            LoadError = null;
            AllRecords = new List<BestLapRecord>();
            LastGame   = null;
            _lastKey   = null;
            _currentRecord = null;

            var dataPath = PluginsDataPath;
            Task.Run(() =>
            {
                try
                {
                    AllRecords = RecordsService.LoadAllRecords(dataPath);
                    var newest = AllRecords.OrderByDescending(r => r.RecordDate).FirstOrDefault();
                    LastGame = newest != null ? newest.Game : null;
                }
                catch (Exception ex) { LoadError = ex.Message; }
                finally { IsLoading = false; }
            });
        }
    }
}
