[CmdletBinding()]
param(
    [string]$SteamworksSdk = $env:STEAMWORKS_SDK
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($SteamworksSdk)) {
    $SteamworksSdk = "C:\Users\Robert\Downloads\steamworks_sdk_162\sdk"
}

$SteamCmd = Join-Path $SteamworksSdk "tools\ContentBuilder\builder\steamcmd.exe"
if (-not (Test-Path -LiteralPath $SteamCmd -PathType Leaf)) {
    throw "SteamCMD was not found at '$SteamCmd'. Pass -SteamworksSdk or set STEAMWORKS_SDK."
}

Write-Host "At the Steam> prompt, type: login YOUR_BUILD_ACCOUNT"
Write-Host "Enter the password and Steam Guard code only when SteamCMD asks for them."
Write-Host "Type quit after the login succeeds."
& $SteamCmd
