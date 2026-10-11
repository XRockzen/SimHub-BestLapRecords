using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace User.TeamPace
{
    public static class RecordsService
    {
        private static readonly Dictionary<string, string> GameDisplayNames;

        static RecordsService()
        {
            GameDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            GameDisplayNames.Add("AssettoCorsa", "Assetto Corsa");
            GameDisplayNames.Add("AssettoCorsaCompetizione", "ACC");
            GameDisplayNames.Add("Automobilista2", "Automobilista 2");
            GameDisplayNames.Add("CodemastersDirtRally2", "Dirt Rally 2");
            GameDisplayNames.Add("IRacing", "iRacing");
            GameDisplayNames.Add("LMU", "Le Mans Ultimate");
            GameDisplayNames.Add("RfactorReader", "rFactor 2");
        }

        // ─── Load all records ────────────────────────────────────────────────

        public static List<BestLapRecord> LoadAllRecords(string pluginsDataPath)
        {
            var records = new List<BestLapRecord>();
            if (!Directory.Exists(pluginsDataPath)) return records;

            CarNameResolver.LoadAliases(Path.Combine(pluginsDataPath, "Common"));

            foreach (var dir in Directory.GetDirectories(pluginsDataPath))
            {
                var dbPath = Path.Combine(dir, "DataStore.db");
                if (!File.Exists(dbPath)) continue;

                var folderName = Path.GetFileName(dir);
                string gameName;
                if (!GameDisplayNames.TryGetValue(folderName, out gameName))
                    gameName = folderName;

                try
                {
                    records.AddRange(LoadGameRecords(dbPath, gameName, folderName));
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn(
                        string.Format("[TeamPace] Failed to load {0}: {1}", dbPath, ex.Message));
                }
            }

            return records.OrderBy(r => r.Game)
                          .ThenBy(r => r.Category)
                          .ThenBy(r => r.Track)
                          .ThenBy(r => r.CarDisplayName)
                          .ToList();
        }

        private static List<BestLapRecord> LoadGameRecords(string dbPath, string gameName, string gameFolderName)
        {
            var results = new List<BestLapRecord>();

            using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", dbPath)))
            {
                var col = db.GetCollection("ptp_datarecords");
                var rawLaps = new List<RawLap>();

                foreach (var doc in col.FindAll())
                {
                    // Replays are never real laps
                    if (doc.ContainsKey("IsReplay") && doc["IsReplay"].AsBoolean) continue;

                    long lapTime = doc.ContainsKey("LapTime") ? doc["LapTime"].AsInt64 : 0;

                    var track = doc.ContainsKey("TrackName") ? doc["TrackName"].AsString : null;
                    var carId = doc.ContainsKey("CarId")     ? doc["CarId"].AsString     : null;
                    if (string.IsNullOrEmpty(track) || string.IsNullOrEmpty(carId)) continue;

                    // LapTime=0 means LMU invalidated the lap and didn't report the time.
                    // These are real driven laps — count them but don't use for best/rhythm.
                    bool isDiscarded = doc.ContainsKey("IsDiscarded") && doc["IsDiscarded"].AsBoolean;
                    // Treat zero-time as discarded for best/rhythm calculation
                    if (lapTime <= 0) isDiscarded = true;

                    var lap = new RawLap();
                    lap.Track       = track;
                    lap.CarId       = carId;
                    lap.SessionId   = doc.ContainsKey("SessionId")  ? doc["SessionId"].AsString   : "";
                    lap.LapNumber   = doc.ContainsKey("LapNumber")  ? doc["LapNumber"].AsInt32    : 0;
                    lap.LapTime     = lapTime;
                    lap.RecordDate  = doc.ContainsKey("RecordDate") ? doc["RecordDate"].AsDateTime : DateTime.MinValue;
                    lap.IsDiscarded = isDiscarded;
                    rawLaps.Add(lap);
                }

                foreach (var group in rawLaps.GroupBy(l => new { l.Track, l.CarId }))
                {
                    var laps      = group.ToList();
                    var validLaps = laps.Where(l => !l.IsDiscarded).ToList();

                    // Best lap and rhythm only from valid laps
                    long bestTicks   = long.MaxValue;
                    DateTime bestDate = DateTime.MinValue;
                    foreach (var l in validLaps)
                    {
                        if (l.LapTime < bestTicks) { bestTicks = l.LapTime; bestDate = l.RecordDate; }
                    }

                    if (bestTicks == long.MaxValue) continue;   // only invalid laps for this car — skip

                    var carInfo = CarNameResolver.Resolve(gameFolderName, group.Key.CarId);
                    long? rhythm = ComputeBestRhythm(validLaps, 5);

                    var rec = new BestLapRecord();
                    rec.Game           = gameName;
                    rec.GameFolderName = gameFolderName;
                    rec.Track          = group.Key.Track;
                    rec.Category       = carInfo.Category;
                    rec.CarId          = group.Key.CarId;
                    rec.CarDisplayName = carInfo.Model;
                    rec.BestLap        = TimeSpan.FromTicks(bestTicks);
                    rec.BestRhythm     = rhythm.HasValue ? TimeSpan.FromTicks(rhythm.Value) : (TimeSpan?)null;
                    rec.RecordDate     = bestDate;
                    long totalTimeTicks = 0;
                    foreach (var l in validLaps) totalTimeTicks += l.LapTime;
                    rec.TotalTime      = TimeSpan.FromTicks(totalTimeTicks);
                    // Add supplementary invalid laps captured at runtime
                    int suppCount = 0;
                    var suppCounts = InvalidLapStore.GetCountsPerTrackCar(gameFolderName);
                    var suppKey = group.Key.Track + "|" + group.Key.CarId;
                    suppCounts.TryGetValue(suppKey, out suppCount);

                    rec.TotalLaps = laps.Count + suppCount;
                    results.Add(rec);
                }
            }

            return results;
        }

        // ─── Load individual lap times (Level 3) ─────────────────────────────

        public static List<LapTimeEntry> LoadLapTimes(
            string pluginsDataPath,
            string gameFolderName,
            string trackName,
            List<string> carIds,
            Dictionary<string, string> carIdToCategory = null)
        {
            var entries = new List<LapTimeEntry>();
            var dbPath = Path.Combine(pluginsDataPath, gameFolderName, "DataStore.db");
            if (!File.Exists(dbPath)) return entries;

            var carIdSet = new HashSet<string>(carIds, StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", dbPath)))
                {
                    var col = db.GetCollection("ptp_datarecords");

                    foreach (var doc in col.FindAll())
                    {
                        // Replays are not real driven laps — always excluded
                        if (doc.ContainsKey("IsReplay") && doc["IsReplay"].AsBoolean) continue;

                        var track = doc.ContainsKey("TrackName") ? doc["TrackName"].AsString : null;
                        if (!string.Equals(track, trackName, StringComparison.OrdinalIgnoreCase)) continue;

                        var carId = doc.ContainsKey("CarId") ? doc["CarId"].AsString : null;
                        if (carId == null || !carIdSet.Contains(carId)) continue;

                        long lapTime = doc.ContainsKey("LapTime") ? doc["LapTime"].AsInt64 : 0;

                        // Invalid (discarded) laps are included but flagged.
                        // LapTime=0 means the game invalidated the lap and didn't report the time.
                        // These are real driven laps — show them with "—" time.
                        bool isDiscarded = doc.ContainsKey("IsDiscarded") && doc["IsDiscarded"].AsBoolean;
                        // Zero-time laps are always treated as invalid (no usable time)
                        if (lapTime <= 0) isDiscarded = true;

                        var entry = new LapTimeEntry();
                        entry.LapNumber      = doc.ContainsKey("LapNumber")  ? doc["LapNumber"].AsInt32    : 0;
                        entry.LapTime        = TimeSpan.FromTicks(lapTime);
                        entry.IsValid        = !isDiscarded;
                        entry.TeamEntry      = carId;
                        entry.CarModel       = CarNameResolver.Resolve(gameFolderName, carId).Model;
                        entry.RecordDate     = doc.ContainsKey("RecordDate") ? doc["RecordDate"].AsDateTime : DateTime.MinValue;
                        entry.SessionId      = doc.ContainsKey("SessionId")  ? doc["SessionId"].AsString   : "";
                        if (carIdToCategory != null)
                        {
                            string cat;
                            carIdToCategory.TryGetValue(carId, out cat);
                            entry.Category = cat;
                        }
                        entry.TrackTemp      = ReadDouble(doc, "TrackTemperature", "TrackTemp");
                        entry.AirTemp        = ReadDouble(doc, "AmbientTemperature", "AirTemp", "AmbientTemp");
                        entry.Weather        = ReadString(doc, "WeatherStatus", "Weather");
                        entry.TrackCondition = ReadString(doc, "TrackSurface", "TrackCondition", "RoadSurface");
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(string.Format("[TeamPace] LoadLapTimes error: {0}", ex.Message));
            }

            // Merge supplementary invalid laps captured at runtime (one query per carId)
            foreach (var qCarId in carIds)
            foreach (var suppDoc in InvalidLapStore.GetLaps(gameFolderName, trackName, qCarId))
            {
                var suppCarId = suppDoc["Car"].AsString;
                if (!carIdSet.Contains(suppCarId)) continue;

                var suppEntry = new LapTimeEntry();
                suppEntry.LapNumber  = suppDoc["LapNumber"].AsInt32;
                suppEntry.LapTime    = TimeSpan.Zero;
                suppEntry.IsValid    = false;
                suppEntry.TeamEntry  = suppCarId;
                suppEntry.CarModel   = CarNameResolver.Resolve(gameFolderName, suppCarId).Model;
                suppEntry.RecordDate = suppDoc["Date"].AsDateTime;
                suppEntry.SessionId  = suppDoc["Session"].AsString;
                if (carIdToCategory != null)
                {
                    string cat;
                    carIdToCategory.TryGetValue(suppCarId, out cat);
                    suppEntry.Category = cat;
                }
                entries.Add(suppEntry);
            }

            // Normalise SessionIds so valid laps (SimHub ID) and invalid laps (XML filename / GUID)
            // from the same real session share the same SessionId before grouping.
            NormalizeSessionIds(entries);

            // Sort by lap number; use date as tiebreaker for same lap number across sessions
            entries = entries.OrderBy(e => e.LapNumber).ThenBy(e => e.RecordDate).ToList();

            // Compute delta vs best VALID lap only
            var validEntries = entries.Where(e => e.IsValid).ToList();
            if (validEntries.Count > 0)
            {
                TimeSpan best = validEntries[0].LapTime;
                foreach (var e in validEntries)
                    if (e.LapTime < best) best = e.LapTime;

                foreach (var e in entries)
                {
                    if (!e.IsValid)
                    {
                        e.DeltaFormatted = "⚠ invalide";
                    }
                    else if (e.LapTime == best)
                    {
                        e.DeltaFormatted = "★ meilleur";
                    }
                    else
                    {
                        e.DeltaFormatted = BestLapRecord.FormatDelta(e.LapTime - best);
                    }
                }
            }
            else
            {
                // Only invalid laps — still label them
                foreach (var e in entries)
                    e.DeltaFormatted = "⚠ invalide";
            }

            return entries;
        }

        // ─── Global activity (lightweight — date + track only) ───────────────

        /// <summary>
        /// Fast scan of all game DBs: returns one <see cref="ActivityLap"/> per driven lap
        /// (valid and invalid, replays excluded).
        /// Only reads TrackName + RecordDate — skips all other fields for performance.
        /// </summary>
        public static List<ActivityLap> LoadActivityLaps(string pluginsDataPath)
        {
            var result = new List<ActivityLap>();
            if (!Directory.Exists(pluginsDataPath)) return result;

            foreach (var dir in Directory.GetDirectories(pluginsDataPath))
            {
                var dbPath = Path.Combine(dir, "DataStore.db");
                if (!File.Exists(dbPath)) continue;

                var folderName = Path.GetFileName(dir);
                string gameName;
                if (!GameDisplayNames.TryGetValue(folderName, out gameName))
                    gameName = folderName;

                try
                {
                    using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", dbPath)))
                    {
                        var col = db.GetCollection("ptp_datarecords");
                        foreach (var doc in col.FindAll())
                        {
                            if (doc.ContainsKey("IsReplay") && doc["IsReplay"].AsBoolean) continue;

                            var track = doc.ContainsKey("TrackName") ? doc["TrackName"].AsString : null;
                            if (string.IsNullOrEmpty(track)) continue;

                            var date = doc.ContainsKey("RecordDate")
                                ? doc["RecordDate"].AsDateTime
                                : DateTime.MinValue;
                            if (date == DateTime.MinValue) continue;

                            long lapTime = doc.ContainsKey("LapTime") ? doc["LapTime"].AsInt64 : 0;
                            result.Add(new ActivityLap { TrackName = track, Game = gameName, Date = date, LapTime = TimeSpan.FromTicks(lapTime) });
                        }
                    }

                    // Merge invalid laps from supplementary store (not in SimHub DB)
                    foreach (var doc in InvalidLapStore.GetAllLaps(folderName))
                    {
                        var track = doc["Track"].AsString;
                        var date  = doc["Date"].AsDateTime;
                        if (string.IsNullOrEmpty(track) || date == DateTime.MinValue) continue;
                        result.Add(new ActivityLap { TrackName = track, Game = gameName, Date = date, LapTime = TimeSpan.Zero });
                    }
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn(
                        string.Format("[TeamPace] LoadActivityLaps error: {0}", ex.Message));
                }
            }

            return result;
        }

        // ─── Field readers (graceful — returns null when absent/invalid) ─────

        private static double? ReadDouble(BsonDocument doc, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!doc.ContainsKey(key)) continue;
                var v = doc[key];
                if (v.IsNull) continue;
                try
                {
                    double d = v.AsDouble;
                    if (!double.IsNaN(d) && d > -999) return d;
                }
                catch { }
            }
            return null;
        }

        private static string ReadString(BsonDocument doc, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!doc.ContainsKey(key)) continue;
                var v = doc[key];
                if (v.IsNull) continue;
                try
                {
                    var s = v.AsString;
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
                catch { }
            }
            return null;
        }

        // ─── Session ID normalisation ─────────────────────────────────────────
        // Valid laps carry SimHub's session ID; invalid laps carry our XML filename or GUID.
        // We reassign valid laps the matching invalid-session ID so they group together.

        private static void NormalizeSessionIds(List<LapTimeEntry> entries)
        {
            var invalids = entries.Where(e => !e.IsValid && !string.IsNullOrEmpty(e.SessionId)).ToList();
            if (invalids.Count == 0) return;

            // Build anchor windows per (SessionId, CarId)
            var anchors = invalids
                .GroupBy(e => new { e.SessionId, e.TeamEntry })
                .Select(g =>
                {
                    var dates = g.Where(l => l.RecordDate != DateTime.MinValue)
                                 .Select(l => l.RecordDate).ToList();
                    if (dates.Count == 0) return null;
                    DateTime lo = dates.Min();
                    DateTime hi = dates.Max();
                    // XML sessions: all dates = sessStart (Max==Min).
                    // Extend the window 6 h forward to cover all laps of the session.
                    if ((hi - lo).TotalMinutes < 1) hi = lo.AddHours(6);
                    else                             hi = hi.AddMinutes(30);
                    return new { g.Key.SessionId, g.Key.TeamEntry, Lo = lo, Hi = hi };
                })
                .Where(a => a != null)
                .ToList();

            foreach (var lap in entries.Where(e => e.IsValid))
            {
                if (lap.RecordDate == DateTime.MinValue) continue;

                var best = anchors
                    .Where(a => string.Equals(a.TeamEntry, lap.TeamEntry, StringComparison.OrdinalIgnoreCase)
                             && lap.RecordDate >= a.Lo.AddMinutes(-5)
                             && lap.RecordDate <= a.Hi)
                    .OrderBy(a => Math.Abs((lap.RecordDate - a.Lo).TotalMinutes))
                    .FirstOrDefault();

                if (best != null)
                    lap.SessionId = best.SessionId;
            }
        }

        // ─── Best 5-lap rhythm ────────────────────────────────────────────────

        private static long? ComputeBestRhythm(List<RawLap> laps, int windowSize)
        {
            long? best = null;
            foreach (var session in laps.GroupBy(l => l.SessionId))
            {
                var sorted = session.OrderBy(l => l.LapNumber).ToList();
                if (sorted.Count < windowSize) continue;

                for (int i = 0; i <= sorted.Count - windowSize; i++)
                {
                    bool ok = true;
                    for (int j = 0; j < windowSize - 1; j++)
                        if (sorted[i + j + 1].LapNumber != sorted[i + j].LapNumber + 1)
                        { ok = false; break; }
                    if (!ok) continue;

                    long sum = 0;
                    for (int j = 0; j < windowSize; j++) sum += sorted[i + j].LapTime;
                    long avg = sum / windowSize;
                    if (!best.HasValue || avg < best.Value) best = avg;
                }
            }
            return best;
        }

        // ─── Inner DTO ────────────────────────────────────────────────────────

        private class RawLap
        {
            public string   Track       { get; set; }
            public string   CarId       { get; set; }
            public string   SessionId   { get; set; }
            public int      LapNumber   { get; set; }
            public long     LapTime     { get; set; }
            public DateTime RecordDate  { get; set; }
            public bool     IsDiscarded { get; set; }
        }
    }
}
