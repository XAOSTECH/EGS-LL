using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EgsLL.Core
{
    /// <summary>
    /// Scans all fixed drives for directories containing .egstore subdirectories,
    /// which indicates an EGS game installation that may not be registered in manifests.
    /// </summary>
    public static class DriveScanner
    {
        /// <summary>
        /// Well-known folder names that EGS commonly installs into.
        /// Scanned first (shallow) before falling back to deeper search.
        /// </summary>
        private static readonly string[] CommonParentFolders = new[]
        {
            "Epic Games",
            "Games",
            "EpicGames"
        };

        /// <summary>
        /// Directories to skip during recursive scan (case-insensitive).
        /// </summary>
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Windows",
            "ProgramData", "Recovery", "PerfLogs",
            "node_modules", ".git", "__pycache__"
        };

        /// <summary>
        /// Scan all fixed drives for game folders with .egstore directories.
        /// Returns GameManifest stubs for each discovered folder.
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
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .ToList();

            // Phase 1: check well-known parent folders on each drive (fast)
            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report("Checking " + drive.Name + " (common paths)...");

                foreach (string parent in CommonParentFolders)
                {
                    string parentPath = Path.Combine(drive.RootDirectory.FullName, parent);
                    ScanParentFolder(parentPath, found, ct);
                }

                // Also check root-level folders named "Epic Games" under
                // Program Files variants
                string[] progDirs =
                {
                    Path.Combine(drive.RootDirectory.FullName, "Program Files"),
                    Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)")
                };

                foreach (string progDir in progDirs)
                {
                    string epicDir = Path.Combine(progDir, "Epic Games");
                    ScanParentFolder(epicDir, found, ct);
                }
            }

            // Phase 2: shallow scan of each drive root for directories that
            // themselves contain .egstore (depth 2 from root)
            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report("Scanning " + drive.Name + " (root folders)...");

                ScanDirectoryShallow(drive.RootDirectory.FullName, found, 2, ct);
            }

            return found.Values.OrderBy(g => g.DisplayName).ToList();
        }

        /// <summary>
        /// Scan children of a known parent directory (e.g. E:\Epic Games\*).
        /// </summary>
        private static void ScanParentFolder(
            string parentPath,
            Dictionary<string, GameManifest> found,
            CancellationToken ct)
        {
            if (!Directory.Exists(parentPath)) return;

            try
            {
                foreach (string subDir in Directory.GetDirectories(parentPath))
                {
                    ct.ThrowIfCancellationRequested();
                    TryAddGame(subDir, found);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        /// <summary>
        /// Recursively scan up to maxDepth levels for .egstore directories.
        /// </summary>
        private static void ScanDirectoryShallow(
            string path,
            Dictionary<string, GameManifest> found,
            int maxDepth,
            CancellationToken ct)
        {
            if (maxDepth <= 0) return;

            try
            {
                foreach (string subDir in Directory.GetDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();

                    string dirName = Path.GetFileName(subDir);
                    if (SkipDirs.Contains(dirName)) continue;
                    if (dirName.StartsWith(".")) continue;

                    if (TryAddGame(subDir, found))
                        continue; // Found a game, don't recurse into it

                    ScanDirectoryShallow(subDir, found, maxDepth - 1, ct);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
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
