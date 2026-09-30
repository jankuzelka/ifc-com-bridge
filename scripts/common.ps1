# Shared helpers for the harness scripts (dot-sourced). Configuration files are never parsed here:
# the CLI owns the one parser/validator (IfcComBridge.Cli config / baseline / baseline-compare).

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found; pass -MSBuild <path to MSBuild.exe>.' }
    $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $found) { throw 'MSBuild.exe not found; pass -MSBuild <path to MSBuild.exe>.' }
    return $found
}

function Find-VSTest {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found; pass -VSTest <path to vstest.console.exe>.' }
    $found = & $vswhere -latest -prerelease -products * -find '**\TestPlatform\vstest.console.exe' | Select-Object -First 1
    if (-not $found) { throw 'vstest.console.exe not found; pass -VSTest <path to vstest.console.exe>.' }
    return $found
}

function Get-CliPath([string] $Configuration = 'Debug') {
    return Join-Path (Get-RepoRoot) "tools\IfcComBridge.Cli\bin\x64\$Configuration\net472\IfcComBridge.Cli.exe"
}

function Assert-CliBuilt([string] $Configuration = 'Debug') {
    $cli = Get-CliPath $Configuration
    if (-not (Test-Path $cli)) { throw "CLI not built: $cli (run scripts\build.ps1 -Configuration $Configuration)" }
    return $cli
}

# Baselines, results and other private outputs must never end up inside the repository.
function Assert-OutsideRepo([string] $Path) {
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $root = (Get-RepoRoot).TrimEnd('\', '/')
    if ($full -eq $root -or $full.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use '$full': it is inside the repository ($root). Choose a folder outside it."
    }
    return $full
}
