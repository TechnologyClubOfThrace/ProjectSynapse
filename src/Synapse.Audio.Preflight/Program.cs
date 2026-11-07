using Synapse.Audio;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("PREFLIGHT: FAIL - Windows is required.");
    return 2;
}

var sampleDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples"));
var deviceId = args.Length > 1 ? args[1] : null;

try
{
    var assets = AudioManifestValidator.Validate(sampleDirectory);
    Console.WriteLine($"Audio assets: PASS ({assets.LayerPaths.Count} layers)");
    Console.WriteLine($"Composition: {assets.Manifest.Composition}");
    Console.WriteLine($"Duration: {assets.Manifest.AudioFormat.DurationSeconds:F3} s");

    var deviceResult = AudioDevicePreflight.Run(deviceId);
    Console.WriteLine($"Selected device: {deviceResult.SelectedDevice.Name}");
    Console.WriteLine($"Device ID: {deviceResult.SelectedDevice.Id}");
    Console.WriteLine(
        $"Mix format: {deviceResult.SelectedDevice.MixSampleRate} Hz, " +
        $"{deviceResult.SelectedDevice.MixChannels} channels, " +
        $"{deviceResult.SelectedDevice.MixBitsPerSample} bit");

    foreach (var warning in deviceResult.Warnings)
    {
        Console.WriteLine($"WARNING: {warning}");
    }

    Console.WriteLine("PREFLIGHT: PASS - no audio was played.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"PREFLIGHT: FAIL - {exception.Message}");
    return 1;
}

