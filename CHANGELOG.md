
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

