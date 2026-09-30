<#
.SYNOPSIS
    Removes the COM registration of IfcComBridge (64-bit .NET Framework RegAsm /unregister and the type
    library registration), or only shows what would be removed (-DryRun).

.DESCRIPTION
    Pass the same library file that was registered: RegAsm reads the classes to remove from it.

    -DryRun: no elevation needed, nothing is written. Computes the registration of the library as
    register-com.ps1 -DryRun does and lists which of its keys exist on this machine now.

    Without -DryRun: needs an elevated PowerShell (the script never elevates itself) and asks for
    confirmation (-Confirm:$false skips it). Runs RegAsm <library> /unregister, unregisters the type
    library (UnRegisterTypeLib, version 1.0, win64) and verifies that the keys are gone. The .tlb file
    next to the library is left in place.

    Exit code: 0 ok, 1 keys still present after unregistering, 2 prerequisites missing.

.EXAMPLE
    .\scripts\unregister-com.ps1 -DryRun
.EXAMPLE
    .\scripts\unregister-com.ps1 -Library 'D:\IfcComBridge\IfcComBridge.dll'     # elevated PowerShell
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $Library,
    [switch] $DryRun,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'com-common.ps1')

$Library = Resolve-ComLibrary $Library
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) ('IfcComBridge.ComRegistration\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutDir = Assert-OutsideRepo $OutDir

Write-Host "library:       $Library"
# Computing the plan writes nothing to the registry; -WhatIf applies to the unregistration only.
$whatIf = $WhatIfPreference
$WhatIfPreference = $false
try { $plan = Get-ComRegistrationPlan -Library $Library -WorkDir $OutDir }
finally { $WhatIfPreference = $whatIf }
if (-not $plan.Clsid) {
    Write-ComChecks $plan.Checks
    Write-Host 'unregister-com: cannot read the COM identity of the library; nothing was changed.' -ForegroundColor Red
    exit 2
}
Write-Host "identity:      $($plan.ProgId) $($plan.Clsid), interface $($plan.InterfaceId), type library $($plan.TypeLibraryId)"

$keys = @($plan.ClassKeys.Keys) + @($plan.TypeLibraryKeys.Keys)
$present = @(Get-ComRegistryState $keys)
Write-Host ''
Write-Host "keys of this registration present on this machine now (read-only): $($present.Count) of $($keys.Count)"
$present | ForEach-Object { Write-Host "  $($_.Key) ($($_.Hive))" }

if ($DryRun) {
    Write-Host ''
    Write-Host 'unregister-com: dry run; would run:'
    Write-Host "  $(Find-RegAsm64) `"$Library`" /unregister"
    Write-Host "  UnRegisterTypeLib $($plan.TypeLibraryId) 1.0 win64"
    Write-Host 'nothing was written to the registry.'
    exit 0
}

try { Assert-Elevated 'Unregistering COM' }
catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }

if (-not $PSCmdlet.ShouldProcess("$($plan.ProgId) $($plan.Clsid) and type library $($plan.TypeLibraryId)", 'Unregister COM')) { exit 0 }

$regasm = Invoke-RegAsm @($Library, '/unregister')
$regasm.StdOut | ForEach-Object { Write-Host "  $_" }
Write-Host "RegAsm /unregister: $(Format-RegAsmResult $regasm)"
if (-not $regasm.Succeeded) {
    Write-Host 'unregister-com: RegAsm FAILED; the type library registration was left in place.' -ForegroundColor Red
    exit 1
}
$result = Invoke-ComHelper @('-UnregisterTypeLibrary', $plan.TypeLibraryId.Trim('{', '}'))
Write-Host "type library: UnRegisterTypeLib returned $($result.HResult)"

$left = @(Get-ComRegistryState $keys | Where-Object { $_.Hive -eq 'LocalMachine' })
if ($left.Count -gt 0) {
    $left | ForEach-Object { Write-Host "  still present: $($_.Key)" -ForegroundColor Red }
    exit 1
}
Write-Host 'unregister-com: unregistered; no key of this registration is left in HKLM.'
exit 0
