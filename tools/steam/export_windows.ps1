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
# Steam exports must also work in an offline/restricted build environment. Refresh
# the runtime-specific assets without auditing; ordinary development restores can
# still perform their normal vulnerability audit.
$env:APPDATA = Join-Path $ProjectRoot ".appdata"
$env:LOCALAPPDATA = Join-Path $ProjectRoot ".localappdata"
$env:NUGET_PACKAGES = Join-Path $ProjectRoot ".nuget\packages"
$env:NuGetAudit = "false"
$godotBuildRoot = Split-Path -Parent (Split-Path -Parent $GodotExecutable)
$customGodotPackages = Join-Path $godotBuildRoot "MyLocalNugetSource"
if (-not (Test-Path -LiteralPath $customGodotPackages -PathType Container)) {
    throw "Custom Godot NuGet packages were not found at '$customGodotPackages'. Build the GodotSharp packages for the custom Steam-enabled Godot build before exporting."
}

# The official GodotSharp package has the same version as this custom package.
# Remove only Godot's workspace-local cached packages so NuGet cannot silently
# reuse the official assembly, which lacks the custom Steam native wrappers.
$godotPackageNames = @("godot.net.sdk", "godotsharp", "godotsharpeditor", "godot.sourcegenerators")
foreach ($packageName in $godotPackageNames) {
    $packagePath = Join-Path $env:NUGET_PACKAGES $packageName
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Recurse -Force
    }
}

$projectFile = Join-Path $ProjectRoot "Battle Arena.csproj"
& dotnet restore $projectFile -r win-x64 -p:NuGetAudit=false --ignore-failed-sources --force-evaluate
if ($LASTEXITCODE -ne 0) {
    throw "The .NET restore required for the Windows export failed with exit code $LASTEXITCODE."
}

$resolvedGodotSharp = Join-Path $env:NUGET_PACKAGES "godotsharp\4.4.0\lib\net8.0\GodotSharp.dll"
if (-not (Test-Path -LiteralPath $resolvedGodotSharp -PathType Leaf) -or
    -not [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($resolvedGodotSharp)).Contains("SteamInstance")) {
    throw "The resolved GodotSharp package does not contain the custom Steam wrapper. Refusing to export an incompatible native/managed build."
}

$arguments = "--headless --path `"$ProjectRoot`" $exportMode `"$Preset`" `"$OutputExecutable`""
$processInfo = [System.Diagnostics.ProcessStartInfo]::new($GodotExecutable, $arguments)
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$godotProcess = [System.Diagnostics.Process]::Start($processInfo)
$deadline = [DateTime]::UtcNow.AddMinutes(5)
$completedSuccessfully = $false
$exportFailed = $false
$outputRead = $godotProcess.StandardOutput.ReadLineAsync()
$errorRead = $godotProcess.StandardError.ReadLineAsync()

while (-not $godotProcess.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    if ($null -ne $outputRead -and $outputRead.IsCompleted) {
        $line = $outputRead.GetAwaiter().GetResult()
        if ($null -eq $line) {
            $outputRead = $null
        }
        else {
            Write-Host $line
            if ($line -match 'Export \.NET Project: Failed|Failed to build project') {
                $exportFailed = $true
            }
            if ($line -match '^savepack: end') {
                $completedSuccessfully = $true
            }
            $outputRead = $godotProcess.StandardOutput.ReadLineAsync()
        }
    }

    if ($null -ne $errorRead -and $errorRead.IsCompleted) {
        $line = $errorRead.GetAwaiter().GetResult()
        if ($null -eq $line) {
            $errorRead = $null
        }
        else {
            Write-Warning $line
            if ($line -match 'Export \.NET Project: Failed|Failed to build project') {
                $exportFailed = $true
            }
            $errorRead = $godotProcess.StandardError.ReadLineAsync()
        }
    }

    if ($completedSuccessfully) {
        Start-Sleep -Seconds 2
        if (-not $godotProcess.HasExited) {
            # This custom Godot editor sometimes hangs while tearing down resources
            # after savepack has explicitly completed.
            $godotProcess.Kill()
            $godotProcess.WaitForExit()
            Write-Warning "Godot export completed, but the editor was stopped after its known shutdown hang."
        }
        break
    }

    Start-Sleep -Milliseconds 50
}

if (-not $godotProcess.HasExited) {
    $godotProcess.Kill()
    throw "Godot Windows export did not complete within five minutes."
}

if (-not (Test-Path -LiteralPath $OutputExecutable -PathType Leaf)) {
    throw "Godot Windows export failed with exit code $($godotProcess.ExitCode)."
}

if (-not $completedSuccessfully -and $godotProcess.ExitCode -ne 0) {
    throw "Godot Windows export failed with exit code $($godotProcess.ExitCode)."
}

if ($exportFailed) {
    Remove-Item -LiteralPath $OutputExecutable -Force -ErrorAction SilentlyContinue
    throw "Godot Windows export failed during .NET publishing."
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
