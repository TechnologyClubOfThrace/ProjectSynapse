namespace Synapse.App;

internal sealed record CompletedContactCycle(
    DateTime StartedUtc,
    DateTime LastContactUtc,
    DateTime CompletedUtc,
    int PeakLevel,
    IReadOnlyDictionary<int, long> LevelDurationsMilliseconds,
    string EndReason)
{
    public long ActiveContactMilliseconds => LevelDurationsMilliseconds.Values.Sum();

    public long GroupSpanMilliseconds => Math.Max(
        0,
        (long)Math.Round((LastContactUtc - StartedUtc).TotalMilliseconds));
}

internal sealed class ParticipationAccumulator
{
    private static readonly TimeSpan GroupSeparationDelay = TimeSpan.FromSeconds(3);
    private readonly double[] levelDurationsMilliseconds = new double[8];
    private DateTime? startedUtc;
    private DateTime? lastContactUtc;
    private DateTime? zeroObservedUtc;
    private DateTime? lastAccountedUtc;
    private int currentLevel;
    private int peakLevel;

    public int CurrentLevel => currentLevel;
    public int ActivePeak => peakLevel;
    public long CurrentActiveDurationMilliseconds =>
        Math.Max(0, (long)Math.Round(levelDurationsMilliseconds.Sum()));

    public void ObserveLevel(int level, DateTime nowUtc)
    {
        if (level is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        AccumulateUntil(nowUtc);
        currentLevel = level;
        if (level > 0)
        {
            startedUtc ??= nowUtc;
            lastAccountedUtc ??= nowUtc;
            lastContactUtc = nowUtc;
            peakLevel = Math.Max(peakLevel, level);
            zeroObservedUtc = null;
        }
        else if (startedUtc is not null)
        {
            zeroObservedUtc ??= nowUtc;
        }
    }

    public CompletedContactCycle? Tick(DateTime nowUtc)
    {
        AccumulateUntil(nowUtc);
        if (startedUtc is null || zeroObservedUtc is null ||
            nowUtc - zeroObservedUtc < GroupSeparationDelay)
        {
            return null;
        }
        return Complete(nowUtc, "released");
    }

    public CompletedContactCycle? HandleInputLoss(DateTime nowUtc)
    {
        AccumulateUntil(nowUtc);
        return startedUtc is null ? null : Complete(nowUtc, "input-loss");
    }

    public void Reset()
    {
        startedUtc = null;
        lastContactUtc = null;
        zeroObservedUtc = null;
        lastAccountedUtc = null;
        currentLevel = 0;
        peakLevel = 0;
        Array.Clear(levelDurationsMilliseconds);
    }

    private void AccumulateUntil(DateTime nowUtc)
    {
        if (lastAccountedUtc is null)
        {
            lastAccountedUtc = nowUtc;
            return;
        }

        var elapsedMilliseconds = Math.Max(
            0,
            (nowUtc - lastAccountedUtc.Value).TotalMilliseconds);
        if (startedUtc is not null && currentLevel > 0)
        {
            levelDurationsMilliseconds[currentLevel] += elapsedMilliseconds;
            lastContactUtc = nowUtc;
        }
        lastAccountedUtc = nowUtc;
    }

    private CompletedContactCycle Complete(DateTime nowUtc, string reason)
    {
        var durations = Enumerable.Range(1, 7).ToDictionary(
            level => level,
            level => Math.Max(0, (long)Math.Round(levelDurationsMilliseconds[level])));
        var completed = new CompletedContactCycle(
            startedUtc!.Value,
            lastContactUtc ?? startedUtc.Value,
            nowUtc,
            peakLevel,
            durations,
            reason);
        Reset();
        return completed;
    }
}
