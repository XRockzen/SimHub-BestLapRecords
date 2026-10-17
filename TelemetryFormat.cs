using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

// Shared by the plugin and TeamPace.Extractor.exe: keep it free of SimHub / WPF references.
namespace User.TeamPace
{
    [Flags]
    public enum TeleLapFlags : byte
    {
        None     = 0,
        Complete = 1,   // line to line, not started in the pits
        OutLap   = 2,   // started in the pit lane or the garage
        InLap    = 4,   // entered the pit lane before the end
    }

    /// <summary>One lap of a stint file: table entry written in the file header.</summary>
    public class TeleLapInfo
    {
        public int          LapNum;      // same numbering as the LMU results file
        public double       StartEt;     // session time at lap start (= "et" of the results file)
        public double       EndEt;
        public float        LapSec;
        public TeleLapFlags Flags;
        public float        FirstDist;   // lap distance of the first point (0 except out-laps)
        public int          Points;
        public float        TopSpeed;
        public long         Offset;      // trace blob position in the file
        public int          Length;

        public bool IsComplete { get { return (Flags & TeleLapFlags.Complete) != 0; } }
        public bool IsOutLap   { get { return (Flags & TeleLapFlags.OutLap)   != 0; } }
        public bool IsInLap    { get { return (Flags & TeleLapFlags.InLap)    != 0; } }
    }

    /// <summary>Header of a .tpt file: one LMU telemetry recording (one stint).</summary>
    public class TeleFileHeader
    {
        public string Driver, Track, Layout, SessionType, Car, CarClass, RecordingTime, Source;
        public long   SourceSize;
        public float  TrackLength;
        public float  Step;
        public int    Version;   // format the file was written with
        public List<TeleLapInfo> Laps = new List<TeleLapInfo>();

        public string Path;   // where it was read from (not stored)

        /// <summary>"2026-10-16T19_48_47Z" → UTC date, MinValue if unreadable.</summary>
        public DateTime RecordingUtc
        {
            get
            {
                DateTime d;
                var s = (RecordingTime ?? "").Replace('_', ':');
                return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.AdjustToUniversal |
                                         System.Globalization.DateTimeStyles.AssumeUniversal, out d)
                    ? d : DateTime.MinValue;
            }
        }
    }

    /// <summary>A lap resampled every <see cref="Step"/> metres of lap distance.</summary>
    public class TeleTrace
    {
        public float   Step, FirstDist;
        public int     N;
        public float[] Time;       // s since lap start
        public float[] Speed;      // km/h
        public float[] Throttle;   // %
        public float[] Brake;      // %
        public float[] Steer;      // % of lock, signed
        public float[] Rpm;
        public sbyte[] Gear;       // -1 reverse, 0 neutral
        public float[] X, Y;       // metres: v2 in a frame shared by every lap of the track, v1 from the lap's first point
        public float[] Lateral;    // v2: distance from the track centre line, metres, + = right (null in v1 files)
        public float[] Edge;       // v2: track edge on the car's side, same convention
        public float[] GLat, GLong; // v3: g forces (null before)
        public byte[]  Abs, Tc;     // v3: share of the time ABS / traction control was active, % (null before)

        public TeleTrace(int n)
        {
            N = n;
            Time = new float[n]; Speed = new float[n]; Throttle = new float[n]; Brake = new float[n];
            Steer = new float[n]; Rpm = new float[n]; Gear = new sbyte[n]; X = new float[n]; Y = new float[n];
            Lateral = new float[n]; Edge = new float[n];
            GLat = new float[n]; GLong = new float[n]; Abs = new byte[n]; Tc = new byte[n];
        }

        public bool HasDriverAids { get { return Abs != null; } }

        public bool HasLateral { get { return Lateral != null; } }

        public float DistAt(int i) { return FirstDist + i * Step; }

        /// <summary>Index of the point at (or just before) a lap distance, clamped.</summary>
        public int IndexAt(double dist)
        {
            int i = (int)Math.Floor((dist - FirstDist) / Step);
            return i < 0 ? 0 : (i >= N ? N - 1 : i);
        }

        /// <summary>Elapsed lap time at a lap distance, interpolated; NaN outside the trace.</summary>
        public double TimeAt(double dist)
        {
            double f = (dist - FirstDist) / Step;
            if (f < 0 || f > N - 1) return double.NaN;
            int i = (int)f;
            if (i >= N - 1) return Time[N - 1];
            double k = f - i;
            return Time[i] + (Time[i + 1] - Time[i]) * k;
        }
    }

    public static class TeleFormat
    {
        public const int Magic   = 0x31545054;   // "TPT1"
        public const int Version = 3;   // 2: shared X/Y frame, lateral position, track edge · 3: g forces, ABS, TC

        public static void Write(string path, TeleFileHeader h, List<TeleTrace> traces)
        {
            var blobs = new List<byte[]>();
            foreach (var t in traces) blobs.Add(Encode(t));

            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms, Encoding.UTF8);
                w.Write(Magic);
                w.Write(Version);
                foreach (var s in new[] { h.Driver, h.Track, h.Layout, h.SessionType, h.Car, h.CarClass, h.RecordingTime, h.Source })
                    w.Write(s ?? "");
                w.Write(h.SourceSize);
                w.Write(h.TrackLength);
                w.Write(h.Step);
                w.Write(h.Laps.Count);

                // Lap table, then blobs; offsets are absolute, fixed once the table size is known
                long tableStart = ms.Position;
                const int entrySize = 4 + 8 + 8 + 4 + 1 + 4 + 4 + 4 + 8 + 4;
                long offset = tableStart + (long)entrySize * h.Laps.Count;
                for (int i = 0; i < h.Laps.Count; i++)
                {
                    var l = h.Laps[i];
                    l.Offset = offset;
                    l.Length = blobs[i].Length;
                    offset += l.Length;
                    w.Write(l.LapNum); w.Write(l.StartEt); w.Write(l.EndEt); w.Write(l.LapSec);
                    w.Write((byte)l.Flags); w.Write(l.FirstDist); w.Write(l.Points); w.Write(l.TopSpeed);
                    w.Write(l.Offset); w.Write(l.Length);
                }
                foreach (var b in blobs) w.Write(b);
                w.Flush();

                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }

        public static TeleFileHeader ReadHeader(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var r = new BinaryReader(fs, Encoding.UTF8);
                if (r.ReadInt32() != Magic) throw new InvalidDataException("not a TeamPace trace file");
                int version = r.ReadInt32();
                if (version > Version) throw new InvalidDataException("trace file from a newer TeamPace");
                var h = new TeleFileHeader
                {
                    Version = version,
                    Driver = r.ReadString(), Track = r.ReadString(), Layout = r.ReadString(),
                    SessionType = r.ReadString(), Car = r.ReadString(), CarClass = r.ReadString(),
                    RecordingTime = r.ReadString(), Source = r.ReadString(),
                    SourceSize = r.ReadInt64(), TrackLength = r.ReadSingle(), Step = r.ReadSingle(),
                    Path = path,
                };
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    h.Laps.Add(new TeleLapInfo
                    {
                        LapNum = r.ReadInt32(), StartEt = r.ReadDouble(), EndEt = r.ReadDouble(), LapSec = r.ReadSingle(),
                        Flags = (TeleLapFlags)r.ReadByte(), FirstDist = r.ReadSingle(), Points = r.ReadInt32(),
                        TopSpeed = r.ReadSingle(), Offset = r.ReadInt64(), Length = r.ReadInt32(),
                    });
                }
                return h;
            }
        }

        public static TeleTrace ReadTrace(TeleFileHeader h, TeleLapInfo lap)
        {
            var path = h.Path;
            byte[] blob = new byte[lap.Length];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(lap.Offset, SeekOrigin.Begin);
                int read = 0;
                while (read < blob.Length)
                {
                    int n = fs.Read(blob, read, blob.Length - read);
                    if (n <= 0) throw new EndOfStreamException();
                    read += n;
                }
            }
            return Decode(blob, h.Version);
        }

        // Quantised integer columns stored as differences from the previous point, then gzip:
        // consecutive points 2 m apart barely differ, which compresses well (~15 KB per 5 km lap).
        private static byte[] Encode(TeleTrace t)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    var w = new BinaryWriter(gz);
                    w.Write(t.Step); w.Write(t.FirstDist); w.Write(t.N);
                    WriteDeltas(w, t.Time,  1000);    // ms
                    WriteDeltas(w, t.Speed, 100);
                    for (int i = 0; i < t.N; i++) w.Write((byte)Clamp(t.Throttle[i] * 2, 0, 255));
                    for (int i = 0; i < t.N; i++) w.Write((byte)Clamp(t.Brake[i] * 2, 0, 255));
                    WriteDeltas(w, t.Steer, 100);
                    WriteDeltas(w, t.Rpm,   1);
                    for (int i = 0; i < t.N; i++) w.Write(t.Gear[i]);
                    WriteDeltas(w, t.X, 20);          // 5 cm
                    WriteDeltas(w, t.Y, 20);
                    WriteDeltas(w, t.Lateral, 100);   // 1 cm
                    WriteDeltas(w, t.Edge, 100);
                    WriteDeltas(w, t.GLat, 100);      // 0.01 g
                    WriteDeltas(w, t.GLong, 100);
                    w.Write(t.Abs);
                    w.Write(t.Tc);
                    w.Flush();
                }
                return ms.ToArray();
            }
        }

        private static TeleTrace Decode(byte[] blob, int version)
        {
            using (var gz = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress))
            {
                var r = new BinaryReader(gz);
                float step = r.ReadSingle(), first = r.ReadSingle();
                var t = new TeleTrace(r.ReadInt32()) { Step = step, FirstDist = first };
                ReadDeltas(r, t.Time,  1000);
                ReadDeltas(r, t.Speed, 100);
                for (int i = 0; i < t.N; i++) t.Throttle[i] = r.ReadByte() / 2f;
                for (int i = 0; i < t.N; i++) t.Brake[i]    = r.ReadByte() / 2f;
                ReadDeltas(r, t.Steer, 100);
                ReadDeltas(r, t.Rpm,   1);
                for (int i = 0; i < t.N; i++) t.Gear[i] = r.ReadSByte();
                ReadDeltas(r, t.X, version >= 2 ? 20 : 4);
                ReadDeltas(r, t.Y, version >= 2 ? 20 : 4);
                if (version >= 2)
                {
                    ReadDeltas(r, t.Lateral, 100);
                    ReadDeltas(r, t.Edge, 100);
                }
                else t.Lateral = t.Edge = null;
                if (version >= 3)
                {
                    ReadDeltas(r, t.GLat, 100);
                    ReadDeltas(r, t.GLong, 100);
                    t.Abs = r.ReadBytes(t.N);
                    t.Tc  = r.ReadBytes(t.N);
                }
                else { t.GLat = t.GLong = null; t.Abs = t.Tc = null; }
                return t;
            }
        }

        // First value as int, then one signed delta per point (an int when it does not fit a short)
        private static void WriteDeltas(BinaryWriter w, float[] v, double scale)
        {
            int prev = 0;
            for (int i = 0; i < v.Length; i++)
            {
                int q = (int)Clamp(v[i] * scale, int.MinValue / 2, int.MaxValue / 2);
                int d = q - prev;
                if (i == 0) w.Write(q);
                else if (d > short.MinValue && d <= short.MaxValue) w.Write((short)d);
                else { w.Write(short.MinValue); w.Write(d); }
                prev = q;
            }
        }

        private static void ReadDeltas(BinaryReader r, float[] v, double scale)
        {
            int q = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (i == 0) q = r.ReadInt32();
                else
                {
                    short d = r.ReadInt16();
                    q += d == short.MinValue ? r.ReadInt32() : d;
                }
                v[i] = (float)(q / scale);
            }
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : Math.Round(v));
        }
    }
}
