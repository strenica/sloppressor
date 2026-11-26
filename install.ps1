[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\VideoSizeCompressor'),
    [switch]$FromPublish,
    [switch]$NoRegister
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$publish = Join-Path $root "artifacts/publish/$Runtime"
$appProject = Join-Path $root 'src/VideoSizeCompressor.App/VideoSizeCompressor.App.csproj'
$cliProject = Join-Path $root 'src/VideoSizeCompressor.Cli/VideoSizeCompressor.Cli.csproj'

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Get-SourceDirectory {
    if ($FromPublish) {
        if (-not (Test-Path (Join-Path $publish 'VideoSizeCompressor.exe'))) {
            throw "No published app at $publish. Run without -FromPublish to build it."
        }
        return $publish
    }
    if (Test-Path $appProject) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'The .NET 10 SDK is required to build from source.'
        }
        if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
        New-Item $publish -ItemType Directory -Force | Out-Null
        # These properties make the script work in a fresh clone even when the
        # repository-level Directory.Build.props is not present.
        $common = @('-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-o', $publish,
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true', '-p:PublishTrimmed=false', '-p:DebugType=None',
            '-p:ImplicitUsings=enable', '-p:Nullable=enable', '-p:LangVersion=14.0')
        Invoke-DotNet (@('publish', $appProject) + $common)
        Invoke-DotNet (@('publish', $cliProject) + $common)
        return $publish
    }
    if (Test-Path (Join-Path $root 'VideoSizeCompressor.exe')) { return $root }
    throw "Neither source projects nor VideoSizeCompressor.exe were found beside $root."
}

if (-not $env:LOCALAPPDATA -and $PSBoundParameters.ContainsKey('Target') -eq $false) {
    throw 'LOCALAPPDATA is not set. Pass -Target with the installation folder.'
}

$running = Get-Process -Name 'VideoSizeCompressor', 'VideoSizeCompressor.Cli' -ErrorAction SilentlyContinue
if ($running) { throw 'Close VideoSizeCompressor and VideoSizeCompressor.Cli before updating.' }

$source = Get-SourceDirectory
$app = Join-Path $source 'VideoSizeCompressor.exe'
$cli = Join-Path $source 'VideoSizeCompressor.Cli.exe'
if (-not (Test-Path $app)) { throw "VideoSizeCompressor.exe was not produced in $source." }

New-Item $Target -ItemType Directory -Force | Out-Null
Copy-Item $app (Join-Path $Target 'VideoSizeCompressor.exe') -Force
if (Test-Path $cli) { Copy-Item $cli (Join-Path $Target 'VideoSizeCompressor.Cli.exe') -Force }

$menu = @(
    (Join-Path $root 'registry/Add-Menu.reg'),
    (Join-Path $root 'Add-Menu.reg'),
    (Join-Path $source 'Add-Menu.reg')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($menu) { Copy-Item $menu $Target -Force }

$remove = @(
    (Join-Path $root 'registry/Remove-Menu.reg'),
    (Join-Path $root 'Remove-Menu.reg'),
    (Join-Path $source 'Remove-Menu.reg')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($remove) { Copy-Item $remove $Target -Force }

$start = @(
    (Join-Path $root 'portable/START-HERE.txt'),
    (Join-Path $root 'START-HERE.txt'),
    (Join-Path $source 'START-HERE.txt')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($start) { Copy-Item $start $Target -Force }

$ffmpeg = @(
    (Join-Path $Target 'ffmpeg/ffmpeg.exe'),
    (Join-Path $Target 'ffmpeg/bin/ffmpeg.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $ffmpeg -and -not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    Write-Warning 'FFmpeg was not found in the target or PATH. Add ffmpeg.exe and ffprobe.exe to the app ffmpeg folder, or choose their folder in the app.'
}

if (-not $NoRegister) {
    $process = Start-Process -FilePath (Join-Path $Target 'VideoSizeCompressor.exe') `
        -ArgumentList 'register --silent' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Explorer registration failed with exit code $($process.ExitCode). Run VideoSizeCompressor.exe register to see the error."
    }
}

Write-Host "Installed: $Target"
Write-Host 'Menu: Compress to 20 MB, Compress to 8 MB, Convert to GIF (High/Balanced/Small/Custom), Custom Size...'
if ($NoRegister) {
    Write-Host 'Explorer registration skipped (-NoRegister).'
} else {
    Write-Host 'Explorer menu registered for this account. Windows 11: Show more options.'
}
