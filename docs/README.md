# EGS-LL

<!-- Project Shields/Badges -->
<p align="center">
  <a href="https://github.com/XAOSTECH/EGS-LL">
    <img alt="GitHub repo" src="https://img.shields.io/badge/GitHub-XAOSTECH%2FEGS--LL-181717?style=for-the-badge&logo=github">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/releases">
    <img alt="GitHub release" src="https://img.shields.io/github/v/release/XAOSTECH/EGS-LL?style=for-the-badge&logo=semantic-release&colour=blue">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/blob/main/LICENCE">
    <img alt="Licence" src="https://img.shields.io/github/licence/XAOSTECH/EGS-LL?style=for-the-badge&colour=green">
  </a>
</p>

<p align="center">
  <a href="https://github.com/XAOSTECH/EGS-LL/actions/workflows/build-gui.yml">
    <img alt="Build GUI" src="https://github.com/XAOSTECH/EGS-LL/actions/workflows/build-gui.yml/badge.svg">
  </a>
  <a href="https://github.com/XAOSTECH/EGS-LL/actions/workflows/bash-lint.yml">
    <img alt="Bash Lint" src="https://github.com/XAOSTECH/EGS-LL/actions/workflows/bash-lint.yml/badge.svg">
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

<p align="center">
  <b>Experienced Game Store Launcher Launcher — quality-of-life wrapper for game launchers on Windows</b>
</p>

---

## 📋 Table of Contents

- [Overview](#-overview)
- [Features](#-features)
- [Installation](#-installation)
- [Usage](#-usage)
- [How It Works](#-how-it-works)
- [Project Structure](#-project-structure)
- [CI/CD](#-cicd)
- [Legal](#-legal)
- [Contributing](#-contributing)
- [Roadmap](#-roadmap)
- [Support](#-support)
- [Licence](#-licence)

---

## 🔍 Overview

A **quality-of-life wrapper** for game launchers on Windows (e.g. Epic Games Store).

EGS-LL does **not** reverse-engineer, modify, or patch the launchers in any way. It exclusively uses publicly available Windows APIs (registry keys, filesystem operations, process monitoring) to automate tedious manual workarounds that users already perform.

### The Problem

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

---

## ✨ Features

EGS-LL ships with two interfaces: a **PowerShell CLI** for scripting and a **WinForms GUI** for point-and-click operation. Both automate the same underlying recovery workflow.

| Feature | CLI | GUI | Status |
|---|:---:|:---:|---|
| **Recover Install** — automate the folder-swap verification trick | ✅ | ✅ | Implemented |
| **List Games** — show all EGS-managed games with install state | ✅ | ✅ | Implemented |
| **Show Info** — display EGS paths, config, and registry data | ✅ | ✅ | Implemented |
| **Drive Scanning** — find unregistered EGS games across all drives | — | ✅ | Implemented |
| **Automated Pause/Resume** — suspend/resume EGS downloads via `NtSuspendProcess` | — | ✅ | Implemented |
| **Real-time Progress** — coloured log and progress bar during recovery | — | ✅ | Implemented |
| **Backup & Restore** — automatic backup with rollback on failure/cancel | ✅ | ✅ | Implemented |

---

## 📥 Installation

### Prerequisites

- **Windows 10/11**
- **Epic Games Store** launcher installed

**CLI only:**
- PowerShell 5.1+ (ships with Windows)

**GUI only:**
- .NET Framework 4.8 (pre-installed on Windows 10 1903+ and Windows 11)

### Option A: GUI (recommended)

Download `EGS-LL.exe` from the [latest release](https://github.com/XAOSTECH/EGS-LL/releases/latest) and run it. No installation required — it's a single portable executable.

> The GUI requests administrator elevation on launch. This is needed for registry access and process suspension.

### Option B: CLI

```powershell
# Clone the repository
git clone https://github.com/XAOSTECH/EGS-LL.git
cd EGS-LL
```

PowerShell may block unsigned scripts by default. To allow EGS-LL to run:

```powershell
# Allow local scripts to run (one-time setup)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Or allow for the current session only
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
```

---

## 🚀 Usage

### GUI

Launch `EGS-LL.exe`. The main window shows all detected EGS games with their install state.

| Action | How |
|---|---|
| **Refresh** | Click *Refresh* to reload games from EGS manifests |
| **Scan Drives** | Click *Scan Drives* to find unregistered game folders (`.egstore`) across all fixed drives |
| **Recover** | Select a game → click *Recover Selected* (or right-click → *Recover*) |
| **EGS Info** | Click *EGS Info* to view launcher paths and registry configuration |

The **Source** column shows where each game was detected: `EGS` (from manifests) or `Scan` (discovered on disk).

During recovery, a modal dialog shows real-time progress with a coloured log. The GUI handles the entire folder-swap flow automatically — including pausing/resuming the EGS download process.

### CLI

```powershell
# List detected games
.\egs-ll.ps1 list

# Show EGS configuration and paths
.\egs-ll.ps1 info

# Recover an existing game folder
.\egs-ll.ps1 recover "Fortnite"

# Recover with a custom game folder path
.\egs-ll.ps1 recover "Fortnite" -GameDir "D:\Games\Fortnite"

# Emergency restore a backup folder
.\egs-ll.ps1 restore "D:\Games\MyGame"

# Skip confirmation prompts
.\egs-ll.ps1 recover "Fortnite" -Yes
```

### Examples

<details>
<summary>📘 Example 1: Recover a game via CLI</summary>

```powershell
.\egs-ll.ps1 recover "Red Dead Redemption 2"
```

The tool will:
1. Locate the game via EGS manifests
2. Validate the `.egstore` directory exists
3. Rename the folder out of the way
4. Guide you through starting/pausing the install in EGS
5. Swap the folders back
6. EGS verifies existing files instead of re-downloading

</details>

<details>
<summary>📗 Example 2: Recover with a custom path</summary>

```powershell
.\egs-ll.ps1 recover "Fortnite" -GameDir "E:\Games\Fortnite"
```

Use `-GameDir` when the game folder exists but has no EGS manifest (e.g. copied from another machine).

</details>

<details>
<summary>📙 Example 3: Discover and recover an unregistered game via GUI</summary>

1. Launch `EGS-LL.exe`
2. Click **Scan Drives** — the scanner checks common game directories then performs a shallow scan of all fixed drives
3. A game found on disk but missing from EGS manifests shows as **Unregistered** with source **Scan**
4. Select it → click **Recover Selected**
5. The recovery dialog automates the entire flow including EGS process suspension

</details>

---

## 🔧 How It Works

### Registry & Manifest Reading

EGS-LL reads:

- `HKLM\SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher` — launcher install paths
- `HKLM\SOFTWARE\WOW6432Node\Epic Games\UnrealEngineLauncher` — data path overrides
- `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` — game manifest files (JSON)
- Per-game `.egstore\` directories — chunk/staging manifests

### Install Recovery Flow

The GUI automates the full flow end-to-end. The CLI guides the user through pause/resume steps manually.

```
┌──────────────────────────────────────────┐
│  User selects game to recover            │
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
     │ (FileSystemWatcher + polling)      │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Suspend EGS download process       │
     │ (NtSuspendProcess via P/Invoke)    │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Delete new folder                  │
     │ Rename Game_bak → Game             │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ Resume EGS process                 │
     │ (NtResumeProcess via P/Invoke)     │
     └─────────┬──────────────────────────┘
               │
     ┌─────────▼──────────────────────────┐
     │ EGS verifies existing files        │
     │ instead of re-downloading          │
     └──────────────────────────────────┘
```

### Drive Scanner (GUI)

The GUI includes a drive scanner that finds EGS games not registered in the launcher manifests:

1. **Phase 1 (fast):** Checks common game directories (`Epic Games`, `Games`, `Program Files`, etc.) on all fixed drives
2. **Phase 2 (thorough):** Shallow recursive scan (depth 2) of drive roots, skipping system directories
3. Results are merged with manifest data — games found only on disk show as **Unregistered** with source **Scan**

---

## 📁 Project Structure

```
EGS-LL/
├── egs-ll.ps1              # CLI entry point
├── lib/
│   ├── registry.ps1        # EGS registry key reader
│   ├── manifest.ps1        # .item / .egstore manifest parser
│   ├── recovery.ps1        # Install recovery automation
│   └── utils.ps1           # Shared helpers (logging, validation)
├── gui/
│   ├── EGS-LL.csproj       # .NET Framework 4.8 WinForms project
│   ├── Program.cs           # Entry point (admin elevation check)
│   ├── app.manifest         # UAC admin elevation manifest
│   ├── Core/
│   │   ├── RegistryHelper.cs   # Read-only EGS registry detection
│   │   ├── ManifestReader.cs   # Parse EGS .item manifest files
│   │   ├── ProcessHelper.cs    # Launch/detect/suspend/resume EGS
│   │   ├── RecoveryEngine.cs   # Full recovery workflow orchestration
│   │   └── DriveScanner.cs     # Scan all drives for unregistered games
│   └── Forms/
│       ├── MainForm.cs         # Main game library grid (dark theme)
│       └── RecoveryForm.cs     # Modal recovery progress dialog
├── .github/workflows/
│   ├── build-gui.yml        # Reusable build workflow (callable)
│   └── release.yml          # Release automation (dev-control template)
├── docs/
│   ├── README.md
│   ├── CONTRIBUTING.md
│   ├── CODE_OF_CONDUCT.md
│   └── SECURITY.md
└── LICENSE
```

---

## 🔄 CI/CD

The project uses GitHub Actions for automated builds and releases.

| Workflow | Purpose |
|---|---|
| **build-gui.yml** | Builds the GUI on `windows-latest`. Reusable via `workflow_call` — the release workflow auto-discovers and triggers it. Also supports `workflow_dispatch` for standalone testing. |
| **release.yml** | Full release automation (changelog, GPG-signed tag, GitHub release with artifacts and checksums). Powered by the [dev-control](https://github.com/XAOSTECH/dev-control) template. |

### Building from Source

The GUI requires the .NET SDK (8.x) to build. The CI handles this automatically — you don't need the SDK installed locally.

```bash
# If you do have the SDK:
dotnet build gui/EGS-LL.csproj -c Release
```

The release workflow passes the version tag to the build, embedding it in the executable. Manual/test builds default to `"experimental"`.

---

## ⚖️ Legal

This project is **not affiliated with, endorsed by, or associated with Epic Games, Inc.**

EGS-LL operates exclusively as an external wrapper. It:

- **Does not** modify, patch, or inject into the Epic Games Store launcher binary
- **Does not** reverse-engineer any proprietary protocols or obfuscation
- **Does not** bypass any DRM, authentication, or licence checks
- **Only** reads publicly accessible registry keys and user-owned files on disk
- **Only** automates filesystem operations (rename/move) that users already perform manually
- **Only** interacts with the launcher through its public URI scheme and standard Windows process APIs

Use of this tool is at your own risk and subject to the Epic Games Store EULA. Review their terms before use.

---

## 🤝 Contributing

Contributions welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) before submitting PRs.

Please keep the wrapper-only philosophy in mind:

- No launcher binary modification
- No network interception or MITM
- No memory injection or hooking
- Registry reads only (no writes to EGS-owned keys)
- Filesystem operations on user-owned game directories only

See also: [Code of Conduct](CODE_OF_CONDUCT.md) | [Security Policy](SECURITY.md)

---

## 🗺️ Roadmap

- [x] Install recovery automation (folder-swap verify trick)
- [x] Game listing with install state
- [x] EGS installation info display
- [x] GUI wrapper with dark theme
- [x] Automated pause/resume via process suspension (`NtSuspendProcess`/`NtResumeProcess`)
- [x] Drive scanning for unregistered games
- [x] CI build pipeline (GitHub Actions, Windows runner)
- [x] Dynamic versioning (tag → version, manual → experimental)
- [x] SHA256 checksums in releases
- [ ] Batch recovery for multiple games
- [ ] Support for additional launchers

See the [open issues](https://github.com/XAOSTECH/EGS-LL/issues) for a full list of proposed features and known issues.

---

## 💬 Support

- 💻 **Issues**: [GitHub Issues](https://github.com/XAOSTECH/EGS-LL/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/XAOSTECH/EGS-LL/discussions)

---

## 📄 Licence

Distributed under the GPL-3.0 Licence. See [`LICENCE`](../LICENCE) for more information.

---

<p align="center">
  <a href="https://github.com/XAOSTECH">
    <img src="https://img.shields.io/badge/Made%20with%20%E2%9D%A4%EF%B8%8F%20by-XAOSTECH-red?style=for-the-badge">
  </a>
</p>
