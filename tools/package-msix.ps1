<#
.SYNOPSIS
    Builds the Pomegranate MSIX for one CPU type.

.DESCRIPTION
    1. Publishes the app self-contained for win-<arch>.
    2. Adds the engines (tools/fetch-engines.ps1), the right-click DLL (tools/build-shell.ps1),
       the logos and the manifest.
    3. Generates resources.pri (so Windows picks the right icon sizes) and packs with makeappx.
    4. Signs only if -CertificatePath is given. The Store signs submitted packages itself; for
       installing on your own PC you need a certificate your PC trusts (see docs/CHECKLIST.md).

    Output: artifacts/msix/Pomegranate_<version>_<arch>.msix

.EXAMPLE
    powershell -File tools/package-msix.ps1 -Arch x64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Arch = 'x64',

    [string] $Version = '0.1.0.0',

    # Must match the signing certificate's subject.
    [string] $Publisher = 'CN=Pomegranate Dev',

    [string] $CertificatePath = '',

    [string] $CertificatePassword = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$artifacts = Join-Path $root 'artifacts'
$layout = Join-Path $artifacts "msix-layout\$Arch"
$outDir = Join-Path $artifacts 'msix'
$msix = Join-Path $outDir "Pomegranate_$($Version)_$Arch.msix"

$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
    Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK with makeappx.exe not found.' }
$tools = Join-Path $sdkBin.FullName 'x64'

# 1. Engines and the shell DLL.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\fetch-engines.ps1') -Arch $Arch
if ($LASTEXITCODE) { throw 'fetch-engines failed.' }
$shellDll = Join-Path $artifacts "shell\$Arch\Pomegranate.Shell.dll"
if (-not (Test-Path $shellDll)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\build-shell.ps1') -Arch $Arch
    if ($LASTEXITCODE) { throw 'build-shell failed.' }
}

# 2. Publish the app.
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
dotnet publish (Join-Path $root 'src\Pomegranate.App\Pomegranate.App.csproj') -c Release -r "win-$Arch" `
    --self-contained true -p:PublishReadyToRun=true -o $layout
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }

Copy-Item -Recurse -Force (Join-Path $root "engines\$Arch") (Join-Path $layout 'engines')
Copy-Item -Force $shellDll $layout
Copy-Item -Force (Join-Path $root 'src\Pomegranate.App\Assets\*.png') (Join-Path $layout 'Assets')

$manifest = Get-Content -Raw (Join-Path $root 'packaging\AppxManifest.template.xml')
$manifest = $manifest.Replace('{{Publisher}}', $Publisher).Replace('{{Version}}', $Version).Replace('{{Arch}}', $Arch)
Set-Content -Path (Join-Path $layout 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# 3. resources.pri, then pack.
$priConfig = Join-Path $artifacts "priconfig-$Arch.xml"
& "$tools\makepri.exe" createconfig /cf $priConfig /dq en-US /o | Out-Null
& "$tools\makepri.exe" new /pr $layout /cf $priConfig /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri failed.' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& "$tools\makeappx.exe" pack /d $layout /p $msix /o
if ($LASTEXITCODE) { throw 'makeappx failed.' }

# 4. Optional signing.
if ($CertificatePath) {
    & "$tools\signtool.exe" sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $msix
    if ($LASTEXITCODE) { throw 'signtool failed.' }
}

$size = [math]::Round((Get-Item $msix).Length / 1MB, 1)
$installed = [math]::Round(((Get-ChildItem -Recurse $layout | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Packed $msix ($size MB download, $installed MB installed)"
