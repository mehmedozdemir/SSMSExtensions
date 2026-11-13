<#
.SYNOPSIS
    Copies the SSMS 22 assemblies SsmsToolkit compiles against into lib/ssms22/.

.DESCRIPTION
    These are Microsoft assemblies: they are referenced with Private=false, are never
    shipped inside the VSIX and must never be committed (lib/ssms22/ is git-ignored).
    At runtime SSMS loads its own copies.

.PARAMETER SsmsIdeDir
    The SSMS IDE folder. Defaults to the standard SSMS 22 install location.
#>
[CmdletBinding()]
param(
    [string]$SsmsIdeDir = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $SsmsIdeDir 'Ssms.exe'))) {
    throw "Ssms.exe not found in '$SsmsIdeDir'. Pass -SsmsIdeDir."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'lib\ssms22'
New-Item -ItemType Directory -Force -Path $target | Out-Null

# Relative to the SSMS IDE folder.
$assemblies = @(
    'SqlWorkbench.Interfaces.dll',
    'SqlPackageBase.dll',
    'Microsoft.SqlServer.ConnectionInfo.dll',
    'Microsoft.SqlServer.RegSvrEnum.dll',
    'Microsoft.SqlServer.GridControl.dll',
    'Microsoft.SqlServer.Smo.dll',
    'Microsoft.SqlServer.Management.Sdk.Sfc.dll',
    'Microsoft.SqlServer.SqlEnum.dll',
    'Microsoft.Data.SqlClient.dll',
    'Extensions\Application\SQLEditors.dll',
    'Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll'
)

$missing = @()
foreach ($rel in $assemblies) {
    $src = Join-Path $SsmsIdeDir $rel
    if (Test-Path $src) {
        Copy-Item $src -Destination $target -Force
        $ver = [Reflection.AssemblyName]::GetAssemblyName($src).Version
        Write-Host ("copied  {0,-55} {1}" -f $rel, $ver)
    } else {
        $missing += $rel
        Write-Warning "missing $rel"
    }
}

$ssmsVersion = (Get-Item (Join-Path $SsmsIdeDir 'Ssms.exe')).VersionInfo.ProductVersion
Set-Content -Path (Join-Path $target 'SOURCE.txt') -Value "Copied from: $SsmsIdeDir`r`nSSMS version: $ssmsVersion`r`nDate: $(Get-Date -Format s)"

if ($missing.Count -gt 0) { exit 1 }
