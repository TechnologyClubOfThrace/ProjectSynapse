namespace Synapse.Input;

public static class TouchCalibrationCalculator
{
    private const int MinimumSamplesPerStage = 10;

    public static TouchCalibrationProfile CreateTwoPointProfile(
        string name,
        IReadOnlyCollection<uint> baselineSamples,
        IReadOnlyCollection<uint> contactSamples,
        TouchCalibrationProfile higherLevelTemplate)
    {
        if (baselineSamples.Count < MinimumSamplesPerStage ||
            contactSamples.Count < MinimumSamplesPerStage)
        {
            throw new InvalidDataException("At least ten samples are required for each calibration stage.");
        }
        TouchCalibrationProfiles.Validate(higherLevelTemplate);

        var baseline = baselineSamples.Order().ToArray();
        var contact = contactSamples.Order().ToArray();
        var baselineUpper = Percentile(baseline, 0.95);
        var contactLower = Percentile(contact, 0.10);
        if (contactLower <= baselineUpper + 20000)
        {
            throw new InvalidDataException(
                "The contact signal is not sufficiently separated from the no-contact baseline.");
        }

        var signalGap = contactLower - baselineUpper;
        var hysteresis = Math.Clamp(signalGap / 10, 2000U, 15000U);
        var firstThreshold = baselineUpper + signalGap / 2;
        if (firstThreshold + hysteresis >= higherLevelTemplate.Thresholds[1])
        {
            throw new InvalidDataException(
                "The calculated first threshold overlaps the provisional higher participation zones.");
        }

        var thresholds = higherLevelTemplate.Thresholds.ToArray();
        thresholds[0] = firstThreshold;
        var profile = new TouchCalibrationProfile
        {
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            Thresholds = [.. thresholds],
            Hysteresis = hysteresis,
            Baseline = Summarize(baseline, baselineUpper),
            Contact = Summarize(contact, contactLower)
        };
        TouchCalibrationProfiles.Validate(profile);
        return profile;
    }

    public static TouchCalibrationProfile CreateFullProfile(
        string name,
        IReadOnlyList<IReadOnlyCollection<uint>> samplesForLevelsZeroToSeven)
    {
        if (samplesForLevelsZeroToSeven.Count != 8)
        {
            throw new InvalidDataException("Exactly eight sample sets are required for levels 0–7.");
        }

        return CreateProfile(name, samplesForLevelsZeroToSeven);
    }

    public static TouchCalibrationProfile CreateProfile(
        string name,
        IReadOnlyList<IReadOnlyCollection<uint>> contiguousSamplesFromLevelZero)
    {
        if (contiguousSamplesFromLevelZero.Count is < 2 or > 8)
        {
            throw new InvalidDataException(
                "Calibration requires contiguous sample sets from level 0 through level 1–7.");
        }

        var summaries = new TouchCalibrationLevelSummary[contiguousSamplesFromLevelZero.Count];
        for (var level = 0; level < summaries.Length; level++)
        {
            summaries[level] = SummarizeAnyLevel(level, contiguousSamplesFromLevelZero[level]);
        }

        return CreateProfileFromSummaries(
            name,
            summaries,
            existingProfile: null,
            changedLevels: Enumerable.Range(0, summaries.Length).ToHashSet());
    }

    public static TouchCalibrationProfile CreateProfileFromSummaries(
        string name,
        IReadOnlyList<TouchCalibrationLevelSummary> contiguousSummariesFromLevelZero,
        TouchCalibrationProfile? existingProfile,
        IReadOnlySet<int> changedLevels)
    {
        if (contiguousSummariesFromLevelZero.Count is < 2 or > 8)
        {
            throw new InvalidDataException(
                "Calibration requires contiguous summaries from level 0 through level 1–7.");
        }
        if (existingProfile is not null)
        {
            TouchCalibrationProfiles.Validate(existingProfile);
        }

        var summaries = contiguousSummariesFromLevelZero.ToArray();
        for (var level = 0; level < summaries.Length; level++)
        {
            if (summaries[level].Level != level || summaries[level].Count < MinimumSamplesPerStage)
            {
                throw new InvalidDataException("Calibration summaries must be contiguous and valid.");
            }
        }

        var gaps = new ulong[summaries.Length - 1];
        for (var boundary = 0; boundary < gaps.Length; boundary++)
        {
            var previousUpper = summaries[boundary].UpperBoundary;
            var nextLower = summaries[boundary + 1].LowerBoundary;
            if (nextLower <= (ulong)previousUpper + 1000UL)
            {
                throw new InvalidDataException(
                    $"Levels {boundary} and {boundary + 1} are not safely separated. " +
                    $"Upper/lower boundaries are {previousUpper:N0}/{nextLower:N0}.");
            }
            gaps[boundary] = (ulong)nextLower - previousUpper;
        }

        var minimumGap = gaps.Min();
        var calculatedHysteresis = checked((uint)Math.Clamp(minimumGap / 4UL, 500UL, 15000UL));
        var hysteresis = existingProfile is null
            ? calculatedHysteresis
            : Math.Min(existingProfile.Hysteresis, calculatedHysteresis);
        var thresholds = new uint[gaps.Length];
        for (var boundary = 0; boundary < gaps.Length; boundary++)
        {
            var preserveExisting = existingProfile is not null &&
                boundary < existingProfile.Thresholds.Count &&
                !changedLevels.Contains(boundary) &&
                !changedLevels.Contains(boundary + 1);
            thresholds[boundary] = preserveExisting
                ? existingProfile!.Thresholds[boundary]
                : checked((uint)(summaries[boundary].UpperBoundary + gaps[boundary] / 2UL));
        }

        var profile = new TouchCalibrationProfile
        {
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            Thresholds = [.. thresholds],
            Hysteresis = hysteresis,
            Baseline = ToLegacySummary(summaries[0], summaries[0].UpperBoundary),
            Contact = ToLegacySummary(summaries[1], summaries[1].LowerBoundary),
            HigherLevels = summaries.Skip(2).ToList(),
            CalibrationLevels = summaries.ToList()
        };
        TouchCalibrationProfiles.Validate(profile);
        return profile;
    }

    public static IReadOnlyList<TouchCalibrationLevelSummary> GetRecordedLevelSummaries(
        TouchCalibrationProfile profile)
    {
        TouchCalibrationProfiles.Validate(profile);
        if (profile.CalibrationLevels is { Count: > 0 })
        {
            return profile.CalibrationLevels.ToArray();
        }
        if (profile.Baseline is null || profile.Contact is null)
        {
            return [];
        }

        var summaries = new List<TouchCalibrationLevelSummary>
        {
            FromLegacySummary(0, profile.Baseline, lowerBoundary: profile.Baseline.Minimum,
                upperBoundary: profile.Baseline.ConservativeBoundary),
            FromLegacySummary(1, profile.Contact, lowerBoundary: profile.Contact.ConservativeBoundary,
                upperBoundary: profile.Contact.Maximum)
        };
        if (profile.HigherLevels is not null)
        {
            summaries.AddRange(profile.HigherLevels);
        }
        return summaries;
    }

    public static TouchCalibrationProfile CreateHigherLevelProfile(
        string name,
        TouchCalibrationProfile existingProfile,
        IReadOnlyList<IReadOnlyCollection<uint>> samplesForLevelsTwoToSeven)
    {
        TouchCalibrationProfiles.Validate(existingProfile);
        if (existingProfile.Contact is null)
        {
            throw new InvalidDataException(
                "A validated level-one contact measurement is required first.");
        }
        if (samplesForLevelsTwoToSeven.Count != 6)
        {
            throw new InvalidDataException("Exactly six sample sets are required for levels 2–7.");
        }

        var summaries = new List<TouchCalibrationLevelSummary>(6);
        var thresholds = existingProfile.Thresholds.ToArray();
        var previousUpperBoundary = existingProfile.Contact.Maximum;

        for (var index = 0; index < samplesForLevelsTwoToSeven.Count; index++)
        {
            var level = index + 2;
            var summary = SummarizeLevel(level, samplesForLevelsTwoToSeven[index]);
            ValidateHigherLevelTransition(
                previousUpperBoundary,
                summary,
                existingProfile.Hysteresis);

            var gap = (ulong)summary.LowerBoundary - previousUpperBoundary;
            thresholds[level - 1] = checked((uint)(previousUpperBoundary + gap / 2));
            summaries.Add(summary);
            previousUpperBoundary = summary.UpperBoundary;
        }

        var profile = new TouchCalibrationProfile
        {
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            Thresholds = [.. thresholds],
            Hysteresis = existingProfile.Hysteresis,
            Baseline = existingProfile.Baseline,
            Contact = existingProfile.Contact,
            HigherLevels = summaries
        };
        TouchCalibrationProfiles.Validate(profile);
        return profile;
    }

    public static TouchCalibrationLevelSummary SummarizeLevel(
        int level,
        IReadOnlyCollection<uint> samples)
    {
        if (level is < 2 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
        return SummarizeAnyLevel(level, samples);
    }

    public static TouchCalibrationLevelSummary SummarizeAnyLevel(
        int level,
        IReadOnlyCollection<uint> samples)
    {
        if (level is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }
        if (samples.Count < MinimumSamplesPerStage)
        {
            throw new InvalidDataException(
                "At least ten samples are required for each higher-level calibration stage.");
        }

        var sorted = samples.Order().ToArray();
        return new TouchCalibrationLevelSummary(
            level,
            sorted.Length,
            sorted[0],
            Percentile(sorted, 0.10),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.90),
            sorted[^1]);
    }

    public static void ValidateHigherLevelTransition(
        uint previousUpperBoundary,
        TouchCalibrationLevelSummary nextLevel,
        uint hysteresis)
    {
        var requiredLowerBoundary = (ulong)previousUpperBoundary + 2UL * hysteresis;
        if (nextLevel.LowerBoundary <= requiredLowerBoundary)
        {
            throw new InvalidDataException(
                $"Level {nextLevel.Level} is not safely separated from the previous level. " +
                $"Its lower boundary is {nextLevel.LowerBoundary:N0}; it must exceed " +
                $"{requiredLowerBoundary:N0} with the current hysteresis.");
        }
    }

    private static TouchCalibrationSampleSummary Summarize(uint[] samples, uint boundary) => new(
        samples.Length,
        samples[0],
        Percentile(samples, 0.50),
        samples[^1],
        boundary);

    private static TouchCalibrationSampleSummary ToLegacySummary(
        TouchCalibrationLevelSummary summary,
        uint boundary) => new(
            summary.Count,
            summary.Minimum,
            summary.Median,
            summary.Maximum,
            boundary);

    private static TouchCalibrationLevelSummary FromLegacySummary(
        int level,
        TouchCalibrationSampleSummary summary,
        uint lowerBoundary,
        uint upperBoundary) => new(
            level,
            summary.Count,
            summary.Minimum,
            lowerBoundary,
            summary.Median,
            upperBoundary,
            summary.Maximum);

    private static uint Percentile(uint[] sortedSamples, double percentile)
    {
        var index = (int)Math.Round((sortedSamples.Length - 1) * percentile,
            MidpointRounding.AwayFromZero);
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }
}
