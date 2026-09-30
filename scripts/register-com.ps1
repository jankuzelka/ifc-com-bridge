<#
.SYNOPSIS
    Registers the IfcComBridge COM class (ProgID IfcComBridge.Runtime) with the 64-bit .NET Framework
    RegAsm, or only checks what would be registered (-DryRun).

.DESCRIPTION
    Builds and tests never register COM; only this script does, and only without -DryRun.

    -DryRun: no elevation needed, nothing is written to the registry.
      - RegAsm /codebase /regfile: the class registration RegAsm would write, saved as a .reg file.
      - The type library, exported into the output folder without registering it.
      - Checks: exactly one creatable class, ProgID IfcComBridge.Runtime <-> CLSID, InprocServer32
        (mscoree.dll, CodeBase = the library), nothing else registered, a dual, automation-compatible
        default interface with explicit DispIds, the type library identity, the geometry engine next
        to the library.
      - The interface and type library keys that the type library registration would write.
      - The current, read-only state of this machine.

    Without -DryRun: needs an elevated PowerShell (the script never elevates itself) and asks for
    confirmation (-Confirm:$false skips it).
      1. All dry-run checks must pass.
      2. RegAsm <library> /codebase registers the class.
      3. The type library is written next to the library and registered (HKCR\TypeLib, HKCR\Interface),
         unless -NoTypeLibrary. Late-bound clients (PHP, VBScript) do not need it; early-bound clients
         compile against the .tlb file. RegAsm /tlb cannot export it from the two-file deployment,
         see com-typelib-export.ps1.
      4. The registry is verified.

    The registration points at the library file itself (/codebase). Register the folder you deploy
    (IfcComBridge.dll with Xbim.Geometry.Engine64.dll next to it), not a build output folder that changes.

    Exit code: 0 ok, 1 a check failed, 2 prerequisites missing (e.g. not elevated).

.EXAMPLE
    .\scripts\register-com.ps1 -DryRun
.EXAMPLE
    .\scripts\register-com.ps1 -Library 'D:\IfcComBridge\IfcComBridge.dll'     # elevated PowerShell
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $Library,
    [switch] $DryRun,
    [string] $OutDir,
    [switch] $NoTypeLibrary
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'com-common.ps1')

$Library = Resolve-ComLibrary $Library
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) ('IfcComBridge.ComRegistration\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutDir = Assert-OutsideRepo $OutDir

Write-Host "library:       $Library"
Write-Host "check output:  $OutDir"
# Computing the plan writes nothing to the registry; -WhatIf applies to the registration only.
$whatIf = $WhatIfPreference
$WhatIfPreference = $false
try { $plan = Get-ComRegistrationPlan -Library $Library -WorkDir $OutDir }
finally { $WhatIfPreference = $whatIf }

Write-Host ''
Write-Host 'checks:'
Write-ComChecks $plan.Checks
if (-not $plan.Clsid) {
    Write-Host 'register-com: checks FAILED; nothing was registered.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "class registration (RegAsm /codebase), also in $($plan.ClassRegFile):"
foreach ($key in $plan.ClassKeys.Keys) {
    Write-Host "  [$key]"
    foreach ($name in $plan.ClassKeys[$key].Keys) { Write-Host ("    {0} = {1}" -f $name, $plan.ClassKeys[$key][$name]) }
}
if (-not $NoTypeLibrary) {
    Write-Host ''
    Write-Host "type library registration of $($plan.TypeLibraryPath) (written by RegisterTypeLib for a dual interface):"
    foreach ($key in $plan.TypeLibraryKeys.Keys) {
        Write-Host "  [$key]"
        foreach ($name in $plan.TypeLibraryKeys[$key].Keys) { Write-Host ("    {0} = {1}" -f $name, $plan.TypeLibraryKeys[$key][$name]) }
    }
}
Write-Host ''
Write-Host "interface $($plan.InterfaceName) $($plan.InterfaceId), vtable (DispId @offset member):"
$plan.Members | ForEach-Object { Write-Host "  $_" }

Write-Host ''
Write-Host 'this machine now (read-only):'
$roots = @("HKEY_CLASSES_ROOT\$($plan.ProgId)", "HKEY_CLASSES_ROOT\CLSID\$($plan.Clsid)",
           "HKEY_CLASSES_ROOT\Interface\$($plan.InterfaceId)", "HKEY_CLASSES_ROOT\TypeLib\$($plan.TypeLibraryId)")
$present = @(Get-ComRegistryState $roots)
if ($present.Count -eq 0) { Write-Host '  IfcComBridge is not registered.' }
$present | ForEach-Object { Write-Host "  registered: $($_.Key) ($($_.Hive))" }

if (-not $plan.Ok) {
    Write-Host ''
    Write-Host 'register-com: checks FAILED; nothing was registered.' -ForegroundColor Red
    exit 1
}
if ($DryRun) {
    Write-Host ''
    Write-Host 'register-com: dry run passed; nothing was written to the registry.'
    exit 0
}

try { Assert-Elevated 'Registering COM' }
catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }

$what = "$($plan.ProgId) $($plan.Clsid) -> $Library" + $(if ($NoTypeLibrary) { '' } else { " and type library $($plan.TypeLibraryPath)" })
if (-not $PSCmdlet.ShouldProcess($what, 'Register COM')) { exit 0 }

$regasm = Invoke-RegAsm @($Library, '/codebase')
$regasm.StdOut | ForEach-Object { Write-Host "  $_" }
Write-Host "RegAsm /codebase: $(Format-RegAsmResult $regasm)"
if (-not $regasm.Succeeded) {
    Write-Host 'register-com: RegAsm FAILED; the type library was not registered.' -ForegroundColor Red
    exit 1
}
$expected = [ordered]@{}
foreach ($key in $plan.ClassKeys.Keys) { $expected[$key] = $plan.ClassKeys[$key] }
if (-not $NoTypeLibrary) {
    Copy-Item $plan.ExportedTypeLibrary $plan.TypeLibraryPath -Force
    Invoke-ComHelper @('-RegisterTypeLibrary', $plan.TypeLibraryPath) | Out-Null
    foreach ($key in $plan.TypeLibraryKeys.Keys) { $expected[$key] = $plan.TypeLibraryKeys[$key] }
}

# Verify the default values of every expected key (HKLM\Software\Classes, 64-bit view).
$failed = 0
foreach ($key in $expected.Keys) {
    $actual = @(Get-ComRegistryState @($key) | Where-Object { $_.Hive -eq 'LocalMachine' }) | Select-Object -First 1
    $want = $expected[$key]['@']
    $ok = $actual -and (-not $want -or [string]::Equals([string]$actual.Default, [string]$want, [StringComparison]::OrdinalIgnoreCase))
    if (-not $ok) { $failed++; Write-Host "  MISSING or different: $key (expected '$want', found '$($actual.Default)')" -ForegroundColor Red }
}
if ($failed -gt 0) { Write-Host "register-com: registered, but $failed key(s) differ from the plan." -ForegroundColor Red; exit 1 }
Write-Host "register-com: registered and verified $($expected.Count) keys. Test it with scripts\com-smoke-test.ps1."
exit 0
