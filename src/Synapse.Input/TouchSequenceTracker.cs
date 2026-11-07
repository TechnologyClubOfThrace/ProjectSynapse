namespace Synapse.Input;

public enum TouchSequenceResult
{
    Accepted,
    DeviceRestart,
    Duplicate,
    OutOfOrder
}

public sealed class TouchSequenceTracker
{
    private uint? lastSequence;
    private ulong? lastUptime;

    public TouchSequenceResult Accept(TouchStateMessage message)
    {
        if (message.Sequence is null || message.UptimeMilliseconds is null)
        {
            return TouchSequenceResult.Accepted;
        }

        var sequence = message.Sequence.Value;
        var uptime = message.UptimeMilliseconds.Value;
        if (lastSequence is null || lastUptime is null)
        {
            lastSequence = sequence;
            lastUptime = uptime;
            return TouchSequenceResult.Accepted;
        }

        if (uptime < lastUptime && sequence <= lastSequence)
        {
            lastSequence = sequence;
            lastUptime = uptime;
            return TouchSequenceResult.DeviceRestart;
        }

        var delta = unchecked(sequence - lastSequence.Value);
        if (delta == 0)
        {
            return TouchSequenceResult.Duplicate;
        }
        if (delta >= 0x80000000U)
        {
            return TouchSequenceResult.OutOfOrder;
        }

        lastSequence = sequence;
        lastUptime = uptime;
        return TouchSequenceResult.Accepted;
    }

    public void Reset()
    {
        lastSequence = null;
        lastUptime = null;
    }
}
