[CmdletBinding()]
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [switch]$SkipParity,
    [switch]$SkipExport
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$expectedAppId = "3820160"
$expectedDepotId = "3820161"
$stagingRoot = Join-Path $projectRoot "build\steam\windows"
$executable = Join-Path $stagingRoot "BattleArena.exe"
$steamApi = Join-Path $stagingRoot "steam_api64.dll"
$developmentAppId = Join-Path $stagingRoot "steam_appid.txt"
$appBuild = Join-Path $projectRoot "steam\app_build_3820160.vdf"
$depotBuild = Join-Path $projectRoot "steam\depot_build_3820161.vdf"

if (-not $SkipParity) {
    & (Join-Path $PSScriptRoot "verify_multiplayer_parity.ps1") `
        -GodotExecutable $GodotExecutable
    if ($LASTEXITCODE -ne 0) {
        throw "The multiplayer parity gate failed."
    }
}

if (-not $SkipExport) {
    & (Join-Path $projectRoot "tools\steam\export_windows.ps1") `
        -GodotExecutable $GodotExecutable -Release
    if ($LASTEXITCODE -ne 0) {
        throw "The Steam Windows release export failed."
    }
}

foreach ($requiredFile in @($executable, $steamApi, $developmentAppId, $appBuild, $depotBuild)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Steam readiness file is missing: $requiredFile"
    }
}

if ((Get-Item -LiteralPath $executable).Length -lt 1MB) {
    throw "The staged BattleArena.exe is unexpectedly small."
}
if ((Get-Item -LiteralPath $steamApi).Length -lt 64KB) {
    throw "The staged steam_api64.dll is unexpectedly small."
}

$header = [IO.File]::ReadAllBytes($executable)[0..1]
if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
    throw "The staged executable does not have a Windows PE header."
}

$stagedAppId = (Get-Content -LiteralPath $developmentAppId -Raw).Trim()
if ($stagedAppId -ne $expectedAppId) {
    throw "steam_appid.txt contains '$stagedAppId'; expected '$expectedAppId'."
}

$appConfiguration = Get-Content -LiteralPath $appBuild -Raw
$depotConfiguration = Get-Content -LiteralPath $depotBuild -Raw
if ($appConfiguration -notmatch ('"AppID"\s+"' + $expectedAppId + '"') -or
    $appConfiguration -notmatch ('"' + $expectedDepotId + '"\s+"depot_build_' + $expectedDepotId + '\.vdf"')) {
    throw "The Steam AppBuild does not reference the expected app and depot IDs."
}
if ($depotConfiguration -notmatch ('"DepotID"\s+"' + $expectedDepotId + '"') -or
    $depotConfiguration -notmatch '"FileExclusion"\s+"steam_appid\.txt"') {
    throw "The Steam depot build is missing its ID or steam_appid.txt exclusion."
}

$contentRootMatch = [regex]::Match(
    $appConfiguration,
    '"ContentRoot"\s+"([^"]+)"')
if (-not $contentRootMatch.Success) {
    throw "The Steam AppBuild has no ContentRoot."
}
$configuredContentRoot = [IO.Path]::GetFullPath(
    (Join-Path (Split-Path -Parent $appBuild) $contentRootMatch.Groups[1].Value))
if ($configuredContentRoot.TrimEnd('\') -ne $stagingRoot.TrimEnd('\')) {
    throw "Steam ContentRoot resolves to '$configuredContentRoot', expected '$stagingRoot'."
}

$unexpectedSecrets = Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | Where-Object {
    $_.Name -match '(?i)secret|credential|password|\.local\.vdf$'
}
if ($unexpectedSecrets.Count -gt 0) {
    throw "Potential secret material was found in the staged depot: $($unexpectedSecrets.Name -join ', ')"
}

Write-Host "Steam Windows readiness passed: parity, release export, PE payload, Steam runtime, AppID $expectedAppId, DepotID $expectedDepotId, content root, and secret scan."
Write-Host "This verifier never logs in or uploads. Run tools\steam\publish_windows.ps1 interactively when ready."
