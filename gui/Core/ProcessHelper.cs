using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace EgsLL.Core
{
    /// <summary>
    /// Launch, detect, suspend, and resume the EGS launcher process.
    /// Uses NtSuspendProcess/NtResumeProcess to pause downloads without
    /// modifying any EGS files or registry keys.
    /// </summary>
    public static class ProcessHelper
    {
        // --- Native interop for process suspend/resume ---

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        private const string EGS_PROCESS_NAME = "EpicGamesLauncher";

        // EGS may split downloads into a separate process.
        // We attempt to suspend/resume all of these.
        private static readonly string[] EGS_PROCESS_NAMES =
        {
            "EpicGamesLauncher",
            "EpicGamesDownloadManager",
            "EpicInstaller"
        };

        /// <summary>
        /// Check if any EGS launcher process is running.
        /// </summary>
        public static bool IsEgsRunning()
        {
            return Process.GetProcessesByName(EGS_PROCESS_NAME).Length > 0;
        }

        /// <summary>
        /// Get the first running EGS process, or null.
        /// </summary>
        public static Process GetEgsProcess()
        {
            return Process.GetProcessesByName(EGS_PROCESS_NAME).FirstOrDefault();
        }

        /// <summary>
        /// Launch EGS via its URI scheme to trigger an install for a specific app.
        /// Uses the full SandboxID:CatalogID:ArtifactID format when all three IDs
        /// are available, falling back to namespace-only for older manifests.
        /// </summary>
        public static bool LaunchEgsInstall(
            string catalogNamespace,
            string launcherExe = null,
            string catalogItemId = null,
            string appName = null)
        {
            // Prefer new 3-part URI: apps/{SandboxID}%3A{CatalogID}%3A{ArtifactID}
            string uri;
            if (!string.IsNullOrEmpty(catalogNamespace)
                && !string.IsNullOrEmpty(catalogItemId)
                && !string.IsNullOrEmpty(appName))
            {
                uri = string.Format(
                    "com.epicgames.launcher://apps/{0}%3A{1}%3A{2}?action=install",
                    Uri.EscapeDataString(catalogNamespace),
                    Uri.EscapeDataString(catalogItemId),
                    Uri.EscapeDataString(appName));
            }
            else
            {
                // Fallback: old deprecated format
                uri = string.Format(
                    "com.epicgames.launcher://apps/{0}?action=install",
                    Uri.EscapeDataString(catalogNamespace ?? ""));
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                // URI scheme not registered — try launching the exe directly
                if (launcherExe != null && System.IO.File.Exists(launcherExe))
                {
                    try
                    {
                        Process.Start(launcherExe);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Launch EGS without targeting a specific game.
        /// </summary>
        public static bool LaunchEgs(string launcherExe = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "com.epicgames.launcher://store",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                if (launcherExe != null && System.IO.File.Exists(launcherExe))
                {
                    try
                    {
                        Process.Start(launcherExe);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Suspend all EGS-related processes (freezes threads — pauses downloads).
        /// Tries the main launcher and the download manager.
        /// Returns true if at least one process was suspended.
        /// </summary>
        public static bool SuspendEgs(out string reason)
        {
            reason = null;
            bool any = false;
            string lastError = null;

            foreach (var name in EGS_PROCESS_NAMES)
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var proc in procs)
                {
                    string r;
                    if (SuspendProcess(proc.Id, out r))
                        any = true;
                    else
                        lastError = r;
                }
            }

            if (!any)
                reason = lastError ?? "No EGS processes found.";

            return any;
        }

        /// <summary>Overload without diagnostics (keeps existing callers compiling).</summary>
        public static bool SuspendEgs()
        {
            return SuspendEgs(out _);
        }

        /// <summary>
        /// Resume all previously suspended EGS-related processes.
        /// Returns true if at least one process was resumed.
        /// </summary>
        public static bool ResumeEgs(out string reason)
        {
            reason = null;
            bool any = false;
            string lastError = null;

            foreach (var name in EGS_PROCESS_NAMES)
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var proc in procs)
                {
                    string r;
                    if (ResumeProcess(proc.Id, out r))
                        any = true;
                    else
                        lastError = r;
                }
            }

            if (!any)
                reason = lastError ?? "No EGS processes found.";

            return any;
        }

        /// <summary>Overload without diagnostics.</summary>
        public static bool ResumeEgs()
        {
            return ResumeEgs(out _);
        }

        private static bool SuspendProcess(int pid, out string reason)
        {
            reason = null;
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                reason = string.Format(
                    "OpenProcess failed for PID {0} (Win32 error {1}). "
                    + "This usually means the process is protected or elevation is required.",
                    pid, err);
                return false;
            }

            try
            {
                int status = NtSuspendProcess(handle);
                if (status != 0)
                {
                    reason = string.Format(
                        "NtSuspendProcess returned NTSTATUS 0x{0:X8} for PID {1}.",
                        status, pid);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool ResumeProcess(int pid, out string reason)
        {
            reason = null;
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                reason = string.Format(
                    "OpenProcess failed for PID {0} (Win32 error {1}).",
                    pid, err);
                return false;
            }

            try
            {
                int status = NtResumeProcess(handle);
                if (status != 0)
                {
                    reason = string.Format(
                        "NtResumeProcess returned NTSTATUS 0x{0:X8} for PID {1}.",
                        status, pid);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
