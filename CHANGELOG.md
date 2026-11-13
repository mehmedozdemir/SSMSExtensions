# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-13
### Added
- **Table Inspector** (Phase 1): Ctrl+Click a table/view name (or alias) in the query editor, the editor
  context menu "Inspect Table", or Ctrl+K, Ctrl+T opens a details tab with Summary, Columns,
  Keys (PK/UNIQUE), Indexes, Constraints (CHECK/DEFAULT), Foreign Keys (click to navigate), Data (read-only TOP N, server-side sort, validated
  WHERE filter, open as query) and DDL (SMO script, copy, open as query). Optional floating window.
  Selecting a column, key, index, constraint or foreign key shows its CREATE/ALTER script in a resizable
  panel below the list (copy / open in new query).
- Feature registry with per-feature on/off switch; a feature that fails repeatedly turns itself off for the
  session and shows an info bar.
- Settings window (Extensions > SsmsToolkit > Settings): trigger, open mode, row limit, timeout,
  READ UNCOMMITTED, production server patterns (red warning strip), language (TR/EN), log level.
- About command, log retention (14 days).
- SSMS-independent `SsmsToolkit.Data` (ScriptDom name/alias resolver, catalog queries) and
  `SsmsToolkit.Presentation` (WPF views) libraries; Playground app; unit and LocalDB integration tests.

### Removed
- Phase 0 spike commands and window.

### Earlier (Phase 0)
- Phase 0 spike: VSIX package for SSMS 22 with menu commands, document-well tool window,
  Ctrl+Click mouse processor that logs the editor content type, and active-connection probe
  (`ISqlEditorService` with `ScriptFactory` fallback) that opens its own read-only connection.
- `build.cmd`, `tools/copy-ssms-refs.ps1`, `tools/install-vsix.ps1`.
- Phase 0 findings (`docs/faz0-bulgular.md`) and manual test checklist (`docs/testing/phase0-spike.md`).
- Project analysis (`docs/ANALIZ.md`), contributor guide (`CLAUDE.md`), README, license.
