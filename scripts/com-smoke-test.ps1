<#
.SYNOPSIS
    Late-bound COM smoke test of IfcComBridge.Runtime from a native IDispatch client (64-bit cscript,
    JScript), the way PHP or VBScript use the library. Never registers anything.

.DESCRIPTION
    -Mode Registered (default)
        Creates IfcComBridge.Runtime through the registry, as a deployed client does. The class must be
        registered already (scripts\register-com.ps1, elevated); if it is not, the test stops (exit 2).

    -Mode RegistrationFree
        Creates the class without any registry entry: IfcComBridge.dll, Xbim.Geometry.Engine64.dll, a copy of
        the 64-bit cscript.exe and two manifests go into a temporary host folder, and the client creates
        the object through an activation context (Microsoft.Windows.ActCtx). The .NET runtime looks for
        the library in the folder of the client executable, hence the cscript.exe copy.

    Checks, all through IDispatch: creation by ProgID, DoXBimLibTest() = true, StopwatchStart/Stop,
    a .NET exception arrives as a COM error, System.Object members are not exposed, Dispose().

    -Config <tests.local.json> additionally runs the characterization steps through COM, one object
    per step, in the baseline's order: single (LoadIfc, SaveIfc and SaveWexbim with modelIndex
    omitted), compose (LoadIfcJson with the products map as JSON text, GetModelGroupId, SaveIfc and
    SaveWexbim per model) and update (LoadIfc, UpdateModel, SaveIfc, SaveWexbim). With -BaselineDir
    the outputs are summarized with the CLI and compared with the baseline (single\roundtrip,
    compose\runtime and update steps, plus the engine, model count, group ids and UpdateModel result).

    Outputs go to -OutDir (default %TEMP%\IfcComBridge.ComSmokeTest\<time>), never into the repository.
    Exit code: 0 ok, 1 a check failed or differs from the baseline, 2 prerequisites missing.

.EXAMPLE
    .\scripts\com-smoke-test.ps1 -Mode RegistrationFree
.EXAMPLE
    .\scripts\com-smoke-test.ps1 -Mode RegistrationFree -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
.EXAMPLE
    .\scripts\com-smoke-test.ps1        # after scripts\register-com.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Registered', 'RegistrationFree')]
    [string] $Mode = 'Registered',
    [string] $Library,
    [string] $Config,
    [string] $BaselineDir,
    [string] $OutDir,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'com-common.ps1')

function Stop-Prerequisite([string] $Message) {
    Write-Host "com-smoke-test: $Message" -ForegroundColor Yellow
    exit 2
}

if ($BaselineDir -and -not $Config) { Stop-Prerequisite '-BaselineDir needs -Config (the baseline inputs).' }
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) ('IfcComBridge.ComSmokeTest\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutDir = Assert-OutsideRepo $OutDir
if (Test-Path $OutDir) { Stop-Prerequisite "output folder exists: $OutDir" }
New-Item -ItemType Directory -Force $OutDir, (Join-Path $OutDir 'single'), (Join-Path $OutDir 'compose\runtime'), (Join-Path $OutDir 'update') | Out-Null

$settings = [ordered]@{ out = $OutDir }
$cscript = Join-Path (Get-NativeSystemDirectory) 'cscript.exe'

if ($Mode -eq 'Registered') {
    $progId = @(Get-ComRegistryState @("HKEY_CLASSES_ROOT\$IntendedProgId\CLSID")) | Select-Object -First 1
    if (-not $progId) {
        Stop-Prerequisite "$IntendedProgId is not registered on this machine. Register it with scripts\register-com.ps1 (elevated PowerShell), or use -Mode RegistrationFree. Nothing was changed."
    }
    $inproc = @(Get-ComRegistryState @("HKEY_CLASSES_ROOT\CLSID\$($progId.Default)\InprocServer32")) | Select-Object -First 1
    $registered = if ($inproc -and $inproc.CodeBase) { ([Uri]$inproc.CodeBase).LocalPath } else { $null }
    Write-Host "registered:    $IntendedProgId $($progId.Default) -> $registered"
    if (-not $registered -or -not (Test-Path $registered)) { Stop-Prerequisite "the registered library file does not exist: $registered" }
    if (-not (Test-Path (Join-Path (Split-Path $registered -Parent) 'Xbim.Geometry.Engine64.dll'))) { Stop-Prerequisite 'Xbim.Geometry.Engine64.dll is not next to the registered library.' }
}
else {
    $Library = Resolve-ComLibrary $Library
    $engine = Join-Path (Split-Path $Library -Parent) 'Xbim.Geometry.Engine64.dll'
    if (-not (Test-Path $engine)) { Stop-Prerequisite "Xbim.Geometry.Engine64.dll is not next to $Library" }
    $identity = (Invoke-ComHelper @('-Identity', $Library)).Declared
    $class = @($identity.RegistrableClasses) | Select-Object -First 1
    if (-not $class -or $class.ProgId -ne $IntendedProgId) { Stop-Prerequisite "$Library does not declare the class $IntendedProgId." }

    $hostDir = Join-Path $OutDir 'host'
    New-Item -ItemType Directory -Force $hostDir | Out-Null
    Copy-Item $Library, $engine, $cscript $hostDir
    $cscript = Join-Path $hostDir 'cscript.exe'
    $name = $identity.AssemblyName
    $version = $identity.AssemblyVersion
@"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity type="win32" name="$name" version="$version" processorArchitecture="amd64" />
  <clrClass clsid="{$($class.Clsid)}" progid="$($class.ProgId)" threadingModel="Both" name="$($class.Name)" runtimeVersion="v4.0.30319" />
  <file name="$([IO.Path]::GetFileName($Library))" />
</assembly>
"@ | Set-Content (Join-Path $hostDir "$name.manifest") -Encoding UTF8
@"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity type="win32" name="IfcComBridge.SmokeTest" version="1.0.0.0" processorArchitecture="amd64" />
  <dependency>
    <dependentAssembly>
      <assemblyIdentity type="win32" name="$name" version="$version" processorArchitecture="amd64" />
    </dependentAssembly>
  </dependency>
</assembly>
"@ | Set-Content (Join-Path $hostDir 'IfcComBridge.SmokeTest.manifest') -Encoding UTF8
    $settings['manifest'] = Join-Path $hostDir 'IfcComBridge.SmokeTest.manifest'
    Write-Host "registration-free host: $hostDir"
}

if ($Config) {
    $cli = Assert-CliBuilt $Configuration
    $run = Invoke-NativeCommand $cli @('config', '--config', $Config, '--json')
    if ($run.ExitCode -ne 0) { Stop-Prerequisite "invalid configuration: $Config`n$((@($run.StdOut) + @($run.StdErr)) -join "`n")" }
    $resolved = ($run.StdOut -join "`n") | ConvertFrom-Json
    if ($resolved.model) {
        $settings['ifc'] = $resolved.model.ifc
        if ($resolved.model.transforms) { $settings['transforms'] = $resolved.model.transforms }
    }
    if ($resolved.composition) {
        $settings['building'] = $resolved.composition.building
        $settings['products'] = $resolved.composition.products
        $settings['productsMap'] = $resolved.composition.productsMap
        $settings['layout'] = $resolved.composition.layout
    }
}

# The client: late-bound calls only. Settings go in and results come back as UTF-16 files, so paths and
# group ids with any characters survive, independent of console code pages; the client's stdout also
# carries the library's own console log.
$client = Join-Path $OutDir 'smoke-test.js'
$settingsFile = Join-Path $OutDir 'smoke-test.settings'
$resultsFile = Join-Path $OutDir 'smoke-test.results'
$settings['results'] = $resultsFile
Set-Content $settingsFile ($settings.Keys | ForEach-Object { "$_=$($settings[$_])" }) -Encoding Unicode
Set-Content $client -Encoding ASCII -Value @'
// IfcComBridge COM smoke test client, written by scripts\com-smoke-test.ps1. Every call is late-bound
// (IDispatch), as PHP or VBScript make them. Output: one STEP or RESULT line per check.
var fso = new ActiveXObject("Scripting.FileSystemObject");
var settings = {};
(function () {
    var file = fso.OpenTextFile(WScript.Arguments(0), 1, false, -1);
    while (!file.AtEndOfStream) {
        var line = file.ReadLine();
        var i = line.indexOf("=");
        if (i > 0) settings[line.substring(0, i)] = line.substring(i + 1);
    }
    file.Close();
})();

var failures = 0;
var log = fso.CreateTextFile(settings.results, true, true); // UTF-16
function print(text) { WScript.StdOut.WriteLine(text); log.WriteLine(text); }
function code(e) { return "0x" + ("00000000" + (e.number >>> 0).toString(16).toUpperCase()).slice(-8); }
function firstLine(text) { return String(text).split("\r")[0].split("\n")[0]; }
function step(name, body) {
    try {
        var detail = body();
        print("STEP ok   " + name + (detail !== undefined ? ": " + detail : ""));
        return true;
    } catch (e) {
        failures++;
        print("STEP FAIL " + name + ": " + code(e) + " " + firstLine(e.message));
        return false;
    }
}
function raises(name, body) {
    try { body(); failures++; print("STEP FAIL " + name + ": no error"); }
    catch (e) { print("STEP ok   " + name + ": " + code(e) + " " + firstLine(e.message)); }
}
function result(name, value) { print("RESULT " + name + "=" + value); }

var context = null;
function create() {
    if (settings.manifest) {
        if (context === null) {
            context = new ActiveXObject("Microsoft.Windows.ActCtx");
            context.Manifest = settings.manifest;
        }
        return context.CreateObject("IfcComBridge.Runtime");
    }
    return new ActiveXObject("IfcComBridge.Runtime");
}
function readUtf8(path) {
    var stream = new ActiveXObject("ADODB.Stream");
    stream.Type = 2;
    stream.Charset = "utf-8";
    stream.Open();
    stream.LoadFromFile(path);
    var text = stream.ReadText();
    stream.Close();
    return text.charCodeAt(0) === 0xFEFF ? text.substring(1) : text;
}
// Same file names as the baseline (ComposeRunner.ModelFileStem).
function stem(index, groupId) {
    var text = (groupId === null || groupId === undefined) ? "" : String(groupId);
    text = text.replace(/[\x00-\x1f"<>|:*?\\\/\s]/g, "_");
    if (text.length === 0) text = "group";
    if (text.length > 60) text = text.substring(0, 60);
    var number = String(index);
    while (number.length < 3) number = "0" + number;
    return number + "_" + text;
}

var runtime = null;
if (step("create IfcComBridge.Runtime (" + (settings.manifest ? "registration-free" : "registered") + ")", function () { runtime = create(); })) {
    step("DoXBimLibTest() returns true", function () {
        var ok = runtime.DoXBimLibTest();
        result("engine.doXBimLibTest", ok);
        if (ok !== true) throw new Error("returned " + ok);
        return ok;
    });
    step("StopwatchStart(), StopwatchStop() returns seconds", function () {
        runtime.StopwatchStart();
        var seconds = runtime.StopwatchStop();
        if (typeof seconds !== "number" || seconds < 0) throw new Error("returned " + seconds);
        return seconds;
    });
    raises("GetModelGroupId(0) without models raises a COM error", function () { runtime.GetModelGroupId(0); });
    raises("ToString() is not exposed (no class interface)", function () { runtime.ToString(); });
    step("Dispose()", function () { runtime.Dispose(); });
}

if (settings.ifc) {
    step("single: LoadIfc, SaveIfc and SaveWexbim with modelIndex omitted", function () {
        var rt = create();
        rt.LoadIfc(settings.ifc);
        rt.SaveIfc(settings.out + "\\single\\roundtrip.ifc");
        rt.SaveWexbim(settings.out + "\\single\\roundtrip.wexbim");
        rt.Dispose();
    });
}
if (settings.building) {
    step("compose: LoadIfcJson, GetModelGroupId, SaveIfc and SaveWexbim per model", function () {
        var rt = create();
        var count = rt.LoadIfcJson(settings.building, settings.products, readUtf8(settings.productsMap), settings.layout);
        result("compose.modelCount", count);
        for (var i = 0; i < count; i++) {
            var groupId = rt.GetModelGroupId(i);
            result("compose.groupId." + i, groupId);
            var name = stem(i, groupId);
            rt.SaveIfc(settings.out + "\\compose\\runtime\\" + name + ".ifc", i);
            rt.SaveWexbim(settings.out + "\\compose\\runtime\\" + name + ".wexbim", i);
        }
        rt.Dispose();
        return count + " model(s)";
    });
}
if (settings.ifc && settings.transforms) {
    step("update: LoadIfc, UpdateModel(file), SaveIfc, SaveWexbim", function () {
        var rt = create();
        rt.LoadIfc(settings.ifc);
        var changed = rt.UpdateModel(settings.transforms);
        result("update.updateModelReturned", changed);
        rt.SaveIfc(settings.out + "\\update\\updated.ifc");
        rt.SaveWexbim(settings.out + "\\update\\updated.wexbim");
        rt.Dispose();
        return "UpdateModel returned " + changed;
    });
}
log.Close();
WScript.Quit(failures === 0 ? 0 : 1);
'@

Write-Host "client:        $cscript (64-bit), JScript, late-bound"
Write-Host "output:        $OutDir"
Write-Host ''
$run = Invoke-NativeCommand $cscript @('//Nologo', '//E:JScript', $client, $settingsFile)
$lines = if (Test-Path $resultsFile) { @(Get-Content $resultsFile -Encoding Unicode) } else { @() }
$results = @{}
foreach ($text in $lines) {
    if ($text -match '^RESULT ([^=]+)=(.*)$') { $results[$Matches[1]] = $Matches[2] }
    elseif ($text -match '^STEP ') { Write-Host "  $($text.Substring(5))" -ForegroundColor $(if ($text -match '^STEP FAIL') { 'Red' } else { 'Gray' }) }
}
$run.StdOut | ForEach-Object { Write-Verbose $_ }
$failures = if ($run.ExitCode -eq 0) { 0 } else { 1 }
if ($run.ExitCode -ne 0 -and -not ($lines -match '^STEP FAIL')) {
    Write-Host "the client failed (cscript exit code $($run.ExitCode)):" -ForegroundColor Red
    @($run.StdErr) + @($run.StdOut) | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

if ($BaselineDir) {
    $BaselineDir = Assert-OutsideRepo $BaselineDir
    Write-Host ''
    Write-Host "baseline comparison: $BaselineDir"
    function Compare-Value([string] $Name, $Expected, $Actual) {
        $same = [string]::Equals([string]$Expected, [string]$Actual, [StringComparison]::OrdinalIgnoreCase)
        Write-Host ("  {0} {1}: baseline {2}, COM {3}" -f $(if ($same) { 'identical ' } else { 'DIFFERENT ' }), $Name, $Expected, $Actual) -ForegroundColor $(if ($same) { 'Gray' } else { 'Red' })
        if (-not $same) { $script:failures++ }
    }
    function Compare-Model([string] $Step, [string] $Stem) {
        $dir = Join-Path $OutDir $Step
        $summary = Join-Path $dir "$Stem.summary.json"
        $run = Invoke-NativeCommand $cli @('summary', '--ifc', (Join-Path $dir "$Stem.ifc"), '--wexbim', (Join-Path $dir "$Stem.wexbim"), '--out', $summary)
        if ($run.ExitCode -ne 0) {
            Write-Host "  FAILED to summarize $Step\$Stem (exit code $($run.ExitCode))" -ForegroundColor Red
            @($run.StdErr) + @($run.StdOut) | ForEach-Object { Write-Host "    $_" }
            $script:failures++
            return
        }
        $run = Invoke-NativeCommand $cli @('compare', '--baseline', (Join-Path (Join-Path $BaselineDir $Step) "$Stem.summary.json"), '--current', $summary)
        $same = $run.ExitCode -eq 0
        Write-Host ("  {0} {1}\{2}" -f $(if ($same) { 'identical ' } else { 'DIFFERENT ' }), $Step, $Stem) -ForegroundColor $(if ($same) { 'Gray' } else { 'Red' })
        if (-not $same) { @($run.StdOut) + @($run.StdErr) | ForEach-Object { Write-Host "    $_" }; $script:failures++ }
    }
    $engine = Get-Content (Join-Path $BaselineDir 'engine\engine.summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Compare-Value 'engine: DoXBimLibTest' $engine.doXBimLibTest $results['engine.doXBimLibTest']
    if ($settings.Contains('ifc')) { Compare-Model 'single' 'roundtrip' }
    if ($settings.Contains('building')) {
        $compose = Get-Content (Join-Path $BaselineDir 'compose\runtime\result.summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        Compare-Value 'compose: model count' $compose.modelCount $results['compose.modelCount']
        $groupIds = @(0..([int]$results['compose.modelCount'] - 1) | Where-Object { $_ -ge 0 } | ForEach-Object { $results["compose.groupId.$_"] })
        Compare-Value 'compose: group ids' (@($compose.groupIds) -join ',') ($groupIds -join ',')
        Get-ChildItem (Join-Path $OutDir 'compose\runtime') -Filter *.ifc | Sort-Object Name | ForEach-Object { Compare-Model 'compose\runtime' $_.BaseName }
    }
    if ($settings.Contains('transforms')) {
        $update = Get-Content (Join-Path $BaselineDir 'update\result.summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        Compare-Value 'update: UpdateModel result' $update.updateModelReturned $results['update.updateModelReturned']
        Compare-Model 'update' 'updated'
    }
}

Write-Host ''
if ($failures -eq 0) { Write-Host "com-smoke-test ($Mode): passed" } else { Write-Host "com-smoke-test ($Mode): FAILED" -ForegroundColor Red }
exit $(if ($failures -eq 0) { 0 } else { 1 })
