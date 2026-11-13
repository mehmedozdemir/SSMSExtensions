# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]
### Added
- Phase 0 spike: VSIX package for SSMS 22 with menu commands, document-well tool window,
  Ctrl+Click mouse processor that logs the editor content type, and active-connection probe
  (`ISqlEditorService` with `ScriptFactory` fallback) that opens its own read-only connection.
- `build.cmd`, `tools/copy-ssms-refs.ps1`, `tools/install-vsix.ps1`.
- Phase 0 findings (`docs/faz0-bulgular.md`) and manual test checklist (`docs/testing/phase0-spike.md`).
- Project analysis (`docs/ANALIZ.md`), contributor guide (`CLAUDE.md`), README, license.
