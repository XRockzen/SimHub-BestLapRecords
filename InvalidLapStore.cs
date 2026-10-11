using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace User.BestLapRecords
{
    /// <summary>
    /// Stores invalid laps (LapTime=0) that SimHub detects but does not persist in its own DB.
    /// Written in real-time from DataUpdate; merged with SimHub records at display time.
    /// </summary>
    public static class InvalidLapStore
    {
        private static string _dbPath;
        private static readonly object _writeLock = new object();

        public static void Init(string commonPath)
        {
            var dir = Path.Combine(commonPath, "BestLapRecords");
            Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, "invalid_laps.db");
        }

        // ─── Write (called from DataUpdate) ──────────────────────────────────

        public static void AddLap(string game, string track, string carId,
                                  int lapNumber, string sessionId, DateTime date)
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            if (string.IsNullOrEmpty(track) || string.IsNullOrEmpty(carId)) return;

            lock (_writeLock)
            {
                try
                {
                    using (var db = new LiteDatabase(string.Format("Filename={0}", _dbPath)))
                    {
                        var col = db.GetCollection("invalid_laps");

                        // Deduplicate: same session + same lap number + same game
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            var existing = col.FindOne(
                                Query.And(
                                    Query.And(Query.EQ("Game",      new BsonValue(game)),
                                              Query.EQ("Session",   new BsonValue(sessionId))),
                                    Query.EQ("LapNumber", new BsonValue(lapNumber))));
                            if (existing != null) return;
                        }

                        var doc = new BsonDocument();
                        doc["Game"]      = new BsonValue(game);
                        doc["Track"]     = new BsonValue(track);
                        doc["Car"]       = new BsonValue(carId);
                        doc["LapNumber"] = new BsonValue(lapNumber);
                        doc["Session"]   = new BsonValue(sessionId ?? "");
                        doc["Date"]      = new BsonValue(date);
                        col.Insert(doc);
                    }
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn(
                        string.Format("[BestLapRecords] InvalidLapStore.AddLap error: {0}", ex.Message));
                }
            }
        }

        // ─── Read (called at display time) ───────────────────────────────────

        public static List<BsonDocument> GetLaps(string game, string track, string carId)
        {
            if (string.IsNullOrEmpty(_dbPath)) return new List<BsonDocument>();
            try
            {
                using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", _dbPath)))
                {
                    var col = db.GetCollection("invalid_laps");
                    return col.Find(
                        Query.And(
                            Query.And(Query.EQ("Game",  new BsonValue(game)),
                                      Query.EQ("Track", new BsonValue(track))),
                            Query.EQ("Car", new BsonValue(carId))
                        )).ToList();
                }
            }
            catch { return new List<BsonDocument>(); }
        }

        /// <summary>Returns all invalid laps for a given game (for activity chart).</summary>
        public static List<BsonDocument> GetAllLaps(string game)
        {
            if (string.IsNullOrEmpty(_dbPath)) return new List<BsonDocument>();
            try
            {
                using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", _dbPath)))
                {
                    return db.GetCollection("invalid_laps")
                             .Find(Query.EQ("Game", new BsonValue(game)))
                             .ToList();
                }
            }
            catch { return new List<BsonDocument>(); }
        }

        /// <summary>Returns Track|Car → invalidLapCount for a given game.</summary>
        public static Dictionary<string, int> GetCountsPerTrackCar(string game)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(_dbPath)) return result;
            try
            {
                using (var db = new LiteDatabase(string.Format("Filename={0};ReadOnly=true", _dbPath)))
                {
                    var col = db.GetCollection("invalid_laps");
                    foreach (var doc in col.Find(Query.EQ("Game", new BsonValue(game))))
                    {
                        var t = doc["Track"].AsString;
                        var c = doc["Car"].AsString;
                        var k = t + "|" + c;
                        int cur;
                        result.TryGetValue(k, out cur);
                        result[k] = cur + 1;
                    }
                }
            }
            catch { }
            return result;
        }
    }
}
