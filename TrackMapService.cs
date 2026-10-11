using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;

namespace User.BestLapRecords
{
    public class TrackMapInfo
    {
        public List<Point> Points { get; set; }
        public double TrackLength { get; set; }
        public string GameCode { get; set; }

        public TrackMapInfo()
        {
            Points = new List<Point>();
        }
    }

    public static class TrackMapService
    {
        public static TrackMapInfo LoadTrackMap(string game, string trackName, string pluginsDataPath,
                                                double canvasWidth, double canvasHeight, int downsample = 8)
        {
            var result = new TrackMapInfo();

            var mapRecordsPath = Path.Combine(pluginsDataPath, game, "MapRecords");
            if (!Directory.Exists(mapRecordsPath)) return result;

            // Try local MapRecords first, then MapRecordsCloud
            var shtlFile = Path.Combine(mapRecordsPath, trackName + ".shtl");
            if (!File.Exists(shtlFile))
            {
                var cloudPath = Path.Combine(pluginsDataPath, game, "MapRecordsCloud");
                foreach (var f in Directory.GetFiles(cloudPath, trackName + "-*.shtl"))
                {
                    shtlFile = f;
                    break;
                }
            }

            if (!File.Exists(shtlFile)) return result;

            try
            {
                double trackLength;
                string gameCode;
                var rawPoints = ReadCoordinates(shtlFile, out trackLength, out gameCode, downsample);
                result.TrackLength = trackLength;
                result.GameCode = gameCode;
                result.Points = NormalizeToCanvas(rawPoints, canvasWidth, canvasHeight);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(string.Format("[BestLapRecords] TrackMap error for {0}: {1}", trackName, ex.Message));
            }

            return result;
        }

        private static List<double[]> ReadCoordinates(string shtlFile, out double trackLength, out string gameCode, int downsample)
        {
            trackLength = 0;
            gameCode = "";
            var coords = new List<double[]>();

            byte[] compressed = File.ReadAllBytes(shtlFile);
            string json;
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                json = reader.ReadToEnd();

            var root = JObject.Parse(json);
            trackLength = root["ReportedTrackLength"] != null ? (double)root["ReportedTrackLength"] : 0;
            gameCode = root["GameCode"] != null ? (string)root["GameCode"] : "";

            var carCoords = root["CarCoordinates"] as JArray;
            if (carCoords == null) return coords;

            int i = 0;
            foreach (var entry in carCoords)
            {
                if (i % downsample != 0) { i++; continue; }
                var value = entry["Value"] as JArray;
                if (value != null && value.Count >= 3)
                {
                    double x = (double)value[0];
                    double z = (double)value[2]; // Y is height, use X and Z for 2D top view
                    coords.Add(new double[] { x, z });
                }
                i++;
            }

            return coords;
        }

        private static List<Point> NormalizeToCanvas(List<double[]> rawPoints, double canvasWidth, double canvasHeight)
        {
            if (rawPoints.Count == 0) return new List<Point>();

            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (var p in rawPoints)
            {
                if (p[0] < minX) minX = p[0];
                if (p[0] > maxX) maxX = p[0];
                if (p[1] < minZ) minZ = p[1];
                if (p[1] > maxZ) maxZ = p[1];
            }

            double rangeX = maxX - minX;
            double rangeZ = maxZ - minZ;
            if (rangeX < 1) rangeX = 1;
            if (rangeZ < 1) rangeZ = 1;

            double margin = 10;
            double scaleX = (canvasWidth - margin * 2) / rangeX;
            double scaleZ = (canvasHeight - margin * 2) / rangeZ;
            double scale = Math.Min(scaleX, scaleZ);

            // Center the track
            double offsetX = margin + (canvasWidth - margin * 2 - rangeX * scale) / 2;
            double offsetZ = margin + (canvasHeight - margin * 2 - rangeZ * scale) / 2;

            var result = new List<Point>();
            foreach (var p in rawPoints)
            {
                double px = offsetX + (p[0] - minX) * scale;
                double pz = offsetZ + (p[1] - minZ) * scale;
                result.Add(new Point(px, pz));
            }
            return result;
        }
    }
}
