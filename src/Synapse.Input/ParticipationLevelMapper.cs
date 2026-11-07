namespace Synapse.Input;

public static class ParticipationLevelMapper
{
    private static readonly int[] LayerCounts = [0, 1, 2, 3, 4, 4, 5, 5];

    public static int ToLayerCount(int participationLevel)
    {
        if (participationLevel is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(participationLevel));
        }

        return LayerCounts[participationLevel];
    }
}
