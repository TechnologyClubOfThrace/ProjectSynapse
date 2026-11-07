using System.Text.Json.Serialization;

namespace Synapse.Audio;

public sealed class AudioManifest
{
    public int FormatVersion { get; init; }
    public string Composition { get; init; } = string.Empty;
    public AudioFormatDefinition AudioFormat { get; init; } = new();
    public double InteractionDurationSeconds { get; init; }
    public bool PlayTailToEnd { get; init; }
    public double MasterGainDb { get; init; }
    public double LimiterCeilingDb { get; init; } = -0.18;
    public List<AudioLayerDefinition> Layers { get; init; } = [];
    public AudioReferenceDefinition Reference { get; init; } = new();
}

public sealed class AudioFormatDefinition
{
    public string Encoding { get; init; } = string.Empty;
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public double DurationSeconds { get; init; }
    public double DurationToleranceSeconds { get; init; } = 0.01;
}

public sealed class AudioLayerDefinition
{
    public int Level { get; init; }
    public string File { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public double GainDb { get; init; }
}

public sealed class AudioReferenceDefinition
{
    public string File { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public bool Runtime { get; init; }
}

public sealed record ValidatedAudioAssets(
    string SampleDirectory,
    string ManifestPath,
    AudioManifest Manifest,
    IReadOnlyList<string> LayerPaths,
    string ReferencePath);

