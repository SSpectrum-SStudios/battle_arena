<#
.SYNOPSIS
    P5B-01: runs the in-engine verification of GodotKinematicCollisionWorld.

.DESCRIPTION
    The motor's ~200 unit tests all run against DeterministicCollisionWorld, so
    they say nothing about whether the Godot adapter behaves the way the motor
    assumes. This gate closes that hole: it drives the real CapsuleMovementSimulator
    against the real PhysicsServer3D, headlessly, and fails the build when the
    engine and the reference world disagree about behaviour.

    Building "Battle Arena.csproj" is what keeps the probe honest — it rebuilds
    BattleArena.Core as a project reference, so the probe cannot silently run
    against a stale motor DLL and report a fix that is not in the binary.
#>
param(
    [string]$GodotExecutable =
        "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe",
    [ValidateRange(10, 300)]
    [int]$TimeoutSeconds = 180,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$scene = "res://scenes/movement/kinematic_collision_world_probe.tscn"

function Stop-ProcessTree {
    param([int]$ProcessId)

    $children = @(Get-CimInstance Win32_Process `
        -Filter "ParentProcessId = $ProcessId" `
        -ErrorAction SilentlyContinue)
    foreach ($child in $children) {
        Stop-ProcessTree -ProcessId $child.ProcessId
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Invoke-BoundedProcess {
    param(
        [string]$Executable,
        [string]$Arguments,
        [string]$Label
    )

    $processInfo = [System.Diagnostics.ProcessStartInfo]::new($Executable, $Arguments)
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $processInfo
    $started = $false
    try {
        if (-not $process.Start()) {
            throw "$Label failed to start."
        }

        $started = $true
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-ProcessTree -ProcessId $process.Id
            $process.WaitForExit()
            Write-Output ($standardOutput.Result + $standardError.Result)
            throw "$Label exceeded the $TimeoutSeconds second process timeout."
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $standardOutput.Result + $standardError.Result
        }
    }
    finally {
        if ($started -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id
            $process.WaitForExit()
        }

        $process.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $GodotExecutable"
}

if (-not $SkipBuild) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $project = Join-Path $projectRoot "Battle Arena.csproj"
    $build = Invoke-BoundedProcess `
        -Executable $dotnet `
        -Arguments "build `"$project`" --no-restore" `
        -Label "Godot C# build"
    Write-Output $build.Output
    if ($build.ExitCode -ne 0) {
        throw "Godot C# build failed with exit code $($build.ExitCode)."
    }
}

$arguments = "--headless --quit-after 900 --path `"$projectRoot`" `"$scene`""
$probe = Invoke-BoundedProcess `
    -Executable $GodotExecutable `
    -Arguments $arguments `
    -Label "Collision world adapter probe"
Write-Output $probe.Output

if ($probe.ExitCode -ne 0) {
    throw "Collision world adapter probe failed with Godot exit code $($probe.ExitCode)."
}

# The probe exits zero on success, but a probe that queried an empty physics
# space would also exit zero by finding nothing. Requiring the explicit marker
# means a vacuous pass fails the gate.
if ($probe.Output -notmatch "\[CollisionWorldProbe\] PASSED:") {
    throw "Collision world adapter probe exited successfully without its PASSED marker."
}

$fatalDiagnostics =
    "(?im)^(SCRIPT ERROR|ERROR):|ObjectDB instances leaked|" +
    "RID allocations leaked|resources still in use|orphan StringName"
if ($probe.Output -match $fatalDiagnostics) {
    throw "Collision world adapter probe emitted a script, native, or resource-leak error."
}

Write-Output "Collision world adapter gate passed."
