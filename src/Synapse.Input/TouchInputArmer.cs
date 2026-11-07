namespace Synapse.Input;

public sealed class TouchInputArmer(int requiredConsecutiveSamples = 2)
{
    private int? candidateLevel;
    private int consecutiveSamples;

    public bool IsArmed { get; private set; }

    public bool TryAccept(int participationLevel, out int acceptedLevel)
    {
        if (participationLevel is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(participationLevel));
        }
        if (requiredConsecutiveSamples < 1)
        {
            throw new InvalidOperationException("At least one arming sample is required.");
        }

        if (IsArmed)
        {
            acceptedLevel = participationLevel;
            return true;
        }

        if (candidateLevel == participationLevel)
        {
            consecutiveSamples++;
        }
        else
        {
            candidateLevel = participationLevel;
            consecutiveSamples = 1;
        }

        if (consecutiveSamples >= requiredConsecutiveSamples)
        {
            IsArmed = true;
            acceptedLevel = participationLevel;
            return true;
        }

        acceptedLevel = 0;
        return false;
    }

    public void Reset()
    {
        IsArmed = false;
        candidateLevel = null;
        consecutiveSamples = 0;
    }
}
