using NAudio.Wave;

namespace Synapse.Audio;

/// <summary>
/// Applies frame-aligned, time-based gain ramps on the audio callback thread.
/// </summary>
public sealed class SmoothGainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly object stateLock = new();
    private float currentGain;
    private float targetGain;
    private float gainStepPerFrame;
    private long remainingRampFrames;

    public SmoothGainSampleProvider(ISampleProvider source, float initialGain = 0)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        currentGain = ValidateGain(initialGain);
        targetGain = currentGain;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public float CurrentGain
    {
        get
        {
            lock (stateLock)
            {
                return currentGain;
            }
        }
    }

    public float TargetGain
    {
        get
        {
            lock (stateLock)
            {
                return targetGain;
            }
        }
    }

    public void SetTarget(float gain, TimeSpan duration)
    {
        gain = ValidateGain(gain);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        lock (stateLock)
        {
            targetGain = gain;
            var rampFrames = (long)Math.Round(duration.TotalSeconds * WaveFormat.SampleRate);
            if (rampFrames <= 0)
            {
                currentGain = targetGain;
                gainStepPerFrame = 0;
                remainingRampFrames = 0;
                return;
            }

            remainingRampFrames = rampFrames;
            gainStepPerFrame = (targetGain - currentGain) / rampFrames;
        }
    }

    public void Reset(float gain = 0)
    {
        gain = ValidateGain(gain);
        lock (stateLock)
        {
            currentGain = gain;
            targetGain = gain;
            gainStepPerFrame = 0;
            remainingRampFrames = 0;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;

        lock (stateLock)
        {
            var end = offset + samplesRead;
            for (var sampleIndex = offset; sampleIndex < end;)
            {
                if (remainingRampFrames > 0)
                {
                    currentGain += gainStepPerFrame;
                    remainingRampFrames--;
                    if (remainingRampFrames == 0)
                    {
                        currentGain = targetGain;
                    }
                }

                for (var channel = 0; channel < channels && sampleIndex < end; channel++, sampleIndex++)
                {
                    buffer[sampleIndex] *= currentGain;
                }
            }
        }

        return samplesRead;
    }

    private static float ValidateGain(float gain)
    {
        if (!float.IsFinite(gain) || gain is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(gain), "Gain must be finite and between 0 and 2.");
        }

        return gain;
    }
}

