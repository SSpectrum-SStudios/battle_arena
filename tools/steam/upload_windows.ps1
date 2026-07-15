[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamAccount,
    [string]$SteamworksSdk = $env:STEAMWORKS_SDK,
    [string]$SetLiveBranch
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$BuildScript = Join-Path $ProjectRoot "steam\app_build_3820160.vdf"
$EffectiveBuildScript = $BuildScript
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

if (-not [string]::IsNullOrWhiteSpace($SetLiveBranch)) {
    if ($SetLiveBranch -notmatch '^[A-Za-z0-9_-]+$') {
        throw "The beta branch may contain only letters, numbers, underscores, and hyphens."
    }

    $EffectiveBuildScript = Join-Path $ProjectRoot "steam\app_build_3820160.local.vdf"
    $buildConfiguration = Get-Content -LiteralPath $BuildScript -Raw
    $buildConfiguration = $buildConfiguration.Replace(
        '    "Preview" "0"',
        "    `"Preview`" `"0`"`r`n    `"SetLive`" `"$SetLiveBranch`"")
    Set-Content -LiteralPath $EffectiveBuildScript -Value $buildConfiguration -NoNewline
}

try {
    Write-Host "SteamCMD will request your password and Steam Guard code interactively when required."
    Write-Host "No credentials are stored by this script or passed on the command line."
    & $SteamCmd +login $SteamAccount +run_app_build $EffectiveBuildScript +quit
    $steamCmdExitCode = $LASTEXITCODE
}
finally {
    if ($EffectiveBuildScript -ne $BuildScript) {
        Remove-Item -LiteralPath $EffectiveBuildScript -Force -ErrorAction SilentlyContinue
    }
}

if ($steamCmdExitCode -ne 0) {
    throw "SteamPipe upload failed with exit code $steamCmdExitCode."
}

if ([string]::IsNullOrWhiteSpace($SetLiveBranch)) {
    Write-Host "SteamPipe upload completed. Assign the resulting build to a test branch in Steamworks."
}
else {
    Write-Host "SteamPipe upload completed and was assigned to beta branch '$SetLiveBranch'."
}
