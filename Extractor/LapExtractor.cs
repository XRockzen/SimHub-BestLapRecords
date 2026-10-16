using System;
using System.Collections.Generic;
using User.TeamPace;

namespace TeamPace.Extractor
{
    /// <summary>
    /// Cuts an LMU recording into laps and resamples each lap on lap distance, so that laps of
    /// different drivers and sessions can be overlaid point by point.
    /// </summary>
    internal static class LapExtractor
    {
        private const float  Step         = 2f;     // metres between points
        private const double MinLapSec    = 5;
        private const double LineSnapM    = 150;    // a lap starting this close before the line starts at 0
        private const double StartTolM    = 50;     // complete lap: starts within this of the line…
        private const double EndTolM      = 80;     // …and ends within this of the track length

        // ─── Channel sampling ─────────────────────────────────────────────────

        /// <summary>A fixed-rate channel with its sample times (taken from the GPS Time channel).</summary>
        private sealed class Series
        {
            public double[] T, V;
            private int _i;

            public Series(double[] t, double[] v) { T = t; V = v; }

            public double At(double q)
            {
                int n = T.Length;
                if (n == 0) return 0;
                if (q <= T[0]) return V[0];
                if (q >= T[n - 1]) return V[n - 1];
                if (_i >= n - 1 || T[_i] > q) _i = Search(q);
                while (_i < n - 2 && T[_i + 1] <= q) _i++;
                double t0 = T[_i], t1 = T[_i + 1];
                double k = t1 > t0 ? (q - t0) / (t1 - t0) : 0;
                return V[_i] + (V[_i + 1] - V[_i]) * k;
            }

            private int Search(double q)
            {
                int lo = 0, hi = T.Length - 1;
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (T[m] <= q) lo = m; else hi = m; }
                return lo;
            }
        }

        /// <summary>An event channel (value changes with a timestamp): step function.</summary>
        private sealed class Events
        {
            public double[] T, V;
            public Events(double[][] cols) { T = cols[0]; V = cols[1]; }

            public double At(double q, double before)
            {
                if (T.Length == 0 || q < T[0]) return before;
                int lo = 0, hi = T.Length - 1;
                while (lo < hi) { int m = (lo + hi + 1) / 2; if (T[m] <= q) lo = m; else hi = m - 1; }
                return V[lo];
            }

            public bool AnyBetween(double a, double b, double value)
            {
                for (int i = 0; i < T.Length; i++) if (T[i] > a && T[i] <= b && V[i] == value) return true;
                return false;
            }
        }

        // ─── Extraction ───────────────────────────────────────────────────────

        public static int Extract(string src, string dest, long sourceSize)
        {
            using (var db = new DuckDb(src))
            {
                var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in db.QueryText("select key, value from metadata", 2)) meta[row[0]] = row[1];

                var freq = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in db.QueryText("select channelName, frequency::VARCHAR from channelsList", 2))
                {
                    double f;
                    if (double.TryParse(row[1], System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out f) && f > 0)
                        freq[row[0]] = f;
                }
                var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in db.QueryText("select eventName from eventsList", 1)) events.Add(row[0]);

                var header = new TeleFileHeader
                {
                    Driver = Get(meta, "DriverName"), Track = Get(meta, "TrackName"), Layout = Get(meta, "TrackLayout"),
                    SessionType = Get(meta, "SessionType"), Car = Get(meta, "CarName"), CarClass = Get(meta, "CarClass"),
                    RecordingTime = Get(meta, "RecordingTime"), Source = System.IO.Path.GetFileName(src),
                    SourceSize = sourceSize, Step = Step,
                };
                var traces = new List<TeleTrace>();

                if (!freq.ContainsKey("GPS Time") || !freq.ContainsKey("Lap Dist") || !events.Contains("Lap"))
                {
                    TeleFormat.Write(dest, header, traces);
                    return 0;
                }

                double fGps = freq["GPS Time"];
                double[] gps = Column(db, "GPS Time");
                if (gps.Length < 2) { TeleFormat.Write(dest, header, traces); return 0; }

                Func<string, Series> channel = name =>
                {
                    double f;
                    if (!freq.TryGetValue(name, out f)) return new Series(new double[0], new double[0]);
                    var v = Column(db, name);
                    return new Series(Times(gps, fGps, v.Length, f), v);
                };
                var speed = channel("Ground Speed");
                var steer = channel("Steering Pos");
                var rpm   = channel("Engine RPM");
                // The driver's pedals: the filtered channels include traction-control cuts
                var thr   = channel(freq.ContainsKey("Throttle Pos Unfiltered") ? "Throttle Pos Unfiltered" : "Throttle Pos");
                var brk   = channel(freq.ContainsKey("Brake Pos Unfiltered") ? "Brake Pos Unfiltered" : "Brake Pos");
                var dist  = channel("Lap Dist");
                var lat   = channel("GPS Latitude");
                var lon   = channel("GPS Longitude");
                var side  = channel("Path Lateral");
                var edge  = channel("Track Edge");

                var laps  = new Events(db.QueryDoubles("select ts::DOUBLE, coalesce(value, 0)::DOUBLE from \"Lap\" order by ts", 2));
                var gear  = events.Contains("Gear")
                    ? new Events(WithoutShiftNeutrals(db.QueryDoubles("select ts::DOUBLE, coalesce(value, 0)::DOUBLE from \"Gear\" order by ts", 2)))
                    : new Events(new[] { new double[0], new double[0] });
                var pits  = events.Contains("In Pits")
                    ? new Events(db.QueryDoubles("select ts::DOUBLE, coalesce(value, 0)::DOUBLE from \"In Pits\" order by ts", 2))
                    : new Events(new[] { new double[0], new double[0] });

                double trackLen = 0;
                foreach (var d in dist.V) if (d > trackLen) trackLen = d;
                header.TrackLength = (float)trackLen;
                if (trackLen < 500 || laps.T.Length == 0) { TeleFormat.Write(dest, header, traces); return 0; }

                double tStart = gps[0], tEnd = gps[gps.Length - 1];
                for (int k = 0; k < laps.T.Length; k++)
                {
                    double a = Math.Max(laps.T[k], tStart);
                    bool closed = k + 1 < laps.T.Length;
                    double b = closed ? laps.T[k + 1] : tEnd;
                    if (b - a < MinLapSec) continue;

                    var info = new TeleLapInfo
                    {
                        LapNum = (int)laps.V[k] + 1,   // LMU counts completed laps; results number from 1
                        StartEt = a, EndEt = b, LapSec = (float)(b - a),
                    };
                    var trace = Resample(a, b, trackLen, dist, speed, steer, rpm, thr, brk, lat, lon, side, edge, gear);
                    if (trace == null || trace.N < 50) continue;

                    bool outLap = pits.At(a + 0.05, 0) > 0.5;
                    bool inLap  = pits.AnyBetween(a + 1, b, 1) || pits.At(b - 0.05, 0) > 0.5;
                    double endDist = trace.DistAt(trace.N - 1);
                    if (outLap) info.Flags |= TeleLapFlags.OutLap;
                    if (inLap)  info.Flags |= TeleLapFlags.InLap;
                    if (closed && !outLap && trace.FirstDist <= StartTolM && endDist >= trackLen - EndTolM)
                        info.Flags |= TeleLapFlags.Complete;

                    info.FirstDist = trace.FirstDist;
                    info.Points    = trace.N;
                    float top = 0;
                    for (int i = 0; i < trace.N; i++) if (trace.Speed[i] > top) top = trace.Speed[i];
                    info.TopSpeed = top;

                    header.Laps.Add(info);
                    traces.Add(trace);
                }

                TeleFormat.Write(dest, header, traces);
                return traces.Count;
            }
        }

        public static void WriteEmpty(string src, string dest, long sourceSize)
        {
            TeleFormat.Write(dest, new TeleFileHeader
            {
                Source = System.IO.Path.GetFileName(src), SourceSize = sourceSize, Step = Step,
            }, new List<TeleTrace>());
        }

        /// <summary>
        /// Lap distance at 10 Hz is unwrapped around the line, then every 100 Hz frame gets a
        /// distance; grid points are placed by interpolating between frames.
        /// </summary>
        private static TeleTrace Resample(double a, double b, double len, Series dist, Series speed, Series steer,
                                          Series rpm, Series thr, Series brk, Series lat, Series lon,
                                          Series side, Series edge, Events gear)
        {
            // Unwrapped distance samples around the lap
            var ut = new List<double>();
            var uv = new List<double>();
            double offset = 0, prev = double.NaN;
            for (int i = 0; i < dist.T.Length; i++)
            {
                double t = dist.T[i];
                if (t < a - 1) continue;
                if (t > b + 1) break;
                double d = dist.V[i];
                if (!double.IsNaN(prev))
                {
                    if (d - prev < -len / 2) offset += len;
                    else if (d - prev > len / 2) offset -= len;
                }
                prev = d;
                ut.Add(t); uv.Add(d + offset);
            }
            if (ut.Count < 4) return null;
            var u = new Series(ut.ToArray(), uv.ToArray());

            double ua = u.At(a);
            double start = ((ua % len) + len) % len;
            if (start > len - LineSnapM) start -= len;
            double baseDist = ua - start;
            double end = u.At(b) - baseDist;
            if (end - Math.Max(0, start) < 200) return null;

            double first = Math.Ceiling(Math.Max(0, start) / Step) * Step;
            int n = (int)Math.Floor((end - first) / Step) + 1;
            if (n < 2) return null;
            var tr = new TeleTrace(n) { Step = Step, FirstDist = (float)first };

            // LMU's GPS is synthetic but fixed per track: an origin on the nearest whole degree
            // puts every lap of the track in the same frame
            double lat0 = double.NaN, lon0 = 0, cosLat = 1;
            int j = 0;
            double grid = first;
            double pt = a, pd = Math.Max(start, u.At(a) - baseDist);
            const double frame = 0.01;
            for (double t = a; j < n && t <= b + frame; t += frame)
            {
                double d = Math.Max(pd, u.At(Math.Min(t, b)) - baseDist);   // never backwards (spins)
                while (j < n && grid <= d)
                {
                    double k = d > pd ? (grid - pd) / (d - pd) : 0;
                    double tg = pt + (t - pt) * k;

                    tr.Time[j]     = (float)(tg - a);
                    tr.Speed[j]    = (float)speed.At(tg);
                    tr.Throttle[j] = (float)thr.At(tg);
                    tr.Brake[j]    = (float)brk.At(tg);
                    tr.Steer[j]    = (float)steer.At(tg);
                    tr.Rpm[j]      = (float)rpm.At(tg);
                    tr.Gear[j]     = (sbyte)Math.Max(-1, Math.Min(12, gear.At(tg, 0)));

                    double la = lat.At(tg), lo = lon.At(tg);
                    if (double.IsNaN(lat0)) { lat0 = Math.Round(la); lon0 = Math.Round(lo); cosLat = Math.Cos(lat0 * Math.PI / 180); }
                    tr.X[j] = (float)((lo - lon0) * 111320 * cosLat);
                    tr.Y[j] = (float)((la - lat0) * 110574);
                    tr.Lateral[j] = (float)side.At(tg);
                    tr.Edge[j]    = (float)edge.At(tg);

                    j++;
                    grid = first + j * Step;
                }
                pt = t; pd = d;
            }
            if (j < n)
            {
                // Recording stopped short of the computed end: keep what was filled
                var cut = new TeleTrace(j) { Step = Step, FirstDist = (float)first };
                Array.Copy(tr.Time, cut.Time, j); Array.Copy(tr.Speed, cut.Speed, j);
                Array.Copy(tr.Throttle, cut.Throttle, j); Array.Copy(tr.Brake, cut.Brake, j);
                Array.Copy(tr.Steer, cut.Steer, j); Array.Copy(tr.Rpm, cut.Rpm, j);
                Array.Copy(tr.Gear, cut.Gear, j); Array.Copy(tr.X, cut.X, j); Array.Copy(tr.Y, cut.Y, j);
                Array.Copy(tr.Lateral, cut.Lateral, j); Array.Copy(tr.Edge, cut.Edge, j);
                return cut;
            }
            return tr;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>The gearbox passes through neutral for a few hundredths at each shift: drop those.</summary>
        private static double[][] WithoutShiftNeutrals(double[][] ev)
        {
            var t = new List<double>();
            var v = new List<double>();
            for (int i = 0; i < ev[0].Length; i++)
            {
                bool blip = ev[1][i] == 0 && i > 0 && i + 1 < ev[0].Length &&
                            ev[1][i - 1] > 0 && ev[1][i + 1] > 0 && ev[0][i + 1] - ev[0][i] < 0.5;
                if (blip) continue;
                t.Add(ev[0][i]); v.Add(ev[1][i]);
            }
            return new[] { t.ToArray(), v.ToArray() };
        }

        private static double[] Column(DuckDb db, string table)
        {
            return db.QueryDoubles("select coalesce(value, 0)::DOUBLE from \"" + table.Replace("\"", "\"\"") + "\"", 1)[0];
        }

        /// <summary>
        /// Sample times of a channel: channels run off the same clock as GPS Time, so sample i
        /// of a channel at f Hz is GPS sample i·(fGps/f). Falls back to a fixed rate.
        /// </summary>
        private static double[] Times(double[] gps, double fGps, int n, double f)
        {
            var t = new double[n];
            double ratio = fGps / f;
            bool aligned = Math.Abs(ratio - Math.Round(ratio)) < 1e-6 &&
                           Math.Abs(n * ratio - gps.Length) <= ratio * 2 + 2;
            for (int i = 0; i < n; i++)
            {
                if (aligned)
                {
                    long g = (long)Math.Round(i * ratio);
                    t[i] = gps[Math.Min(g, gps.Length - 1)];
                }
                else t[i] = gps[0] + i / f;
            }
            return t;
        }

        private static string Get(Dictionary<string, string> meta, string key)
        {
            string v;
            return meta.TryGetValue(key, out v) ? v : "";
        }
    }
}
