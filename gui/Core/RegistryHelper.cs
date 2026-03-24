using System;
using System.IO;
using Microsoft.Win32;

namespace EgsLL.Core
{
    /// <summary>
    /// Read-only access to EGS registry keys and well-known paths.
    /// Never writes to the registry.
    /// </summary>
    public static class RegistryHelper
    {
        private static readonly string Launcher64 =
            @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher";
        private static readonly string Launcher32 =
            @"SOFTWARE\Epic Games\EpicGamesLauncher";

        private static readonly string Uninstall64 =
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{A2543E3C-4D82-49DC-B4A0-A5692E2B39FC}";
        private static readonly string Uninstall32 =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A2543E3C-4D82-49DC-B4A0-A5692E2B39FC}";

        public static EgsInstallInfo GetInstallInfo()
        {
            var info = new EgsInstallInfo();

            // AppDataPath from launcher key (64-bit, then 32-bit)
            info.AppDataPath = ReadLMValue(Launcher64, "AppDataPath")
                            ?? ReadLMValue(Launcher32, "AppDataPath");

            if (info.AppDataPath != null)
                info.Found = true;

            // Install location from uninstall key
            info.InstallDir = ReadLMValue(Uninstall64, "InstallLocation")
                           ?? ReadLMValue(Uninstall32, "InstallLocation");

            if (info.InstallDir != null)
            {
                info.Found = true;
                info.Version = ReadLMValue(Uninstall64, "DisplayVersion")
                            ?? ReadLMValue(Uninstall32, "DisplayVersion");

                string exe64 = Path.Combine(info.InstallDir,
                    @"Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe");
                string exe32 = Path.Combine(info.InstallDir,
                    @"Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe");

                if (File.Exists(exe64))
                    info.LauncherExe = exe64;
                else if (File.Exists(exe32))
                    info.LauncherExe = exe32;
            }

            // Data & manifests paths
            if (info.AppDataPath != null && Directory.Exists(info.AppDataPath))
            {
                info.DataPath = info.AppDataPath;
                info.ManifestsPath = Path.Combine(info.AppDataPath, "Manifests");
            }

            // Fallback to well-known default
            string defaultData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Epic\EpicGamesLauncher\Data");
            string defaultManifests = Path.Combine(defaultData, "Manifests");

            if ((info.ManifestsPath == null || !Directory.Exists(info.ManifestsPath))
                && Directory.Exists(defaultManifests))
            {
                info.DataPath = defaultData;
                info.ManifestsPath = defaultManifests;
                info.Found = true;
            }

            // Launcher exe fallback
            if (info.LauncherExe == null)
            {
                string progX86 = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);
                string candidate = Path.Combine(progX86,
                    @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe");

                if (File.Exists(candidate))
                {
                    info.LauncherExe = candidate;
                    if (info.InstallDir == null)
                        info.InstallDir = Path.Combine(progX86, "Epic Games");
                    info.Found = true;
                }
            }

            return info;
        }

        private static string ReadLMValue(string subKey, string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey, false))
                {
                    return key?.GetValue(valueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class EgsInstallInfo
    {
        public bool Found { get; set; }
        public string LauncherExe { get; set; }
        public string DataPath { get; set; }
        public string ManifestsPath { get; set; }
        public string AppDataPath { get; set; }
        public string InstallDir { get; set; }
        public string Version { get; set; }
    }
}
