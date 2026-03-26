using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EgsLL.Core
{
    /// <summary>
    /// Scans all ready drives for game launcher folders (e.g. "Epic Games")
    /// using a breadth-first search. Stops recursion on each drive as soon as
    /// a target folder is found (assumes one per drive). Follows no symlinks
    /// or reparse points.
    /// </summary>
    public static class DriveScanner
    {
        /// <summary>
        /// Folder names to search for. Each entry is a game store's
        /// well-known installation parent folder. Currently only EGS.
        /// </summary>
        private static readonly string[] TargetFolders = new[]
        {
            "Epic Games"
        };

        /// <summary>
        /// Directories to skip during traversal (case-insensitive).
        /// </summary>
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Windows",
            "Recovery", "PerfLogs"
        };

        /// <summary>
        /// Scan all ready drives for game launcher folders.
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
                BfsSearchDrive(drive.RootDirectory.FullName, found, progress, ct);
            }

            return found.Values.OrderBy(g => g.DisplayName).ToList();
        }

        /// <summary>
        /// Breadth-first search of a single drive. Processes directories
        /// level by level so shallow hits are found quickly. Stops as
        /// soon as one target folder is found on this drive.
        /// Skips reparse points (symlinks, junctions) to avoid loops
        /// and false positives from e.g. Wine prefix mounts.
        /// </summary>
        private static void BfsSearchDrive(
            string root,
            Dictionary<string, GameManifest> found,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                string current = queue.Dequeue();

                string[] children;
                try
                {
                    children = Directory.GetDirectories(current);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (string child in children)
                {
                    ct.ThrowIfCancellationRequested();

                    string dirName = Path.GetFileName(child);
                    if (SkipDirs.Contains(dirName)) continue;

                    // Skip reparse points (symlinks, junctions) to avoid
                    // loops and Wine prefix false positives
                    try
                    {
                        var attrs = File.GetAttributes(child);
                        if ((attrs & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch { continue; }

                    // Check if this directory matches any target folder
                    bool isTarget = false;
                    for (int i = 0; i < TargetFolders.Length; i++)
                    {
                        if (string.Equals(dirName, TargetFolders[i],
                                StringComparison.OrdinalIgnoreCase))
                        {
                            isTarget = true;
                            break;
                        }
                    }

                    if (isTarget)
                    {
                        progress?.Report("Found: " + child);
                        HarvestGames(child, found, ct);
                        // One target per drive is enough — stop searching
                        return;
                    }

                    // Enqueue for next level
                    queue.Enqueue(child);
                }
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
