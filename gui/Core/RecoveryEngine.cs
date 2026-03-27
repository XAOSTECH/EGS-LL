using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EgsLL.Core
{
    /// <summary>
    /// Orchestrates the full recovery workflow:
    /// 1. Rename game folder to backup
    /// 2. Launch EGS install via URI
    /// 3. FileSystemWatcher detects new folder creation
    /// 4. Suspend EGS process (pauses download) — or ask user to pause
    /// 5. Delete new (empty) folder, rename backup back
    /// 6. Resume EGS process (triggers verification)
    ///
    /// When running without admin elevation, NtSuspendProcess cannot
    /// always access the EGS process. The engine uses a three-tier
    /// cascade to pause the download:
    ///   1. UI Automation — click the Pause button via the Windows
    ///      accessibility tree (no elevation, legal, future-proof)
    ///   2. NtSuspendProcess — freeze the launcher process
    ///   3. User-action prompt — ask the user to pause manually
    /// </summary>
    public class RecoveryEngine : IDisposable
    {
        public const string BackupSuffix = "_egsll_bak";

        private FileSystemWatcher _watcher;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<bool> _userActionTcs;

        public event Action<RecoveryStage, string> StageChanged;
        public event Action<bool, string> Completed;

        public string GamePath { get; private set; }
        public string ParentDir { get; private set; }
        public string FolderName { get; private set; }
        public string BackupPath { get; private set; }

        public RecoveryEngine(string gamePath)
        {
            GamePath = gamePath;
            ParentDir = Path.GetDirectoryName(gamePath);
            FolderName = Path.GetFileName(gamePath);
            BackupPath = Path.Combine(ParentDir, FolderName + BackupSuffix);
        }

        /// <summary>
        /// Validate preconditions before starting recovery.
        /// Returns null on success, error message on failure.
        /// </summary>
        public string Validate()
        {
            if (!Directory.Exists(GamePath))
                return "Game folder does not exist: " + GamePath;

            if (Directory.Exists(BackupPath))
                return "Backup path already exists: " + BackupPath
                     + "\nRemove or rename it first.";

            return null;
        }

        /// <summary>
        /// Called by the UI when the user confirms they have paused EGS.
        /// Unblocks the engine to continue with the folder swap.
        /// </summary>
        public void ConfirmUserAction()
        {
            _userActionTcs?.TrySetResult(true);
        }

        /// <summary>
        /// Run the full automated recovery flow asynchronously.
        /// Reports progress via StageChanged events.
        /// Reports completion via Completed event.
        /// </summary>
        public async Task RunAsync(GameManifest manifest)
        {
            _cts = new CancellationTokenSource();

            try
            {
                // --- Step 1: Rename folder to backup ---
                Report(RecoveryStage.Renaming,
                    string.Format("Renaming {0} \u2192 {1}{2}", FolderName, FolderName, BackupSuffix));

                Directory.Move(GamePath, BackupPath);

                Report(RecoveryStage.Renamed, "Folder renamed to backup.");

                // --- Step 2: Launch EGS install ---
                Report(RecoveryStage.LaunchingEgs, "Launching EGS install...");

                string launcherExe = null;
                var egsInfo = RegistryHelper.GetInstallInfo();
                if (egsInfo.Found)
                    launcherExe = egsInfo.LauncherExe;

                bool launched = false;
                if (manifest != null && !string.IsNullOrEmpty(manifest.CatalogNamespace))
                {
                    launched = ProcessHelper.LaunchEgsInstall(
                        manifest.CatalogNamespace, launcherExe,
                        manifest.CatalogItemId, manifest.AppName);
                }

                if (!launched)
                {
                    launched = ProcessHelper.LaunchEgs(launcherExe);
                }

                if (!launched)
                {
                    Report(RecoveryStage.LaunchingEgs,
                        "Could not launch EGS automatically. Please open it manually and start the install.");
                }

                // --- Step 3: Wait for EGS to create the new folder ---
                Report(RecoveryStage.WaitingForFolder,
                    "Waiting for EGS to create: " + FolderName);

                bool folderCreated = await WaitForFolderAsync(GamePath, TimeSpan.FromMinutes(10));

                if (!folderCreated)
                {
                    Report(RecoveryStage.Error, "Timed out waiting for EGS to create the game folder. Restoring backup...");
                    RestoreBackup();
                    OnCompleted(false, "Timed out. Backup restored.");
                    return;
                }

                Report(RecoveryStage.FolderDetected, "New folder detected. Waiting for download to begin...");

                // Wait for EGS to write initial files — the cascade
                // must NOT fire until the download has genuinely
                // started, otherwise we suspend a process that hasn't
                // touched .egstore yet.
                bool downloadStarted = await WaitForDownloadStartAsync(GamePath, TimeSpan.FromMinutes(5));

                if (!downloadStarted)
                {
                    Report(RecoveryStage.Error,
                        "Timed out waiting for EGS to start the download. "
                        + "The install may need to be triggered manually.");
                    RestoreBackup();
                    OnCompleted(false, "Download never started. Backup restored.");
                    return;
                }

                Report(RecoveryStage.FolderDetected, "Download activity confirmed.");

                // --- Step 4: Pause the download (three-tier cascade) ---

                // Tier 1: UI Automation — click the Pause button in EGS
                Report(RecoveryStage.SuspendingEgs, "Tier 1: Attempting UIA pause (accessibility tree)...");

                bool paused = false;
                bool usedNtSuspend = false;

                var uiaResult = await UIAutomationHelper.WaitAndPauseAsync(
                    TimeSpan.FromSeconds(10), _cts.Token);

                if (uiaResult.Success)
                {
                    paused = true;
                    Report(RecoveryStage.EgsSuspended, "Download paused via UI Automation: " + uiaResult.Detail);
                }

                // Tier 2: NtSuspendProcess — freeze the process
                if (!paused)
                {
                    Report(RecoveryStage.SuspendingEgs,
                        "UIA could not pause: " + uiaResult.Detail);
                    Report(RecoveryStage.SuspendingEgs,
                        "Tier 2: Attempting NtSuspendProcess...");

                    string suspendReason;
                    bool suspended = ProcessHelper.SuspendEgs(out suspendReason);
                    if (suspended)
                    {
                        paused = true;
                        usedNtSuspend = true;
                        Report(RecoveryStage.EgsSuspended, "EGS process suspended (download frozen).");
                    }
                    else
                    {
                        Report(RecoveryStage.SuspendingEgs,
                            "NtSuspendProcess failed: " + (suspendReason ?? "unknown error"));
                    }
                }

                // Tier 3: Ask the user to pause manually
                if (!paused)
                {
                    Report(RecoveryStage.UserActionRequired,
                        "Tier 3: Automatic pause failed. Manual action required.");
                    Report(RecoveryStage.UserActionRequired,
                        "Please PAUSE the download in Epic Games Store, then click \u2018I\u2019ve Paused\u2019 below.");

                    _userActionTcs = new TaskCompletionSource<bool>();

                    using (var reg = _cts.Token.Register(() => _userActionTcs.TrySetCanceled()))
                    {
                        await _userActionTcs.Task;
                    }

                    Report(RecoveryStage.EgsSuspended, "User confirmed download is paused. Continuing...");
                }

                // --- Step 5: Swap folders ---
                Report(RecoveryStage.Swapping, "Removing new folder and restoring backup...");

                try
                {
                    long newSize = GetFolderSizeBytes(GamePath);
                    long newSizeMB = newSize / (1024 * 1024);

                    if (newSizeMB > 500)
                    {
                        Report(RecoveryStage.Swapping,
                            string.Format("New folder is {0} MB \u2014 larger than expected but proceeding.", newSizeMB));
                    }

                    Directory.Delete(GamePath, true);
                    Directory.Move(BackupPath, GamePath);

                    Report(RecoveryStage.SwapComplete, "Folder swap complete!");
                }
                catch (Exception ex)
                {
                    Report(RecoveryStage.Error, "Swap failed: " + ex.Message);

                    if (usedNtSuspend)
                        ProcessHelper.ResumeEgs();

                    OnCompleted(false,
                        "Swap failed. Your files are at: " + BackupPath);
                    return;
                }

                // --- Step 6: Resume EGS ---
                Report(RecoveryStage.ResumingEgs, "Resuming EGS (will verify existing files)...");

                if (usedNtSuspend)
                {
                    // We froze the process — need to thaw it
                    bool resumed = ProcessHelper.ResumeEgs();
                    if (resumed)
                        Report(RecoveryStage.Complete, "EGS resumed. It should now VERIFY instead of re-downloading.");
                    else
                        Report(RecoveryStage.Complete, "Could not resume EGS automatically. Please resume the download in EGS manually.");
                }
                else if (paused)
                {
                    // Paused via UIA or user action — try UIA resume, then prompt
                    var uiaResume = UIAutomationHelper.ResumeDownload();
                    if (uiaResume.Success)
                    {
                        Report(RecoveryStage.Complete,
                            "Download resumed via UI Automation. EGS should now verify existing files.");
                    }
                    else
                    {
                        Report(RecoveryStage.Complete,
                            "Please RESUME the download in EGS. It should verify existing files.");
                    }
                }
                else
                {
                    Report(RecoveryStage.Complete, "Please RESUME the download in EGS. It should verify existing files.");
                }

                OnCompleted(true, "Recovery complete!");
            }
            catch (OperationCanceledException)
            {
                Report(RecoveryStage.Error, "Recovery cancelled. Attempting to restore backup...");
                RestoreBackup();
                OnCompleted(false, "Cancelled. Backup restored.");
            }
            catch (Exception ex)
            {
                Report(RecoveryStage.Error, "Unexpected error: " + ex.Message);
                RestoreBackup();
                OnCompleted(false, "Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Cancel the recovery flow. Attempts to restore the backup.
        /// </summary>
        public void Cancel()
        {
            _userActionTcs?.TrySetCanceled();
            _cts?.Cancel();
        }

        /// <summary>
        /// Emergency restore: put the backup folder back.
        /// </summary>
        public bool RestoreBackup()
        {
            try
            {
                if (!Directory.Exists(BackupPath))
                    return false;

                if (Directory.Exists(GamePath))
                {
                    try { Directory.Delete(GamePath, true); }
                    catch { /* best effort */ }
                }

                if (!Directory.Exists(GamePath))
                {
                    Directory.Move(BackupPath, GamePath);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for a directory to be created.
        /// Uses FileSystemWatcher for instant detection + polling as fallback.
        /// </summary>
        private async Task<bool> WaitForFolderAsync(string path, TimeSpan timeout)
        {
            // Use FileSystemWatcher for instant detection + polling as fallback
            var tcs = new TaskCompletionSource<bool>();

            _watcher = new FileSystemWatcher(ParentDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += (s, e) =>
            {
                if (string.Equals(e.Name, FolderName, StringComparison.OrdinalIgnoreCase))
                    tcs.TrySetResult(true);
            };

            // Also poll in case the watcher misses it
            var pollTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (Directory.Exists(path))
                    {
                        tcs.TrySetResult(true);
                        return;
                    }
                    await Task.Delay(1000);
                }
            });

            // Race: watcher/poll vs timeout
            var timeoutTask = Task.Delay(timeout, _cts.Token);
            var winner = await Task.WhenAny(tcs.Task, timeoutTask);

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;

            return winner == tcs.Task && tcs.Task.Result;
        }

        /// <summary>
        /// Wait until EGS begins writing files into the folder,
        /// indicating the download has actually started. Checks
        /// for .egstore (EGS download state) or any files.
        /// Returns false if the timeout elapses without activity.
        /// </summary>
        private async Task<bool> WaitForDownloadStartAsync(string path, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            string egstorePath = Path.Combine(path, ".egstore");

            while (DateTime.UtcNow < deadline && !_cts.Token.IsCancellationRequested)
            {
                if (Directory.Exists(egstorePath))
                    return true;

                try
                {
                    if (Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length > 0)
                        return true;
                }
                catch { /* folder may be briefly locked */ }

                await Task.Delay(1000, _cts.Token);
            }

            return false;
        }

        private static long GetFolderSizeBytes(string path)
        {
            if (!Directory.Exists(path)) return 0;

            long total = 0;
            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { /* skip locked files */ }
                }
            }
            catch { /* access denied etc. */ }

            return total;
        }

        private void Report(RecoveryStage stage, string message)
        {
            StageChanged?.Invoke(stage, message);
        }

        private void OnCompleted(bool success, string message)
        {
            Completed?.Invoke(success, message);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _watcher?.Dispose();
        }
    }

    public enum RecoveryStage
    {
        Renaming,
        Renamed,
        LaunchingEgs,
        WaitingForFolder,
        FolderDetected,
        SuspendingEgs,
        EgsSuspended,
        UserActionRequired,
        Swapping,
        SwapComplete,
        ResumingEgs,
        Complete,
        Error
    }
}
