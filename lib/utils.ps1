#Requires -Version 5.1
<#
.SYNOPSIS
    Shared utility functions for EGS-LL.
#>

Set-StrictMode -Version Latest

# --- Logging ---

function Write-EgsLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('Info','Warn','Error','Success','Debug')][string]$Level = 'Info'
    )

    $prefix = switch ($Level) {
        'Info'    { '[*]' }
        'Warn'    { '[!]' }
        'Error'   { '[X]' }
        'Success' { '[+]' }
        'Debug'   { '[~]' }
    }

    $color = switch ($Level) {
        'Info'    { 'Cyan' }
        'Warn'    { 'Yellow' }
        'Error'   { 'Red' }
        'Success' { 'Green' }
        'Debug'   { 'DarkGray' }
    }

    Write-Host "$prefix $Message" -ForegroundColor $color
}

# --- Path Helpers ---

function Resolve-EgsPath {
    <#
    .SYNOPSIS
        Expand environment variables and resolve a path. Returns $null if invalid.
    #>
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (Test-Path -LiteralPath $expanded) {
        return (Resolve-Path -LiteralPath $expanded).Path
    }
    return $expanded
}

function Test-GameFolder {
    <#
    .SYNOPSIS
        Check whether a folder looks like a valid EGS game directory
        (contains an .egstore subfolder with manifests).
    #>
    param([Parameter(Mandatory)][string]$FolderPath)

    if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) {
        return $false
    }

    $egstore = Join-Path $FolderPath '.egstore'
    return (Test-Path -LiteralPath $egstore -PathType Container)
}

function Get-FolderSizeMB {
    <#
    .SYNOPSIS
        Get approximate folder size in MB.
    #>
    param([Parameter(Mandatory)][string]$FolderPath)

    if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) {
        return 0
    }

    $bytes = (Get-ChildItem -LiteralPath $FolderPath -Recurse -File -ErrorAction SilentlyContinue |
              Measure-Object -Property Length -Sum).Sum
    if ($null -eq $bytes) { return 0 }
    return [math]::Round($bytes / 1MB, 2)
}

function Format-Size {
    <#
    .SYNOPSIS
        Format a byte count as a human-readable string.
    #>
    param([long]$Bytes)

    if ($Bytes -ge 1TB) { return "{0:N2} TB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

# --- Confirmation ---

function Confirm-Action {
    <#
    .SYNOPSIS
        Ask the user for yes/no confirmation.
    #>
    param(
        [Parameter(Mandatory)][string]$Prompt,
        [switch]$DefaultYes
    )

    $suffix = if ($DefaultYes) { '[Y/n]' } else { '[y/N]' }
    $response = Read-Host "$Prompt $suffix"

    if ([string]::IsNullOrWhiteSpace($response)) {
        return [bool]$DefaultYes
    }

    return $response.Trim().ToLower() -in @('y', 'yes')
}

# --- Process Helpers ---

function Get-EgsProcess {
    <#
    .SYNOPSIS
        Get the running Epic Games Store launcher process, if any.
    #>
    Get-Process -Name 'EpicGamesLauncher' -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Test-EgsRunning {
    <#
    .SYNOPSIS
        Check if the EGS launcher is currently running.
    #>
    return $null -ne (Get-EgsProcess)
}

function Wait-ForPath {
    <#
    .SYNOPSIS
        Wait for a filesystem path to appear, with a timeout.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$TimeoutSeconds = 120,
        [int]$PollIntervalMs = 500
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) { return $true }
        Start-Sleep -Milliseconds $PollIntervalMs
    }

    return $false
}
