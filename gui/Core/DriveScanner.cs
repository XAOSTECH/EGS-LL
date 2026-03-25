using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EgsLL.Core
{
    /// <summary>
    /// Recursively scans all ready drives for "Epic Games" folders and
    /// discovers game installations via their .egstore subdirectories.
    /// </summary>
    public static class DriveScanner
    {
        private const string EpicGamesFolderName = "Epic Games";

        /// <summary>
        /// Directories to skip during recursive traversal (case-insensitive).
        /// These are OS/system paths that will never contain game installs.
        /// </summary>
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Windows",
            "Recovery", "PerfLogs"
        };

        /// <summary>
        /// Scan all ready drives recursively for "Epic Games" folders.
        /// Returns GameManifest stubs for each discovered game directory.
        /// </summary>
        public static Task<List<GameManifest>> ScanAsync(
            IProgress<string> progress = null,
            CancellationToken ct = default)
        {
            return Task.Run(() => Scan(progress, ct), ct);
        }

        private static List<GameManifest> Scan(
            IProgress<string> progress, CancellationToken ct)
        {
            var found = new Dictionary<string, GameManifest>(
                StringComparer.OrdinalIgnoreCase);

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();

            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report("Scanning " + drive.Name + "...");
                SearchForEpicGames(drive.RootDirectory.FullName, found, progress, ct);
            }

            return found.Values.OrderBy(g => g.DisplayName).ToList();
        }

        /// <summary>
        /// Recursively walk the directory tree looking for folders named
        /// "Epic Games". When found, scan their children for .egstore.
        /// Errors on individual directories are silently skipped so that
        /// one access-denied folder never aborts the rest of the drive.
        /// </summary>
        private static void SearchForEpicGames(
            string path,
            Dictionary<string, GameManifest> found,
            IProgress<string> progress,
            CancellationToken ct)
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(path);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (string child in children)
            {
                ct.ThrowIfCancellationRequested();

                string dirName = Path.GetFileName(child);

                if (SkipDirs.Contains(dirName)) continue;

                if (string.Equals(dirName, EpicGamesFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    // Found an "Epic Games" folder — harvest game dirs inside it
                    progress?.Report("Found: " + child);
                    HarvestGames(child, found, ct);
                    continue;
                }

                // Keep recursing
                SearchForEpicGames(child, found, progress, ct);
            }
        }

        /// <summary>
        /// Given an "Epic Games" folder, check each child directory for
        /// .egstore and add it as a discovered game.
        /// </summary>
        private static void HarvestGames(
            string epicGamesPath,
            Dictionary<string, GameManifest> found,
            CancellationToken ct)
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(epicGamesPath);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (string gameDir in children)
            {
                ct.ThrowIfCancellationRequested();
                TryAddGame(gameDir, found);
            }
        }

        /// <summary>
        /// Check if a directory has .egstore and add it as a discovered game.
        /// Returns true if it was a game folder.
        /// </summary>
        private static bool TryAddGame(
            string dirPath,
            Dictionary<string, GameManifest> found)
        {
            string egstore = Path.Combine(dirPath, ".egstore");
            if (!Directory.Exists(egstore)) return false;
            if (found.ContainsKey(dirPath)) return true;

            // Try to read the display name from .egstore .item files
            string displayName = null;
            try
            {
                var itemFiles = Directory.GetFiles(egstore, "*.item");
                if (itemFiles.Length > 0)
                {
                    var m = ManifestReader.ReadSingle(itemFiles[0]);
                    if (m != null)
                        displayName = m.DisplayName;
                }
            }
            catch { /* best effort */ }

            if (displayName == null)
                displayName = Path.GetFileName(dirPath);

            bool hasChunks = false;
            try
            {
                hasChunks = Directory.GetFiles(egstore, "*.manifest").Length > 0;
            }
            catch { /* best effort */ }

            found[dirPath] = new GameManifest
            {
                DisplayName = displayName,
                InstallLocation = dirPath,
                IsDiscovered = true,
                HasChunkManifests = hasChunks
            };

            return true;
        }

        /// <summary>
        /// Merge manifest-registered games with drive-discovered games.
        /// Manifest entries take priority; discovered entries fill in the gaps.
        /// </summary>
        public static List<GameManifest> MergeResults(
            List<GameManifest> fromManifests,
            List<GameManifest> fromScan)
        {
            var merged = new Dictionary<string, GameManifest>(
                StringComparer.OrdinalIgnoreCase);

            // Manifest entries first (authoritative)
            foreach (var m in fromManifests)
            {
                if (!string.IsNullOrEmpty(m.InstallLocation))
                    merged[m.InstallLocation] = m;
            }

            // Discovered entries fill gaps
            foreach (var d in fromScan)
            {
                if (!string.IsNullOrEmpty(d.InstallLocation)
                    && !merged.ContainsKey(d.InstallLocation))
                {
                    merged[d.InstallLocation] = d;
                }
            }

            return merged.Values.OrderBy(g => g.DisplayName).ToList();
        }
    }
}
