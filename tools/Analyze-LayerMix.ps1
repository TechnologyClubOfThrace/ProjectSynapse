[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$LayerFiles,

    [Parameter()]
    [string]$ReferenceOutput
)

$ErrorActionPreference = "Stop"

if ($LayerFiles.Count -lt 1) {
    throw "At least one layer file is required."
}

$resolvedFiles = $LayerFiles | ForEach-Object {
    (Resolve-Path -LiteralPath $_).Path
}

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public sealed class CumulativeMixResult
{
    public int LayerCount { get; set; }
    public double Peak { get; set; }
    public double Rms { get; set; }
}

public static class SynapseLayerMixAnalyzer
{
    private sealed class WaveInput : IDisposable
    {
        public BinaryReader Reader { get; private set; }
        public int SampleRate { get; private set; }
        public short Channels { get; private set; }
        public short BitsPerSample { get; private set; }
        public long SampleCount { get; private set; }

        public WaveInput(string path)
        {
            Reader = new BinaryReader(File.OpenRead(path));
            if (Encoding.ASCII.GetString(Reader.ReadBytes(4)) != "RIFF")
                throw new InvalidDataException("Not a RIFF file: " + path);
            Reader.ReadUInt32();
            if (Encoding.ASCII.GetString(Reader.ReadBytes(4)) != "WAVE")
                throw new InvalidDataException("Not a WAVE file: " + path);

            bool hasFormat = false;
            bool hasData = false;
            while (Reader.BaseStream.Position + 8 <= Reader.BaseStream.Length)
            {
                string id = Encoding.ASCII.GetString(Reader.ReadBytes(4));
                uint size = Reader.ReadUInt32();
                long next = Reader.BaseStream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    ushort encoding = Reader.ReadUInt16();
                    Channels = Reader.ReadInt16();
                    SampleRate = Reader.ReadInt32();
                    Reader.ReadUInt32();
                    Reader.ReadUInt16();
                    BitsPerSample = Reader.ReadInt16();
                    if (encoding != 1 || BitsPerSample != 16)
                        throw new InvalidDataException("Expected PCM 16-bit audio: " + path);
                    hasFormat = true;
                }
                else if (id == "data")
                {
                    SampleCount = size / 2;
                    hasData = true;
                    break;
                }
                Reader.BaseStream.Position = Math.Min(next, Reader.BaseStream.Length);
            }

            if (!hasFormat || !hasData)
                throw new InvalidDataException("Missing WAV format or data chunk: " + path);
        }

        public void Dispose()
        {
            Reader.Dispose();
        }
    }

    public static CumulativeMixResult[] Analyze(string[] paths, string referenceOutput)
    {
        var inputs = new WaveInput[paths.Length];
        BinaryWriter output = null;
        try
        {
            for (int index = 0; index < paths.Length; index++)
                inputs[index] = new WaveInput(paths[index]);

            long sampleCount = inputs[0].SampleCount;
            int sampleRate = inputs[0].SampleRate;
            short channels = inputs[0].Channels;
            for (int index = 1; index < inputs.Length; index++)
            {
                if (inputs[index].SampleCount != sampleCount ||
                    inputs[index].SampleRate != sampleRate ||
                    inputs[index].Channels != channels)
                    throw new InvalidDataException("Layer formats or durations do not match.");
            }

            var peaks = new double[inputs.Length];
            var sumSquares = new double[inputs.Length];
            if (!String.IsNullOrWhiteSpace(referenceOutput))
            {
                output = new BinaryWriter(File.Create(referenceOutput));
                long dataBytes = sampleCount * 2;
                output.Write(Encoding.ASCII.GetBytes("RIFF"));
                output.Write((uint)(36 + dataBytes));
                output.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                output.Write((uint)16);
                output.Write((ushort)1);
                output.Write((ushort)channels);
                output.Write((uint)sampleRate);
                output.Write((uint)(sampleRate * channels * 2));
                output.Write((ushort)(channels * 2));
                output.Write((ushort)16);
                output.Write(Encoding.ASCII.GetBytes("data"));
                output.Write((uint)dataBytes);
            }

            for (long sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                double cumulative = 0.0;
                for (int layer = 0; layer < inputs.Length; layer++)
                {
                    cumulative += inputs[layer].Reader.ReadInt16() / 32768.0;
                    peaks[layer] = Math.Max(peaks[layer], Math.Abs(cumulative));
                    sumSquares[layer] += cumulative * cumulative;
                }
                if (output != null)
                {
                    int pcm = (int)Math.Round(cumulative * 32767.0);
                    pcm = Math.Max(short.MinValue, Math.Min(short.MaxValue, pcm));
                    output.Write((short)pcm);
                }
            }

            var results = new CumulativeMixResult[inputs.Length];
            for (int layer = 0; layer < inputs.Length; layer++)
            {
                results[layer] = new CumulativeMixResult
                {
                    LayerCount = layer + 1,
                    Peak = peaks[layer],
                    Rms = Math.Sqrt(sumSquares[layer] / sampleCount)
                };
            }
            return results;
        }
        finally
        {
            if (output != null) output.Dispose();
            foreach (var input in inputs)
                if (input != null) input.Dispose();
        }
    }
}
'@

$resolvedReferenceOutput = if ($ReferenceOutput) {
    [System.IO.Path]::GetFullPath($ReferenceOutput)
}
else {
    $null
}

[SynapseLayerMixAnalyzer]::Analyze($resolvedFiles, $resolvedReferenceOutput) |
    Select-Object LayerCount,
        @{ Name = "Peak"; Expression = { [Math]::Round($_.Peak, 4) } },
        @{ Name = "PeakDbFS"; Expression = {
            if ($_.Peak -le 0) { [double]::NegativeInfinity }
            else { [Math]::Round(20 * [Math]::Log10($_.Peak), 2) }
        } },
        @{ Name = "Rms"; Expression = { [Math]::Round($_.Rms, 4) } },
        @{ Name = "RmsDbFS"; Expression = {
            if ($_.Rms -le 0) { [double]::NegativeInfinity }
            else { [Math]::Round(20 * [Math]::Log10($_.Rms), 2) }
        } }
