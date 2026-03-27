
## [0.1.1] - 2026-03-27

### Added
- wait for folder to reach 150 MB before cascade
- export raw logs

### Fixed
- gate cascade on download start, add log timestamps
- UIA stall + download manager awareness

## [0.1.0] - 2026-03-26 (re-release)

### Added
- UI Automation for pause/resume via accessibility tree
- new URI format, diagnostic suspend, richer errors
- add drive scanner, dynamic versioning, SHA256 in releases
- add WinForms GUI for automated game recovery

### Fixed
- permissions
- user-action fallback when suspend fails
- cursor not resetting after drive scan completes
- strip v prefix and +sha suffix from displayed version
- recursive search across all drives for Epic Games folders
- replace PS7-only ?? operators with PS5.1-compatible if/else

### Changed
- chore(dc-init): load workflows,actions
- refactor(scanner): BFS search, one-per-drive stop, array targets
- chore: update CHANGELOG for v0.1.0
- chore(dc-init): update workflows
- refactor(ci): make build-gui.yml a callable reusable workflow
- ci: add build + release workflow for GUI
- chore(README): reformat to match template, add execution policy docs

### Documentation
- add trust/verification section, update for UIA cascade
- update README to reflect CLI + GUI dual-interface state

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

