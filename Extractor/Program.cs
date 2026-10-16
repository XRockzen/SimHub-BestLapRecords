using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using User.TeamPace;

namespace TeamPace.Extractor
{
    /// <summary>
    /// 64-bit helper started by the plugin (SimHub is 32-bit and DuckDB has no 32-bit build).
    /// Reads LMU's own telemetry recordings with the duckdb.dll shipped with the game and
    /// writes one compact .tpt per recording, one resampled trace per lap.
    ///
    ///   TeamPace.Extractor.exe &lt;duckdb.dll&gt; &lt;LMU Telemetry folder&gt; &lt;output folder&gt;
    ///
    /// Prints "progress i/n" lines and a final "done extracted=.. locked=.. failed=.." line.
    /// Exit code: 0 = done, 3 = some recordings still in use (retry later), 1 = error.
    /// </summary>
    internal static class Program
    {
        private const double MinFileAgeSec = 20;   // LMU may still be writing a recent file

        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: TeamPace.Extractor <duckdb.dll> <telemetry dir> <output dir>");
                return 1;
            }
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }

            string dll = args[0], inDir = args[1], outDir = args[2];
            try
            {
                DuckDb.Load(dll);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("duckdb load failed: " + ex.Message);
                return 1;
            }
            Directory.CreateDirectory(outDir);

            var files = new List<string>(Directory.GetFiles(inDir, "*.duckdb"));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            // Only what is new or has grown since the last run
            var todo = new List<string>();
            foreach (var f in files)
            {
                var dest = OutputPath(outDir, f);
                if (!File.Exists(dest)) { todo.Add(f); continue; }
                try
                {
                    var h = TeleFormat.ReadHeader(dest);
                    // Grown since, or written by an older extractor (missing channels)
                    if (h.SourceSize != new FileInfo(f).Length || h.Version < TeleFormat.Version) todo.Add(f);
                }
                catch { todo.Add(f); }
            }

            int extracted = 0, locked = 0, failed = 0, laps = 0;
            for (int i = 0; i < todo.Count; i++)
            {
                var src = todo[i];
                Console.WriteLine("progress {0}/{1}", i + 1, todo.Count);
                var info = new FileInfo(src);
                if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < MinFileAgeSec) { locked++; continue; }
                try
                {
                    laps += LapExtractor.Extract(src, OutputPath(outDir, src), info.Length);
                    extracted++;
                }
                catch (DuckDbLockedException) { locked++; }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine("{0}: {1}", Path.GetFileName(src), ex.Message);
                    // Remember broken files so they are not retried on every run
                    try { LapExtractor.WriteEmpty(src, OutputPath(outDir, src), info.Length); } catch { }
                }
            }
            Console.WriteLine("done extracted={0} laps={1} locked={2} failed={3}", extracted, laps, locked, failed);
            return locked > 0 ? 3 : 0;
        }

        /// <summary>
        /// "Autódromo José Carlos Pace_P_2026-10-16T19_02_04Z" → "AutódromoJoséCar_P_2026-10-16T19_02_04Z.tpt":
        /// short enough for deep install folders and team folders (260-character path limit).
        /// </summary>
        private static string OutputPath(string outDir, string source)
        {
            var stem = Path.GetFileNameWithoutExtension(source);
            var m = System.Text.RegularExpressions.Regex.Match(stem, @"^(.*)_([PQRW])_(\d{4}-.*)$");
            if (m.Success)
            {
                var track = new System.Text.StringBuilder();
                foreach (char c in m.Groups[1].Value)
                    if (char.IsLetterOrDigit(c) && track.Length < 16) track.Append(c);
                stem = track + "_" + m.Groups[2].Value + "_" + m.Groups[3].Value;
            }
            else if (stem.Length > 60) stem = stem.Substring(0, 60);
            return Path.Combine(outDir, stem + ".tpt");
        }
    }
}
