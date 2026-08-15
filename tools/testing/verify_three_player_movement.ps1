[CmdletBinding()]
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [ValidateRange(1, 65535)]
    [int]$Port = 7794,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $GodotExecutable"
}

if (-not $SkipBuild) {
    & dotnet build "Battle Arena.csproj" --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "The Godot C# project failed to build with exit code $LASTEXITCODE."
    }
}

$runId = [Guid]::NewGuid().ToString("N")
$logRoot = [System.IO.Path]::GetTempPath()
$logs = @{
    HostOutput = Join-Path $logRoot "battle_arena_three_host_$runId.log"
    HostError = Join-Path $logRoot "battle_arena_three_host_$runId.err.log"
    Client2Output = Join-Path $logRoot "battle_arena_three_client2_$runId.log"
    Client2Error = Join-Path $logRoot "battle_arena_three_client2_$runId.err.log"
    Client3Output = Join-Path $logRoot "battle_arena_three_client3_$runId.log"
    Client3Error = Join-Path $logRoot "battle_arena_three_client3_$runId.err.log"
}
$processes = @()

try {
    $hostArguments = @(
        "--headless", "--max-fps", "60", "--path", $projectRoot, "--",
        "--host", "--port=$Port", "--name=ThreePlayerHost",
        "--auto-start-players=2", "--movement-backlog-smoke"
    )
    $client2Arguments = @(
        "--headless", "--max-fps", "60", "--path", $projectRoot, "--",
        "--join=127.0.0.1", "--port=$Port", "--name=ThreePlayerClient2",
        "--prediction-port=$($Port + 8)",
        "--force-prediction-failure-once",
        "--movement-backlog-smoke"
    )
    $client3Arguments = @(
        "--headless", "--max-fps", "60", "--path", $projectRoot, "--",
        "--join=127.0.0.1", "--port=$Port", "--name=ThreePlayerClient3",
        "--prediction-port=$($Port + 9)",
        "--movement-backlog-smoke"
    )

    $processes += Start-Process -FilePath $GodotExecutable `
        -ArgumentList $hostArguments -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $logs.HostOutput `
        -RedirectStandardError $logs.HostError -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 900
    $processes += Start-Process -FilePath $GodotExecutable `
        -ArgumentList $client2Arguments -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $logs.Client2Output `
        -RedirectStandardError $logs.Client2Error -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 350
    $processes += Start-Process -FilePath $GodotExecutable `
        -ArgumentList $client3Arguments -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $logs.Client3Output `
        -RedirectStandardError $logs.Client3Error -WindowStyle Hidden -PassThru

    foreach ($process in $processes) {
        if (-not $process.WaitForExit(30000)) {
            $timeoutLog = (Get-Content -LiteralPath $logs.Values `
                -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
            throw "The three-process movement smoke test timed out.`n$timeoutLog"
        }
    }

    $hostLog = (Get-Content -LiteralPath $logs.HostOutput,$logs.HostError `
        -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    $client2Log = (Get-Content -LiteralPath $logs.Client2Output,$logs.Client2Error `
        -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    $client3Log = (Get-Content -LiteralPath $logs.Client3Output,$logs.Client3Error `
        -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    $combinedLog = $hostLog + $client2Log + $client3Log

    if ($hostLog -notmatch "Three-player authority movement smoke passed") {
        throw "The authority did not recover from Combatant 3's backlog.`n$hostLog"
    }
    if ($client2Log -notmatch "Three-player observer movement smoke passed") {
        throw "Client 2 did not observe Combatant 3 moving promptly.`n$client2Log"
    }
    if ($client3Log -notmatch "Three-player owner movement smoke passed") {
        throw "Combatant 3 did not retain responsive local prediction.`n$client3Log"
    }
    if ($client2Log -notmatch "PredictionMesh.*Authenticated" -or
        $client3Log -notmatch "PredictionMesh.*Authenticated") {
        throw "The localhost ENet prediction pair did not mutually authenticate.`n$combinedLog"
    }
    if ($client2Log -notmatch "PredictionMesh.*AuthorityFallback") {
        throw "The forced ENet route failure did not enter authority fallback before recovery.`n$client2Log"
    }
    if ($combinedLog -match "Unhandled exception|SCRIPT ERROR|E [0-9]+:") {
        throw "Godot reported an error during the three-player movement smoke test.`n$combinedLog"
    }

    Write-Host "Three-player movement gate passed: owner prediction, authority backlog compaction, and observer presentation."
}
finally {
    foreach ($process in $processes) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }
    Remove-Item -LiteralPath $logs.Values -ErrorAction SilentlyContinue
}
