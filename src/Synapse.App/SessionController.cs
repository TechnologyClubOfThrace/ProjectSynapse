using Synapse.Input;

namespace Synapse.App;

internal enum PresentationPhase
{
    Ready,
    Building,
    Paused,
    Climax,
    Ending,
    Fault
}

internal enum AudioCommandType
{
    Start,
    Resume,
    SetLevel,
    PauseAfterFade,
    BeginTail,
    Stop
}

internal sealed record AudioCommand(AudioCommandType Type, int Level = 0);

internal sealed record SessionSnapshot(
    PresentationPhase Phase,
    int ParticipationLevel,
    int EffectiveLayerCount,
    double ContactMilliseconds,
    double InteractionDurationMilliseconds,
    double AudioDurationSeconds,
    double AudioPositionSeconds,
    bool TailActive);

internal sealed class SessionController
{
    private const double ContactGraceMilliseconds = 250;
    private const double IdleResetMilliseconds = 20_000;
    private const double ClimaxLeadMilliseconds = 20_000;

    private double interactionDurationMilliseconds = 180_000;
    private double climaxStartMilliseconds = 160_000;
    private double audioEndSeconds = 183.101;
    private double audioEndToleranceSeconds = 0.01;

    private DateTime lastTickAt = DateTime.UtcNow;
    private DateTime lastContactAt = DateTime.MinValue;
    private int inputLevel;
    private int commandedLevel = -1;
    private double contactMilliseconds;
    private PresentationPhase phase = PresentationPhase.Ready;

    public SessionSnapshot Snapshot(double audioPositionSeconds = 0) => new(
        phase,
        inputLevel,
        EffectiveLayerCount,
        contactMilliseconds,
        interactionDurationMilliseconds,
        audioEndSeconds,
        audioPositionSeconds,
        phase == PresentationPhase.Ending);

    public void Configure(
        double interactionDurationSeconds,
        double audioDurationSeconds,
        double audioDurationToleranceSeconds)
    {
        if (phase != PresentationPhase.Ready)
        {
            throw new InvalidOperationException("Session timing can change only while ready.");
        }
        if (!double.IsFinite(interactionDurationSeconds) || interactionDurationSeconds <= 0 ||
            !double.IsFinite(audioDurationSeconds) || audioDurationSeconds < interactionDurationSeconds ||
            !double.IsFinite(audioDurationToleranceSeconds) || audioDurationToleranceSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionDurationSeconds));
        }

        interactionDurationMilliseconds = interactionDurationSeconds * 1000;
        climaxStartMilliseconds = Math.Max(0,
            interactionDurationMilliseconds - ClimaxLeadMilliseconds);
        audioEndSeconds = audioDurationSeconds;
        audioEndToleranceSeconds = audioDurationToleranceSeconds;
        ResetState(DateTime.UtcNow);
    }

    public void SetParticipationLevel(int level, DateTime now)
    {
        if (level is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        inputLevel = level;
        if (level > 0)
        {
            lastContactAt = now;
        }
    }

    public IReadOnlyList<AudioCommand> Tick(DateTime now, double audioPositionSeconds)
    {
        var commands = new List<AudioCommand>();
        var elapsed = Math.Clamp((now - lastTickAt).TotalMilliseconds, 0, 250);
        lastTickAt = now;

        if (phase == PresentationPhase.Fault)
        {
            return commands;
        }

        if (phase == PresentationPhase.Ending)
        {
            if (audioPositionSeconds >= audioEndSeconds - audioEndToleranceSeconds)
            {
                commands.Add(new AudioCommand(AudioCommandType.Stop));
                ResetState(now);
            }
            return commands;
        }

        if (inputLevel > 0)
        {
            contactMilliseconds = Math.Min(
                interactionDurationMilliseconds,
                contactMilliseconds + elapsed);

            if (phase == PresentationPhase.Ready)
            {
                commands.Add(new AudioCommand(AudioCommandType.Start));
                phase = PresentationPhase.Building;
            }
            else if (phase == PresentationPhase.Paused)
            {
                commands.Add(new AudioCommand(AudioCommandType.Resume));
                phase = contactMilliseconds >= climaxStartMilliseconds
                    ? PresentationPhase.Climax
                    : PresentationPhase.Building;
            }

            if (contactMilliseconds >= interactionDurationMilliseconds)
            {
                phase = PresentationPhase.Ending;
                commandedLevel = 5;
                commands.Add(new AudioCommand(AudioCommandType.BeginTail, 5));
                return commands;
            }

            var desiredLevel = contactMilliseconds >= climaxStartMilliseconds
                ? 5
                : ParticipationLevelMapper.ToLayerCount(inputLevel);
            if (contactMilliseconds >= climaxStartMilliseconds)
            {
                phase = PresentationPhase.Climax;
            }

            if (desiredLevel != commandedLevel)
            {
                commandedLevel = desiredLevel;
                commands.Add(new AudioCommand(AudioCommandType.SetLevel, desiredLevel));
            }
            return commands;
        }

        if (phase == PresentationPhase.Ready)
        {
            return commands;
        }

        var withoutContact = lastContactAt == DateTime.MinValue
            ? double.PositiveInfinity
            : (now - lastContactAt).TotalMilliseconds;

        if (withoutContact <= ContactGraceMilliseconds)
        {
            return commands;
        }

        if (withoutContact >= IdleResetMilliseconds)
        {
            commands.Add(new AudioCommand(AudioCommandType.Stop));
            ResetState(now);
            return commands;
        }

        if (phase != PresentationPhase.Paused)
        {
            commandedLevel = 0;
            phase = PresentationPhase.Paused;
            commands.Add(new AudioCommand(AudioCommandType.PauseAfterFade));
        }

        return commands;
    }

    public IReadOnlyList<AudioCommand> Reset(DateTime now)
    {
        var commands = phase == PresentationPhase.Ready
            ? Array.Empty<AudioCommand>()
            : [new AudioCommand(AudioCommandType.Stop)];
        ResetState(now);
        return commands;
    }

    public IReadOnlyList<AudioCommand> HandleInputLoss(DateTime now)
    {
        inputLevel = 0;
        lastTickAt = now;
        if (phase is PresentationPhase.Ready or PresentationPhase.Paused or PresentationPhase.Fault)
        {
            return Array.Empty<AudioCommand>();
        }

        commandedLevel = 0;
        phase = PresentationPhase.Paused;
        return [new AudioCommand(AudioCommandType.PauseAfterFade)];
    }

    public void SetFault()
    {
        phase = PresentationPhase.Fault;
        inputLevel = 0;
        commandedLevel = 0;
    }

    private int EffectiveLayerCount => phase switch
    {
        PresentationPhase.Climax or PresentationPhase.Ending => 5,
        PresentationPhase.Building => ParticipationLevelMapper.ToLayerCount(inputLevel),
        _ => 0
    };

    private void ResetState(DateTime now)
    {
        phase = PresentationPhase.Ready;
        inputLevel = 0;
        commandedLevel = -1;
        contactMilliseconds = 0;
        lastContactAt = DateTime.MinValue;
        lastTickAt = now;
    }
}

