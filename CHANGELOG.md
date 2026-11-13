# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]

## [0.3.0] - 2026-11-13
### Added
- **Result Grid Tools** (Phase 3.1, off by default; turn on in Settings): right-click the SSMS results grid >
  *Copy As (SsmsToolkit)* to copy the selection as CSV, JSON, Markdown or a distinct `IN (...)` list.
  Selecting several cells shows count, distinct, sum, average, min and max in the status bar.
- **INSERT statements dialog** (*Copy As > INSERT Statements...*): editable target table (suggested from the
  result schema and the query's FROM tables), selected cells or the whole result, rows per INSERT (1-1000),
  exclude identity/computed columns or wrap with `SET IDENTITY_INSERT`, optional `[ ]` quoting, live preview,
  progress bar with cancel, and output to the clipboard, a new connected query window or a `.sql` file.

### Changed
- The editor context menu id used for "Inspect Table" (`0x50`) is now confirmed from SSMS 22.12's SQLEditors.dll.

## [0.2.0] - 2026-11-13
### Added
- **Phase 2:** Triggers and Dependencies tabs (definitions in the script panel; tables/views open in a new
  inspector); link-style underline and hand cursor on the identifier under the mouse while Ctrl is held;
  Object Explorer "Inspect Table" context menu item for tables and views, and Ctrl+K, Ctrl+T while
  Object Explorer has focus; themed scroll bars.

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
