#Requires -Version 5.1
<#
.SYNOPSIS
    Parse Epic Games Store manifest files (.item) and .egstore data.
.DESCRIPTION
    Reads the JSON .item files that EGS writes to its Manifests directory.
    Each file describes one installed (or installing) game.
    Read-only — never modifies manifest files.
#>

Set-StrictMode -Version Latest

function Read-EgsManifest {
    <#
    .SYNOPSIS
        Parse a single .item manifest file and return a structured object.
    #>
    param([Parameter(Mandatory)][string]$FilePath)

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        Write-EgsLog "Manifest not found: $FilePath" -Level Warn
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $FilePath -Raw -Encoding UTF8
        $json = $raw | ConvertFrom-Json

        return [PSCustomObject]@{
            ManifestFile     = $FilePath
            DisplayName      = $json.DisplayName
            InstallLocation  = $json.InstallLocation
            AppName          = $json.AppName
            CatalogNamespace = $json.CatalogNamespace
            CatalogItemId    = $json.CatalogItemId
            AppVersionString = $json.AppVersionString
            LaunchCommand    = $json.LaunchCommand
            LaunchExecutable = $json.LaunchExecutable
            InstallSize      = $json.InstallSize
            bIsIncompleteInstall = $json.bIsIncompleteInstall
            bNeedsValidation = if ($null -ne $json.bNeedsValidation) { $json.bNeedsValidation } else { $false }
            StagingLocation  = $json.StagingLocation
            MandatoryAppFolderName = $json.MandatoryAppFolderName
            InstallationGuid = $json.InstallationGuid
        }
    }
    catch {
        Write-EgsLog "Failed to parse manifest: $FilePath — $_" -Level Warn
        return $null
    }
}

function Get-AllEgsManifests {
    <#
    .SYNOPSIS
        Read all .item manifests from the EGS manifests directory.
    .PARAMETER ManifestsPath
        Path to the Manifests directory. If not specified, auto-detected from registry.
    #>
    param([string]$ManifestsPath)

    if (-not $ManifestsPath) {
        $info = Get-EgsInstallInfo
        if (-not $info.Found -or -not $info.ManifestsPath) {
            Write-EgsLog "Cannot locate EGS manifests directory." -Level Error
            return @()
        }
        $ManifestsPath = $info.ManifestsPath
    }

    if (-not (Test-Path -LiteralPath $ManifestsPath -PathType Container)) {
        Write-EgsLog "Manifests directory does not exist: $ManifestsPath" -Level Error
        return @()
    }

    $items = Get-ChildItem -LiteralPath $ManifestsPath -Filter '*.item' -File -ErrorAction SilentlyContinue
    if (-not $items -or $items.Count -eq 0) {
        Write-EgsLog "No manifest files found in: $ManifestsPath" -Level Warn
        return @()
    }

    $manifests = @()
    foreach ($item in $items) {
        $manifest = Read-EgsManifest -FilePath $item.FullName
        if ($manifest) {
            $manifests += $manifest
        }
    }

    return $manifests
}

function Find-EgsManifest {
    <#
    .SYNOPSIS
        Search for a game manifest by display name (case-insensitive, partial match).
    #>
    param(
        [Parameter(Mandatory)][string]$GameName,
        [string]$ManifestsPath
    )

    $all = Get-AllEgsManifests -ManifestsPath $ManifestsPath
    if ($all.Count -eq 0) { return $null }

    # Exact match first
    $exact = $all | Where-Object { $_.DisplayName -eq $GameName }
    if ($exact) { return $exact | Select-Object -First 1 }

    # Case-insensitive partial match
    $partial = $all | Where-Object { $_.DisplayName -like "*$GameName*" }
    if ($partial.Count -eq 1) { return $partial[0] }

    if ($partial.Count -gt 1) {
        Write-EgsLog "Multiple matches for '$GameName':" -Level Warn
        foreach ($m in $partial) {
            Write-Host "  - $($m.DisplayName) ($($m.AppName))"
        }
        Write-EgsLog "Please use a more specific name." -Level Warn
        return $null
    }

    # Try matching AppName
    $byApp = $all | Where-Object { $_.AppName -like "*$GameName*" }
    if ($byApp) { return $byApp | Select-Object -First 1 }

    Write-EgsLog "No game found matching '$GameName'." -Level Error
    return $null
}

function Show-EgsGames {
    <#
    .SYNOPSIS
        Display a formatted list of all EGS-managed games.
    #>
    param([string]$ManifestsPath)

    $manifests = Get-AllEgsManifests -ManifestsPath $ManifestsPath
    if ($manifests.Count -eq 0) {
        Write-EgsLog "No games found." -Level Warn
        return
    }

    Write-EgsLog "EGS Games ($($manifests.Count) found)" -Level Success
    Write-Host ""

    $i = 0
    foreach ($m in ($manifests | Sort-Object DisplayName)) {
        $i++
        $installed = if (Test-Path -LiteralPath $m.InstallLocation -ErrorAction SilentlyContinue) { 'Yes' } else { 'No ' }
        $egstore = if (Test-GameFolder -FolderPath $m.InstallLocation) { '.egstore' } else { '        ' }
        $incomplete = if ($m.bIsIncompleteInstall) { ' [INCOMPLETE]' } else { '' }
        $needsVal = if ($m.bNeedsValidation) { ' [NEEDS VALIDATION]' } else { '' }

        Write-Host ("  {0,3}. {1,-40} Dir:{2}  {3}{4}{5}" -f $i, $m.DisplayName, $installed, $egstore, $incomplete, $needsVal)
        Write-Host ("       Path: {0}" -f $m.InstallLocation) -ForegroundColor DarkGray
        if ($m.InstallSize) {
            Write-Host ("       Size: {0}" -f (Format-Size $m.InstallSize)) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

function Get-EgstoreInfo {
    <#
    .SYNOPSIS
        Read metadata from a game's .egstore directory.
    #>
    param([Parameter(Mandatory)][string]$GamePath)

    $egstorePath = Join-Path $GamePath '.egstore'

    if (-not (Test-Path -LiteralPath $egstorePath -PathType Container)) {
        return $null
    }

    $info = [PSCustomObject]@{
        Path       = $egstorePath
        HasBkp     = $false
        Manifests  = @()
        StagingDir = $null
    }

    # Check for backup tag files and chunk manifests
    $bkpFiles = Get-ChildItem -LiteralPath $egstorePath -Filter '*.manifest' -File -ErrorAction SilentlyContinue
    if ($bkpFiles) {
        $info.HasBkp = $true
        $info.Manifests = $bkpFiles | ForEach-Object { $_.FullName }
    }

    # Check for staging directory
    $stagingCandidates = Get-ChildItem -LiteralPath $egstorePath -Directory -ErrorAction SilentlyContinue
    if ($stagingCandidates) {
        $info.StagingDir = $stagingCandidates | Select-Object -First 1 | ForEach-Object { $_.FullName }
    }

    return $info
}
