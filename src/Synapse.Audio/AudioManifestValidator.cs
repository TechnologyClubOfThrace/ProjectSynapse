using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;

namespace Synapse.Audio;

public static class AudioManifestValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ValidatedAudioAssets Validate(string sampleDirectory, string? manifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleDirectory);

        var directory = Path.GetFullPath(sampleDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Sample directory not found: {directory}");
        }

        var resolvedManifestPath = Path.GetFullPath(
            manifestPath ?? Path.Combine(directory, "manifest.json"));

        if (!File.Exists(resolvedManifestPath))
        {
            throw new FileNotFoundException("Audio manifest not found.", resolvedManifestPath);
        }

        var manifest = LoadManifest(resolvedManifestPath);

        var layerPaths = manifest.Layers
            .OrderBy(layer => layer.Level)
            .Select(layer => ResolveContainedFile(directory, layer.File))
            .ToArray();
        var referencePath = ResolveContainedFile(directory, manifest.Reference.File);

        var declaredNames = manifest.Layers.Select(layer => layer.File)
            .Append(manifest.Reference.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var undeclaredWavFiles = Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !declaredNames.Contains(name))
            .ToArray();

        if (undeclaredWavFiles.Length > 0)
        {
            throw new InvalidDataException(
                $"Undeclared WAV files found: {string.Join(", ", undeclaredWavFiles)}");
        }

        foreach (var layer in manifest.Layers.OrderBy(layer => layer.Level))
        {
            var path = ResolveContainedFile(directory, layer.File);
            ValidateHash(path, layer.Sha256, $"layer {layer.Level}");
            ValidateWave(path, manifest.AudioFormat, $"layer {layer.Level}");
        }

        ValidateHash(referencePath, manifest.Reference.Sha256, "reference mix");
        ValidateWave(referencePath, manifest.AudioFormat, "reference mix");

        return new ValidatedAudioAssets(
            directory,
            resolvedManifestPath,
            manifest,
            layerPaths,
            referencePath);
    }

    public static AudioManifest LoadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var resolvedPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Audio manifest not found.", resolvedPath);
        }

        var json = File.ReadAllText(resolvedPath);
        var manifest = JsonSerializer.Deserialize<AudioManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Audio manifest is empty or invalid.");
        ValidateManifestShape(manifest);
        return manifest;
    }

    private static void ValidateManifestShape(AudioManifest manifest)
    {
        if (manifest.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported audio manifest version: {manifest.FormatVersion}");
        }

        if (!string.Equals(manifest.AudioFormat.Encoding, "PCM", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only PCM WAV assets are supported.");
        }

        if (manifest.AudioFormat.Channels != 2 ||
            manifest.AudioFormat.SampleRate <= 0 ||
            manifest.AudioFormat.BitsPerSample != 16 ||
            manifest.AudioFormat.DurationSeconds <= 0 ||
            manifest.AudioFormat.DurationToleranceSeconds < 0)
        {
            throw new InvalidDataException("Unsupported or incomplete audio format definition.");
        }

        if (manifest.Layers.Count != 5)
        {
            throw new InvalidDataException($"Exactly 5 layers are required; found {manifest.Layers.Count}.");
        }

        var levels = manifest.Layers.Select(layer => layer.Level).Order().ToArray();
        if (!levels.SequenceEqual(Enumerable.Range(1, 5)))
        {
            throw new InvalidDataException("Layer levels must be exactly 1,2,3,4,5.");
        }

        if (manifest.Layers.Any(layer =>
                string.IsNullOrWhiteSpace(layer.File) ||
                string.IsNullOrWhiteSpace(layer.Sha256) ||
                !double.IsFinite(layer.GainDb) ||
                layer.GainDb is < -60 or > 6))
        {
            throw new InvalidDataException("A layer definition is incomplete or outside safe gain limits.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Reference.File) ||
            string.IsNullOrWhiteSpace(manifest.Reference.Sha256) ||
            manifest.Reference.Runtime)
        {
            throw new InvalidDataException("The reference mix must be declared and excluded from runtime playback.");
        }

        if (manifest.InteractionDurationSeconds <= 0 ||
            manifest.InteractionDurationSeconds > manifest.AudioFormat.DurationSeconds)
        {
            throw new InvalidDataException("Interaction duration is outside the audio duration.");
        }

        if (!double.IsFinite(manifest.MasterGainDb) || manifest.MasterGainDb is < -60 or > 0 ||
            !double.IsFinite(manifest.LimiterCeilingDb) || manifest.LimiterCeilingDb is < -12 or > 0)
        {
            throw new InvalidDataException("Master gain or limiter ceiling is outside safe limits.");
        }
    }

    private static string ResolveContainedFile(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            throw new InvalidDataException($"Invalid asset path: {fileName}");
        }

        var fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
        var relative = Path.GetRelativePath(directory, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Asset path escapes the sample directory: {fileName}");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Audio asset not found.", fullPath);
        }

        return fullPath;
    }

    private static void ValidateHash(string path, string expectedHash, string label)
    {
        using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 verification failed for {label}: {Path.GetFileName(path)}");
        }
    }

    private static void ValidateWave(string path, AudioFormatDefinition expected, string label)
    {
        using var reader = new WaveFileReader(path);
        var format = reader.WaveFormat;

        if (format.Encoding != WaveFormatEncoding.Pcm ||
            format.Channels != expected.Channels ||
            format.SampleRate != expected.SampleRate ||
            format.BitsPerSample != expected.BitsPerSample)
        {
            throw new InvalidDataException($"Audio format mismatch for {label}: {Path.GetFileName(path)}");
        }

        if (Math.Abs(reader.TotalTime.TotalSeconds - expected.DurationSeconds) >
            expected.DurationToleranceSeconds)
        {
            throw new InvalidDataException($"Audio duration mismatch for {label}: {Path.GetFileName(path)}");
        }
    }
}

