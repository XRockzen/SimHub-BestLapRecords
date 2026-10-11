using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace User.BestLapRecords
{
    public class UpdateInfo
    {
        public Version Version { get; set; }
        public string  Tag     { get; set; }
        public string  Notes   { get; set; }
        public string  HtmlUrl { get; set; }
        public string  DllUrl  { get; set; }
        public string  ShaUrl  { get; set; }
    }

    /// <summary>
    /// Self-update from GitHub Releases. A release must carry the plugin DLL and a
    /// ".sha256" file; the user confirms the install, then restarts SimHub.
    /// </summary>
    public static class PluginUpdater
    {
        public const string Owner   = "XRockzen";
        public const string Repo    = "SimHub-BestLapRecords";
        public const string DllName = "User.BestLapRecords.dll";

        // Overridable so the flow can be tested against local files
        public static string ApiBase = "https://api.github.com";

        public static UpdateInfo Available { get; private set; }
        public static Version    PendingRestartVersion { get; private set; }
        public static string     LastError { get; private set; }
        public static bool       Dismissed { get; set; }

        /// <summary>Raised (on a worker thread) when the update state changes.</summary>
        public static event Action Changed;

        public static bool IsConfigured { get { return Owner != "OWNER_NOT_SET"; } }

        public static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string InstalledDllPath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static string Format(Version v)
        {
            return v == null ? "?" : string.Format("v{0}.{1}.{2}", v.Major, v.Minor, Math.Max(0, v.Build));
        }

        // ─── Check ────────────────────────────────────────────────────────────

        public static void CheckInBackground()
        {
            if (!IsConfigured) return;
            Task.Run(() =>
            {
                try
                {
                    Available = CheckForUpdate(CurrentVersion);
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    SimHub.Logging.Current.Info("[BestLapRecords] Update check failed: " + ex.Message);
                }
                Raise();
            });
        }

        /// <summary>Latest release if it is newer than <paramref name="current"/>, else null.</summary>
        public static UpdateInfo CheckForUpdate(Version current)
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(
                Download(string.Format("{0}/repos/{1}/{2}/releases/latest", ApiBase, Owner, Repo))).TrimStart('﻿'));

            var tag = (string)json["tag_name"] ?? "";
            var version = ParseTag(tag);
            if (version == null || Normalize(version) <= Normalize(current)) return null;

            var info = new UpdateInfo
            {
                Version = version,
                Tag     = tag,
                Notes   = ((string)json["body"] ?? "").Trim(),
                HtmlUrl = (string)json["html_url"],
            };
            var assets = json["assets"] as JArray;
            if (assets != null)
                foreach (var a in assets)
                {
                    var name = (string)a["name"];
                    var url  = (string)a["browser_download_url"];
                    if (string.Equals(name, DllName, StringComparison.OrdinalIgnoreCase)) info.DllUrl = url;
                    if (string.Equals(name, DllName + ".sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
                }
            if (info.DllUrl == null || info.ShaUrl == null) return null;   // incomplete release
            return info;
        }

        // ─── Install ──────────────────────────────────────────────────────────

        public static void InstallInBackground(Action<string> done)
        {
            var info = Available;
            Task.Run(() =>
            {
                string error = info == null ? "Aucune mise à jour disponible" : Install(info, InstalledDllPath);
                if (error == null)
                {
                    PendingRestartVersion = info.Version;
                    Available = null;
                }
                Raise();
                if (done != null) done(error);
            });
        }

        /// <summary>Downloads, verifies and swaps the DLL. Returns null on success, else an error message.</summary>
        public static string Install(UpdateInfo info, string targetDll)
        {
            try
            {
                if (ApiBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = string.Format("https://github.com/{0}/{1}/releases/download/", Owner, Repo);
                    if (!info.DllUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        !info.ShaUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return "Adresse de téléchargement inattendue, mise à jour refusée";
                }

                var dir = Path.Combine(Path.GetTempPath(), "BestLapRecords_update");
                Directory.CreateDirectory(dir);
                var tmp = Path.Combine(dir, DllName);
                File.WriteAllBytes(tmp, Download(info.DllUrl));

                var expected = Regex.Match(Encoding.UTF8.GetString(Download(info.ShaUrl)), "[0-9a-fA-F]{64}").Value;
                if (expected.Length == 0) return "Fichier d'empreinte illisible";
                if (!string.Equals(Sha256(tmp), expected, StringComparison.OrdinalIgnoreCase))
                    return "Empreinte SHA-256 différente : fichier corrompu ou modifié, mise à jour refusée";

                var name = AssemblyName.GetAssemblyName(tmp);
                if (name.Name != "User.BestLapRecords") return "Le fichier téléchargé n'est pas ce plugin";
                if (Normalize(name.Version) != Normalize(info.Version))
                    return string.Format("Version du fichier ({0}) différente de la release ({1})", Format(name.Version), Format(info.Version));

                return Swap(tmp, targetDll, expected);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // A loaded DLL cannot be overwritten but can be renamed: the running SimHub keeps
        // the old code until restart, the next start loads the new file.
        private static string Swap(string newDll, string target, string expectedSha)
        {
            var old = NextOldName(target);
            try
            {
                File.Move(target, old);
                try { File.Copy(newDll, target, true); }
                catch
                {
                    File.Move(old, target);
                    throw;
                }
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return SwapElevated(newDll, target, old, expectedSha);
            }
        }

        // Protected folder (e.g. C:\Program Files): same swap through an elevated cmd (UAC prompt)
        private static string SwapElevated(string newDll, string target, string old, string expectedSha)
        {
            var script = Path.Combine(Path.GetDirectoryName(newDll), "install_update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                string.Format("move /Y \"{0}\" \"{1}\" >nul\r\n", target, old) +
                string.Format("copy /Y \"{0}\" \"{1}\" >nul || move /Y \"{2}\" \"{1}\" >nul\r\n", newDll, target, old),
                Encoding.Default);
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
                {
                    Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
                });
                p.WaitForExit(60000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "Autorisation administrateur refusée";
            }
            return File.Exists(target) && string.Equals(Sha256(target), expectedSha, StringComparison.OrdinalIgnoreCase)
                ? null : "Le remplacement du fichier a échoué";
        }

        private static string NextOldName(string target)
        {
            var old = target + ".old";
            if (!File.Exists(old)) return old;
            try { File.Delete(old); return old; }
            catch { return target + "." + DateTime.Now.Ticks + ".old"; }   // still loaded: previous update not restarted yet
        }

        /// <summary>Removes DLLs left by a previous update (they are no longer loaded after a restart).</summary>
        public static void CleanupPrevious()
        {
            try
            {
                var dir = Path.GetDirectoryName(InstalledDllPath);
                foreach (var f in Directory.GetFiles(dir, DllName + "*.old"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = WebRequest.Create(url);
            req.Timeout = 20000;
            var http = req as HttpWebRequest;
            if (http != null)
            {
                http.UserAgent = "User.BestLapRecords-updater";
                http.Accept = url.Contains("/releases/download/") ? "application/octet-stream" : "application/vnd.github+json";
            }
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private static Version ParseTag(string tag)
        {
            var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                               m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}
