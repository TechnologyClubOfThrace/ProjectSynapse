using System.Text.Json;

namespace Synapse.Input;

public sealed class TouchCalibrationProfile
{
    public int FormatVersion { get; init; } = 1;
    public string Name { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public List<uint> Thresholds { get; init; } = [];
    public uint Hysteresis { get; init; }
    public TouchCalibrationSampleSummary? Baseline { get; init; }
    public TouchCalibrationSampleSummary? Contact { get; init; }
    public List<TouchCalibrationLevelSummary>? HigherLevels { get; init; }
    public List<TouchCalibrationLevelSummary>? CalibrationLevels { get; init; }
}

public sealed record TouchCalibrationSampleSummary(
    int Count,
    uint Minimum,
    uint Median,
    uint Maximum,
    uint ConservativeBoundary);

public sealed record TouchCalibrationLevelSummary(
    int Level,
    int Count,
    uint Minimum,
    uint LowerBoundary,
    uint Median,
    uint UpperBoundary,
    uint Maximum);

public sealed record SavedTouchCalibrationProfile(
    string FilePath,
    TouchCalibrationProfile Profile)
{
    public string FileName => Path.GetFileName(FilePath);
}

public sealed record TouchCalibrationProfileLoadIssue(
    string FileName,
    string Message);

public sealed record TouchCalibrationProfileCatalog(
    IReadOnlyList<SavedTouchCalibrationProfile> Profiles,
    IReadOnlyList<TouchCalibrationProfileLoadIssue> Issues);

public static class TouchCalibrationProfiles
{
    private const string ActiveProfileFile = "active-calibration.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static TouchCalibrationProfile CreateDefault() => new()
    {
        Name = "Default provisional profile",
        Thresholds = [180000, 440000, 470000, 500000, 535000, 575000, 610000],
        Hysteresis = 7000
    };

    public static TouchCalibrationProfile LoadActiveOrDefault(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(Path.GetFullPath(directory), ActiveProfileFile);
        if (!File.Exists(path))
        {
            return CreateDefault();
        }

        var profile = JsonSerializer.Deserialize<TouchCalibrationProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The active touch calibration profile is empty.");
        Validate(profile);
        return profile;
    }

    public static string SaveAndActivate(string directory, TouchCalibrationProfile profile)
    {
        Validate(profile);
        var resolvedDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(resolvedDirectory);
        var slug = CreateSafeSlug(profile.Name);
        var profilePath = Path.Combine(resolvedDirectory, $"{slug}.json");
        var activePath = Path.Combine(resolvedDirectory, ActiveProfileFile);
        if (File.Exists(activePath))
        {
            var historyDirectory = Path.Combine(resolvedDirectory, "history");
            Directory.CreateDirectory(historyDirectory);
            var backupPath = Path.Combine(
                historyDirectory,
                $"active-calibration-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
            File.Copy(activePath, backupPath, overwrite: false);
        }
        WriteAtomic(profilePath, profile);
        WriteAtomic(activePath, profile);
        return profilePath;
    }

    public static TouchCalibrationProfileCatalog DiscoverSaved(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var resolvedDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(resolvedDirectory))
        {
            return new TouchCalibrationProfileCatalog([], []);
        }

        var profiles = new List<SavedTouchCalibrationProfile>();
        var issues = new List<TouchCalibrationProfileLoadIssue>();
        foreach (var path in Directory.EnumerateFiles(
                     resolvedDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(
                    Path.GetFileName(path),
                    ActiveProfileFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var profile = JsonSerializer.Deserialize<TouchCalibrationProfile>(
                        File.ReadAllText(path),
                        JsonOptions)
                    ?? throw new InvalidDataException("The calibration profile is empty.");
                Validate(profile);
                profiles.Add(new SavedTouchCalibrationProfile(path, profile));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                issues.Add(new TouchCalibrationProfileLoadIssue(
                    Path.GetFileName(path),
                    exception.Message));
            }
        }

        return new TouchCalibrationProfileCatalog(
            profiles
                .OrderByDescending(item => item.Profile.CreatedUtc)
                .ThenBy(item => item.Profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            issues);
    }

    public static void Validate(TouchCalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.FormatVersion != 1 || string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidDataException("Unsupported or unnamed calibration profile.");
        }
        if (profile.Thresholds.Count is < 1 or > 7)
        {
            throw new InvalidDataException("A calibration profile must contain between one and seven thresholds.");
        }
        for (var index = 1; index < profile.Thresholds.Count; index++)
        {
            if (profile.Thresholds[index] <= profile.Thresholds[index - 1])
            {
                throw new InvalidDataException("Calibration thresholds must be strictly increasing.");
            }
        }
        if (profile.HigherLevels is not null)
        {
            if (profile.HigherLevels.Count != profile.Thresholds.Count - 1)
            {
                throw new InvalidDataException(
                    "Higher-level summaries do not match the calibrated threshold count.");
            }
            for (var index = 0; index < profile.HigherLevels.Count; index++)
            {
                var summary = profile.HigherLevels[index];
                if (summary.Level != index + 2 || summary.Count < 10 ||
                    summary.Minimum > summary.LowerBoundary ||
                    summary.LowerBoundary > summary.Median ||
                    summary.Median > summary.UpperBoundary ||
                    summary.UpperBoundary > summary.Maximum)
                {
                    throw new InvalidDataException("A higher-level calibration summary is invalid.");
                }
            }
        }
        if (profile.CalibrationLevels is not null)
        {
            if (profile.CalibrationLevels.Count != profile.Thresholds.Count + 1)
            {
                throw new InvalidDataException(
                    "Calibration level summaries do not match the threshold count.");
            }
            for (var index = 0; index < profile.CalibrationLevels.Count; index++)
            {
                var summary = profile.CalibrationLevels[index];
                if (summary.Level != index || summary.Count < 10 ||
                    summary.Minimum > summary.LowerBoundary ||
                    summary.LowerBoundary > summary.Median ||
                    summary.Median > summary.UpperBoundary ||
                    summary.UpperBoundary > summary.Maximum)
                {
                    throw new InvalidDataException("A calibration level summary is invalid.");
                }
            }
        }
        if (profile.Hysteresis is < 500 or > 100000 ||
            profile.Thresholds[0] <= profile.Hysteresis ||
            profile.Thresholds[^1] > uint.MaxValue - profile.Hysteresis)
        {
            throw new InvalidDataException("Calibration hysteresis is outside safe limits.");
        }
    }

    private static void WriteAtomic(string path, TouchCalibrationProfile profile)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profile, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateSafeSlug(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = string.Join('-', new string(characters)
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "touch-calibration" : slug;
    }
}
