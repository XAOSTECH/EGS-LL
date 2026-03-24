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
        /// Falls back to launching the exe directly if URI fails.
        /// </summary>
        public static bool LaunchEgsInstall(string catalogNamespace, string launcherExe = null)
        {
            // Try URI scheme first: com.epicgames.launcher://apps/{ns}?action=install
            string uri = string.Format(
                "com.epicgames.launcher://apps/{0}?action=install",
                Uri.EscapeDataString(catalogNamespace));

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
        /// Suspend the EGS process (freezes all threads — pauses downloads).
        /// Returns true if successful.
        /// </summary>
        public static bool SuspendEgs()
        {
            var proc = GetEgsProcess();
            if (proc == null) return false;

            return SuspendProcess(proc.Id);
        }

        /// <summary>
        /// Resume a previously suspended EGS process.
        /// Returns true if successful.
        /// </summary>
        public static bool ResumeEgs()
        {
            var proc = GetEgsProcess();
            if (proc == null) return false;

            return ResumeProcess(proc.Id);
        }

        private static bool SuspendProcess(int pid)
        {
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (handle == IntPtr.Zero) return false;

            try
            {
                return NtSuspendProcess(handle) == 0;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool ResumeProcess(int pid)
        {
            IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (handle == IntPtr.Zero) return false;

            try
            {
                return NtResumeProcess(handle) == 0;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
