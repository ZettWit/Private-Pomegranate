<#
.SYNOPSIS
    Downloads the pinned engine binaries Pomegranate bundles (build time only; the app never downloads anything).

.DESCRIPTION
    Puts files in engines/<arch>/:
      ffmpeg/ffmpeg.exe, ffmpeg/ffprobe.exe, ffmpeg/*.dll, ffmpeg/LICENSE.txt
          (FFmpeg full GPL build from BtbN, shared-library variant: ffmpeg and ffprobe share
           the DLLs, which roughly halves the size compared with two static executables)
      dnglab/dnglab.exe                                         (dnglab, LGPL-2.1; x64 only for now)

    Every download is checked against a pinned SHA-256. Re-running skips engines that are already present.

    Note: BtbN deletes old "autobuild-*" releases after a while. When a pinned URL stops working,
    pick a new release, update the URL and hash below, and re-run the engine tests.

.EXAMPLE
    pwsh tools/fetch-engines.ps1 -Arch x64
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Arch = 'x64',

    [string] $Destination = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Destination) {
    # $PSScriptRoot isn't reliable in param defaults on Windows PowerShell 5.1.
    $scriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Destination = Join-Path (Split-Path -Parent $scriptFolder) 'engines'
}
$ProgressPreference = 'SilentlyContinue'

$engines = @{
    x64 = @(
        @{
            Name    = 'ffmpeg'
            Url     = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-11-18-12-39/ffmpeg-n9.0.2-57-gd838f9f9ae-win64-gpl-shared-9.0.zip'
            Sha256  = '42359d70f065f253b3cdc5ae930bc56477e4bc2b7dc43047f0692ea8ca997718'
            Files   = @('bin/ffmpeg.exe', 'bin/ffprobe.exe', 'bin/*.dll', 'LICENSE.txt')
        },
        @{
            Name    = 'dnglab'
            Url     = 'https://github.com/dnglab/dnglab/releases/download/v0.8.0/dnglab-win-x64_v0.8.0.zip'
            Sha256  = 'd3add6b71568bf5f7938468b0b40a0ee1eaec01e0057fe78dcc453188010be9a'
            Files   = @('dnglab.exe')
        }
    )
    arm64 = @(
        @{
            Name    = 'ffmpeg'
            Url     = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-11-18-12-39/ffmpeg-n9.0.2-57-gd838f9f9ae-winarm64-gpl-shared-9.0.zip'
            Sha256  = '1ff219ef3b228f60f268216e60ba958973fbfb2a9ecaf14b7a01763d517951a8'
            Files   = @('bin/ffmpeg.exe', 'bin/ffprobe.exe', 'bin/*.dll', 'LICENSE.txt')
        }
        # dnglab has no Windows ARM64 release yet; it would need building from source (spec §13).
    )
}

$archFolder = Join-Path $Destination $Arch
New-Item -ItemType Directory -Force -Path $archFolder | Out-Null

foreach ($engine in $engines[$Arch]) {
    $target = Join-Path $archFolder $engine.Name
    $expected = $engine.Files | Where-Object { $_ -notlike '*`**' } | ForEach-Object { Join-Path $target (Split-Path $_ -Leaf) }
    if ((Test-Path $target) -and @($expected | Where-Object { -not (Test-Path $_) }).Count -eq 0) {
        Write-Host "$($engine.Name) ($Arch): already present"
        continue
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("pomegranate-engine-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work 'download.zip'
        Write-Host "$($engine.Name) ($Arch): downloading $($engine.Url)"
        Invoke-WebRequest -Uri $engine.Url -OutFile $zip -UseBasicParsing

        $actual = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
        if ($actual -ne $engine.Sha256) {
            throw "$($engine.Name): SHA-256 mismatch. Expected $($engine.Sha256), got $actual."
        }

        $extracted = Join-Path $work 'x'
        Expand-Archive -Path $zip -DestinationPath $extracted

        # Release zips usually have one top-level folder; look inside it if so.
        $root = $extracted
        $children = @(Get-ChildItem $extracted)
        if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
            $root = $children[0].FullName
        }

        New-Item -ItemType Directory -Force -Path $target | Out-Null
        foreach ($file in $engine.Files) {
            if ($file -like '*`**') {
                $matches = @(Get-ChildItem -Path (Join-Path $root (Split-Path $file -Parent)) -Filter (Split-Path $file -Leaf) -File)
                if ($matches.Count -eq 0) { throw "$($engine.Name): nothing matches $file in the download." }
                $matches | Copy-Item -Destination $target -Force
                continue
            }

            $source = Join-Path $root $file
            if (-not (Test-Path $source)) {
                $found = Get-ChildItem -Path $extracted -Recurse -Filter (Split-Path $file -Leaf) | Select-Object -First 1
                if (-not $found) { throw "$($engine.Name): $file not found in the download." }
                $source = $found.FullName
            }
            Copy-Item -Path $source -Destination $target -Force
        }

        Write-Host "$($engine.Name) ($Arch): ok"
    }
    finally {
        Remove-Item -Recurse -Force -Path $work -ErrorAction SilentlyContinue
    }
}
