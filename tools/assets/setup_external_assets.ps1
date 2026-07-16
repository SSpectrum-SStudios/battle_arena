[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ManifestPath = Join-Path $PSScriptRoot "external_assets.json"
$StagingRoot = Join-Path $ProjectRoot "asset_staging"
$DownloadsRoot = Join-Path $StagingRoot "downloads"
$PackagesRoot = Join-Path $StagingRoot "packages"

function Assert-PathWithinStaging {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullStagingRoot = [IO.Path]::GetFullPath($StagingRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullStagingRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify '$fullPath' because it is outside '$StagingRoot'."
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "External asset manifest was not found at '$ManifestPath'."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $null -eq $manifest.sources) {
    throw "External asset manifest schema is unsupported or invalid."
}

New-Item -ItemType Directory -Force -Path $DownloadsRoot, $PackagesRoot | Out-Null
# Git ignores the staging tree, and Godot must ignore it as well. Otherwise the
# editor imports every unused source asset merely because it sits under res://.
Set-Content -LiteralPath (Join-Path $StagingRoot ".gdignore") -Value "" -Encoding ASCII

foreach ($source in $manifest.sources) {
    $archivePath = Join-Path $DownloadsRoot $source.archiveFileName
    $packagePath = Join-Path $PackagesRoot $source.id
    $temporaryPath = Join-Path $StagingRoot ("extracting-" + $source.id)
    Assert-PathWithinStaging $archivePath
    Assert-PathWithinStaging $packagePath
    Assert-PathWithinStaging $temporaryPath

    if ($Force -and (Test-Path -LiteralPath $archivePath)) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Write-Host "Downloading $($source.displayName)..."
        Invoke-WebRequest -UseBasicParsing -Uri $source.downloadUrl -OutFile $archivePath
    }

    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $source.sha256) {
        Remove-Item -LiteralPath $archivePath -Force
        throw "Checksum validation failed for '$($source.displayName)'. Expected $($source.sha256), received $actualHash. The invalid archive was removed."
    }

    if ($Force -and (Test-Path -LiteralPath $packagePath)) {
        Remove-Item -LiteralPath $packagePath -Recurse -Force
    }

    if (Test-Path -LiteralPath $packagePath -PathType Container) {
        Write-Host "$($source.displayName) is already prepared."
        continue
    }

    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $temporaryPath | Out-Null
    try {
        Write-Host "Extracting $($source.displayName)..."
        Expand-Archive -LiteralPath $archivePath -DestinationPath $temporaryPath -Force
        $archiveRoot = Join-Path $temporaryPath $source.archiveRoot
        if (-not (Test-Path -LiteralPath $archiveRoot -PathType Container)) {
            throw "Archive '$archivePath' did not contain the expected root '$($source.archiveRoot)'."
        }

        Move-Item -LiteralPath $archiveRoot -Destination $packagePath
        [pscustomobject]@{
            id = $source.id
            version = $source.version
            sourcePage = $source.sourcePage
            sha256 = $source.sha256
            license = $source.license
            preparedAtUtc = [DateTime]::UtcNow.ToString("O")
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packagePath ".asset-source.json") -Encoding UTF8
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force
        }
    }

    Write-Host "$($source.displayName) prepared at $packagePath"
}

Write-Host "External asset setup complete. Staged assets are intentionally ignored by Git."
