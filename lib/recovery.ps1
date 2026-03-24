#Requires -Version 5.1
<#
.SYNOPSIS
    Automate EGS game install recovery via the folder-swap verification trick.
.DESCRIPTION
    This is the core QoL feature of EGS-LL. It automates the well-known
    manual workaround for making EGS verify existing game files instead of
    re-downloading them.

    The workflow:
    1. Validate the existing game folder has .egstore data
    2. Rename the folder out of the way (Game → Game_egsll_bak)
    3. Prompt the user to start the install in EGS
    4. Wait for EGS to create the new (empty) game folder
    5. Prompt the user to pause the download
    6. Delete the new folder and rename the backup back
    7. Prompt the user to resume — EGS verifies instead of downloading

    All operations are filesystem-only (rename/delete of user-owned dirs).
    No launcher binaries are modified.
#>

Set-StrictMode -Version Latest

$script:BACKUP_SUFFIX = '_egsll_bak'

function Start-RecoveryFlow {
    <#
    .SYNOPSIS
        Run the interactive install recovery workflow for a game.
    .PARAMETER GameName
        Display name (or partial match) of the game to recover.
    .PARAMETER GameDir
        Optional: explicit path to the game folder to recover.
        If not provided, looked up from the EGS manifest.
    .PARAMETER SkipConfirm
        Skip confirmation prompts (for scripted use).
    #>
    param(
        [Parameter(Mandatory)][string]$GameName,
        [string]$GameDir,
        [switch]$SkipConfirm
    )

    Write-Host ""
    Write-EgsLog "=== EGS-LL Install Recovery ===" -Level Info
    Write-Host ""

    # --- Step 1: Locate the game ---
    $manifest = $null
    $gamePath = $null
    $gameDisplayName = $GameName

    if ($GameDir) {
        # User provided an explicit path
        $gamePath = $GameDir
        Write-EgsLog "Using provided game directory: $gamePath" -Level Info
    }
    else {
        # Look up from manifest
        $manifest = Find-EgsManifest -GameName $GameName
        if ($manifest) {
            $gamePath = $manifest.InstallLocation
            $gameDisplayName = $manifest.DisplayName
            Write-EgsLog "Found in EGS manifests: $gameDisplayName" -Level Success
            Write-EgsLog "Install location: $gamePath" -Level Info
        }
        else {
            Write-EgsLog "Game not found in EGS manifests." -Level Error
            Write-EgsLog "If the game folder exists but has no manifest, use -GameDir to specify the path." -Level Info
            return $false
        }
    }

    # --- Step 2: Validate the game folder ---
    if (-not (Test-Path -LiteralPath $gamePath -PathType Container)) {
        Write-EgsLog "Game folder does not exist: $gamePath" -Level Error
        return $false
    }

    $egstoreInfo = Get-EgstoreInfo -GamePath $gamePath
    if (-not $egstoreInfo) {
        Write-EgsLog "No .egstore directory found in: $gamePath" -Level Warn
        Write-EgsLog "This folder may not be a valid EGS installation." -Level Warn

        if (-not $SkipConfirm) {
            $proceed = Confirm-Action "Continue anyway? The recovery may not work without .egstore data."
            if (-not $proceed) {
                Write-EgsLog "Aborted by user." -Level Info
                return $false
            }
        }
    }
    else {
        Write-EgsLog ".egstore data found -- this folder is a valid EGS installation." -Level Success
        if ($egstoreInfo.HasBkp) {
            Write-EgsLog "Chunk manifests present (good -- EGS can verify from these)." -Level Success
        }
    }

    $folderSize = Get-FolderSizeMB -FolderPath $gamePath
    Write-EgsLog "Folder size: ${folderSize} MB" -Level Info
    Write-Host ""

    # --- Step 3: Confirm the plan ---
    $parentDir = Split-Path $gamePath -Parent
    $folderName = Split-Path $gamePath -Leaf
    $backupName = "${folderName}${script:BACKUP_SUFFIX}"
    $backupPath = Join-Path $parentDir $backupName

    if (Test-Path -LiteralPath $backupPath) {
        Write-EgsLog "Backup path already exists: $backupPath" -Level Error
        Write-EgsLog "Please remove or rename it first." -Level Info
        return $false
    }

    Write-EgsLog "Recovery Plan:" -Level Info
    Write-Host "  1. Rename  : $folderName -> $backupName"
    Write-Host "  2. You start the install in EGS (pointing to: $parentDir)"
    Write-Host "  3. Wait for EGS to create the new folder and begin downloading"
    Write-Host "  4. You pause the download in EGS"
    Write-Host "  5. Delete the new (empty) folder and restore the backup"
    Write-Host "  6. You resume in EGS -- it verifies existing files"
    Write-Host ""

    if (-not $SkipConfirm) {
        $proceed = Confirm-Action "Ready to begin?"
        if (-not $proceed) {
            Write-EgsLog "Aborted by user." -Level Info
            return $false
        }
    }

    # --- Step 4: Rename the folder ---
    Write-Host ""
    Write-EgsLog "Renaming: $folderName -> $backupName" -Level Info

    try {
        Rename-Item -LiteralPath $gamePath -NewName $backupName -ErrorAction Stop
        Write-EgsLog "Folder renamed successfully." -Level Success
    }
    catch {
        Write-EgsLog "Failed to rename folder: $_" -Level Error
        Write-EgsLog "Make sure no programs have files open in this directory." -Level Info
        return $false
    }

    # --- Step 5: Wait for user to start install in EGS ---
    Write-Host ""
    Write-EgsLog "ACTION REQUIRED:" -Level Warn
    Write-Host ""
    Write-Host "  1. Open Epic Games Store launcher"
    Write-Host "  2. Go to the game's page and click INSTALL"
    Write-Host "  3. Set the install location to: $parentDir"
    Write-Host "  4. Let the install begin"
    Write-Host ""

    # Offer to open EGS via URI scheme
    if ($manifest -and $manifest.CatalogNamespace -and $manifest.CatalogItemId -and $manifest.AppName) {
        $uri = "com.epicgames.launcher://store/p/$($manifest.CatalogNamespace)"
        Write-EgsLog "Tip: You can also navigate to the game in the store manually." -Level Debug
    }

    Write-Host ""
    Read-Host "Press ENTER after you have clicked Install and the download has started"

    # --- Step 6: Wait for the new folder to appear ---
    Write-EgsLog "Waiting for EGS to create: $gamePath" -Level Info

    $folderAppeared = Wait-ForPath -Path $gamePath -TimeoutSeconds 300 -PollIntervalMs 1000

    if (-not $folderAppeared) {
        Write-EgsLog "Timed out waiting for EGS to create the game folder." -Level Error
        Write-EgsLog "Restoring backup..." -Level Warn
        Restore-Backup -BackupPath $backupPath -OriginalPath $gamePath
        return $false
    }

    Write-EgsLog "New folder detected: $gamePath" -Level Success

    # Brief pause to let EGS write initial files
    Write-EgsLog "Waiting a few seconds for stability..." -Level Info
    Start-Sleep -Seconds 5

    # --- Step 7: User pauses the download ---
    Write-Host ""
    Write-EgsLog "ACTION REQUIRED:" -Level Warn
    Write-Host ""
    Write-Host "  >>> PAUSE the download in Epic Games Store NOW <<<"
    Write-Host ""
    Read-Host "Press ENTER after pausing the download"

    # --- Step 8: Swap folders ---
    Write-Host ""
    Write-EgsLog "Performing folder swap..." -Level Info

    # Delete the new (mostly empty) folder
    try {
        # Small safety check: don't delete if the new folder is suspiciously large
        $newFolderSize = Get-FolderSizeMB -FolderPath $gamePath
        if ($newFolderSize -gt 500) {
            Write-EgsLog "New folder is ${newFolderSize} MB -- larger than expected." -Level Warn

            if (-not $SkipConfirm) {
                $proceed = Confirm-Action "The new folder is larger than expected. Delete it and restore backup?"
                if (-not $proceed) {
                    Write-EgsLog "Aborted. Both folders are preserved -- clean up manually." -Level Warn
                    Write-EgsLog "  New folder:    $gamePath" -Level Info
                    Write-EgsLog "  Backup folder: $backupPath" -Level Info
                    return $false
                }
            }
        }

        Remove-Item -LiteralPath $gamePath -Recurse -Force -ErrorAction Stop
        Write-EgsLog "Removed new (empty) folder." -Level Success
    }
    catch {
        Write-EgsLog "Failed to remove new folder: $_" -Level Error
        Write-EgsLog "Try closing any file explorer windows and retry." -Level Info
        Write-EgsLog "  New folder:    $gamePath" -Level Info
        Write-EgsLog "  Backup folder: $backupPath" -Level Info
        return $false
    }

    # Rename backup back to original
    try {
        Rename-Item -LiteralPath $backupPath -NewName $folderName -ErrorAction Stop
        Write-EgsLog "Restored backup: $backupName -> $folderName" -Level Success
    }
    catch {
        Write-EgsLog "Failed to restore backup: $_" -Level Error
        Write-EgsLog "IMPORTANT: Your game files are at: $backupPath" -Level Warn
        Write-EgsLog "Manually rename '$backupName' back to '$folderName'." -Level Warn
        return $false
    }

    # --- Step 9: Resume ---
    Write-Host ""
    Write-EgsLog "=== Folder swap complete! ===" -Level Success
    Write-Host ""
    Write-EgsLog "ACTION REQUIRED:" -Level Warn
    Write-Host ""
    Write-Host "  >>> RESUME the download in Epic Games Store <<<"
    Write-Host ""
    Write-Host "  EGS should detect the existing files and start VERIFYING"
    Write-Host "  instead of re-downloading. This is much faster!"
    Write-Host ""
    Write-EgsLog "Recovery complete for: $gameDisplayName" -Level Success

    return $true
}

function Restore-Backup {
    <#
    .SYNOPSIS
        Emergency restore: rename the backup folder back to the original name.
    #>
    param(
        [Parameter(Mandatory)][string]$BackupPath,
        [Parameter(Mandatory)][string]$OriginalPath
    )

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        Write-EgsLog "Backup not found at: $BackupPath" -Level Error
        return $false
    }

    # If the original path exists (partially created by EGS), remove it
    if (Test-Path -LiteralPath $OriginalPath) {
        try {
            Remove-Item -LiteralPath $OriginalPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-EgsLog "Cannot remove: $OriginalPath -- $_" -Level Error
            return $false
        }
    }

    $originalName = Split-Path $OriginalPath -Leaf
    try {
        Rename-Item -LiteralPath $BackupPath -NewName $originalName -ErrorAction Stop
        Write-EgsLog "Backup restored to: $OriginalPath" -Level Success
        return $true
    }
    catch {
        Write-EgsLog "Failed to restore backup: $_" -Level Error
        Write-EgsLog "Your files are at: $BackupPath" -Level Warn
        return $false
    }
}

function Start-QuickRecover {
    <#
    .SYNOPSIS
        Non-interactive recovery: just do the folder swap assuming EGS is
        already paused and has created the new folder.
    .DESCRIPTION
        For advanced users or scripted workflows. Assumes:
        - The backup folder (Game_egsll_bak) exists
        - The new EGS folder (Game) exists and is small/empty
        - EGS download is paused
    #>
    param(
        [Parameter(Mandatory)][string]$GamePath
    )

    $parentDir = Split-Path $GamePath -Parent
    $folderName = Split-Path $GamePath -Leaf
    $backupPath = Join-Path $parentDir "${folderName}${script:BACKUP_SUFFIX}"

    if (-not (Test-Path -LiteralPath $backupPath)) {
        Write-EgsLog "No backup found at: $backupPath" -Level Error
        return $false
    }

    if (-not (Test-Path -LiteralPath $GamePath)) {
        Write-EgsLog "New folder not found at: $GamePath -- nothing to swap." -Level Error
        Write-EgsLog "Hint: Just rename '$($folderName)$($script:BACKUP_SUFFIX)' back to '$folderName'." -Level Info
        return $false
    }

    Write-EgsLog "Quick swap: removing new folder and restoring backup..." -Level Info
    return Restore-Backup -BackupPath $backupPath -OriginalPath $GamePath
}
