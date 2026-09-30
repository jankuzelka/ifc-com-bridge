<#
.SYNOPSIS
    Captures a characterization baseline of the CURRENT library from the inputs in a local test
    configuration (tests.local.json).

.DESCRIPTION
    Runs 'IfcComBridge.Cli baseline --config': engine check, then for each configured scenario
    load/save + WexBIM, compose through both APIs, update. Writes *.summary.json files, the raw
    IFC/WexBIM outputs and manifest.json (input file names and SHA-256 only, no folder paths).

    Capture ONCE from the unmodified code before a change, then use compare-baseline.ps1 with
    the same -Config after the change. The configuration is parsed and validated by the CLI
    (one implementation for scripts and tests); the output folder must be outside the repository.

.EXAMPLE
    .\scripts\capture-baseline.ps1 -Config D:\private\tests.local.json -OutDir D:\private\baseline\reference
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Config,
    [Parameter(Mandatory = $true)]
    [string] $OutDir,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$OutDir = Assert-OutsideRepo $OutDir
$cli = Assert-CliBuilt $Configuration

$arguments = @('baseline', '--config', $Config, '--out', $OutDir)
if ($Force) { $arguments += '--force' }
& $cli @arguments
exit $LASTEXITCODE
