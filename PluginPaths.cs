using System;
using System.IO;

namespace User.TeamPace
{
    public static class PluginPaths
    {
        public const string DataFolderName   = "TeamPace";
        private const string LegacyFolderName = "BestLapRecords";   // name before v2.0.0

        /// <summary>Plugin data folder under PluginsData\Common, migrated from the legacy name once.</summary>
        public static string DataDir(string commonPath)
        {
            var dir    = Path.Combine(commonPath, DataFolderName);
            var legacy = Path.Combine(commonPath, LegacyFolderName);
            if (!Directory.Exists(dir) && Directory.Exists(legacy))
            {
                try { Directory.Move(legacy, dir); }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Warn("[TeamPace] Data folder migration failed: " + ex.Message);
                }
            }
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
