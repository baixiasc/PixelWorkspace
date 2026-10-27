<#
Builds the two release packages.

  dist/folder/   -> launcher exe + app/ + usage text  (recommended)
  dist/single/   -> one self-contained exe
  dist/*.zip

Usage:
  pwsh scripts/build-release.ps1              # build both packages and zip them
  pwsh scripts/build-release.ps1 -Verify      # also run self-test and smoke-test
  pwsh scripts/build-release.ps1 -SingleFileOnly
#>
param(
    [switch]$SingleFileOnly,
    [switch]$SkipZip,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src/PixelWorkspace/PixelWorkspace.csproj'
$launcherProject = Join-Path $root 'src/Launcher/Launcher.csproj'
$dist = Join-Path $root 'dist'

function Invoke-DotNet {
    param([string[]]$Arguments)
    dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Compress-Package {
    param([string]$Source, [string]$Destination)
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            if (Test-Path $Destination) { Remove-Item $Destination -Force -ErrorAction SilentlyContinue }
            Compress-Archive -Path (Join-Path $Source '*') -DestinationPath $Destination -CompressionLevel Optimal -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 20) { throw }
            # the just-run smoke test can hold the exe for a moment
            Start-Sleep -Milliseconds 500
        }
    }
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

if (-not $SingleFileOnly) {
    $package = Join-Path $dist 'folder'
    $appOut = Join-Path $package 'app'
    Write-Host '==> publish app (folder layout)'
    Invoke-DotNet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $appOut, '-p:DebugType=None', '-p:DebugSymbols=false')

    Write-Host '==> publish launcher'
    $launcherOut = Join-Path $dist '_launcher'
    Invoke-DotNet @('publish', $launcherProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:PublishTrimmed=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $launcherOut)
    Get-ChildItem $launcherOut -Filter *.exe | ForEach-Object { Copy-Item $_.FullName $package -Force }
    Remove-Item $launcherOut -Recurse -Force

    Get-ChildItem (Join-Path $root 'docs') -Filter *.txt | ForEach-Object { Copy-Item $_.FullName $package -Force }
    Get-ChildItem $appOut -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

    if ($Verify) {
        $env:PIXELWORKSPACE_DATA_DIR = Join-Path $dist '_data'
        $exe = Get-ChildItem $appOut -Filter PixelWorkspace.exe | Select-Object -First 1
        Write-Host '==> self-test'
        cmd /c "`"$($exe.FullName)`" --self-test > nul 2>&1"
        Write-Host "    exit=$LASTEXITCODE"
        if ($LASTEXITCODE -ne 0) { throw 'self-test failed' }
        Write-Host '==> smoke-test'
        cmd /c "`"$($exe.FullName)`" --smoke-test > nul 2>&1"
        Write-Host "    exit=$LASTEXITCODE"
        if ($LASTEXITCODE -ne 0) { throw 'smoke-test failed' }
        Remove-Item (Join-Path $dist '_data') -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not $SkipZip) {
        Write-Host '==> zip folder package'
        Compress-Package -Source $package -Destination (Join-Path $dist 'pixel-workspace-folder.zip')
    }
}

# single-file package (always built)
    $singleOut = Join-Path $dist 'single'
    Write-Host '==> publish single file'
    Invoke-DotNet @('publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', (Join-Path $dist '_single'), '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:IncludeAllContentForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false')
    New-Item -ItemType Directory -Path $singleOut | Out-Null
    Get-ChildItem (Join-Path $dist '_single') -Filter *.exe | ForEach-Object { Copy-Item $_.FullName $singleOut -Force }
    Remove-Item (Join-Path $dist '_single') -Recurse -Force

    if ($Verify) {
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $dist '_extract'
        $env:PIXELWORKSPACE_DATA_DIR = Join-Path $dist '_data'
        $exe = Get-ChildItem $singleOut -Filter *.exe | Select-Object -First 1
        Write-Host '==> single file smoke-test'
        cmd /c "`"$($exe.FullName)`" --smoke-test > nul 2>&1"
        Write-Host "    exit=$LASTEXITCODE"
        if ($LASTEXITCODE -ne 0) { throw 'single file smoke-test failed' }
        Remove-Item (Join-Path $dist '_extract') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $dist '_data') -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not $SkipZip) {
        Write-Host '==> zip single package'
        Compress-Package -Source $singleOut -Destination (Join-Path $dist 'pixel-workspace-single.zip')
    }

Get-ChildItem $dist | Select-Object Name, @{ n = 'SizeMB'; e = { if ($_.PSIsContainer) { '' } else { [math]::Round($_.Length / 1MB, 2) } } } | Format-Table -AutoSize
Write-Host "done: $dist"
