<#
.SYNOPSIS
    Installs (or reinstalls) the built SsmsToolkit.vsix into SSMS 22 for the current user.

.PARAMETER Configuration
    Build configuration whose VSIX is installed. Default: Release.

.PARAMETER Uninstall
    Uninstall instead of install.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Uninstall,
    [string]$SsmsIdeDir = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $SsmsIdeDir 'VSIXInstaller.exe'
$manifest = Join-Path $repoRoot 'src\SsmsToolkit\source.extension.vsixmanifest'
$vsixId = ([xml](Get-Content $manifest)).PackageManifest.Metadata.Identity.Id

if (-not (Test-Path $installer)) { throw "VSIXInstaller.exe not found: $installer" }
if (Get-Process -Name 'Ssms' -ErrorAction SilentlyContinue) {
    throw 'SSMS is running. Close it first.'
}

if ($Uninstall) {
    Write-Host "Uninstalling $vsixId"
    $p = Start-Process $installer -ArgumentList "/quiet", "/uninstall:$vsixId" -Wait -PassThru
} else {
    $vsix = Join-Path $repoRoot "src\SsmsToolkit\bin\$Configuration\SsmsToolkit.vsix"
    if (-not (Test-Path $vsix)) { throw "VSIX not found: $vsix (run build.cmd $Configuration)" }
    # Remove an older copy first so a same-version rebuild is really replaced.
    Start-Process $installer -ArgumentList "/quiet", "/uninstall:$vsixId" -Wait | Out-Null
    Write-Host "Installing $vsix"
    $p = Start-Process $installer -ArgumentList "/quiet", "`"$vsix`"" -Wait -PassThru
}

Write-Host "VSIXInstaller exit code: $($p.ExitCode)"
Write-Host "Installer logs: $env:TEMP\dd_VSIXInstaller_*.log"
exit $p.ExitCode
