[CmdletBinding()]
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.exe",
    [ValidateRange(1, 65535)]
    [int]$Port = 7777,
    [string]$HostName = "LocalHost",
    [string]$ClientName = "LocalClient",
    [ValidateRange(1, 7)]
    [int]$ClientCount = 1,
    [ValidateRange(1, 65528)]
    [int]$PredictionPortBase = 7780
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $GodotExecutable"
}

$hostArguments = @(
    "--path", $projectRoot,
    "--",
    "--host",
    "--port=$Port",
    "--name=$HostName",
    "--auto-start-players=$ClientCount"
)

$hostProcess = Start-Process `
    -FilePath $GodotExecutable `
    -ArgumentList $hostArguments `
    -WorkingDirectory $projectRoot `
    -WindowStyle Normal `
    -PassThru

$clientProcesses = @()
for ($index = 1; $index -le $ClientCount; $index++) {
    Start-Sleep -Milliseconds $(if ($index -eq 1) { 1200 } else { 600 })
    $resolvedClientName = if ($ClientCount -eq 1) {
        $ClientName
    } else {
        "$ClientName$index"
    }
    $clientArguments = @(
        "--path", $projectRoot,
        "--",
        "--join=127.0.0.1",
        "--port=$Port",
        "--prediction-port=$($PredictionPortBase + $index - 1)",
        "--name=$resolvedClientName"
    )
    $clientProcesses += Start-Process `
        -FilePath $GodotExecutable `
        -ArgumentList $clientArguments `
        -WorkingDirectory $projectRoot `
        -WindowStyle Normal `
        -PassThru
}

Write-Host "Local playtest launched."
Write-Host "Host PID:   $($hostProcess.Id)"
for ($index = 0; $index -lt $clientProcesses.Count; $index++) {
    Write-Host "Client $($index + 1) PID: $($clientProcesses[$index].Id)"
}
Write-Host "Escape releases the mouse. Click either game window to recapture it."
Write-Host "Prediction ports: $PredictionPortBase-$($PredictionPortBase + $ClientCount - 1)"
Write-Host "F9 toggles remote interpolation on the client; F10 toggles lag compensation on the host."
