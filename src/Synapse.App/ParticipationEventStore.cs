using System.Text;
using System.Text.Json;

namespace Synapse.App;

internal sealed record ParticipationTotals(
    long EstimatedParticipations,
    long Synapses,
    int MaximumSimultaneous,
    long TotalActiveContactMilliseconds,
    long LongestActiveContactMilliseconds,
    DateTime? LastResetUtc)
{
    public static ParticipationTotals Empty { get; } = new(0, 0, 0, 0, 0, null);

    public ParticipationTotals Add(CompletedContactCycle cycle) => this with
    {
        EstimatedParticipations = checked(EstimatedParticipations + cycle.PeakLevel),
        Synapses = checked(Synapses + 1),
        MaximumSimultaneous = Math.Max(MaximumSimultaneous, cycle.PeakLevel),
        TotalActiveContactMilliseconds = checked(
            TotalActiveContactMilliseconds + cycle.ActiveContactMilliseconds),
        LongestActiveContactMilliseconds = Math.Max(
            LongestActiveContactMilliseconds,
            cycle.ActiveContactMilliseconds)
    };

    public ParticipationTotals AddLegacy(int peakLevel) => this with
    {
        EstimatedParticipations = checked(EstimatedParticipations + peakLevel),
        Synapses = checked(Synapses + 1),
        MaximumSimultaneous = Math.Max(MaximumSimultaneous, peakLevel)
    };
}

internal sealed record ParticipationStoreLoadResult(
    ParticipationTotals Totals,
    int IgnoredInvalidLines);

internal sealed class ParticipationEventStore
{
    private const string EventFileName = "participation-events.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string directory;
    private readonly string eventPath;

    public ParticipationEventStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        eventPath = Path.Combine(this.directory, EventFileName);
    }

    public ParticipationStoreLoadResult Load()
    {
        if (!File.Exists(eventPath))
        {
            return new ParticipationStoreLoadResult(ParticipationTotals.Empty, 0);
        }

        var totals = ParticipationTotals.Empty;
        var invalidLines = 0;
        foreach (var line in File.ReadLines(eventPath, Utf8WithoutBom))
        {
            try
            {
                var record = JsonSerializer.Deserialize<ParticipationEventRecord>(line, JsonOptions);
                if (record is null)
                {
                    invalidLines++;
                    continue;
                }

                if (record.Type == "reset" && record.FormatVersion is 1 or 2)
                {
                    totals = new ParticipationTotals(0, 0, 0, 0, 0, record.OccurredUtc);
                }
                else if (record.FormatVersion == 1 && record.Type == "contact" &&
                         record.PeakLevel is >= 1 and <= 7)
                {
                    // V1 included the three-second separation delay in its duration.
                    // We import its reliable counters, but do not present that duration
                    // as clean human contact time.
                    totals = totals.AddLegacy(record.PeakLevel.Value);
                }
                else if (record.FormatVersion == 2 && record.Type == "synapse" &&
                         TryCreateCycle(record, out var cycle))
                {
                    totals = totals.Add(cycle!);
                }
                else
                {
                    invalidLines++;
                }
            }
            catch (JsonException)
            {
                invalidLines++;
            }
        }
        return new ParticipationStoreLoadResult(totals, invalidLines);
    }

    public void AppendContact(
        CompletedContactCycle cycle,
        string calibrationProfile,
        string composition,
        string appVersion) => AppendContact(
            Guid.NewGuid(),
            cycle,
            calibrationProfile,
            composition,
            appVersion);

    public void AppendContact(
        Guid eventId,
        CompletedContactCycle cycle,
        string calibrationProfile,
        string composition,
        string appVersion)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("The synapse event ID cannot be empty.", nameof(eventId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(calibrationProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        Append(new ParticipationEventRecord(
            2,
            "synapse",
            eventId.ToString("D"),
            cycle.CompletedUtc,
            cycle.StartedUtc,
            cycle.LastContactUtc,
            cycle.PeakLevel,
            null,
            cycle.ActiveContactMilliseconds,
            cycle.GroupSpanMilliseconds,
            cycle.LevelDurationsMilliseconds.ToDictionary(pair => pair.Key, pair => pair.Value),
            calibrationProfile,
            composition,
            appVersion,
            cycle.EndReason));
    }

    public void AppendReset(DateTime occurredUtc) => Append(new ParticipationEventRecord(
        2,
        "reset",
        Guid.NewGuid().ToString("D"),
        occurredUtc,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        Application.ProductVersion,
        null));

    private void Append(ParticipationEventRecord record)
    {
        Directory.CreateDirectory(directory);
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        using var stream = new FileStream(
            eventPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, Utf8WithoutBom);
        writer.Write(line);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static bool TryCreateCycle(
        ParticipationEventRecord record,
        out CompletedContactCycle? cycle)
    {
        cycle = null;
        if (string.IsNullOrWhiteSpace(record.EventId) ||
            record.StartedUtc is null || record.LastContactUtc is null ||
            record.PeakLevel is not (>= 1 and <= 7) ||
            record.ActiveContactMilliseconds is null or < 0 ||
            record.GroupSpanMilliseconds is null or < 0 ||
            record.LevelDurationsMilliseconds is null ||
            record.LevelDurationsMilliseconds.Any(pair =>
                pair.Key is < 1 or > 7 || pair.Value < 0))
        {
            return false;
        }

        var completeDurations = Enumerable.Range(1, 7).ToDictionary(
            level => level,
            level => record.LevelDurationsMilliseconds.GetValueOrDefault(level));
        if (completeDurations.Values.Sum() != record.ActiveContactMilliseconds.Value)
        {
            return false;
        }

        cycle = new CompletedContactCycle(
            record.StartedUtc.Value,
            record.LastContactUtc.Value,
            record.OccurredUtc,
            record.PeakLevel.Value,
            completeDurations,
            record.EndReason ?? "unknown");
        return cycle.GroupSpanMilliseconds == record.GroupSpanMilliseconds.Value;
    }

    private sealed record ParticipationEventRecord(
        int FormatVersion,
        string Type,
        string? EventId,
        DateTime OccurredUtc,
        DateTime? StartedUtc,
        DateTime? LastContactUtc,
        int? PeakLevel,
        long? DurationMilliseconds,
        long? ActiveContactMilliseconds,
        long? GroupSpanMilliseconds,
        Dictionary<int, long>? LevelDurationsMilliseconds,
        string? CalibrationProfile,
        string? Composition,
        string? AppVersion,
        string? EndReason);
}
