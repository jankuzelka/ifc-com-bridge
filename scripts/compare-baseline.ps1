<#
.SYNOPSIS
    Re-runs the characterization steps with the CURRENT build and compares them with a baseline.

.DESCRIPTION
    Runs 'IfcComBridge.Cli baseline-compare --config': verifies that the configured inputs are the
    baseline's (same roles, byte-identical by SHA-256 in manifest.json), re-runs every step into a
    temporary folder outside the repository and compares the summaries. The temporary run is deleted
    when everything matches (unless -KeepCurrent) and kept, with its path printed, on differences.

    Exit code: 0 identical, 1 differences, 3 configuration error or inputs differ from the baseline.

.EXAMPLE
    .\scripts\compare-baseline.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Config,
    [Parameter(Mandatory = $true)]
    [string] $BaselineDir,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $Ignore,
    [switch] $KeepCurrent
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$BaselineDir = Assert-OutsideRepo $BaselineDir
$cli = Assert-CliBuilt $Configuration

$arguments = @('baseline-compare', '--config', $Config, '--baseline', $BaselineDir)
if ($Ignore) { $arguments += @('--ignore', $Ignore) }
if ($KeepCurrent) { $arguments += '--keep' }
& $cli @arguments
exit $LASTEXITCODE
