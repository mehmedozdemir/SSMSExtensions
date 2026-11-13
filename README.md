# SsmsToolkit

A toggleable, multi-feature extension for **SQL Server Management Studio 22**.

> **Status:** Early development (Phase 0 — feasibility spike). Not ready for use.

## Planned features

| Feature | Description | Status |
|---|---|---|
| **Table Inspector** | Ctrl+Click a table name in the query editor to open a details tab: summary, columns, indexes, foreign keys, data preview (read-only, top N rows), DDL. | Planned |
| Result Grid tools | Copy selection as INSERT / JSON / CSV / Markdown / `IN (...)`, selection aggregates. | Idea |

Every feature can be enabled or disabled from the SsmsToolkit settings window.

## Requirements
- Windows x64
- SQL Server Management Studio 22

## Disclaimer
Microsoft does not officially support third-party extensions in SSMS 21/22. SsmsToolkit uses undocumented SSMS internals that may change with any SSMS update. Use at your own risk; disable or uninstall the extension if SSMS misbehaves.

## Building
See [CLAUDE.md](CLAUDE.md) for build, install and debug instructions.

## License
[MIT](LICENSE)
