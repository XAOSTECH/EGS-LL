#Requires -Version 5.1
<#
.SYNOPSIS
    EGS-LL — Experienced Game Store Launcher Launcher
    Quality-of-life wrapper for the Epic Games Store on Windows.

.DESCRIPTION
    A FOSS wrapper that automates common EGS workarounds using only
    public Windows APIs (registry reads, filesystem operations).

    No launcher binaries are modified, reverse-engineered, or patched.

.PARAMETER Command
    The action to perform:
      list     — List all EGS-managed games
      info     — Show EGS install paths and configuration
      recover  — Recover/verify an existing game installation
      restore  — Emergency restore a backup folder
      help     — Show this help text

.PARAMETER GameName
    Game name (for recover/restore commands). Partial match supported.

.PARAMETER GameDir
    Explicit game folder path (overrides manifest lookup).

.PARAMETER Yes
    Skip confirmation prompts.

.EXAMPLE
    .\egs-ll.ps1 list
    .\egs-ll.ps1 info
    .\egs-ll.ps1 recover "Red Dead Redemption 2"
    .\egs-ll.ps1 recover "Fortnite" -GameDir "D:\Games\Fortnite"
    .\egs-ll.ps1 restore "D:\Games\MyGame"
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('list', 'info', 'recover', 'restore', 'help', '')]
    [string]$Command = 'help',

    [Parameter(Position = 1)]
    [string]$GameName,

    [string]$GameDir,

    [switch]$Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Load Modules ---
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $scriptDir 'lib\utils.ps1')
. (Join-Path $scriptDir 'lib\registry.ps1')
. (Join-Path $scriptDir 'lib\manifest.ps1')
. (Join-Path $scriptDir 'lib\recovery.ps1')

# --- Banner ---
function Show-Banner {
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════╗" -ForegroundColor DarkCyan
    Write-Host "  ║  EGS-LL — Experienced Game Store Launcher Launcher  ║" -ForegroundColor DarkCyan
    Write-Host "  ║  Quality-of-life wrapper  •  v0.1.0           ║" -ForegroundColor DarkCyan
    Write-Host "  ╚═══════════════════════════════════════════════╝" -ForegroundColor DarkCyan
    Write-Host ""
}

function Show-Help {
    Show-Banner
    Write-Host '  Usage: .\egs-ll.ps1 <command> [options]'
    Write-Host ''
    Write-Host '  Commands:' -ForegroundColor Cyan
    Write-Host '    list                          List all EGS-managed games'
    Write-Host '    info                          Show EGS installation details'
    Write-Host '    recover <name> [-GameDir <p>]  Recover/verify a game install'
    Write-Host '    restore <path>                Emergency restore a backup folder'
    Write-Host '    help                          Show this help'
    Write-Host ''
    Write-Host '  Options:' -ForegroundColor Cyan
    Write-Host '    -GameDir <path>  Override game folder path (for recover)'
    Write-Host '    -Yes             Skip confirmation prompts'
    Write-Host ""
    Write-Host "  Examples:" -ForegroundColor Cyan
    Write-Host '    .\egs-ll.ps1 list'
    Write-Host '    .\egs-ll.ps1 recover "Red Dead Redemption 2"'
    Write-Host '    .\egs-ll.ps1 recover "Fortnite" -GameDir "D:\Games\Fortnite"'
    Write-Host '    .\egs-ll.ps1 restore "D:\Games\MyGame"'
    Write-Host ""
    Write-Host "  This tool does NOT modify the Epic Games Store launcher." -ForegroundColor DarkGray
    Write-Host "  It only uses public registry keys and filesystem operations." -ForegroundColor DarkGray
    Write-Host ""
}

# --- Main ---

Show-Banner

switch ($Command) {
    'list' {
        Show-EgsGames
    }

    'info' {
        Show-EgsInfo
    }

    'recover' {
        if (-not $GameName) {
            Write-EgsLog 'Usage: .\egs-ll.ps1 recover <game-name> [-GameDir <path>]' -Level Error
            Write-EgsLog 'Example: .\egs-ll.ps1 recover "Red Dead Redemption 2"' -Level Info
            exit 1
        }

        $result = Start-RecoveryFlow -GameName $GameName -GameDir $GameDir -SkipConfirm:$Yes
        if (-not $result) {
            exit 1
        }
    }

    'restore' {
        if (-not $GameName) {
            Write-EgsLog 'Usage: .\egs-ll.ps1 restore <game-folder-path>' -Level Error
            Write-EgsLog 'Example: .\egs-ll.ps1 restore "D:\Games\MyGame"' -Level Info
            exit 1
        }

        $result = Start-QuickRecover -GamePath $GameName
        if (-not $result) {
            exit 1
        }
    }

    'help' {
        Show-Help
    }

    default {
        Show-Help
    }
}
