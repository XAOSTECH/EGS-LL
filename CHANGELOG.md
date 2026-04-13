
## [0.1.0] - 2026-04-13 (re-release)

### Added
- wait for folder to reach 150 MB before cascade
- export raw logs

### Fixed
- verify pause before swapping, stop trusting API claims
- gate cascade on download start, add log timestamps
- UIA stall + download manager awareness
- permissions

### Changed
- chore: update git tree visualisation
- Merge pull request #2 from XAOSTECH:anglicise/20260401-023306
- chore: convert American spellings to British English
- chore: update git tree visualisation
- chore(README): clear lie
- chore: consolidate releases — absorb changes into v0.1.0
- chore(dc-init): load workflows,actions
- chore: update CHANGELOG for v0.1.2
- chore: update CHANGELOG for v0.1.1
- chore(dc-init): load workflows,actions
- chore: update CHANGELOG for v0.1.0 (re-release)
- chore(dc-init): load workflows,actions

### Documentation
- add trust/verification section, update for UIA cascade

## [0.1.0] - 2026-03-25

### Added
- add drive scanner, dynamic versioning, SHA256 in releases
- add WinForms GUI for automated game recovery

### Fixed
- strip v prefix and +sha suffix from displayed version
- recursive search across all drives for Epic Games folders
- replace PS7-only ?? operators with PS5.1-compatible if/else
- escape angle brackets in PowerShell strings

### Changed
- chore(dc-init): update workflows
- refactor(ci): make build-gui.yml a callable reusable workflow
- ci: add build + release workflow for GUI
- chore(README): reformat to match template, add execution policy docs

### Documentation
- update README to reflect CLI + GUI dual-interface state

