#Requires -Version 5.1
<#
.SYNOPSIS
    Read Epic Games Store configuration from the Windows registry.
.DESCRIPTION
    Reads publicly accessible registry keys to determine EGS install location,
    data paths, and launcher executable path. Read-only — never writes to the registry.
#>

Set-StrictMode -Version Latest

# --- Known Registry Paths ---

$script:EGS_REG_PATHS = @{
    Launcher64   = 'HKLM:\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher'
    Launcher32   = 'HKLM:\SOFTWARE\Epic Games\EpicGamesLauncher'
    Unreal64     = 'HKLM:\SOFTWARE\WOW6432Node\Epic Games\UnrealEngineLauncher'
    Unreal32     = 'HKLM:\SOFTWARE\Epic Games\UnrealEngineLauncher'
    Uninstall64  = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{A2543E3C-4D82-49DC-B4A0-A5692E2B39FC}'
    Uninstall32  = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A2543E3C-4D82-49DC-B4A0-A5692E2B39FC}'
}

# --- Default Paths ---

$script:EGS_DEFAULT_DATA = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data'
$script:EGS_DEFAULT_MANIFESTS = Join-Path $script:EGS_DEFAULT_DATA 'Manifests'

function Get-EgsRegistryValue {
    <#
    .SYNOPSIS
        Safely read a single registry value. Returns $null if not found.
    #>
    param(
        [Parameter(Mandatory)][string]$KeyPath,
        [Parameter(Mandatory)][string]$ValueName
    )

    try {
        $key = Get-ItemProperty -LiteralPath $KeyPath -Name $ValueName -ErrorAction Stop
        return $key.$ValueName
    }
    catch {
        return $null
    }
}

function Get-EgsInstallInfo {
    <#
    .SYNOPSIS
        Gather EGS installation information from the registry.
    .OUTPUTS
        PSCustomObject with properties: LauncherPath, DataPath, ManifestsPath,
        AppDataPath, InstallDir, Version, Found
    #>

    $info = [PSCustomObject]@{
        Found          = $false
        LauncherPath   = $null
        LauncherExe    = $null
        DataPath       = $null
        ManifestsPath  = $null
        AppDataPath    = $null
        InstallDir     = $null
        Version        = $null
    }

    # Try 64-bit then 32-bit registry paths
    foreach ($regKey in @($script:EGS_REG_PATHS.Launcher64, $script:EGS_REG_PATHS.Launcher32)) {
        $appData = Get-EgsRegistryValue -KeyPath $regKey -ValueName 'AppDataPath'
        if ($appData) {
            $info.AppDataPath = $appData
            $info.Found = $true
            break
        }
    }

    # Try uninstall keys for install directory & exe path
    foreach ($regKey in @($script:EGS_REG_PATHS.Uninstall64, $script:EGS_REG_PATHS.Uninstall32)) {
        $installLoc = Get-EgsRegistryValue -KeyPath $regKey -ValueName 'InstallLocation'
        if ($installLoc) {
            $info.InstallDir = $installLoc
            $info.Found = $true

            $displayVersion = Get-EgsRegistryValue -KeyPath $regKey -ValueName 'DisplayVersion'
            if ($displayVersion) { $info.Version = $displayVersion }

            # Common exe location
            $exeCandidate = Join-Path $installLoc 'Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe'
            if (Test-Path -LiteralPath $exeCandidate) {
                $info.LauncherExe = $exeCandidate
            }
            # Fallback: Win32
            $exeCandidate32 = Join-Path $installLoc 'Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe'
            if (-not $info.LauncherExe -and (Test-Path -LiteralPath $exeCandidate32)) {
                $info.LauncherExe = $exeCandidate32
            }

            break
        }
    }

    # Determine data & manifests paths
    if ($info.AppDataPath -and (Test-Path -LiteralPath $info.AppDataPath)) {
        $info.DataPath = $info.AppDataPath
        $info.ManifestsPath = Join-Path $info.AppDataPath 'Manifests'
    }

    # Fallback to well-known default location
    if (-not $info.ManifestsPath -or -not (Test-Path -LiteralPath $info.ManifestsPath)) {
        if (Test-Path -LiteralPath $script:EGS_DEFAULT_MANIFESTS) {
            $info.DataPath = $script:EGS_DEFAULT_DATA
            $info.ManifestsPath = $script:EGS_DEFAULT_MANIFESTS
            $info.Found = $true
        }
    }

    # Try the common launcher path as ultimate fallback
    if (-not $info.LauncherExe) {
        $commonPaths = @(
            "${env:ProgramFiles(x86)}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
            "${env:ProgramFiles}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"
        )
        foreach ($p in $commonPaths) {
            if ($p -and (Test-Path -LiteralPath $p)) {
                $info.LauncherExe = $p
                if (-not $info.InstallDir) {
                    # Walk up from Launcher\Portal\Binaries\Win64
                    $info.InstallDir = (Split-Path (Split-Path (Split-Path (Split-Path $p))))
                }
                $info.Found = $true
                break
            }
        }
    }

    return $info
}

function Show-EgsInfo {
    <#
    .SYNOPSIS
        Display EGS installation info in a formatted table.
    #>
    $info = Get-EgsInstallInfo

    if (-not $info.Found) {
        Write-EgsLog "Epic Games Store installation not detected." -Level Error
        Write-EgsLog "Checked registry paths:" -Level Debug
        foreach ($key in $script:EGS_REG_PATHS.Keys) {
            Write-EgsLog "  $($script:EGS_REG_PATHS[$key])" -Level Debug
        }
        Write-EgsLog "Also checked default data path: $script:EGS_DEFAULT_MANIFESTS" -Level Debug
        return $null
    }

    Write-EgsLog "Epic Games Store Installation" -Level Success
    Write-Host ""
    Write-Host "  Install Dir   : $($info.InstallDir ?? 'N/A')"
    Write-Host "  Launcher Exe  : $($info.LauncherExe ?? 'N/A')"
    Write-Host "  Version       : $($info.Version ?? 'N/A')"
    Write-Host "  Data Path     : $($info.DataPath ?? 'N/A')"
    Write-Host "  Manifests     : $($info.ManifestsPath ?? 'N/A')"
    Write-Host "  App Data Path : $($info.AppDataPath ?? 'N/A')"
    Write-Host "  Running       : $(if (Test-EgsRunning) { 'Yes' } else { 'No' })"
    Write-Host ""

    if ($info.ManifestsPath -and (Test-Path -LiteralPath $info.ManifestsPath)) {
        $manifestCount = (Get-ChildItem -LiteralPath $info.ManifestsPath -Filter '*.item' -File -ErrorAction SilentlyContinue).Count
        Write-Host "  Game manifests found: $manifestCount"
    }

    return $info
}
