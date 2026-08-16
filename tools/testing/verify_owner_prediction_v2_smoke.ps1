# Exercises the V2 exact-scheduling path end to end on a real two-process match.
#
# The V2 path is behind a feature flag that defaults to Legacy, so every other
# gate in this repository runs straight past it. That is exactly how a method
# left as `throw new NotImplementedException()` inside the V2 frame loop can
# survive a full green parity run: nothing ever calls it. This gate selects V2
# explicitly and asserts the scheduler actually accounted for frames, admitted
# the remote client's commands, and produced owner state.
[CmdletBinding()]
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [ValidateRange(1, 65535)]
    [int]$Port = 7795
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $GodotExecutable"
}

$runId = [Guid]::NewGuid().ToString("N")
$logRoot = [System.IO.Path]::GetTempPath()
$hostOutput = Join-Path $logRoot "battle_arena_v2_host_$runId.log"
$hostError = Join-Path $logRoot "battle_arena_v2_host_$runId.err.log"
$clientOutput = Join-Path $logRoot "battle_arena_v2_client_$runId.log"
$clientError = Join-Path $logRoot "battle_arena_v2_client_$runId.err.log"
$hostProcess = $null
$clientProcess = $null

try {
    $hostArguments = @(
        "--headless", "--max-fps", "60", "--path", $projectRoot, "--",
        "--host", "--port=$Port", "--name=V2Host", "--auto-start",
        "--owner-prediction-v2", "--combat-smoke"
    )
    $clientArguments = @(
        "--headless", "--max-fps", "60", "--path", $projectRoot, "--",
        "--join=127.0.0.1", "--port=$Port", "--name=V2Client", "--combat-smoke"
    )

    $hostProcess = Start-Process -FilePath $GodotExecutable `
        -ArgumentList $hostArguments -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $hostOutput -RedirectStandardError $hostError `
        -WindowStyle Hidden -PassThru

    Start-Sleep -Milliseconds 1200

    $clientProcess = Start-Process -FilePath $GodotExecutable `
        -ArgumentList $clientArguments -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $clientOutput -RedirectStandardError $clientError `
        -WindowStyle Hidden -PassThru

    $clientExited = $clientProcess.WaitForExit(25000)
    $hostExited = $hostProcess.WaitForExit(25000)
    if (-not $clientExited -or -not $hostExited) {
        throw "The V2 exact-scheduling smoke test timed out."
    }

    $hostLog = (Get-Content -LiteralPath $hostOutput,$hostError -Raw -ErrorAction SilentlyContinue) `
        -join [Environment]::NewLine
    $clientLog = (Get-Content -LiteralPath $clientOutput,$clientError -Raw -ErrorAction SilentlyContinue) `
        -join [Environment]::NewLine

    # The host must actually have selected V2, or everything below would be
    # asserting against the Legacy path.
    if ($hostLog -notmatch "(?m)^\[NetworkArena\] Owner prediction mode: FrameRewindV2\r?$") {
        throw "The host did not select the V2 owner-prediction path.`n$hostLog"
    }
    if ($hostLog -notmatch "(?m)^\[NetworkArena\] V2 scheduling host built; combatants (?<combatants>[1-9][0-9]*); ") {
        throw "The V2 scheduling host was never built.`n$hostLog"
    }

    $summaryPattern =
        '(?m)^\[NetworkArena\] V2 scheduling summary; frames (?<frames>-?[0-9]+); ' +
        'combatants (?<combatants>[0-9]+); ' +
        'remote admitted (?<admitted>[0-9]+); ' +
        'remote duplicate (?<duplicate>[0-9]+); ' +
        'remote late (?<late>[0-9]+); ' +
        'remote refused (?<refused>[0-9]+); ' +
        'owner states (?<states>[0-9]+); ' +
        'state backlogs (?<backlogs>[0-9]+); ' +
        'lead updates (?<lead>[0-9]+)\r?$'
    $summary = [regex]::Match($hostLog, $summaryPattern)
    if (-not $summary.Success) {
        throw "The host did not report a V2 scheduling summary.`n$hostLog"
    }

    $frames = [long]$summary.Groups["frames"].Value
    $combatants = [int]$summary.Groups["combatants"].Value
    $admitted = [long]$summary.Groups["admitted"].Value
    $refused = [long]$summary.Groups["refused"].Value
    $states = [long]$summary.Groups["states"].Value

    # Frames really advanced through the scheduler, not just the engine callback.
    if ($frames -lt 30) {
        throw "The V2 scheduler completed only $frames frames; the frame loop is not advancing.`n$hostLog"
    }
    if ($combatants -lt 2) {
        throw "The V2 host scheduled $combatants combatants; the remote client was never registered.`n$hostLog"
    }

    # Both host and client must drive the scheduler. Remote commands reaching it
    # is the half that silently does not happen if ingress is never wired.
    if ($admitted -le 0) {
        throw "No remote owner command reached the V2 scheduler; remote players would resolve from fallback only.`n$hostLog"
    }
    # Duplicates are expected: the client resends its recent history every
    # bundle. Faulted refusals are not, and a healthy link should produce none.
    if ($refused -gt 0) {
        throw "The V2 scheduler faulted on $refused remote commands; a healthy link should produce none.`n$hostLog"
    }
    if ($states -le 0) {
        throw "The V2 host never produced owner scheduling state.`n$hostLog"
    }

    if (($hostLog + $clientLog) -match "Unhandled exception|NotImplementedException|SCRIPT ERROR|E [0-9]+:") {
        throw "Godot reported an error during the V2 smoke test.`n$hostLog`n$clientLog"
    }

    Write-Host ("V2 exact-scheduling smoke passed: $frames frames, $combatants combatants, " +
        "$admitted remote commands admitted, $states owner states built.")
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
