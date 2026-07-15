[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamAccount,
    [string]$SteamworksSdk = $env:STEAMWORKS_SDK
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$BuildScript = Join-Path $ProjectRoot "steam\app_build_3820160.vdf"
$ContentExecutable = Join-Path $ProjectRoot "build\steam\windows\BattleArena.exe"

if ([string]::IsNullOrWhiteSpace($SteamworksSdk)) {
    $SteamworksSdk = "C:\Users\Robert\Downloads\steamworks_sdk_162\sdk"
}

$SteamCmd = Join-Path $SteamworksSdk "tools\ContentBuilder\builder\steamcmd.exe"
if (-not (Test-Path -LiteralPath $SteamCmd -PathType Leaf)) {
    throw "SteamCMD was not found at '$SteamCmd'. Pass -SteamworksSdk or set STEAMWORKS_SDK."
}

if (-not (Test-Path -LiteralPath $ContentExecutable -PathType Leaf)) {
    throw "No staged Windows build was found. Run tools\steam\export_windows.ps1 first."
}

Write-Host "SteamCMD will request your password and Steam Guard code interactively when required."
Write-Host "No credentials are stored by this script."
& $SteamCmd +login $SteamAccount +run_app_build $BuildScript +quit
if ($LASTEXITCODE -ne 0) {
    throw "SteamPipe upload failed with exit code $LASTEXITCODE."
}

Write-Host "SteamPipe upload completed. Assign the resulting build to a test branch in Steamworks before testing."
