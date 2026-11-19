<#
.SYNOPSIS
    Builds Pomegranate.Shell.dll (the Windows 11 right-click menu command) with the MSVC compiler.

.DESCRIPTION
    Needs Visual Studio 2022 or its Build Tools with the "Desktop development with C++" workload.
    ARM64 also needs the "MSVC ARM64 build tools" component. Output: artifacts/shell/<arch>/Pomegranate.Shell.dll

.EXAMPLE
    powershell -File tools/build-shell.ps1 -Arch x64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$source = Join-Path $root 'src\Pomegranate.Shell'
$out = Join-Path $root "artifacts\shell\$Arch"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$component = if ($Arch -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
$vs = & $vswhere -latest -products * -requires $component -property installationPath
if (-not $vs) { throw 'No Visual Studio C++ tools found.' }

$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$hostTarget = if ($Arch -eq 'arm64') {
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64_arm64' }
} else { 'x64' }

$cl = "cl /nologo /LD /O2 /EHsc /W4 /WX /std:c++17 /DUNICODE /D_UNICODE " +
      "`"$source\PomegranateShell.cpp`" /Fe:`"$out\Pomegranate.Shell.dll`" /Fo:`"$out\\`" " +
      "/link /DEF:`"$source\PomegranateShell.def`" /NOLOGO"

# vcvarsall looks for vswhere on PATH.
$env:PATH = "$(Split-Path $vswhere);$env:PATH"
cmd /c "`"$vcvars`" $hostTarget >nul && $cl"
if ($LASTEXITCODE -ne 0) { throw "Compiling the shell extension failed ($LASTEXITCODE)." }
Write-Host "Built $out\Pomegranate.Shell.dll"
