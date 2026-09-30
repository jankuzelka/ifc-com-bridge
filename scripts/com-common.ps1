# Shared helpers for the COM scripts (dot-sourced by register-com.ps1, unregister-com.ps1 and
# com-smoke-test.ps1). Nothing here writes to the registry: registration happens only in
# register-com.ps1 / unregister-com.ps1 without -DryRun, after an elevation check and a confirmation.

. (Join-Path $PSScriptRoot 'common.ps1')

# The public ProgID of IfcComBridge. The CLSID, IID and LIBID are read from the library itself
# (ComIdentity.cs is their only definition) and checked for consistency.
$script:IntendedProgId = 'IfcComBridge.Runtime'

# The universal marshaler (PSOAInterface) that type library registration uses for dual interfaces.
$script:TypeLibraryMarshaler = '{00020424-0000-0000-C000-000000000046}'

# 64-bit system folder, also from a 32-bit PowerShell (where System32 is redirected).
function Get-NativeSystemDirectory {
    if ([Environment]::Is64BitProcess) { return Join-Path $env:WINDIR 'System32' }
    return Join-Path $env:WINDIR 'Sysnative'
}

function Find-RegAsm64 {
    $regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
    if (-not (Test-Path $regasm)) { throw "64-bit .NET Framework 4 RegAsm not found: $regasm" }
    return $regasm
}

# Quotes one argument for a Windows command line (the rules CommandLineToArgvW and the C runtime use).
function ConvertTo-NativeArgument([string] $Argument) {
    if ($Argument -and $Argument -notmatch '[\s"]') { return $Argument }
    $escaped = [regex]::Replace($Argument, '(\\*)"', { param($m) ($m.Groups[1].Value * 2) + '\"' })
    $escaped = [regex]::Replace($escaped, '(\\+)$', { param($m) $m.Groups[1].Value * 2 })
    return '"' + $escaped + '"'
}

<#
  Runs a native program and returns its exit code, standard output and standard error separately.
  Native programs are never called with "& program 2>&1" here: Windows PowerShell 5.1 turns every
  redirected stderr line into a NativeCommandError record, which $ErrorActionPreference = 'Stop' makes
  terminating even when the program succeeds (RegAsm writes its warnings to stderr). This works the same
  in Windows PowerShell 5.1 and PowerShell 7, whatever the error preference.
#>
function Invoke-NativeCommand([string] $FilePath, [string[]] $Arguments) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = (@($Arguments) | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        # Read both streams at the same time, so that a full pipe never blocks the program.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut   = @($stdout.Result -split "`r?`n" | Where-Object { $_ -ne '' })
            StdErr   = @($stderr.Result -split "`r?`n" | Where-Object { $_ -ne '' })
        }
    }
    finally {
        $process.Dispose()
    }
}

# RegAsm's warning that /codebase is used with an assembly that has no strong name. Expected: the library is
# not signed, and signing is a separate deployment decision (docs/com-contract.md).
$script:UnsignedCodeBaseWarning = 'warning RA0000 : Registering an unsigned assembly with /codebase'

<#
  Runs the 64-bit RegAsm. Success means exit code 0 and no diagnostic other than the expected
  unsigned-assembly warning, which is then reported as a warning. A nonzero exit code or any other
  diagnostic (errors, other warnings such as "no types were registered") is a failure, reported with all
  of RegAsm's output, the RA0000 warning included.
#>
function Invoke-RegAsm([string[]] $Arguments) {
    $run = Invoke-NativeCommand (Find-RegAsm64) (@($Arguments) + '/nologo')
    $expected = @($run.StdErr | Where-Object { $_ -like "*$UnsignedCodeBaseWarning*" })
    $other = @($run.StdErr | Where-Object { $_ -notlike "*$UnsignedCodeBaseWarning*" }) +
             @($run.StdOut | Where-Object { $_ -match ':\s*(error|warning)\s+RA\d+' })
    $succeeded = $run.ExitCode -eq 0 -and $other.Count -eq 0
    return [pscustomobject]@{
        ExitCode  = $run.ExitCode
        Succeeded = $succeeded
        Warnings  = $(if ($succeeded) { $expected } else { @() })
        Problems  = $(if ($succeeded) { @() } else { @($run.StdErr) + @($run.StdOut) })
        StdOut    = $run.StdOut
    }
}

function Format-RegAsmResult($Result) {
    if ($Result.Succeeded) {
        $text = "exit code $($Result.ExitCode)"
        if ($Result.Warnings.Count -gt 0) { $text += '; expected warning RA0000: unsigned assembly with /codebase (strong-name signing is a separate deployment decision)' }
        return $text
    }
    return "exit code $($Result.ExitCode): " + ($Result.Problems -join ' | ')
}

function Test-IsElevated {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Elevated([string] $Action) {
    if (-not (Test-IsElevated)) {
        throw "$Action writes to HKEY_CLASSES_ROOT and needs an elevated PowerShell (Run as administrator). " +
              'This script never elevates itself. Use -DryRun to check the registration without writing anything.'
    }
}

# The library to register: -Library, or the Release build output of this repository.
function Resolve-ComLibrary([string] $Library) {
    if (-not $Library) {
        $Library = Join-Path (Get-RepoRoot) 'src\IfcComBridge\bin\x64\Release\IfcComBridge.dll'
    }
    if (-not (Test-Path $Library -PathType Leaf)) { throw "Library not found: $Library (build it with scripts\build.ps1 -Configuration Release, or pass -Library)." }
    return (Resolve-Path $Library).Path
}

# Runs com-typelib-export.ps1 in 64-bit Windows PowerShell 5.1 and returns its JSON output as objects.
function Invoke-ComHelper([string[]] $Arguments) {
    $powershell = Join-Path (Get-NativeSystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $helper = Join-Path $PSScriptRoot 'com-typelib-export.ps1'
    $run = Invoke-NativeCommand $powershell (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $helper) + $Arguments)
    if ($run.ExitCode -ne 0) { throw "com-typelib-export.ps1 failed (exit code $($run.ExitCode)):`n$((@($run.StdErr) + @($run.StdOut)) -join "`n")" }
    return ($run.StdOut -join "`n") | ConvertFrom-Json
}

# Parses a REGEDIT4 file written by RegAsm /regfile: key -> @{ valueName ('@' = default) -> value }.
# RegAsm writes it as UTF-8 without a BOM despite the REGEDIT4 header, so it is read as UTF-8 explicitly
# (Get-Content would assume the ANSI code page in Windows PowerShell 5.1 and garble non-ASCII paths).
function Read-RegFile([string] $Path) {
    $keys = [ordered]@{}
    $current = $null
    foreach ($line in [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)) {
        if ($line -match '^\[(.+)\]$') {
            $current = $Matches[1]
            $keys[$current] = [ordered]@{}
        }
        elseif ($current) {
            $m = [regex]::Match($line, '^(?:@|"(?<name>(?:[^"\\]|\\.)*)")="(?<value>(?:[^"\\]|\\.)*)"$')
            if ($m.Success) {
                $name = if ($m.Groups['name'].Success) { $m.Groups['name'].Value -replace '\\(.)', '$1' } else { '@' }
                $keys[$current][$name] = $m.Groups['value'].Value -replace '\\(.)', '$1'
            }
        }
    }
    return $keys
}

function New-ComCheck([string] $Check, [bool] $Ok, [string] $Detail) {
    return [pscustomobject]@{ Check = $Check; Ok = $Ok; Detail = $Detail }
}

function Write-ComChecks($Checks) {
    foreach ($c in $Checks) {
        $mark = if ($c.Ok) { 'ok  ' } else { 'FAIL' }
        Write-Host ("  [{0}] {1}" -f $mark, $c.Check) -ForegroundColor $(if ($c.Ok) { 'Gray' } else { 'Red' })
        if ($c.Detail) { Write-Host "         $($c.Detail)" }
    }
}

<#
  The registration plan of a library, computed without writing to the registry:
  - RegAsm /codebase /regfile: the class registration RegAsm would write;
  - the type library, exported to WorkDir without registering it, and the interface and type library
    keys its registration would write (derived by the rules of RegisterTypeLib for dual interfaces);
  - checks that all of it is the intended IfcComBridge COM contract.
#>
function Get-ComRegistrationPlan([string] $Library, [string] $WorkDir, [string] $TypeLibraryPath) {
    New-Item -ItemType Directory -Force $WorkDir | Out-Null
    $checks = New-Object System.Collections.Generic.List[object]
    $libraryDir = Split-Path $Library -Parent
    if (-not $TypeLibraryPath) { $TypeLibraryPath = Join-Path $libraryDir ([IO.Path]::GetFileNameWithoutExtension($Library) + '.tlb') }

    $engine = Join-Path $libraryDir 'Xbim.Geometry.Engine64.dll'
    $checks.Add((New-ComCheck 'Xbim.Geometry.Engine64.dll is next to the library' (Test-Path $engine) $engine))

    # Class registration as RegAsm computes it.
    $regFile = Join-Path $WorkDir 'IfcComBridge.classes.reg'
    $regasm = Invoke-RegAsm @($Library, '/codebase', "/regfile:$regFile")
    $regasmOk = $regasm.Succeeded -and (Test-Path $regFile)
    $checks.Add((New-ComCheck 'RegAsm /regfile succeeds (64-bit .NET Framework RegAsm)' $regasmOk (Format-RegAsmResult $regasm)))
    $reg = if ($regasmOk) { Read-RegFile $regFile } else { [ordered]@{} }

    # Declared identity and type library (exported, not registered).
    $exportedTlb = Join-Path $WorkDir ([IO.Path]::GetFileName($TypeLibraryPath))
    try { $info = Invoke-ComHelper @('-Library', $Library, '-TypeLibraryPath', $exportedTlb) }
    catch {
        $checks.Add((New-ComCheck 'The COM identity and type library of the library can be read' $false $_.Exception.Message))
        return [pscustomobject]@{ Library = $Library; Checks = $checks.ToArray(); Ok = $false; ClassKeys = $reg; TypeLibraryKeys = [ordered]@{}; Members = @() }
    }
    $declared = $info.Declared
    $classes = @($declared.RegistrableClasses)
    $checks.Add((New-ComCheck 'Assembly is COM-invisible by default (ComVisible(false))' (-not $declared.AssemblyComVisible) ''))
    $checks.Add((New-ComCheck 'Exactly one creatable class' ($classes.Count -eq 1) (($classes | ForEach-Object Name) -join ', ')))
    $class = $classes | Select-Object -First 1
    $clsid = '{' + ([string]$class.Clsid).ToUpperInvariant() + '}'
    $checks.Add((New-ComCheck "The class has the ProgID $IntendedProgId" ($class.ProgId -eq $IntendedProgId) "$($class.Name) $clsid ProgID $($class.ProgId)"))

    # The .reg content.
    $progIdKey = "HKEY_CLASSES_ROOT\$IntendedProgId"
    $clsidKey = "HKEY_CLASSES_ROOT\CLSID\$clsid"
    $checks.Add((New-ComCheck 'ProgID -> CLSID' ($reg["$progIdKey\CLSID"] -and $reg["$progIdKey\CLSID"]['@'] -eq $clsid) "$progIdKey\CLSID"))
    $checks.Add((New-ComCheck 'CLSID -> ProgID' ($reg["$clsidKey\ProgId"] -and $reg["$clsidKey\ProgId"]['@'] -eq $IntendedProgId) "$clsidKey\ProgId"))
    $inproc = $reg["$clsidKey\InprocServer32"]
    $inprocOk = $inproc -and $inproc['@'] -eq 'mscoree.dll' -and $inproc['Class'] -eq $class.Name -and
        $inproc['ThreadingModel'] -eq 'Both' -and $inproc['RuntimeVersion'] -eq 'v4.0.30319' -and $inproc['Assembly'] -eq $declared.AssemblyFullName
    $checks.Add((New-ComCheck 'InprocServer32: mscoree.dll, the runtime class, .NET 4, ThreadingModel Both' ([bool]$inprocOk) "$clsidKey\InprocServer32"))
    # RegAsm writes the CodeBase unescaped (spaces stay spaces): compare it as a local path.
    $codeBase = if ($inproc -and $inproc['CodeBase']) { $inproc['CodeBase'] } else { '' }
    $codeBaseOk = $codeBase -like 'file:///*' -and [string]::Equals(([Uri]$codeBase).LocalPath, $Library, [StringComparison]::OrdinalIgnoreCase)
    $checks.Add((New-ComCheck 'CodeBase is the library file' $codeBaseOk $codeBase))
    $unexpected = @($reg.Keys | Where-Object { $_ -ne $progIdKey -and -not $_.StartsWith("$progIdKey\") -and $_ -ne $clsidKey -and -not $_.StartsWith("$clsidKey\") })
    $checks.Add((New-ComCheck 'RegAsm registers nothing else (no other class, ProgID or record)' ($regasmOk -and $unexpected.Count -eq 0) ($unexpected -join ', ')))

    # The type library.
    $tlb = $info.TypeLibrary
    $warnings = @($info.ExportEvents | Where-Object { $_ -notmatch '^NOTIF_TYPECONVERTED' })
    $checks.Add((New-ComCheck 'Type library exports without warnings' ($warnings.Count -eq 0) ($warnings -join ' | ')))
    $checks.Add((New-ComCheck 'Type library: LIBID of the library, version 1.0, 64-bit' ($tlb.Guid -eq $declared.TypeLibraryId -and $tlb.Version -eq '1.0' -and $tlb.SysKind -eq 'SYS_WIN64') "$($tlb.Name) {$($tlb.Guid)} $($tlb.Version) $($tlb.SysKind)"))
    $coclass = @($tlb.Types | Where-Object { $_.Kind -eq 'TKIND_COCLASS' })
    $default = @($coclass | ForEach-Object { $_.Implements } | Where-Object { $_.Flags -match 'IMPLTYPEFLAG_FDEFAULT' })
    $checks.Add((New-ComCheck 'Type library: one creatable coclass with the CLSID' ($coclass.Count -eq 1 -and "{$($coclass[0].Guid.ToUpperInvariant())}" -eq $clsid -and $coclass[0].Flags -match 'TYPEFLAG_FCANCREATE') "$($coclass.Name)"))
    $interfaceName = if ($default.Count -gt 0) { $default[0].Name } else { '' }
    $interface = @($tlb.Types | Where-Object { $_.Kind -eq 'TKIND_DISPATCH' -and $_.Name -eq $interfaceName }) | Select-Object -First 1
    $declaredInterface = @($declared.ComVisibleTypes | Where-Object { $_.Kind -eq 'interface' })
    $interfaceOk = $default.Count -eq 1 -and $interface -and $declaredInterface.Count -eq 1 -and $interface.Guid -eq $declaredInterface[0].Guid -and
        $interface.Flags -match 'TYPEFLAG_FDUAL' -and $interface.Vtable.Flags -match 'TYPEFLAG_FOLEAUTOMATION' -and (@($interface.Vtable.Implements | ForEach-Object Name) -join ',') -eq 'IDispatch'
    $iid = if ($interface) { '{' + $interface.Guid.ToUpperInvariant() + '}' } else { '' }
    $checks.Add((New-ComCheck 'Default interface is dual (IDispatch + vtable) and automation-compatible' ([bool]$interfaceOk) "$interfaceName $iid"))
    $dispIds = @($interface.Vtable.Functions | ForEach-Object { [int]($_ -split ' ')[0] })
    $explicitDispIds = $dispIds.Count -gt 0 -and @($dispIds | Where-Object { $_ -lt 1 -or $_ -ge 0x10000 }).Count -eq 0 -and @($dispIds | Sort-Object -Unique).Count -eq $dispIds.Count
    $checks.Add((New-ComCheck 'Interface members have explicit, unique DispIds' $explicitDispIds "$($dispIds.Count) members: DispIds $($dispIds -join ', ')"))

    # What type library registration writes for it (RegisterTypeLib rules for a dual interface).
    $libid = '{' + ([string]$tlb.Guid).ToUpperInvariant() + '}'
    $typeLibraryKeys = [ordered]@{
        "HKEY_CLASSES_ROOT\TypeLib\$libid\1.0\0\win64"                = [ordered]@{ '@' = $TypeLibraryPath }
        "HKEY_CLASSES_ROOT\TypeLib\$libid\1.0\FLAGS"                  = [ordered]@{ '@' = '0' }
        "HKEY_CLASSES_ROOT\Interface\$iid"                            = [ordered]@{ '@' = $interfaceName }
        "HKEY_CLASSES_ROOT\Interface\$iid\ProxyStubClsid32"           = [ordered]@{ '@' = $TypeLibraryMarshaler }
        "HKEY_CLASSES_ROOT\Interface\$iid\TypeLib"                    = [ordered]@{ '@' = $libid; 'Version' = '1.0' }
    }

    return [pscustomobject]@{
        Library             = $Library
        ProgId              = $IntendedProgId
        Clsid               = $clsid
        ClassName           = $class.Name
        InterfaceId         = $iid
        InterfaceName       = $interfaceName
        TypeLibraryId       = $libid
        AssemblyName        = $declared.AssemblyName
        AssemblyVersion     = $declared.AssemblyVersion
        ClassRegFile        = $regFile
        ClassKeys           = $reg
        ExportedTypeLibrary = $exportedTlb
        TypeLibraryPath     = $TypeLibraryPath
        TypeLibraryKeys     = $typeLibraryKeys
        Members             = @($interface.Vtable.Functions)
        Checks              = $checks.ToArray()
        Ok                  = @($checks | Where-Object { -not $_.Ok }).Count -eq 0
    }
}

# Read-only: which of the given HKCR keys exist, in the 64-bit view of HKLM and HKCU (HKCR merges both).
function Get-ComRegistryState([string[]] $Keys) {
    foreach ($key in $Keys) {
        $sub = $key -replace '^HKEY_CLASSES_ROOT\\', 'Software\Classes\'
        foreach ($hive in 'LocalMachine', 'CurrentUser') {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, 'Registry64')
            $k = $base.OpenSubKey($sub)
            if ($k) {
                [pscustomobject]@{ Key = $key; Hive = $hive; Default = $k.GetValue(''); CodeBase = $k.GetValue('CodeBase') }
                $k.Close()
            }
            $base.Close()
        }
    }
}
