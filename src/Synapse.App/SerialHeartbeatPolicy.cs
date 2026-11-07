namespace Synapse.App;

internal static class SerialHeartbeatPolicy
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(2500);

    public static bool HasExpired(
        bool connectionExpected,
        bool alreadyTimedOut,
        DateTime lastValidMessageUtc,
        DateTime nowUtc) =>
        connectionExpected &&
        !alreadyTimedOut &&
        lastValidMessageUtc != DateTime.MinValue &&
        nowUtc - lastValidMessageUtc > Timeout;
}
