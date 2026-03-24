# EGS-LL

<!-- Project Shields/Badges -->
<p align="center">
  <a href="https://github.com/XAOSTECH/EGS-LL">
    <img alt="GitHub repo" src="https://img.shields.io/badge/GitHub-XAOSTECH%2F-EGS-LL-181717?style=for-the-badge&logo=github">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/releases">
    <img alt="GitHub release" src="https://img.shields.io/github/v/release/XAOSTECH/EGS-LL?style=for-the-badge&logo=semantic-release&colour=blue">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/blob/main/LICENCE">
    <img alt="Licence" src="https://img.shields.io/github/licence/XAOSTECH/EGS-LL?style=for-the-badge&colour=green">
  </a>
</p>

<p align="center">
  <a href="https://github.com/XAOSTECH/EGS-LL/actions">
    <img alt="CI Status" src="https://github.com/XAOSTECH/EGS-LL/actions/workflows/bash-lint.yml/badge.svg?branch=Main>
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/issues">
    <img alt="Issues" src="https://img.shields.io/github/issues/XAOSTECH/EGS-LL?style=flat-square&logo=github&colour=yellow">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/pulls">
    <img alt="Pull Requests" src="https://img.shields.io/github/issues-pr/XAOSTECH/EGS-LL?style=flat-square&logo=github&colour=purple">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/stargazers">
    <img alt="Stars" src="https://img.shields.io/github/stars/XAOSTECH/EGS-LL?style=flat-square&logo=github&colour=gold">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/network/members">
    <img alt="Forks" src="https://img.shields.io/github/forks/XAOSTECH/EGS-LL?style=flat-square&logo=github">
  </a>
</p>

<p align="center">
  <img alt="Last Commit" src="https://img.shields.io/github/last-commit/XAOSTECH/EGS-LL?style=flat-square&logo=git&colour=blue">
  <img alt="Repo Size" src="https://img.shields.io/github/repo-size/XAOSTECH/EGS-LL?style=flat-square&logo=files&colour=teal">
  <img alt="Code Size" src="https://img.shields.io/github/languages/code-size/XAOSTECH/EGS-LL?style=flat-square&logo=files&colour=orange">
  <img alt="Contributors" src="https://img.shields.io/github/contributors/XAOSTECH/EGS-LL?style=flat-square&logo=github&colour=green">
</p>

<!-- Optional: Stability/Maturity Badge -->
<p align="center">
  <img alt="Stability" src="https://img.shields.io/badge/stability-experimental-orange?style=flat-square">
  <img alt="Maintenance" src="https://img.shields.io/maintenance/yes/2026?style=flat-square">
</p>

---

# EGS-LL — Expeerienced Game Store Launcher Launcher

A **quality-of-life wrapper** for the game launchers on Windows. (e.g. Epic Games Store)

EGS-LL does **not** reverse-engineer, modify, or patch the launchers (e.g. Epic Games Store launcher) in any way. It exclusively uses publicly available Windows APIs (registry keys, filesystem operations, process monitoring) to automate tedious manual workarounds that users already perform.

---

## The Problem

When you move, back up, or restore a game folder that was installed through the Epic Games Store, the launcher refuses to recognise the existing files. Instead it shows **"Destination folder is not empty"** and insists on a full re-download — even though a perfectly valid `.egstore` backup folder with manifests is sitting right there.

Users have worked around this for years with a tedious manual dance:

1. Rename the existing game folder (e.g. `RedDeadRedemption2` → `RedDeadRedemption22`)
2. Click **Install** in the launcher (pointing at the same parent directory)
3. Wait for the download to start (optionally let it reach ~3 % for stability)
4. **Pause** the download
5. Delete the newly created (mostly-empty) folder
6. Rename the backup folder back to the original name
7. **Resume** — the launcher detects the `.egstore` data and **verifies** instead of re-downloading

EGS-LL automates this entire process.

## Features

| Feature | Status |
|---|---|
| **Recover Install** — automate the folder-swap verification trick | ✅ Implemented |
| **List Games** — show all EGS-managed games with install state | ✅ Implemented |
| **Show Info** — display EGS paths, config, and registry data | ✅ Implemented |

## Requirements

- **Windows 10/11** with PowerShell 5.1+ (ships with Windows)
- **Epic Games Store** launcher installed
- Run from a **regular (non-admin) terminal** unless noted otherwise

## Quick Start

```powershell
# Open PowerShell and navigate to the EGS-LL directory

# List detected games
.\egs-ll.ps1 list

# Show EGS configuration and paths
.\egs-ll.ps1 info

# Recover an existing game folder (automates the verify workaround)
.\egs-ll.ps1 recover "Fortnite"

# Recover with a custom game folder path
.\egs-ll.ps1 recover "Fortnite" -GameDir "D:\Games\Fortnite"
```

## How It Works

### Registry & Manifest Reading

EGS-LL reads:

- `HKLM\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher` — launcher install paths
- `HKLM\SOFTWARE\WOW6432Node\Epic Games\UnrealEngineLauncher` — data path overrides
- `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` — game manifest files (JSON)
- Per-game `.egstore\` directories — chunk/staging manifests

### Install Recovery Flow

```
┌──────────────────────────────────────────┐
│  User runs: egs-ll.ps1 recover "Game"   │
└──────────────┬───────────────────────────┘
               │
     ┌─────────▼──────────┐
     │ Read EGS manifests │
     │ Locate game folder │
     └─────────┬──────────┘
               │
     ┌─────────▼──────────────────┐
     │ Validate .egstore exists   │
     │ (proves prior EGS install) │
     └─────────┬──────────────────┘
               │
     ┌─────────▼──────────────────────┐
     │ Rename folder: Game → Game_bak │
     └─────────┬──────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Launch EGS install via URI scheme  │
     │ com.epicgames.launcher://install/  │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Monitor for new folder creation    │
     │ Wait for download to start (~3 %)  │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼───────────────────┐
     │ Pause download (via UI/API) │
     └─────────┬───────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Delete new folder                  │
     │ Rename Game_bak → Game             │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Resume — EGS verifies existing     │
     │ files instead of re-downloading    │
     └──────────────────────────────────┘
```

## Project Structure

```
EGS-LL/
├── egs-ll.ps1          # CLI entry point
├── lib/
│   ├── registry.ps1    # EGS registry key reader
│   ├── manifest.ps1    # .item / .egstore manifest parser
│   ├── recovery.ps1    # Install recovery automation
│   └── utils.ps1       # Shared helpers (logging, validation)
├── LICENSE
└── README.md
```

## Legal

This project is **not affiliated with, endorsed by, or associated with Epic Games, Inc.**

EGS-LL operates exclusively as an external wrapper. It:

- **Does not** modify, patch, or inject into the Epic Games Store launcher binary
- **Does not** reverse-engineer any proprietary protocols or obfuscation
- **Does not** bypass any DRM, authentication, or license checks
- **Only** reads publicly accessible registry keys and user-owned files on disk
- **Only** automates filesystem operations (rename/move) that users already perform manually
- **Only** interacts with the launcher through its public URI scheme and standard Windows process APIs

Use of this tool is at your own risk and subject to the Epic Games Store EULA. Review their terms before use.

## Contributing

Contributions welcome! Please keep the wrapper-only philosophy in mind:

- No launcher binary modification
- No network interception or MITM
- No memory injection or hooking
- Registry reads only (no writes to EGS-owned keys)
- Filesystem operations on user-owned game directories only

## License

[GPL-3](../LICENSE)
