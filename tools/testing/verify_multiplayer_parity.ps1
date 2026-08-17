[CmdletBinding()]
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [ValidateRange(1, 65535)]
    [int]$Port = 7793,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $GodotExecutable"
}

function Invoke-CheckedDotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipBuild) {
    Invoke-CheckedDotnet @("build", "Battle Arena.csproj", "--no-restore")
}

Invoke-CheckedDotnet @(
    "test",
    "tests/BattleArena.Core.Tests/BattleArena.Core.Tests.csproj",
    "--no-restore"
)
Invoke-CheckedDotnet @(
    "test",
    "tests/BattleArena.Multiplayer.Tests/BattleArena.Multiplayer.Tests.csproj",
    "--no-restore"
)

$runId = [Guid]::NewGuid().ToString("N")
$logRoot = [System.IO.Path]::GetTempPath()
$hostOutput = Join-Path $logRoot "battle_arena_parity_host_$runId.log"
$hostError = Join-Path $logRoot "battle_arena_parity_host_$runId.err.log"
$clientOutput = Join-Path $logRoot "battle_arena_parity_client_$runId.log"
$clientError = Join-Path $logRoot "battle_arena_parity_client_$runId.err.log"
$hostProcess = $null
$clientProcess = $null

try {
    $hostArguments = @(
        "--headless",
        "--max-fps", "60",
        "--path", $projectRoot,
        "--",
        "--host",
        "--port=$Port",
        "--name=ParityHost",
        "--auto-start",
        "--remote-commit-phase-smoke",
        "--combat-smoke"
    )
    $clientArguments = @(
        "--headless",
        "--max-fps", "60",
        "--path", $projectRoot,
        "--",
        "--join=127.0.0.1",
        "--port=$Port",
        "--name=ParityClient",
        "--force-prediction-startup-failure",
        "--remote-commit-phase-smoke",
        "--combat-smoke"
    )

    $hostProcess = Start-Process `
        -FilePath $GodotExecutable `
        -ArgumentList $hostArguments `
        -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $hostOutput `
        -RedirectStandardError $hostError `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Milliseconds 1200

    $clientProcess = Start-Process `
        -FilePath $GodotExecutable `
        -ArgumentList $clientArguments `
        -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $clientOutput `
        -RedirectStandardError $clientError `
        -WindowStyle Hidden `
        -PassThru

    $clientExited = $clientProcess.WaitForExit(20000)
    $hostExited = $hostProcess.WaitForExit(20000)
    if (-not $clientExited -or -not $hostExited) {
        throw "The two-process multiplayer smoke test timed out."
    }

    $hostLog = (Get-Content -LiteralPath $hostOutput,$hostError -Raw -ErrorAction SilentlyContinue) `
        -join [Environment]::NewLine
    $clientLog = (Get-Content -LiteralPath $clientOutput,$clientError -Raw -ErrorAction SilentlyContinue) `
        -join [Environment]::NewLine
    if ($hostLog -notmatch "Combat smoke passed") {
        throw "The authority did not report a successful combat smoke test.`n$hostLog"
    }

    if ($clientLog -notmatch "Network timing smoke passed") {
        throw "The client did not receive the 60 Hz movement stream or synchronize authority time.`n$clientLog"
    }

    if ($clientLog -notmatch "Prediction transport unavailable; authority relay only") {
        throw "The optional prediction startup-failure path was not exercised.`n$clientLog"
    }

    $remoteCommitPattern =
        '(?m)^\[NetworkArena\] Remote commit phase smoke passed; ' +
        'physics collision commits (?<physics>[1-9][0-9]*); ' +
        'render visual commits (?<visual>[1-9][0-9]*); ' +
        'visual position changes (?<visualChanges>[1-9][0-9]*); ' +
        'lifecycle commits (?<lifecycle>[1-9][0-9]*); ' +
        'lifecycle stage LifeTwoPoseCommitted; final life 2 alive True; ' +
        'phase violations 0; render body mutations 0\r?$'
    $remoteCommitResults = [regex]::Matches($clientLog, $remoteCommitPattern)
    if ($remoteCommitResults.Count -ne 1) {
        throw "The client did not prove fixed-physics remote collision commits " +
            "and render-only visual commits.`n$clientLog"
    }

    $ownerPredictionModePattern =
        '(?m)^\[NetworkArena\] Owner prediction mode: (?<mode>[^\r\n]+)\r?$'
    $hostModeSelections = [regex]::Matches($hostLog, $ownerPredictionModePattern)
    $clientModeSelections = [regex]::Matches($clientLog, $ownerPredictionModePattern)
    $hostMode = if ($hostModeSelections.Count -eq 1) {
        $hostModeSelections[0].Groups["mode"].Value
    } else {
        $null
    }
    $clientMode = if ($clientModeSelections.Count -eq 1) {
        $clientModeSelections[0].Groups["mode"].Value
    } else {
        $null
    }
    if ($hostModeSelections.Count -ne 1 -or
        $clientModeSelections.Count -ne 1 -or
        $hostMode -cne "Legacy" -or
        $clientMode -cne "Legacy") {
        throw "Host and client must each report exactly one owner-prediction selection " +
            "whose complete value is Legacy. Host diagnostics: $($hostModeSelections.Count); " +
            "host mode: '$hostMode'. Client diagnostics: $($clientModeSelections.Count); " +
            "client mode: '$clientMode'.`n$hostLog`n$clientLog"
    }

    if (($hostLog + $clientLog) -match "Unhandled exception|SCRIPT ERROR|E [0-9]+:") {
        throw "Godot reported an error during the parity smoke test.`n$hostLog`n$clientLog"
    }

    # V2 exact-scheduling smoke. The V2 path is flag-gated and off by default,
    # so without this gate nothing executes it and a stubbed-out method inside it
    # can pass every other check in this file.
    $v2Port = if ($Port -lt 65534) { $Port + 2 } else { $Port - 2 }
    & (Join-Path $PSScriptRoot "verify_owner_prediction_v2_smoke.ps1") `
        -GodotExecutable $GodotExecutable `
        -Port $v2Port

    # In-engine collision adapter gate (P5B-01). The motor's unit tests all run
    # against DeterministicCollisionWorld, which approximates the capsule as a
    # box, so they cannot see engine-boundary defects. This gate found three,
    # including a character permanently stuck on a 0.25 m step while every unit
    # test passed. It runs here so a motor change cannot reach a playtest without
    # being exercised against the real PhysicsServer3D.
    & (Join-Path $PSScriptRoot "run_kinematic_collision_world_probe.ps1") `
        -GodotExecutable $GodotExecutable `
        -SkipBuild

    $threePlayerPort = if ($Port -lt 65535) { $Port + 1 } else { $Port - 1 }
    & (Join-Path $PSScriptRoot "verify_three_player_movement.ps1") `
        -GodotExecutable $GodotExecutable `
        -Port $threePlayerPort `
        -SkipBuild

    Write-Host "Multiplayer parity gate passed: build, core tests, protocol tests, authority-only fallback, V2 exact-scheduling smoke, in-engine collision adapter, three-player movement, prediction, attack, and authoritative damage."
}
finally {
    foreach ($process in @($clientProcess, $hostProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    Remove-Item -LiteralPath `
        $hostOutput,$hostError,$clientOutput,$clientError `
        -ErrorAction SilentlyContinue
}
