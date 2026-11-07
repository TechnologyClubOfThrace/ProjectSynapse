using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synapse.Audio;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    int MixSampleRate,
    int MixChannels,
    int MixBitsPerSample);

public sealed record AudioDevicePreflightResult(
    AudioDeviceInfo SelectedDevice,
    IReadOnlyList<AudioDeviceInfo> ActiveRenderDevices,
    IReadOnlyList<string> Warnings);

public static class AudioDevicePreflight
{
    public static AudioDevicePreflightResult Run(string? configuredDeviceId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Synapse audio output requires Windows.");
        }

        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            throw new InvalidOperationException("Windows has no default multimedia render endpoint.");
        }

        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var results = new List<AudioDeviceInfo>(devices.Count);

        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            var format = device.AudioClient.MixFormat;
            results.Add(new AudioDeviceInfo(
                device.ID,
                device.FriendlyName,
                device.ID.Equals(defaultDevice.ID, StringComparison.OrdinalIgnoreCase),
                format.SampleRate,
                format.Channels,
                format.BitsPerSample));
        }

        var selected = string.IsNullOrWhiteSpace(configuredDeviceId)
            ? results.Single(device => device.IsDefault)
            : results.FirstOrDefault(device =>
                device.Id.Equals(configuredDeviceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Configured audio device is not active: {configuredDeviceId}");

        var warnings = new List<string>();
        if (selected.MixChannels < 2)
        {
            throw new InvalidOperationException(
                $"Selected audio device is not stereo-capable: {selected.Name}");
        }

        if (selected.MixSampleRate != 44100)
        {
            warnings.Add(
                $"Device mix rate is {selected.MixSampleRate} Hz; Windows shared-mode conversion will be required.");
        }

        using var selectedDevice = string.IsNullOrWhiteSpace(configuredDeviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(configuredDeviceId);
        using var outputProbe = new WasapiOut(
            selectedDevice,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 100);
        outputProbe.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));

        return new AudioDevicePreflightResult(selected, results, warnings);
    }
}

