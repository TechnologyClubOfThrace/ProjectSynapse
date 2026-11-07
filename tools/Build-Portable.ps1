[CmdletBinding()]
param(
    [string]$PackageName = "Synapse-v2-portable-win-x64",
    [string]$AssetsRoot = "local-data"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot "src\Synapse.App\Synapse.App.csproj"
$resolvedAssetsRoot = if ([System.IO.Path]::IsPathRooted($AssetsRoot)) {
    [System.IO.Path]::GetFullPath($AssetsRoot)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $AssetsRoot))
}
$sourceCompositions = Join-Path $resolvedAssetsRoot "compositions"
$sourceCalibration = Join-Path $resolvedAssetsRoot "calibrations\active-calibration.json"
$templateRoot = Join-Path $PSScriptRoot "portable"
$portableRoot = Join-Path $projectRoot "artifacts\portable"
$packageRoot = Join-Path $portableRoot $PackageName
$zipPath = Join-Path $portableRoot ($PackageName + ".zip")
$zipChecksumPath = $zipPath + ".sha256.txt"

if (Test-Path -LiteralPath $packageRoot) {
    throw "Portable output already exists: $packageRoot"
}
if (Test-Path -LiteralPath $zipPath) {
    throw "Portable ZIP already exists: $zipPath"
}
if (-not (Test-Path -LiteralPath $sourceCompositions)) {
    throw "Composition library was not found: $sourceCompositions"
}
if (-not (Test-Path -LiteralPath $sourceCalibration)) {
    throw "Active calibration was not found: $sourceCalibration"
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

& dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$destinationCompositions = Join-Path $packageRoot "compositions"
$destinationCalibrations = Join-Path $packageRoot "data\calibrations"
$destinationRuntime = Join-Path $packageRoot "data\runtime"
New-Item -ItemType Directory -Path $destinationCompositions -Force | Out-Null
New-Item -ItemType Directory -Path $destinationCalibrations -Force | Out-Null
New-Item -ItemType Directory -Path $destinationRuntime -Force | Out-Null

Copy-Item -Path (Join-Path $sourceCompositions "*") `
    -Destination $destinationCompositions -Recurse
Copy-Item -LiteralPath $sourceCalibration `
    -Destination (Join-Path $destinationCalibrations "active-calibration.json")
Copy-Item -LiteralPath (Join-Path $templateRoot "START_SYNAPSE.bat") `
    -Destination (Join-Path $packageRoot "START_SYNAPSE.bat")
Copy-Item -LiteralPath (Join-Path $templateRoot "README_PORTABLE.txt") `
    -Destination (Join-Path $packageRoot "README_PORTABLE.txt")

$hashManifestPath = Join-Path $packageRoot "FILE_HASHES_SHA256.txt"
$packageRootPrefix = $packageRoot.TrimEnd('\') + '\'
$hashLines = Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
    Where-Object { $_.FullName -ne $hashManifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($packageRootPrefix.Length)
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash  $relativePath"
    }
Set-Content -LiteralPath $hashManifestPath -Value $hashLines -Encoding UTF8

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath $zipChecksumPath `
    -Value "$zipHash  $([System.IO.Path]::GetFileName($zipPath))" -Encoding ASCII

Write-Output "PORTABLE_FOLDER=$packageRoot"
Write-Output "PORTABLE_ZIP=$zipPath"
Write-Output "PORTABLE_ZIP_SHA256=$zipHash"
