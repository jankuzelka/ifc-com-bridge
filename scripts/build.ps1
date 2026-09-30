<#
.SYNOPSIS
    Restores and builds IfcComBridge.sln (library, CLI and tests) with Visual Studio's MSBuild.

.DESCRIPTION
    Building never registers COM and needs no elevation: the library project forces
    RegisterForComInterop=false, and this script also passes it as a global property for every project.
    The library's output goes to src\IfcComBridge\bin\x64\<Configuration>\ (IfcComBridge.dll, its PDB
    and Xbim.Geometry.Engine64.dll).

.PARAMETER PackagesDirectory
    Optional NuGet package folder for this build only (sets NUGET_PACKAGES for the MSBuild process),
    e.g. to keep the user-wide package cache untouched.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $MSBuild,
    [string] $PackagesDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot
if (-not $MSBuild) { $MSBuild = Find-MSBuild }
if ($PackagesDirectory) { $env:NUGET_PACKAGES = $PackagesDirectory }

Write-Host "MSBuild: $MSBuild"
& $MSBuild (Join-Path $repoRoot 'IfcComBridge.sln') /restore /nologo /m /v:minimal `
    "/p:Configuration=$Configuration" '/p:Platform=x64' '/p:RegisterForComInterop=false'
exit $LASTEXITCODE
