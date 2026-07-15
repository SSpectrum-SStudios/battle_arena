[CmdletBinding()]
param(
    [string]$GodotExecutable = $env:GODOT_MONO_CONSOLE,
    [string]$Preset = "Steam Windows",
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$OutputDirectory = Join-Path $ProjectRoot "build\steam\windows"
$OutputExecutable = Join-Path $OutputDirectory "BattleArena.exe"

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    $GodotExecutable = "D:\Godot_Building_Src\godot\bin\godot.windows.editor.x86_64.mono.console.exe"
}

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable was not found at '$GodotExecutable'. Pass -GodotExecutable or set GODOT_MONO_CONSOLE."
}

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "export_presets.cfg") -PathType Leaf)) {
    throw "Godot export_presets.cfg is missing. Create a Windows Desktop preset named '$Preset' in the editor."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exportMode = if ($Release) { "--export-release" } else { "--export-debug" }
Remove-Item -LiteralPath $OutputExecutable -Force -ErrorAction SilentlyContinue

$arguments = "--headless --path `"$ProjectRoot`" $exportMode `"$Preset`" `"$OutputExecutable`""
$processInfo = [System.Diagnostics.ProcessStartInfo]::new($GodotExecutable, $arguments)
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$godotProcess = [System.Diagnostics.Process]::Start($processInfo)
$deadline = [DateTime]::UtcNow.AddMinutes(5)
$lastLength = -1L
$stableSince = [DateTime]::UtcNow

while (-not $godotProcess.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $OutputExecutable -PathType Leaf) {
        $length = (Get-Item -LiteralPath $OutputExecutable).Length
        if ($length -ne $lastLength) {
            $lastLength = $length
            $stableSince = [DateTime]::UtcNow
        }
        elseif ($length -gt 0 -and ([DateTime]::UtcNow - $stableSince).TotalSeconds -ge 10) {
            # This custom Godot build sometimes completes the file and then hangs
            # while tearing down editor resources. The stable output is complete.
            $godotProcess.Kill()
            $godotProcess.WaitForExit()
            Write-Warning "Godot export completed, but the editor was stopped after its known shutdown hang."
            break
        }
    }
}

if (-not $godotProcess.HasExited) {
    $godotProcess.Kill()
    throw "Godot Windows export did not complete within five minutes."
}

if (-not (Test-Path -LiteralPath $OutputExecutable -PathType Leaf)) {
    throw "Godot Windows export failed with exit code $($godotProcess.ExitCode)."
}

$SteamApiSource = Join-Path (Split-Path -Parent $GodotExecutable) "steam_api64.dll"
$SteamApiDestination = Join-Path $OutputDirectory "steam_api64.dll"
if (-not (Test-Path -LiteralPath $SteamApiDestination -PathType Leaf)) {
    if (-not (Test-Path -LiteralPath $SteamApiSource -PathType Leaf)) {
        throw "steam_api64.dll was not exported and was not found beside the Godot executable."
    }

    Copy-Item -LiteralPath $SteamApiSource -Destination $SteamApiDestination
}

# Useful only for launching the exported executable directly during development.
# The depot configuration explicitly excludes this file from Steam uploads.
Set-Content -LiteralPath (Join-Path $OutputDirectory "steam_appid.txt") -Value "3820160" -NoNewline
Write-Host "Steam Windows build staged at $OutputDirectory"
