using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace User.TeamPace
{
    public static class LmuXmlParser
    {
        private static readonly string[] _candidatePaths = new[]
        {
            @"F:\Program Files (x86)\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
            @"C:\Program Files (x86)\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
            @"D:\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
            @"E:\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
            @"G:\Steam\steamapps\common\Le Mans Ultimate\UserData\Log\Results",
        };

        // FileSystemWatcher for live session detection
        private static FileSystemWatcher _watcher;
        private static string            _watchPlayerName;

        // ─── Discovery ─────────────────────────────────────────────────────────

        public static string AutoDiscoverResultsPath()
        {
            foreach (var path in _candidatePaths)
                if (Directory.Exists(path)) return path;
            return "";
        }

        // ─── Bulk import ───────────────────────────────────────────────────────

        /// <summary>Scans the directory, imports all XML files not yet parsed.</summary>
        /// <returns>Number of sessions imported (player found).</returns>
        public static int ScanAndImport(string resultsDir, string playerName,
                                        Action<string> log = null)
        {
            if (!Directory.Exists(resultsDir))
            {
                Log(log, "[LmuXml] Results directory not found: {0}", resultsDir);
                return 0;
            }
            if (string.IsNullOrWhiteSpace(playerName))
            {
                Log(log, "[LmuXml] Player name is empty — cannot import sessions");
                return 0;
            }

            string[] files;
            try { files = Directory.GetFiles(resultsDir, "*.xml", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                Log(log, "[LmuXml] Cannot list files: {0}", ex.Message);
                return 0;
            }

            int imported = 0;
            int skipped  = 0;

            foreach (var file in files)
            {
                var fileId = Path.GetFileNameWithoutExtension(file);
                if (LmuSessionStore.IsFileParsed(fileId)) { skipped++; continue; }

                try
                {
                    LmuSessionRecord session;
                    List<LmuLapRecord> laps;
                    if (ParseFile(file, playerName, out session, out laps))
                    {
                        LmuSessionStore.SaveSession(session, laps);
                        imported++;
                    }
                    else
                    {
                        // Player not found — mark as parsed so we don't re-read
                        LmuSessionStore.MarkFileParsed(fileId);
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "[LmuXml] Error parsing {0}: {1}", fileId, ex.Message);
                    LmuSessionStore.MarkFileParsed(fileId); // avoid infinite retry
                }
            }

            Log(log,
                "[LmuXml] Scan done — {0} imported, {1} skipped (total files: {2})",
                imported, skipped, files.Length);
            return imported;
        }

        // ─── Live watcher ──────────────────────────────────────────────────────

        public static void StartWatcher(string resultsDir, string playerName)
        {
            StopWatcher();
            if (!Directory.Exists(resultsDir) || string.IsNullOrWhiteSpace(playerName)) return;

            _watchPlayerName = playerName;
            _watcher = new FileSystemWatcher(resultsDir, "*.xml")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileChanged;
        }

        public static void StopWatcher()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Changed -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        private static void OnFileCreated(object sender, FileSystemEventArgs e)
            { ImportSingleFile(e.FullPath); }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
            { ImportSingleFile(e.FullPath); }

        private static void ImportSingleFile(string filePath)
        {
            var fileId = Path.GetFileNameWithoutExtension(filePath);
            if (LmuSessionStore.IsFileParsed(fileId)) return;

            // Small delay so the game finishes writing the file
            System.Threading.Thread.Sleep(2000);
            try
            {
                LmuSessionRecord session;
                List<LmuLapRecord> laps;
                if (ParseFile(filePath, _watchPlayerName, out session, out laps))
                {
                    LmuSessionStore.SaveSession(session, laps);
                    LmuTeamSync.ExportAsync();
                }
                else
                    LmuSessionStore.MarkFileParsed(fileId);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(
                    string.Format("[LmuXml] Watcher error on {0}: {1}", fileId, ex.Message));
                LmuSessionStore.MarkFileParsed(fileId);
            }
        }

        // ─── Single file parse ─────────────────────────────────────────────────

        public static bool ParseFile(string filePath, string playerName,
                                     out LmuSessionRecord session,
                                     out List<LmuLapRecord> laps)
        {
            session = null;
            laps    = new List<LmuLapRecord>();

            var fileId = Path.GetFileNameWithoutExtension(filePath);

            XDocument xdoc;
            try { xdoc = XDocument.Load(filePath); }
            catch { return false; }

            // Root is <rFactorXML>; header fields live in <RaceResults> child
            var root   = xdoc.Root;
            var header = root.Element("RaceResults") ?? root;

            // ── Session date from filename + XML override ──────────────────────
            var sessionDate = ExtractDateFromFilename(fileId);
            var dtElem      = header.Element("DateTime");
            if (dtElem != null)
            {
                double unix;
                if (double.TryParse(dtElem.Value, NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out unix) && unix > 0)
                    sessionDate = DateTimeOffset.FromUnixTimeSeconds((long)unix).UtcDateTime;
            }

            session = new LmuSessionRecord
            {
                FileId          = fileId,
                SessionType     = ExtractSessionType(fileId),
                SessionDate     = sessionDate,
                TrackVenue      = GetText(header, "TrackVenue"),
                TrackCourse     = GetText(header, "TrackCourse"),
                TrackEvent      = GetText(header, "TrackEvent"),
                TrackLengthM    = GetFloat(header, "TrackLength"),
                Setting         = GetText(header, "Setting"),
                ServerName      = GetText(header, "ServerName"),
                RaceTimeMinutes = GetFloat(header, "RaceTime"),
                RaceLaps        = GetInt(header,   "RaceLaps"),
                DamageMult      = GetFloatDef(header, "DamageMult",   1f),
                FuelMult        = GetFloatDef(header, "FuelMult",     1f),
                TireMult        = GetFloatDef(header, "TireMult",     1f),
                MechFailRate    = GetFloat(header, "MechFailRate"),
                PlayerName      = playerName,
            };

            // ── Locate the session block — LMU numbers practice blocks ("Practice1") ──
            XElement block = null;
            foreach (var child in header.Elements())
            {
                var n = child.Name.LocalName;
                if ((n.StartsWith("Race")    || n.StartsWith("Qualify") ||
                     n.StartsWith("Practice") || n.StartsWith("Warmup")) &&
                    child.Element("Driver") != null)
                { block = child; break; }
            }
            if (block == null) block = header;

            double blockUnix;
            var blockDt = block.Element("DateTime");
            if (blockDt != null && double.TryParse(blockDt.Value, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out blockUnix) && blockUnix > 0)
                session.BlockStartUtc = DateTimeOffset.FromUnixTimeSeconds((long)blockUnix).UtcDateTime;

            // ── Count player incidents and penalties from Stream ───────────────
            var stream = block.Element("Stream") ?? header.Element("Stream");
            if (stream != null)
            {
                foreach (var ev in stream.Elements("Incident"))
                    if (StartsWithName(ev.Value, playerName)) session.IncidentCount++;
                foreach (var ev in stream.Elements("Penalty"))
                    if (StartsWithName(ev.Value, playerName)) session.PenaltyCount++;
            }

            // ── Find the player's <Driver> element ────────────────────────────
            XElement playerDriver = null;
            foreach (var drv in block.Elements("Driver"))
            {
                var nameEl = drv.Element("Name");
                if (nameEl == null) continue;
                var drvName = nameEl.Value.Trim();
                // Exact match first, then contains (clan tag / server suffix)
                if (string.Equals(drvName, playerName, StringComparison.OrdinalIgnoreCase) ||
                    drvName.IndexOf(playerName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    playerName.IndexOf(drvName,  StringComparison.OrdinalIgnoreCase) >= 0)
                { playerDriver = drv; break; }
            }

            if (playerDriver == null)
            {
                session = null;
                return false;
            }

            // ── Parse driver result ────────────────────────────────────────────
            session.CarType       = GetText(playerDriver, "CarType");
            session.CarClass      = GetText(playerDriver, "CarClass");
            session.TeamName      = GetText(playerDriver, "TeamName");
            session.GridPos       = GetInt(playerDriver,  "GridPos");
            session.ClassGridPos  = GetInt(playerDriver,  "ClassGridPos");
            session.Position      = GetInt(playerDriver,  "Position");
            session.ClassPosition = GetInt(playerDriver,  "ClassPosition");
            session.TotalLaps     = GetInt(playerDriver,  "Laps");
            session.Pitstops      = GetInt(playerDriver,  "Pitstops");
            session.FinishStatus  = GetText(playerDriver, "FinishStatus");
            session.DNFReason     = GetText(playerDriver, "DNFReason");
            session.ControlAndAids= GetText(playerDriver, "ControlAndAids");

            int carNum;
            int.TryParse(GetText(playerDriver, "CarNumber"), out carNum);
            session.CarNumber = carNum;

            var bestLapSec = GetFloat(playerDriver, "BestLapTime");
            session.BestLapTime = bestLapSec > 0
                ? TimeSpan.FromSeconds(bestLapSec) : TimeSpan.Zero;

            var finishSec = GetFloat(playerDriver, "FinishTime");
            session.FinishTime = finishSec > 0
                ? TimeSpan.FromSeconds(finishSec) : TimeSpan.Zero;

            // ── Parse per-lap data ─────────────────────────────────────────────
            foreach (var lapEl in playerDriver.Elements("Lap"))
                laps.Add(ParseLapElement(lapEl, fileId));

            return true;
        }

        // ─── <Lap> parsing ─────────────────────────────────────────────────────

        private static LmuLapRecord ParseLapElement(XElement el, string sessionId)
        {
            var lap = new LmuLapRecord { SessionId = sessionId };

            int lapNum; int.TryParse(Attr(el, "num"), out lapNum);
            lap.LapNum = lapNum;

            int pos; int.TryParse(Attr(el, "p"), out pos);
            lap.Position = pos;

            float et; float.TryParse(Attr(el, "et"), NumberStyles.Any,
                                     CultureInfo.InvariantCulture, out et);
            lap.ElapsedTime = et;

            lap.S1          = AttrFloat(el, "s1",        -1f);
            lap.S2          = AttrFloat(el, "s2",        -1f);
            lap.S3          = AttrFloat(el, "s3",        -1f);
            lap.TopSpeedKph = AttrFloat(el, "topspeed",   0f);
            lap.FuelRemaining     = AttrFloat(el, "fuel",    0f);
            lap.FuelUsed          = AttrFloat(el, "fuelUsed",0f);
            lap.VirtualEnergy     = AttrFloat(el, "ve",      0f);
            lap.VirtualEnergyUsed = AttrFloat(el, "veUsed",  0f);
            lap.TireWearFL = AttrFloat(el, "twfl", 0f);
            lap.TireWearFR = AttrFloat(el, "twfr", 0f);
            lap.TireWearRL = AttrFloat(el, "twrl", 0f);
            lap.TireWearRR = AttrFloat(el, "twrr", 0f);
            lap.FrontCompound = Attr(el, "fcompound") ?? "";
            lap.RearCompound  = Attr(el, "rcompound") ?? "";
            lap.IsPitLap      = Attr(el, "pit") == "1";

            // Lap time from inner text: "157.1803" or "--.----"
            var timeText = (el.Value ?? "").Trim();
            if (timeText != "--.----" && timeText != "--.-" && timeText.Length > 0)
            {
                float sec;
                if (float.TryParse(timeText, NumberStyles.Any,
                                   CultureInfo.InvariantCulture, out sec) && sec > 0)
                {
                    lap.LapTimeTicks = TimeSpan.FromSeconds(sec).Ticks;
                    lap.IsValid      = true;
                }
            }

            return lap;
        }

        // ─── Filename helpers ──────────────────────────────────────────────────

        private static string ExtractSessionType(string fileId)
        {
            if (fileId.EndsWith("R1", StringComparison.OrdinalIgnoreCase)) return "R1";
            if (fileId.EndsWith("R2", StringComparison.OrdinalIgnoreCase)) return "R2";
            if (fileId.EndsWith("Q1", StringComparison.OrdinalIgnoreCase)) return "Q1";
            if (fileId.EndsWith("Q2", StringComparison.OrdinalIgnoreCase)) return "Q2";
            if (fileId.EndsWith("P1", StringComparison.OrdinalIgnoreCase)) return "P1";
            if (fileId.EndsWith("P2", StringComparison.OrdinalIgnoreCase)) return "P2";
            return "P1";
        }

        private static DateTime ExtractDateFromFilename(string fileId)
        {
            // Pattern: 2025_03_15_15_53_17-03R1
            if (fileId.Length >= 19)
            {
                DateTime dt;
                if (DateTime.TryParseExact(fileId.Substring(0, 19),
                    "yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt))
                    return dt;
            }
            return DateTime.MinValue;
        }

        // ─── XML helpers ───────────────────────────────────────────────────────

        private static string GetText(XElement parent, string name)
        {
            var el = parent == null ? null : parent.Element(name);
            return el == null ? "" : (el.Value ?? "").Trim();
        }

        private static float GetFloat(XElement parent, string name)
        {
            float v; float.TryParse(GetText(parent, name), NumberStyles.Any,
                                     CultureInfo.InvariantCulture, out v); return v;
        }

        private static float GetFloatDef(XElement parent, string name, float def)
        {
            var s = GetText(parent, name);
            if (string.IsNullOrEmpty(s)) return def;
            float v;
            return float.TryParse(s, NumberStyles.Any,
                                  CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static int GetInt(XElement parent, string name)
        {
            int v; int.TryParse(GetText(parent, name), out v); return v;
        }

        private static string Attr(XElement el, string name)
        {
            var a = el.Attribute(name); return a == null ? null : a.Value;
        }

        private static float AttrFloat(XElement el, string name, float def)
        {
            var s = Attr(el, name);
            if (s == null) return def;
            float v;
            return float.TryParse(s, NumberStyles.Any,
                                  CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static bool StartsWithName(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return false;
            return text.StartsWith(name, StringComparison.OrdinalIgnoreCase);
        }

        private static void Log(Action<string> log, string format, params object[] args)
        {
            var msg = string.Format(format, args);
            SimHub.Logging.Current.Info(msg);
            if (log != null) log(msg);
        }
    }
}
