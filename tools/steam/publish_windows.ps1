[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SteamAccount,
    [Parameter(Mandatory = $true)]
    [string]$BetaBranch,
    [string]$GodotExecutable = $env:GODOT_MONO_CONSOLE,
    [string]$SteamworksSdk = $env:STEAMWORKS_SDK,
    [switch]$SkipExport
)

$ErrorActionPreference = "Stop"
$ExportScript = Join-Path $PSScriptRoot "export_windows.ps1"
$UploadScript = Join-Path $PSScriptRoot "upload_windows.ps1"

if (-not $SkipExport) {
    $exportArguments = @{ Release = $true }
    if (-not [string]::IsNullOrWhiteSpace($GodotExecutable)) {
        $exportArguments.GodotExecutable = $GodotExecutable
    }

    & $ExportScript @exportArguments
}

$uploadArguments = @{
    SteamAccount = $SteamAccount
    SetLiveBranch = $BetaBranch
}
if (-not [string]::IsNullOrWhiteSpace($SteamworksSdk)) {
    $uploadArguments.SteamworksSdk = $SteamworksSdk
}

& $UploadScript @uploadArguments
