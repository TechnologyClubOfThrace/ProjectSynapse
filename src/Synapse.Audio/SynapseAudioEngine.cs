using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Synapse.Audio;

public enum SynapseAudioState
{
    Closed,
    Ready,
    Playing,
    Paused,
    Faulted,
    Disposed
}

public sealed class AudioEngineFaultEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>
/// Five-layer, single-clock audio engine for the Synapse installation.
/// </summary>
public sealed class SynapseAudioEngine : IDisposable
{
    private readonly object stateLock = new();
    private readonly List<WaveFileReader> readers = [];
    private readonly List<SmoothGainSampleProvider> gains = [];
    private readonly TimeSpan fadeIn;
    private readonly TimeSpan fadeOut;
    private readonly TimeSpan muteFade;
    private MMDeviceEnumerator? deviceEnumerator;
    private MMDevice? selectedDevice;
    private WasapiOut? output;
    private SmoothGainSampleProvider? masterOutputGain;
    private MasterSafetySampleProvider? masterSafety;
    private ValidatedAudioAssets? assets;
    private SynapseAudioState state = SynapseAudioState.Closed;
    private long transitionVersion;
    private int outputLatencyMilliseconds = 100;
    private bool muted;

    public SynapseAudioEngine(
        TimeSpan? fadeIn = null,
        TimeSpan? fadeOut = null,
        TimeSpan? muteFade = null)
    {
        this.fadeIn = fadeIn ?? TimeSpan.FromMilliseconds(900);
        this.fadeOut = fadeOut ?? TimeSpan.FromMilliseconds(1500);
        this.muteFade = muteFade ?? TimeSpan.FromMilliseconds(500);
    }

    public event EventHandler<AudioEngineFaultEventArgs>? Faulted;

    public SynapseAudioState State
    {
        get
        {
            lock (stateLock)
            {
                return state;
            }
        }
    }

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (stateLock)
            {
                return readers.Count == 0 ? TimeSpan.Zero : readers[0].CurrentTime;
            }
        }
    }

    public long LimitedSampleCount => masterSafety?.LimitedSampleCount ?? 0;

    public bool IsMuted
    {
        get
        {
            lock (stateLock)
            {
                return muted;
            }
        }
    }

    public void Open(string sampleDirectory, string? configuredDeviceId = null, int latencyMilliseconds = 100)
    {
        var validatedAssets = AudioManifestValidator.Validate(sampleDirectory);
        Open(validatedAssets, configuredDeviceId, latencyMilliseconds);
    }

    public void Open(
        ValidatedAudioAssets validatedAssets,
        string? configuredDeviceId = null,
        int latencyMilliseconds = 100)
    {
        ArgumentNullException.ThrowIfNull(validatedAssets);
        if (latencyMilliseconds is < 20 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyMilliseconds));
        }

        lock (stateLock)
        {
            ThrowIfDisposed();
            CloseCore();

            try
            {
                assets = validatedAssets;
                deviceEnumerator = new MMDeviceEnumerator();
                selectedDevice = string.IsNullOrWhiteSpace(configuredDeviceId)
                    ? deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : deviceEnumerator.GetDevice(configuredDeviceId);

                var orderedLayers = assets.Manifest.Layers.OrderBy(layer => layer.Level).ToArray();
                for (var index = 0; index < assets.LayerPaths.Count; index++)
                {
                    var reader = new WaveFileReader(assets.LayerPaths[index]);
                    readers.Add(reader);
                    gains.Add(new SmoothGainSampleProvider(reader.ToSampleProvider(), 0));
                }

                var mixer = new MixingSampleProvider(gains)
                {
                    ReadFully = true
                };
                var masterGain = DecibelsToLinear((float)assets.Manifest.MasterGainDb);
                var ceiling = DecibelsToLinear((float)assets.Manifest.LimiterCeilingDb);
                masterOutputGain = new SmoothGainSampleProvider(mixer, muted ? 0 : 1);
                masterSafety = new MasterSafetySampleProvider(masterOutputGain, masterGain, ceiling);

                output = new WasapiOut(
                    selectedDevice,
                    AudioClientShareMode.Shared,
                    useEventSync: true,
                    latencyMilliseconds);
                outputLatencyMilliseconds = latencyMilliseconds;
                output.PlaybackStopped += OnPlaybackStopped;
                output.Init(masterSafety.ToWaveProvider());
                state = SynapseAudioState.Ready;
            }
            catch
            {
                CloseCore();
                throw;
            }
        }
    }

    public void Start()
    {
        lock (stateLock)
        {
            EnsureReadyForTransport();
            foreach (var reader in readers)
            {
                reader.Position = 0;
            }
            foreach (var gain in gains)
            {
                gain.Reset(0);
            }

            output!.Play();
            state = SynapseAudioState.Playing;
            transitionVersion++;
        }
    }

    public void Resume()
    {
        lock (stateLock)
        {
            ThrowIfDisposed();
            if (state != SynapseAudioState.Paused)
            {
                throw new InvalidOperationException($"Cannot resume audio from state {state}.");
            }

            output!.Play();
            state = SynapseAudioState.Playing;
            transitionVersion++;
        }
    }

    public async Task PauseAfterFadeAsync(CancellationToken cancellationToken = default)
    {
        long requestedVersion;
        lock (stateLock)
        {
            ThrowIfDisposed();
            if (state != SynapseAudioState.Playing)
            {
                return;
            }

            SetLevelCore(0, fadeOut);
            requestedVersion = ++transitionVersion;
        }

        var deadline = DateTime.UtcNow + fadeOut +
            TimeSpan.FromMilliseconds(outputLatencyMilliseconds + 500);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (stateLock)
            {
                ThrowIfDisposed();
                if (state != SynapseAudioState.Playing || transitionVersion != requestedVersion)
                {
                    return;
                }
                if (gains.All(gain => gain.CurrentGain <= 0.0001f))
                {
                    break;
                }
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        lock (stateLock)
        {
            ThrowIfDisposed();
            if (state != SynapseAudioState.Playing || transitionVersion != requestedVersion)
            {
                return;
            }

            output!.Pause();
            state = SynapseAudioState.Paused;
        }
    }

    public void SetLevel(int level)
    {
        if (level is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        lock (stateLock)
        {
            ThrowIfDisposed();
            if (state is not (SynapseAudioState.Playing or SynapseAudioState.Paused))
            {
                throw new InvalidOperationException($"Cannot set audio level in state {state}.");
            }

            SetLevelCore(level, level == 0 ? fadeOut : fadeIn);
            transitionVersion++;
        }
    }

    public void SetMuted(bool value)
    {
        lock (stateLock)
        {
            ThrowIfDisposed();
            muted = value;
            if (masterOutputGain is null)
            {
                return;
            }

            if (state == SynapseAudioState.Playing)
            {
                masterOutputGain.SetTarget(value ? 0 : 1, muteFade);
            }
            else
            {
                masterOutputGain.Reset(value ? 0 : 1);
            }
        }
    }

    public void BeginTail()
    {
        lock (stateLock)
        {
            ThrowIfDisposed();
            if (state == SynapseAudioState.Paused)
            {
                output!.Play();
                state = SynapseAudioState.Playing;
            }

            if (state != SynapseAudioState.Playing)
            {
                throw new InvalidOperationException($"Cannot begin tail in state {state}.");
            }

            SetLevelCore(5, fadeIn);
            transitionVersion++;
        }
    }

    public bool HasReachedEnd()
    {
        lock (stateLock)
        {
            return assets is not null &&
                CurrentPosition.TotalSeconds >= assets.Manifest.AudioFormat.DurationSeconds -
                assets.Manifest.AudioFormat.DurationToleranceSeconds;
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            ThrowIfDisposed();
            if (output is null)
            {
                return;
            }

            foreach (var gain in gains)
            {
                gain.Reset(0);
            }
            output.Stop();
            foreach (var reader in readers)
            {
                reader.Position = 0;
            }
            state = SynapseAudioState.Ready;
            transitionVersion++;
        }
    }

    public void Close()
    {
        lock (stateLock)
        {
            ThrowIfDisposed();
            CloseCore();
        }
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (state == SynapseAudioState.Disposed)
            {
                return;
            }

            CloseCore();
            state = SynapseAudioState.Disposed;
        }
        GC.SuppressFinalize(this);
    }

    private void SetLevelCore(int level, TimeSpan duration)
    {
        var orderedLayers = assets!.Manifest.Layers.OrderBy(layer => layer.Level).ToArray();
        for (var index = 0; index < gains.Count; index++)
        {
            var target = index < level
                ? DecibelsToLinear((float)orderedLayers[index].GainDb)
                : 0;
            gains[index].SetTarget(target, duration);
        }
    }

    private void EnsureReadyForTransport()
    {
        ThrowIfDisposed();
        if (state != SynapseAudioState.Ready)
        {
            throw new InvalidOperationException($"Cannot start audio from state {state}.");
        }
        if (output is null || assets is null || readers.Count != 5)
        {
            throw new InvalidOperationException("Audio engine is not fully initialized.");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is null)
        {
            return;
        }

        lock (stateLock)
        {
            if (state == SynapseAudioState.Disposed)
            {
                return;
            }
            state = SynapseAudioState.Faulted;
        }

        Faulted?.Invoke(this, new AudioEngineFaultEventArgs(args.Exception));
    }

    private void CloseCore()
    {
        transitionVersion++;
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                output.Stop();
            }
            catch
            {
                // Device removal can make Stop fail; disposal must still continue.
            }
            output.Dispose();
            output = null;
        }

        foreach (var reader in readers)
        {
            reader.Dispose();
        }
        readers.Clear();
        gains.Clear();
        selectedDevice?.Dispose();
        selectedDevice = null;
        deviceEnumerator?.Dispose();
        deviceEnumerator = null;
        masterSafety = null;
        masterOutputGain = null;
        assets = null;
        outputLatencyMilliseconds = 100;

        if (state != SynapseAudioState.Disposed)
        {
            state = SynapseAudioState.Closed;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(state == SynapseAudioState.Disposed, this);
    }

    private static float DecibelsToLinear(float decibels) => MathF.Pow(10, decibels / 20);
}

