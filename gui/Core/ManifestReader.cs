using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace EgsLL.Core
{
    /// <summary>
    /// Read-only parser for EGS .item manifest files (JSON).
    /// </summary>
    public static class ManifestReader
    {
        public static List<GameManifest> ReadAll(string manifestsPath = null)
        {
            if (manifestsPath == null)
            {
                var info = RegistryHelper.GetInstallInfo();
                if (!info.Found || info.ManifestsPath == null)
                    return new List<GameManifest>();
                manifestsPath = info.ManifestsPath;
            }

            if (!Directory.Exists(manifestsPath))
                return new List<GameManifest>();

            var results = new List<GameManifest>();
            foreach (string file in Directory.GetFiles(manifestsPath, "*.item"))
            {
                var m = ReadSingle(file);
                if (m != null)
                    results.Add(m);
            }

            return results.OrderBy(m => m.DisplayName).ToList();
        }

        public static GameManifest ReadSingle(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = File.ReadAllText(filePath);
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                return new GameManifest
                {
                    ManifestFile = filePath,
                    DisplayName = GetString(dict, "DisplayName"),
                    InstallLocation = GetString(dict, "InstallLocation"),
                    AppName = GetString(dict, "AppName"),
                    CatalogNamespace = GetString(dict, "CatalogNamespace"),
                    CatalogItemId = GetString(dict, "CatalogItemId"),
                    AppVersionString = GetString(dict, "AppVersionString"),
                    LaunchExecutable = GetString(dict, "LaunchExecutable"),
                    InstallSize = GetLong(dict, "InstallSize"),
                    MandatoryAppFolderName = GetString(dict, "MandatoryAppFolderName"),
                    IsIncompleteInstall = GetBool(dict, "bIsIncompleteInstall"),
                    NeedsValidation = GetBool(dict, "bNeedsValidation"),
                };
            }
            catch
            {
                return null;
            }
        }

        public static GameManifest FindByName(string gameName, string manifestsPath = null)
        {
            var all = ReadAll(manifestsPath);
            if (all.Count == 0) return null;

            // Exact match
            var exact = all.FirstOrDefault(m =>
                string.Equals(m.DisplayName, gameName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Partial match
            var partial = all.Where(m =>
                m.DisplayName != null &&
                m.DisplayName.IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (partial.Count == 1) return partial[0];

            // AppName match
            var byApp = all.FirstOrDefault(m =>
                m.AppName != null &&
                m.AppName.IndexOf(gameName, StringComparison.OrdinalIgnoreCase) >= 0);

            return byApp;
        }

        /// <summary>
        /// Check if a game folder has a valid .egstore subdirectory with manifests.
        /// </summary>
        public static bool HasEgstore(string gamePath)
        {
            if (!Directory.Exists(gamePath)) return false;
            string egstore = Path.Combine(gamePath, ".egstore");
            return Directory.Exists(egstore);
        }

        /// <summary>
        /// Check if .egstore contains chunk manifests (needed for verification).
        /// </summary>
        public static bool HasChunkManifests(string gamePath)
        {
            string egstore = Path.Combine(gamePath, ".egstore");
            if (!Directory.Exists(egstore)) return false;

            return Directory.GetFiles(egstore, "*.manifest").Length > 0;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object val) && val is string s)
                return s;
            return null;
        }

        private static long GetLong(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out object val)) return 0;
            if (val is int i) return i;
            if (val is long l) return l;
            if (val is decimal d) return (long)d;
            long.TryParse(val?.ToString(), out long result);
            return result;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out object val)) return false;
            if (val is bool b) return b;
            bool.TryParse(val?.ToString(), out bool result);
            return result;
        }
    }

    public class GameManifest
    {
        public string ManifestFile { get; set; }
        public string DisplayName { get; set; }
        public string InstallLocation { get; set; }
        public string AppName { get; set; }
        public string CatalogNamespace { get; set; }
        public string CatalogItemId { get; set; }
        public string AppVersionString { get; set; }
        public string LaunchExecutable { get; set; }
        public long InstallSize { get; set; }
        public string MandatoryAppFolderName { get; set; }
        public bool IsIncompleteInstall { get; set; }
        public bool NeedsValidation { get; set; }

        public string FormattedSize
        {
            get
            {
                if (InstallSize <= 0) return "N/A";
                double gb = InstallSize / (1024.0 * 1024 * 1024);
                if (gb >= 1) return string.Format("{0:N2} GB", gb);
                double mb = InstallSize / (1024.0 * 1024);
                return string.Format("{0:N2} MB", mb);
            }
        }

        public bool FolderExists
        {
            get { return !string.IsNullOrEmpty(InstallLocation) && Directory.Exists(InstallLocation); }
        }

        public string Status
        {
            get
            {
                if (IsIncompleteInstall) return "Incomplete";
                if (NeedsValidation) return "Needs Validation";
                if (!FolderExists) return "Missing";
                return "Installed";
            }
        }
    }
}
