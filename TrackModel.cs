using System;
using System.Collections.Generic;

namespace User.TeamPace
{
    /// <summary>
    /// A circuit rebuilt from many recorded laps: centre line, track edges and corners.
    ///
    /// LMU's GPS latitude is stored in 32-bit floats around 60°, i.e. in 0.42 m steps, too coarse
    /// for a corner close-up. Its lateral position (distance from the centre line) is exact, so a
    /// lap's line is drawn as centre line + lateral offset: smooth, and every lap of every driver
    /// in the same frame. The centre line itself is the average of all laps, which cancels the
    /// GPS steps (≈ 10 cm agreement between laps, measured on Algarve).
    /// </summary>
    public class TrackModel
    {
        public class Corner
        {
            public int    Number;
            public double Start, End, Apex;   // lap distance, m
            public bool   Left;
            public double Angle;              // degrees of heading change
            public double MinRadius;          // m

            public string Name { get { return "V" + Number; } }
        }

        public const double Step = 2.0;

        public double Length { get; private set; }
        public int    N      { get; private set; }
        public double[] Cx, Cy;        // centre line
        public double[] Nx, Ny;        // unit normal pointing left
        public double[] EdgeLeft;      // distance from the centre line to each edge (positive)
        public double[] EdgeRight;
        public List<Corner> Corners = new List<Corner>();

        private TrackModel() { }

        /// <summary>Builds the model from laps having lateral data; null when there are none.</summary>
        public static TrackModel Build(List<TeleTrace> laps, double length)
        {
            var usable = laps.FindAll(t => t.HasLateral && t.N > 10);
            if (usable.Count == 0) return null;

            // Length from the laps themselves: a recording's maximum lap distance can be off
            var ends = new List<double>();
            foreach (var t in usable) if (t.FirstDist < 10) ends.Add(t.DistAt(t.N - 1));
            if (ends.Count > 0) { ends.Sort(); length = ends[ends.Count / 2] + Step; }
            if (length < 500) return null;
            usable = usable.FindAll(t => t.DistAt(t.N - 1) < length + 50);

            var m = new TrackModel { Length = length, N = (int)Math.Ceiling(length / Step) };
            int n = m.N;

            // 1. Average raw line, only to get the direction of travel at each point
            var sx = new double[n]; var sy = new double[n]; var cnt = new int[n];
            foreach (var t in usable)
                for (int i = 0; i < t.N; i++)
                {
                    int k = m.Index(t.DistAt(i));
                    sx[k] += t.X[i]; sy[k] += t.Y[i]; cnt[k]++;
                }
            if (!Average(sx, sy, cnt)) return null;
            Smooth(sx, 5); Smooth(sy, 5);
            m.Normals(sx, sy);

            // 2. Centre line = position + lateral offset towards the centre (lateral is + to the right)
            var cx = new double[n]; var cy = new double[n]; cnt = new int[n];
            foreach (var t in usable)
                for (int i = 0; i < t.N; i++)
                {
                    int k = m.Index(t.DistAt(i));
                    cx[k] += t.X[i] + t.Lateral[i] * m.Nx[k];
                    cy[k] += t.Y[i] + t.Lateral[i] * m.Ny[k];
                    cnt[k]++;
                }
            Average(cx, cy, cnt);
            Smooth(cx, 3); Smooth(cy, 3);
            m.Cx = cx; m.Cy = cy;
            m.Normals(cx, cy);

            // 3. Edges: each sample gives the edge on the side the car was on
            var left = new List<float>[n]; var right = new List<float>[n];
            foreach (var t in usable)
                for (int i = 0; i < t.N; i++)
                {
                    int k = m.Index(t.DistAt(i));
                    float e = t.Edge[i];
                    if (e > 0.5f)       (right[k] ?? (right[k] = new List<float>())).Add(e);
                    else if (e < -0.5f) (left[k]  ?? (left[k]  = new List<float>())).Add(-e);
                }
            m.EdgeLeft  = Fill(Medians(left));
            m.EdgeRight = Fill(Medians(right));
            if (m.EdgeLeft == null && m.EdgeRight == null) { m.EdgeLeft = Const(n, 6); m.EdgeRight = Const(n, 6); }
            else if (m.EdgeLeft == null)  m.EdgeLeft  = (double[])m.EdgeRight.Clone();
            else if (m.EdgeRight == null) m.EdgeRight = (double[])m.EdgeLeft.Clone();
            Smooth(m.EdgeLeft, 4); Smooth(m.EdgeRight, 4);

            m.FindCorners();
            return m;
        }

        public int Index(double dist)
        {
            int k = (int)Math.Round(dist / Step);
            return ((k % N) + N) % N;
        }

        /// <summary>Position of a lap point: centre line + its lateral offset.</summary>
        public void Position(double dist, double lateral, out double x, out double y)
        {
            int k = Index(dist);
            x = Cx[k] - lateral * Nx[k];
            y = Cy[k] - lateral * Ny[k];
        }

        /// <summary>Track edge points at a distance (left edge, right edge).</summary>
        public void Edges(double dist, out double lx, out double ly, out double rx, out double ry)
        {
            int k = Index(dist);
            lx = Cx[k] + EdgeLeft[k] * Nx[k];  ly = Cy[k] + EdgeLeft[k] * Ny[k];
            rx = Cx[k] - EdgeRight[k] * Nx[k]; ry = Cy[k] - EdgeRight[k] * Ny[k];
        }

        public Corner CornerAt(double dist)
        {
            foreach (var c in Corners) if (dist >= c.Start - 60 && dist <= c.End + 20) return c;
            return null;
        }

        // ─── Building blocks ──────────────────────────────────────────────────

        private void Normals(double[] x, double[] y)
        {
            Nx = new double[N]; Ny = new double[N];
            for (int k = 0; k < N; k++)
            {
                int a = (k - 2 + N) % N, b = (k + 2) % N;
                double dx = x[b] - x[a], dy = y[b] - y[a];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) { Nx[k] = k > 0 ? Nx[k - 1] : 0; Ny[k] = k > 0 ? Ny[k - 1] : 1; continue; }
                Nx[k] = -dy / len; Ny[k] = dx / len;
            }
        }

        /// <summary>
        /// Corners: stretches where the centre line turns tighter than a 200 m radius, merged when
        /// close and in the same direction, kept when the heading changes by 25° or more.
        /// </summary>
        private void FindCorners()
        {
            var heading = new double[N];
            for (int k = 0; k < N; k++)
            {
                int b = (k + 1) % N;
                heading[k] = Math.Atan2(Cy[b] - Cy[k], Cx[b] - Cx[k]);
            }
            for (int k = 1; k < N; k++)   // unwrap
            {
                double d = heading[k] - heading[k - 1];
                while (d > Math.PI)  { heading[k] -= 2 * Math.PI; d -= 2 * Math.PI; }
                while (d < -Math.PI) { heading[k] += 2 * Math.PI; d += 2 * Math.PI; }
            }

            const int w = 10;   // ±20 m
            var curv = new double[N];
            for (int k = 0; k < N; k++)
            {
                int a = Math.Max(0, k - w), b = Math.Min(N - 1, k + w);
                curv[k] = b > a ? (heading[b] - heading[a]) / ((b - a) * Step) : 0;
            }
            Smooth(curv, 5);

            const double threshold = 1 / 200.0;
            var spans = new List<int[]>();
            for (int k = 0; k < N; k++)
            {
                if (Math.Abs(curv[k]) <= threshold) continue;
                int j = k;
                while (j + 1 < N && Math.Abs(curv[j + 1]) > threshold && Math.Sign(curv[j + 1]) == Math.Sign(curv[k])) j++;
                if (spans.Count > 0)
                {
                    var last = spans[spans.Count - 1];
                    if ((k - last[1]) * Step < 25 && Math.Sign(curv[last[1]]) == Math.Sign(curv[k])) { last[1] = j; k = j; continue; }
                }
                spans.Add(new[] { k, j });
                k = j;
            }

            foreach (var s in spans)
            {
                double angle = Math.Abs(heading[s[1]] - heading[s[0]]) * 180 / Math.PI;
                if (angle < 25) continue;
                int apex = s[0];
                for (int k = s[0]; k <= s[1]; k++) if (Math.Abs(curv[k]) > Math.Abs(curv[apex])) apex = k;
                Corners.Add(new Corner
                {
                    Number = Corners.Count + 1,
                    Start = s[0] * Step, End = s[1] * Step, Apex = apex * Step,
                    Left = curv[apex] > 0, Angle = angle,
                    MinRadius = Math.Abs(curv[apex]) > 1e-6 ? 1 / Math.Abs(curv[apex]) : 9999,
                });
            }
        }

        /// <summary>Lap split into one sector per corner (boundaries halfway between corners).</summary>
        public List<KeyValuePair<double, double>> CornerSectors()
        {
            var list = new List<KeyValuePair<double, double>>();
            for (int i = 0; i < Corners.Count; i++)
            {
                double a = i == 0 ? 0 : (Corners[i - 1].End + Corners[i].Start) / 2;
                double b = i == Corners.Count - 1 ? Length : (Corners[i].End + Corners[i + 1].Start) / 2;
                list.Add(new KeyValuePair<double, double>(a, b));
            }
            return list;
        }

        private static bool Average(double[] x, double[] y, int[] cnt)
        {
            int n = x.Length, filled = 0;
            for (int k = 0; k < n; k++)
                if (cnt[k] > 0) { x[k] /= cnt[k]; y[k] /= cnt[k]; filled++; }
                else { x[k] = double.NaN; y[k] = double.NaN; }
            if (filled < n / 2) return false;
            Interpolate(x); Interpolate(y);
            return true;
        }

        private static double[] Medians(List<float>[] lists)
        {
            var v = new double[lists.Length];
            int filled = 0;
            for (int k = 0; k < lists.Length; k++)
            {
                if (lists[k] == null) { v[k] = double.NaN; continue; }
                lists[k].Sort();
                v[k] = lists[k][lists[k].Count / 2];
                filled++;
            }
            return filled == 0 ? null : v;
        }

        private static double[] Fill(double[] v)
        {
            if (v != null) Interpolate(v);
            return v;
        }

        /// <summary>Linear fill of NaN gaps, the array being a closed loop.</summary>
        private static void Interpolate(double[] v)
        {
            int n = v.Length, first = -1;
            for (int k = 0; k < n; k++) if (!double.IsNaN(v[k])) { first = k; break; }
            if (first < 0) return;
            int prev = first;
            for (int step = 1; step <= n; step++)
            {
                int k = (first + step) % n;
                if (double.IsNaN(v[k])) continue;
                int gap = (k - prev + n) % n;
                if (gap == 0) gap = n;
                for (int g = 1; g < gap; g++)
                    v[(prev + g) % n] = v[prev] + (v[k] - v[prev]) * g / gap;
                prev = k;
            }
        }

        /// <summary>Moving average over ±w points on a closed loop.</summary>
        private static void Smooth(double[] v, int w)
        {
            int n = v.Length;
            var copy = (double[])v.Clone();
            for (int k = 0; k < n; k++)
            {
                double s = 0;
                for (int j = -w; j <= w; j++) s += copy[((k + j) % n + n) % n];
                v[k] = s / (2 * w + 1);
            }
        }

        private static double[] Const(int n, double value)
        {
            var v = new double[n];
            for (int k = 0; k < n; k++) v[k] = value;
            return v;
        }
    }
}
