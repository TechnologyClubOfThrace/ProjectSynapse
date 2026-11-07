using System.Globalization;

namespace Synapse.Input;

public sealed record TouchStateMessage(
    int ParticipationLevel,
    uint RawValue,
    uint SmoothValue,
    uint? Sequence,
    ulong? UptimeMilliseconds);

public static class TouchMessageParser
{
    public const int MaximumLineLength = 160;

    public static bool TryParse(string? line, out TouchStateMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumLineLength)
        {
            return false;
        }

        var fields = line.Trim().Split(',');
        if (fields.Length is not (6 or 10) ||
            !fields[0].Equals("LEVEL", StringComparison.Ordinal) ||
            !fields[2].Equals("VALUE", StringComparison.Ordinal) ||
            !fields[4].Equals("SMOOTH", StringComparison.Ordinal) ||
            !TryParseInt(fields[1], out var level) || level is < 0 or > 7 ||
            !TryParseUInt(fields[3], out var raw) ||
            !TryParseUInt(fields[5], out var smooth))
        {
            return false;
        }

        uint? sequence = null;
        ulong? uptime = null;
        if (fields.Length == 10)
        {
            if (!fields[6].Equals("SEQ", StringComparison.Ordinal) ||
                !fields[8].Equals("UPTIME", StringComparison.Ordinal) ||
                !TryParseUInt(fields[7], out var parsedSequence) ||
                !ulong.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var parsedUptime))
            {
                return false;
            }
            sequence = parsedSequence;
            uptime = parsedUptime;
        }

        message = new TouchStateMessage(level, raw, smooth, sequence, uptime);
        return true;
    }

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TryParseUInt(string value, out uint result) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
