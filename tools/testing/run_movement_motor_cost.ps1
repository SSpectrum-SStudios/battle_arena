<#
.SYNOPSIS
    P5B-03: measures the explicit motor's real per-frame query cost in-engine.

.DESCRIPTION
    P01-11 measured a single query at about 10.8 microseconds. The motor issues
    several per frame — overlap recovery, the initial sweep, a re-sweep per slide
    iteration, a ground probe, and three more when a step is solved — and replay
    multiplies that by history depth.

    Reports queries and microseconds per frame at replay depths of 1, 8, and 32
    over open ground and over a corner with a climbable lip, and fails when the
    95th percentile frame exceeds a quarter of a 60 Hz frame at or below the
    supported replay depth. The percentile rather than the single worst frame,
    because the worst frame in a headless probe is dominated by collection pauses
    rather than query cost; mean and true worst are both still reported. Depths
    above the supported cap are measured and reported but not gated.

    This is what turns Phase 6's replay-depth cap from a guess into a number, and
    it feeds P11-04.
#>
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 300,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_godot_probe_common.ps1")

Invoke-GodotProbe `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path `
    -Scene "res://scenes/movement/movement_motor_cost_probe.tscn" `
    -Label "Movement motor cost probe" `
    -SuccessMarker "[MotorCostProbe] PASSED:" `
    -TimeoutSeconds $TimeoutSeconds `
    -SkipBuild:$SkipBuild

Write-Output "Movement motor cost gate passed."
