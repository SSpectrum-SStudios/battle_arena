<#
.SYNOPSIS
    P5B-02: proves the explicit motor matches the legacy motor's accepted feel.

.DESCRIPTION
    Runs both motors over the real arena course headlessly and compares the
    quantities a player perceives — top speed, acceleration, braking, jump apex
    and airtime, stair climb, crouch travel, roll distance, obstacle slide — plus
    per-frame position and velocity over a short shared-spawn window.

    It deliberately does NOT assert per-frame position parity across the whole
    trace. Both motors are closed loops over different collision algorithms, so
    divergence compounds and any tolerance wide enough to pass a long trace is
    wide enough to hide a real regression. See P5B-02 in
    docs/LOCAL_OWNER_PREDICTION_IMPLEMENTATION_CHECKLIST.md for the reasoning.

    Scope: the trace contains no attack, because attack movement influence is
    unauthored until P05-18. A pass means "the motors agree with no attack step
    active".
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
    -Scene "res://scenes/movement/movement_motor_parity_probe.tscn" `
    -Label "Movement motor parity probe" `
    -SuccessMarker "[MotorParityProbe] PASSED:" `
    -TimeoutSeconds $TimeoutSeconds `
    -SkipBuild:$SkipBuild

Write-Output "Movement motor parity gate passed."
