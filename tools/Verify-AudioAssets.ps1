[CmdletBinding()]
param(
    [Parameter()]
    [string]$SampleDirectory,

    [Parameter()]
    [string]$ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $SampleDirectory) {
    $SampleDirectory = Join-Path $PSScriptRoot "..\local-data\compositions\01_tree_installation"
}

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $SampleDirectory "manifest.json"
}

function Get-WavMetadata {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $reader = [System.IO.BinaryReader]::new($stream)

    try {
        $riff = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        [void]$reader.ReadUInt32()
        $wave = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))

        if ($riff -ne "RIFF" -or $wave -ne "WAVE") {
            throw "The file is not a valid RIFF/WAVE file."
        }

        $format = $null
        $dataSize = $null

        while ($stream.Position + 8 -le $stream.Length) {
            $chunkId = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
            $chunkSize = $reader.ReadUInt32()
            $nextPosition = $stream.Position + $chunkSize + ($chunkSize % 2)

            if ($chunkId -eq "fmt ") {
                $format = [pscustomobject]@{
                    Encoding      = $reader.ReadUInt16()
                    Channels      = $reader.ReadUInt16()
                    SampleRate    = $reader.ReadUInt32()
                    ByteRate      = $reader.ReadUInt32()
                    BlockAlign    = $reader.ReadUInt16()
                    BitsPerSample = $reader.ReadUInt16()
                }
            }
            elseif ($chunkId -eq "data") {
                $dataSize = [uint32]$chunkSize
            }

            $stream.Position = [Math]::Min($nextPosition, $stream.Length)

            if ($null -ne $format -and $null -ne $dataSize) {
                break
            }
        }

        if ($null -eq $format -or $null -eq $dataSize) {
            throw "The fmt or data chunk is missing."
        }

        [pscustomobject]@{
            Encoding        = [int]$format.Encoding
            Channels        = [int]$format.Channels
            SampleRate      = [int]$format.SampleRate
            BitsPerSample   = [int]$format.BitsPerSample
            DurationSeconds = [double]$dataSize / [double]$format.ByteRate
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SampleDirectory -PathType Container)) {
    throw "The sample directory was not found: $SampleDirectory"
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "The manifest was not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
$layers = @($manifest.layers)

if ($layers.Count -ne 5) {
    throw "The manifest must contain exactly 5 layers. Found: $($layers.Count)"
}

$expectedLevels = 1..5
$actualLevels = @($layers | ForEach-Object { [int]$_.level } | Sort-Object)
if (($actualLevels -join ",") -ne ($expectedLevels -join ",")) {
    throw "Layer levels must be exactly 1,2,3,4,5. Found: $($actualLevels -join ',')"
}

$declaredFiles = @($layers | ForEach-Object { [string]$_.file })
$referenceFile = [string]$manifest.reference.file
$allDeclaredFiles = @($declaredFiles + $referenceFile)
$actualWavFiles = @(Get-ChildItem -LiteralPath $SampleDirectory -Filter "*.wav" -File | ForEach-Object { $_.Name })
$undeclaredFiles = @($actualWavFiles | Where-Object { $_ -notin $allDeclaredFiles })

if ($undeclaredFiles.Count -gt 0) {
    throw "WAV files not declared in the manifest were found: $($undeclaredFiles -join ', ')"
}

$expectedFormat = $manifest.audioFormat
$tolerance = [double]$expectedFormat.durationToleranceSeconds
$results = @()

foreach ($layer in $layers) {
    $filePath = Join-Path $SampleDirectory ([string]$layer.file)
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Layer $($layer.level) is missing: $($layer.file)"
    }

    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    if ($hash -ne [string]$layer.sha256) {
        throw "SHA-256 verification failed for layer $($layer.level): $($layer.file)"
    }

    $metadata = Get-WavMetadata -Path $filePath
    if ($metadata.Encoding -ne 1) {
        throw "Layer $($layer.level) is not PCM: $($layer.file)"
    }
    if ($metadata.Channels -ne [int]$expectedFormat.channels -or
        $metadata.SampleRate -ne [int]$expectedFormat.sampleRate -or
        $metadata.BitsPerSample -ne [int]$expectedFormat.bitsPerSample) {
        throw "Incompatible audio format in layer $($layer.level): $($layer.file)"
    }
    if ([Math]::Abs($metadata.DurationSeconds - [double]$expectedFormat.durationSeconds) -gt $tolerance) {
        throw "Incompatible duration in layer $($layer.level): $($layer.file)"
    }

    $results += [pscustomobject]@{
        Level           = [int]$layer.level
        File            = [string]$layer.file
        SampleRate      = $metadata.SampleRate
        Channels        = $metadata.Channels
        Bits            = $metadata.BitsPerSample
        DurationSeconds = [Math]::Round($metadata.DurationSeconds, 3)
        Sha256          = "OK"
    }
}

$referencePath = Join-Path $SampleDirectory $referenceFile
if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
    throw "The reference mix is missing: $referenceFile"
}

$referenceHash = (Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash
if ($referenceHash -ne [string]$manifest.reference.sha256) {
    throw "SHA-256 verification failed for the reference mix: $referenceFile"
}

$results | Format-Table -AutoSize
Write-Host ""
Write-Host "AUDIO PREFLIGHT: PASS" -ForegroundColor Green
Write-Host "5 runtime layers verified. The reference mix is not a runtime layer."
