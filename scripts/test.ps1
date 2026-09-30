<#
.SYNOPSIS
    Runs the characterization tests (after scripts\build.ps1) with Visual Studio's vstest.console.

.DESCRIPTION
    IfcComBridge.Tests always runs and needs no private data.

    IfcComBridge.IntegrationTests runs against PRIVATE models described by -Config (tests.local.json,
    kept outside the repository). Scenarios that the configuration leaves out are skipped; without
    -Config every private-data test is skipped, which is the normal CI mode. The configuration is
    validated (by the CLI) before any test runs; an invalid one stops the run with every problem
    listed. -BaselineDir additionally compares the current library with a captured baseline.

    Only the configuration PATH reaches the test host, through variables set for this run and
    restored afterwards. Results go OUTSIDE the repository by default: integration-test output can
    quote names from private models.

.EXAMPLE
    .\scripts\test.ps1
    .\scripts\test.ps1 -Config D:\private\tests.local.json
    .\scripts\test.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $Config,
    [string] $BaselineDir,
    [switch] $UnitOnly,
    [string] $ResultsDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'IfcComBridge.TestResults'),
    [string] $VSTest
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot
$ResultsDirectory = Assert-OutsideRepo $ResultsDirectory
if (-not $VSTest) { $VSTest = Find-VSTest }
if ($UnitOnly -and ($Config -or $BaselineDir)) { throw '-Config and -BaselineDir apply to the integration tests; do not combine them with -UnitOnly.' }
if ($BaselineDir -and -not $Config) { throw '-BaselineDir needs -Config (the inputs the baseline was captured from).' }

$assemblies = @(Join-Path $repoRoot "tests\IfcComBridge.Tests\bin\x64\$Configuration\net472\IfcComBridge.Tests.dll")
if (-not $UnitOnly) {
    $assemblies += Join-Path $repoRoot "tests\IfcComBridge.IntegrationTests\bin\x64\$Configuration\net472\IfcComBridge.IntegrationTests.dll"
}
foreach ($assembly in $assemblies) {
    if (-not (Test-Path $assembly)) { throw "Not built: $assembly (run scripts\build.ps1 -Configuration $Configuration)" }
}

# Validate the configuration with the one shared parser before running anything.
$configPath = $null
if ($Config) {
    $configPath = [System.IO.Path]::GetFullPath($Config)
    $cli = Assert-CliBuilt $Configuration
    & $cli config --config $configPath
    if ($LASTEXITCODE -ne 0) { throw "Invalid test configuration (problems listed above); no tests were run." }
}
$baselinePath = if ($BaselineDir) { Assert-OutsideRepo $BaselineDir } else { $null }

# Hand only the configuration path to the test host, for this run only. Without -Config the
# variables are cleared so that the result depends on the parameters alone.
$saved = @{
    IFCCOMBRIDGE_TEST_CONFIG  = $env:IFCCOMBRIDGE_TEST_CONFIG
    IFCCOMBRIDGE_BASELINE_DIR = $env:IFCCOMBRIDGE_BASELINE_DIR
}
try {
    $env:IFCCOMBRIDGE_TEST_CONFIG = $configPath
    $env:IFCCOMBRIDGE_BASELINE_DIR = $baselinePath

    Write-Host "vstest: $VSTest"
    Write-Host "results: $ResultsDirectory"
    Write-Host ("private configuration: " + $(if ($configPath) { $configPath } else { 'none (private-data tests will be skipped)' }))
    & $VSTest @assemblies '/Platform:x64' '/Framework:.NETFramework,Version=v4.7.2' `
        "/ResultsDirectory:$ResultsDirectory" '/Logger:trx' '/Logger:console;verbosity=normal'
    $exitCode = $LASTEXITCODE
}
finally {
    $env:IFCCOMBRIDGE_TEST_CONFIG = $saved.IFCCOMBRIDGE_TEST_CONFIG
    $env:IFCCOMBRIDGE_BASELINE_DIR = $saved.IFCCOMBRIDGE_BASELINE_DIR
}
exit $exitCode
