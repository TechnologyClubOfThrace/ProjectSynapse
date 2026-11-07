using System.Threading;
using NAudio.Wave;

namespace Synapse.Audio;

/// <summary>
/// Applies master gain and a final sample ceiling. The ceiling is a safety net,
/// not a substitute for approved mix gain staging.
/// </summary>
public sealed class MasterSafetySampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly float masterGain;
    private readonly float ceiling;
    private long limitedSampleCount;

    public MasterSafetySampleProvider(ISampleProvider source, float masterGain, float ceiling)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.masterGain = ValidatePositiveFinite(masterGain, nameof(masterGain));
        this.ceiling = ValidatePositiveFinite(ceiling, nameof(ceiling));
        if (ceiling > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ceiling), "Ceiling cannot exceed 1.0.");
        }
    }

    public WaveFormat WaveFormat => source.WaveFormat;
    public long LimitedSampleCount => Interlocked.Read(ref limitedSampleCount);

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        var limited = 0L;
        var end = offset + samplesRead;

        for (var index = offset; index < end; index++)
        {
            var sample = buffer[index] * masterGain;
            if (sample > ceiling)
            {
                sample = ceiling;
                limited++;
            }
            else if (sample < -ceiling)
            {
                sample = -ceiling;
                limited++;
            }

            buffer[index] = sample;
        }

        if (limited > 0)
        {
            Interlocked.Add(ref limitedSampleCount, limited);
        }

        return samplesRead;
    }

    private static float ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}

