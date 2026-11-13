# CLAUDE.md — SsmsToolkit

Guidance for Claude Code working in this repository. Read `docs/ANALIZ.md` (Turkish) for the full analysis and approved decisions.

## What this is
A multi-feature, toggleable extension for **SQL Server Management Studio 22** (x64), delivered as a `.vsix`.
First feature: **Table Inspector** — Ctrl+Left-Click on a table name in the query editor opens a DBeaver-like details tab (Summary, Columns, Indexes, Foreign Keys, Data, DDL).

Microsoft does not officially support SSMS extensions. Everything that touches undocumented SSMS internals must be isolated and must fail safely.

## Hard rules
1. **No development beyond the current approved phase.** Phases are in `docs/ANALIZ.md` §7. Phase 0 (spike) must be validated by the user inside SSMS before Phase 1 starts. Ask before expanding scope.
2. **All SSMS-specific / undocumented API usage lives in `src/SsmsToolkit/SsmsInterop/`.** Features call interfaces from there; nothing else references `SqlWorkbench.Interfaces`, `SqlPackageBase`, `ServiceCache`, `GridControl` etc. directly.
3. **Never crash SSMS.** Every feature entry point (commands, MEF processors, event handlers) catches exceptions, logs them, and on repeated failure disables the feature via `FeatureRegistry`.
4. **Never block the UI thread.** All DB work is async with `CancellationToken` and `CommandTimeout`. Use `JoinableTaskFactory` correctly (`SwitchToMainThreadAsync` only when touching UI/VS services).
5. **Read-only.** The extension never issues DML/DDL. Data queries are parameterized `SELECT TOP (@n)` with a configurable limit (default **200**). Object names are always quoted with `QUOTENAME`/bracket-escaping, never string-concatenated raw.
6. **Do not ship SSMS/VS assemblies in the VSIX** and **do not commit them** (`lib/ssms22/` is git-ignored; populate with `tools/copy-ssms-refs.ps1`). Reference them with `Private=false`.
7. **Never log or persist passwords / connection strings with credentials.**
8. Code, comments, commit messages, and identifiers in **English**. User-facing strings go through a resource file (Turkish + English).
9. Prefer what SSMS already loads (Microsoft.Data.SqlClient, SMO, ScriptDom, Newtonsoft.Json) over adding new NuGet dependencies, to avoid assembly-version conflicts. Verify the DLL exists in the SSMS install before relying on it.

## Tech
- .NET Framework 4.8, C# latest, VSSDK in-proc `AsyncPackage`, WPF.
- Manifest: `InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,23.0)"`, `ProductArchitecture` amd64.
- Verify in Phase 0 which VS shell version SSMS 22.12 is built on and pick the matching `Microsoft.VisualStudio.SDK` package version (`ExcludeAssets="runtime"`).
- SSMS install root: `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE`.

## Layout (target)
```
src/SsmsToolkit/            VSIX project (Package/, Core/, SsmsInterop/, Features/, UI/)
src/SsmsToolkit.Data/       SSMS-independent metadata & data queries (unit-testable)
src/SsmsToolkit.Playground/ WPF app to iterate on UI without SSMS
tests/SsmsToolkit.Tests/
lib/ssms22/                 reference DLLs copied from SSMS (git-ignored)
tools/                      scripts (copy refs, install/uninstall VSIX)
docs/                       analysis, manual test checklists
```

## Build / install / debug
- Build: `build.cmd` (uses `vswhere` → MSBuild). `build.cmd Debug` for debug.
- Install for testing (SSMS closed): `"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /quiet <path>\SsmsToolkit.vsix`
- Uninstall: `VSIXInstaller.exe /uninstall:<VsixId>`
- Debug: start external program `SSMS.exe`, attach VS debugger.
- Logs: `%LocalAppData%\SsmsToolkit\logs`. Settings: `%LocalAppData%\SsmsToolkit\settings.json`.
- You (Claude Code) cannot click inside SSMS. At the end of each task, write/refresh a short manual test checklist in `docs/testing/` for the user.

## Git & versioning
- Public GitHub repo. SemVer; `0.x` until Phase 1 is done.
- Branch per task: `feature/<name>`, `fix/<name>`; merge to `main` via PR. `main` must always build.
- Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`, `test:`).
- Release: update `CHANGELOG.md`, tag `vX.Y.Z`, create GitHub Release with the `.vsix` attached.
