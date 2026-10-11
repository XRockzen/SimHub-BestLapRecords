using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace User.BestLapRecords
{
    public class CarInfo
    {
        public string Category { get; set; }
        public string Model    { get; set; }
    }

    public static class CarNameResolver
    {
        // (gameFolderName + "|" + carId) -> CarInfo
        private static readonly Dictionary<string, CarInfo> _lookup
            = new Dictionary<string, CarInfo>(StringComparer.OrdinalIgnoreCase);

        // (gameFolderName + "|" + modelName) -> List<carId>
        private static readonly Dictionary<string, List<string>> _modelToCarIds
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static void LoadAliases(string pluginsDataCommonPath)
        {
            _lookup.Clear();
            _modelToCarIds.Clear();

            var aliasFile = Path.Combine(pluginsDataCommonPath, "BestLapRecords", "car_aliases.json");
            if (!File.Exists(aliasFile)) return;

            try
            {
                var json = File.ReadAllText(aliasFile, Encoding.UTF8);
                var root = JObject.Parse(json);

                foreach (var gameProp in root.Properties())
                {
                    var gameFolderName = gameProp.Name;
                    if (gameProp.Value.Type != JTokenType.Object) continue;

                    foreach (var catProp in ((JObject)gameProp.Value).Properties())
                    {
                        var category = catProp.Name;
                        if (catProp.Value.Type != JTokenType.Object) continue;

                        foreach (var modelProp in ((JObject)catProp.Value).Properties())
                        {
                            var model = modelProp.Name;
                            var modelKey = gameFolderName + "|" + model;
                            if (!_modelToCarIds.ContainsKey(modelKey))
                                _modelToCarIds[modelKey] = new List<string>();

                            if (modelProp.Value.Type != JTokenType.Array) continue;

                            foreach (var carIdToken in (JArray)modelProp.Value)
                            {
                                var carId = carIdToken.ToString();
                                var lookupKey = gameFolderName + "|" + carId;
                                _lookup[lookupKey] = new CarInfo { Category = category, Model = model };
                                _modelToCarIds[modelKey].Add(carId);
                            }
                        }
                    }
                }

                SimHub.Logging.Current.Info(string.Format("[BestLapRecords] Loaded {0} car aliases", _lookup.Count));
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn(string.Format("[BestLapRecords] car_aliases.json error: {0}", ex.Message));
            }
        }

        public static CarInfo Resolve(string gameFolderName, string carId)
        {
            if (string.IsNullOrEmpty(carId))
                return new CarInfo { Category = "", Model = carId };

            // 1. Lookup in aliases
            var key = gameFolderName + "|" + carId;
            CarInfo info;
            if (_lookup.TryGetValue(key, out info))
                return info;

            // 2. Fallback: auto-detect
            return new CarInfo
            {
                Category = FallbackCategory(gameFolderName, carId),
                Model    = FallbackModel(gameFolderName, carId)
            };
        }

        public static List<string> GetCarIdsForModel(string gameFolderName, string modelName)
        {
            var key = gameFolderName + "|" + modelName;
            List<string> ids;
            if (_modelToCarIds.TryGetValue(key, out ids))
                return ids;
            // If not in JSON (fallback car), return the model name itself as carId
            return new List<string> { modelName };
        }

        // ─── Fallback helpers ─────────────────────────────────────────────────

        private static string FallbackCategory(string gameFolderName, string carId)
        {
            var game = gameFolderName.ToUpperInvariant();

            if (game == "LMU")
            {
                if (carId.StartsWith("GT3_", StringComparison.OrdinalIgnoreCase))  return "LMGT3";
                if (carId.StartsWith("GTE_", StringComparison.OrdinalIgnoreCase))  return "GTE";
                if (carId.StartsWith("LMP2_", StringComparison.OrdinalIgnoreCase)) return "LMP2";
                if (carId.StartsWith("LMP3_", StringComparison.OrdinalIgnoreCase)) return "LMP3";
                if (carId.StartsWith("Hyper_", StringComparison.OrdinalIgnoreCase)) return "HYPERCAR";
                return "HYPERCAR";
            }
            if (game == "ASSETTOCORSACOMPETIZIONE") return "GT3";
            return "";
        }

        private static string FallbackModel(string gameFolderName, string carId)
        {
            var game = gameFolderName.ToUpperInvariant();

            if (game == "LMU")
            {
                // Strip class prefix
                var s = Regex.Replace(carId, @"^(GT3_|GTE_|LMP2_ELMS_|LMP2_|LMP3_|Hyper_)", "", RegexOptions.IgnoreCase);
                // Strip trailing year/number  e.g. " 2025" or " 2025_28"
                s = Regex.Replace(s, @"\s+\d{4}(_\d+)?$", "");
                return s.Trim();
            }
            if (game == "ASSETTOCORSACOMPETIZIONE")
            {
                // audi_r8_lms_evo_ii -> Audi R8 LMS Evo II
                var s = carId.Replace("_", " ");
                s = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
                s = Regex.Replace(s, @"\bGt3\b", "GT3");
                s = Regex.Replace(s, @"\bGt4\b", "GT4");
                s = Regex.Replace(s, @"\bLms\b", "LMS");
                s = Regex.Replace(s, @"\bAmg\b", "AMG");
                return s;
            }
            return carId;
        }
    }
}
