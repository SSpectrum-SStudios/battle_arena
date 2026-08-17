<#
.SYNOPSIS
    Shared plumbing for running a headless Godot probe scene as a build gate.

.DESCRIPTION
    Dot-source this from a per-probe runner. It exists because every probe runner
    needs the same three things and getting any of them subtly wrong makes a gate
    that cannot fail:

      * a hard process timeout with the tree killed on expiry, so a probe that
        hangs fails instead of blocking a build forever;
      * stdout AND stderr captured, since probes report failures through
        GD.PrintErr;
      * a required success marker in the output, so a probe that exits zero
        without running its cases — an empty physics space, a scene that failed
        to load — still fails.

    Existing runners predate this and keep their own copies; they are left alone
    rather than refactored under an unrelated change.
#>

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
        [string]$Label,
        [int]$TimeoutSeconds
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

<#
.SYNOPSIS
    Builds the Godot C# project, runs one probe scene headlessly, and enforces
    both its exit code and its success marker.
#>
function Invoke-GodotProbe {
    param(
        [Parameter(Mandatory = $true)][string]$GodotExecutable,
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Scene,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$SuccessMarker,
        [int]$TimeoutSeconds = 180,
        [int]$QuitAfterFrames = 900,
        [switch]$SkipBuild
    )

    if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
        throw "Godot executable was not found: $GodotExecutable"
    }

    # Building the Godot project rebuilds BattleArena.Core as a project
    # reference, so a probe can never silently run against a stale motor DLL and
    # report a fix that is not in the binary.
    if (-not $SkipBuild) {
        $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
        $project = Join-Path $ProjectRoot "Battle Arena.csproj"
        $build = Invoke-BoundedProcess `
            -Executable $dotnet `
            -Arguments "build `"$project`" --no-restore" `
            -Label "Godot C# build" `
            -TimeoutSeconds $TimeoutSeconds
        Write-Output $build.Output
        if ($build.ExitCode -ne 0) {
            throw "Godot C# build failed with exit code $($build.ExitCode)."
        }
    }

    $arguments =
        "--headless --quit-after $QuitAfterFrames --path `"$ProjectRoot`" `"$Scene`""
    $probe = Invoke-BoundedProcess `
        -Executable $GodotExecutable `
        -Arguments $arguments `
        -Label $Label `
        -TimeoutSeconds $TimeoutSeconds
    Write-Output $probe.Output

    if ($probe.ExitCode -ne 0) {
        throw "$Label failed with Godot exit code $($probe.ExitCode)."
    }

    if ($probe.Output -notmatch [regex]::Escape($SuccessMarker)) {
        throw "$Label exited successfully without its '$SuccessMarker' marker, so it did not " +
            "actually run its cases."
    }

    $fatalDiagnostics =
        "(?im)^(SCRIPT ERROR|ERROR):|ObjectDB instances leaked|" +
        "RID allocations leaked|resources still in use|orphan StringName"
    if ($probe.Output -match $fatalDiagnostics) {
        throw "$Label emitted a script, native, or resource-leak error."
    }
}
