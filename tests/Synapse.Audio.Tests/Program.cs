using Microsoft.Data.Sqlite;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Synapse.Audio;
using Synapse.App;
using Synapse.Input;

var failures = new List<string>();

Run("Five providers share one mixer clock", TestMixer, failures);
Run("Gain ramps are frame-aligned", TestGainRamp, failures);
Run("Master ceiling limits unsafe samples", TestMasterSafety, failures);
Run("Audio mute can be prepared without changing transport", TestAudioMuteState, failures);
Run("Participation levels map safely to five layers", TestParticipationMapping, failures);
Run("Serial touch messages are parsed strictly", TestTouchMessageParser, failures);
Run("Serial sequence rejects duplicate and stale packets", TestTouchSequence, failures);
Run("Input loss immediately requests fade and pause", TestInputLoss, failures);
Run("Heartbeat expires even when Windows closes the port", TestHeartbeatPolicy, failures);
Run("Synapse counter separates active time from group delay", TestParticipationCounter, failures);
Run("Synapse history keeps v1 compatibility and v2 metrics", TestParticipationStore, failures);
Run("Optional SQLite telemetry drains safely and links synapses", TestSensorTelemetry, failures);
Run("SQLite telemetry closes an active session during disposal", TestSensorTelemetryDisposal, failures);
Run("Serial input arms only after stable packets", TestInputArming, failures);
Run("Two-point calibration creates a safe touch threshold", TestTouchCalibration, failures);
Run("Higher-level calibration preserves the validated first level", TestHigherLevelCalibration, failures);
Run("Higher-level calibration rejects overlapping signals", TestHigherLevelOverlap, failures);
Run("Unified calibration creates all seven ordered thresholds", TestUnifiedCalibration, failures);
Run("Unified calibration rejects overlapping adjacent ranges", TestUnifiedCalibrationOverlap, failures);
Run("Partial calibration disables unrecorded higher levels", TestPartialCalibration, failures);
Run("Calibration extension preserves existing thresholds", TestCalibrationExtension, failures);
Run("Calibration save keeps a timestamped previous profile", TestCalibrationBackup, failures);
Run("Calibration library isolates invalid saved profiles", TestCalibrationLibrary, failures);
Run("Calibrated classifier applies hysteresis", TestTouchClassifier, failures);
Run("Calibration rejects hysteresis overflow", TestCalibrationOverflow, failures);
Run("Runtime data option keeps data in the selected directory", TestRuntimeDataOption, failures);

if (args.Length == 1)
{
    Run("Composition catalog and real WAV assets validate", () =>
    {
        var catalog = CompositionCatalog.Discover(args[0]);
        Assert(catalog.Compositions.Count >= 1, "Expected at least one composition.");
        var assets = AudioManifestValidator.Validate(catalog.Compositions[0].Directory);
        Assert(assets.LayerPaths.Count == 5, "Expected five validated layers.");
    }, failures);
    Run("Invalid composition metadata is isolated", () =>
    {
        var sourceCatalog = CompositionCatalog.Discover(args[0]);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"synapse-catalog-{Guid.NewGuid():N}");
        try
        {
            var validDirectory = Directory.CreateDirectory(Path.Combine(temporaryRoot, "01_valid")).FullName;
            var invalidDirectory = Directory.CreateDirectory(Path.Combine(temporaryRoot, "02_invalid")).FullName;
            File.Copy(sourceCatalog.Compositions[0].ManifestPath,
                Path.Combine(validDirectory, "manifest.json"));
            File.WriteAllText(Path.Combine(invalidDirectory, "manifest.json"), "{ invalid json }");

            var isolatedCatalog = CompositionCatalog.Discover(temporaryRoot);
            Assert(isolatedCatalog.Compositions.Count == 1, "Expected one valid composition.");
            Assert(isolatedCatalog.Issues.Count == 1, "Expected one isolated catalog issue.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }, failures);
}

static void TestRuntimeDataOption()
{
    var expected = Path.GetFullPath("selected-runtime-data");
    var dataOptions = AppOptions.Parse(["--data", expected]);
    var runtimeOptions = AppOptions.Parse(["--runtime", expected]);

    Assert(dataOptions.RuntimeDataDirectory == expected,
        "The --data option must select the requested runtime directory.");
    Assert(runtimeOptions.RuntimeDataDirectory == expected,
        "The --runtime alias must select the requested runtime directory.");
}

if (failures.Count == 0)
{
    Console.WriteLine("TESTS: PASS");
    return 0;
}

foreach (var failure in failures)
{
    Console.Error.WriteLine($"FAIL: {failure}");
}
return 1;

static void TestMixer()
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    var providers = Enumerable.Range(0, 5)
        .Select(_ => (ISampleProvider)new ConstantSampleProvider(format, 0.1f, frames: 16))
        .ToArray();
    var mixer = new MixingSampleProvider(providers);
    var buffer = new float[32];
    var read = mixer.Read(buffer, 0, buffer.Length);

    Assert(read == buffer.Length, "Mixer returned an unexpected sample count.");
    Assert(buffer.All(sample => Math.Abs(sample - 0.5f) < 0.0001f), "Mixer did not sum all five providers.");
}

static void TestAudioMuteState()
{
    using var engine = new SynapseAudioEngine();
    Assert(engine.State == SynapseAudioState.Closed, "A new audio engine was not closed.");
    engine.SetMuted(true);
    Assert(engine.IsMuted, "The audio engine did not retain the muted state.");
    Assert(engine.State == SynapseAudioState.Closed,
        "Changing mute unexpectedly changed the audio transport state.");
    engine.SetMuted(false);
    Assert(!engine.IsMuted, "The audio engine did not clear the muted state.");
}

static void TestGainRamp()
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(10, 2);
    var source = new ConstantSampleProvider(format, 1, frames: 10);
    var gain = new SmoothGainSampleProvider(source, 0);
    gain.SetTarget(1, TimeSpan.FromSeconds(1));
    var buffer = new float[20];
    gain.Read(buffer, 0, buffer.Length);

    for (var frame = 0; frame < 10; frame++)
    {
        Assert(Math.Abs(buffer[frame * 2] - buffer[frame * 2 + 1]) < 0.0001f,
            "Stereo channels received different gain within a frame.");
    }
    Assert(Math.Abs(buffer[^1] - 1) < 0.0001f, "Gain ramp did not reach its target.");
}

static void TestMasterSafety()
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    var source = new ConstantSampleProvider(format, 2, frames: 2);
    var safety = new MasterSafetySampleProvider(source, 1, 0.98f);
    var buffer = new float[4];
    safety.Read(buffer, 0, buffer.Length);

    Assert(buffer.All(sample => Math.Abs(sample - 0.98f) < 0.0001f), "Unsafe samples were not limited.");
    Assert(safety.LimitedSampleCount == 4, "Limited sample count is incorrect.");
}

static void TestParticipationMapping()
{
    var expected = new[] { 0, 1, 2, 3, 4, 4, 5, 5 };
    for (var participation = 0; participation <= 7; participation++)
    {
        Assert(ParticipationLevelMapper.ToLayerCount(participation) == expected[participation],
            $"Unexpected layer count for participation level {participation}.");
    }
}

static void TestTouchMessageParser()
{
    Assert(TouchMessageParser.TryParse(
        "LEVEL,7,VALUE,610000,SMOOTH,602500,SEQ,42,UPTIME,12000", out var message),
        "Valid extended message was rejected.");
    Assert(message is { ParticipationLevel: 7, RawValue: 610000, SmoothValue: 602500,
        Sequence: 42, UptimeMilliseconds: 12000 }, "Parsed message fields are incorrect.");
    Assert(!TouchMessageParser.TryParse("LEVEL,8,VALUE,1,SMOOTH,1", out _),
        "Out-of-range participation level was accepted.");
    Assert(!TouchMessageParser.TryParse("LEVEL,2,VALUE,-1,SMOOTH,1", out _),
        "Negative sensor value was accepted.");
    Assert(!TouchMessageParser.TryParse("LEVEL,2,SMOOTH,1,VALUE,1", out _),
        "Message with reordered fields was accepted.");
}

static void TestTouchSequence()
{
    var tracker = new TouchSequenceTracker();
    Assert(tracker.Accept(new TouchStateMessage(1, 1, 1, 10, 1000)) == TouchSequenceResult.Accepted,
        "First sequence was rejected.");
    Assert(tracker.Accept(new TouchStateMessage(1, 1, 1, 11, 2000)) == TouchSequenceResult.Accepted,
        "Increasing sequence was rejected.");
    Assert(tracker.Accept(new TouchStateMessage(1, 1, 1, 11, 2100)) == TouchSequenceResult.Duplicate,
        "Duplicate sequence was accepted.");
    Assert(tracker.Accept(new TouchStateMessage(1, 1, 1, 9, 2200)) == TouchSequenceResult.OutOfOrder,
        "Stale sequence was accepted.");
    Assert(tracker.Accept(new TouchStateMessage(0, 1, 1, 0, 100)) == TouchSequenceResult.DeviceRestart,
        "Device restart was not recognized.");
}

static void TestInputLoss()
{
    var controller = new SessionController();
    var startedAt = DateTime.UtcNow;
    controller.SetParticipationLevel(3, startedAt);
    var startCommands = controller.Tick(startedAt.AddMilliseconds(50), 0);
    Assert(startCommands.Any(command => command.Type == AudioCommandType.Start),
        "Session did not start before the input-loss test.");

    var lossCommands = controller.HandleInputLoss(startedAt.AddMilliseconds(100));
    Assert(lossCommands.Count == 1 && lossCommands[0].Type == AudioCommandType.PauseAfterFade,
        "Input loss did not request an immediate fade and pause.");
    var snapshot = controller.Snapshot();
    Assert(snapshot.Phase == PresentationPhase.Paused && snapshot.ParticipationLevel == 0,
        "Input loss did not move the session into a safe paused state.");
    Assert(controller.HandleInputLoss(startedAt.AddMilliseconds(150)).Count == 0,
        "Repeated input loss emitted a duplicate pause command.");
}

static void TestHeartbeatPolicy()
{
    var lastPacket = DateTime.UtcNow;
    Assert(SerialHeartbeatPolicy.HasExpired(
            connectionExpected: true,
            alreadyTimedOut: false,
            lastPacket,
            lastPacket + SerialHeartbeatPolicy.Timeout + TimeSpan.FromMilliseconds(1)),
        "An expected connection with a stale heartbeat was not treated as lost.");
    Assert(!SerialHeartbeatPolicy.HasExpired(
            connectionExpected: false,
            alreadyTimedOut: false,
            lastPacket,
            lastPacket + SerialHeartbeatPolicy.Timeout + TimeSpan.FromSeconds(1)),
        "An intentionally disconnected input triggered a heartbeat timeout.");
    Assert(!SerialHeartbeatPolicy.HasExpired(
            connectionExpected: true,
            alreadyTimedOut: false,
            lastPacket,
            lastPacket + TimeSpan.FromMilliseconds(100)),
        "A fresh heartbeat was treated as lost.");
}

static void TestParticipationCounter()
{
    var counter = new ParticipationAccumulator();
    var started = DateTime.UtcNow;
    counter.ObserveLevel(1, started);
    counter.ObserveLevel(3, started.AddSeconds(1));
    counter.ObserveLevel(2, started.AddSeconds(2));
    Assert(counter.Tick(started.AddSeconds(3)) is null,
        "An active synapse completed before release.");
    counter.ObserveLevel(0, started.AddSeconds(3));
    Assert(counter.Tick(started.AddMilliseconds(5900)) is null,
        "The empty interval separated the group too early.");
    counter.ObserveLevel(1, started.AddMilliseconds(5950));
    counter.ObserveLevel(0, started.AddSeconds(6));
    Assert(counter.Tick(started.AddMilliseconds(8900)) is null,
        "A brief re-touch did not preserve the active group.");
    var completed = counter.Tick(started.AddMilliseconds(9001));
    Assert(completed is { PeakLevel: 3, EndReason: "released" },
        "The completed synapse did not retain its peak level.");
    if (completed is null)
    {
        throw new InvalidOperationException("The completed synapse was not returned.");
    }
    Assert(completed.ActiveContactMilliseconds == 3050,
        "The completed synapse did not retain clean active contact time.");
    Assert(completed.GroupSpanMilliseconds == 6000,
        "The completed synapse included the separation delay in its group span.");
    Assert(completed.LevelDurationsMilliseconds[1] == 1050 &&
           completed.LevelDurationsMilliseconds[2] == 1000 &&
           completed.LevelDurationsMilliseconds[3] == 1000,
        "The completed synapse did not retain time per participation level.");
    Assert(counter.Tick(started.AddSeconds(4)) is null,
        "A completed synapse was counted more than once.");
}

static void TestParticipationStore()
{
    var root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "tmp",
        $"participation-store-{Guid.NewGuid():N}"));
    var allowedRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tmp")) +
        Path.DirectorySeparatorChar;
    Assert(root.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase),
        "Participation store test path escaped the workspace tmp directory.");
    try
    {
        var now = DateTime.UtcNow;
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "participation-events.jsonl"),
            $$"""{"formatVersion":1,"type":"contact","occurredUtc":"{{now.AddSeconds(2):O}}","startedUtc":"{{now:O}}","peakLevel":3,"durationMilliseconds":5000,"calibrationProfile":"legacy","endReason":"released"}""" +
            Environment.NewLine);
        var store = new ParticipationEventStore(root);
        var legacy = store.Load();
        Assert(legacy.Totals is
            { EstimatedParticipations: 3, Synapses: 1, TotalActiveContactMilliseconds: 0 },
            "A valid v1 contact record was not imported safely.");

        var linkedEventId = Guid.NewGuid();
        store.AppendContact(
            linkedEventId,
            CreateCycle(now.AddSeconds(3), 2000, 3, "released"),
            "test-profile", "test-composition", "2.0.0");
        store.AppendContact(
            CreateCycle(now.AddSeconds(6), 1000, 2, "released"),
            "test-profile", "test-composition", "2.0.0");
        store.AppendReset(now.AddSeconds(5));
        store.AppendContact(
            CreateCycle(now.AddSeconds(9), 2000, 4, "input-loss"),
            "test-profile", "test-composition", "2.0.0");

        var loaded = new ParticipationEventStore(root).Load();
        Assert(loaded.Totals is
            {
                EstimatedParticipations: 4,
                Synapses: 1,
                MaximumSimultaneous: 4,
                TotalActiveContactMilliseconds: 2000,
                LongestActiveContactMilliseconds: 2000
            },
            "Participation totals were not reconstructed correctly after reset.");
        Assert(loaded.IgnoredInvalidLines == 0,
            "Valid participation history contained rejected lines.");
        Assert(File.ReadAllText(Path.Combine(root, "participation-events.jsonl"))
                .Contains(linkedEventId.ToString("D"), StringComparison.Ordinal),
            "The supplied synapse ID was not shared with the participation history.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static CompletedContactCycle CreateCycle(
        DateTime startedUtc,
        long activeMilliseconds,
        int peakLevel,
        string reason)
    {
        var durations = Enumerable.Range(1, 7).ToDictionary(level => level, _ => 0L);
        durations[peakLevel] = activeMilliseconds;
        var lastContactUtc = startedUtc.AddMilliseconds(activeMilliseconds);
        return new CompletedContactCycle(
            startedUtc,
            lastContactUtc,
            lastContactUtc.AddSeconds(3),
            peakLevel,
            durations,
            reason);
    }
}

static void TestSensorTelemetry()
{
    var root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "tmp",
        $"sensor-telemetry-{Guid.NewGuid():N}"));
    var allowedRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tmp")) +
        Path.DirectorySeparatorChar;
    Assert(root.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase),
        "Telemetry test path escaped the workspace tmp directory.");

    var recorder = new SensorTelemetryRecorder(root);
    try
    {
        Assert(!recorder.TryRecord(new SensorTelemetrySample(
                DateTime.UtcNow, null, "idle", 100, 100, 0, 0, 1, 1000)),
            "Telemetry accepted a sample while disabled.");

        var sessionId = Guid.NewGuid();
        var synapseId = Guid.NewGuid();
        recorder.StartAsync(new SensorTelemetrySession(
                sessionId,
                DateTime.UtcNow,
                "COM_TEST",
                "test-profile",
                "test-composition",
                "2.0.0-test"))
            .GetAwaiter().GetResult();
        Assert(recorder.IsRecording, "Telemetry did not enter the recording state.");
        Assert(recorder.TryRecord(new SensorTelemetrySample(
                DateTime.UtcNow,
                null,
                "idle",
                110,
                105,
                0,
                0,
                2,
                2000)),
            "Telemetry rejected a valid idle sample.");
        Assert(recorder.TryRecord(new SensorTelemetrySample(
                DateTime.UtcNow.AddSeconds(1),
                synapseId,
                "active",
                250000,
                240000,
                1,
                2,
                3,
                3000)),
            "Telemetry rejected a valid active sample.");
        recorder.StopAsync().GetAwaiter().GetResult();
        Assert(!recorder.IsRecording, "Telemetry remained active after stop.");

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = recorder.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        connection.Open();
        using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText = """
                SELECT status, sample_count, dropped_sample_count
                FROM telemetry_sessions
                WHERE session_id = $sessionId;
                """;
            sessionCommand.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
            using var reader = sessionCommand.ExecuteReader();
            Assert(reader.Read(), "Telemetry session was not persisted.");
            Assert(reader.GetString(0) == "stopped" && reader.GetInt64(1) == 2 &&
                   reader.GetInt64(2) == 0,
                "Telemetry session did not close cleanly with the expected counters.");
        }
        using (var sampleCommand = connection.CreateCommand())
        {
            sampleCommand.CommandText = """
                SELECT synapse_id, raw_value, smooth_value, firmware_level,
                       app_level, sequence, uptime_ms
                FROM telemetry_samples
                WHERE phase = 'active';
                """;
            using var reader = sampleCommand.ExecuteReader();
            Assert(reader.Read(), "Active telemetry sample was not persisted.");
            Assert(reader.GetString(0) == synapseId.ToString("D") &&
                   reader.GetInt64(1) == 250000 && reader.GetInt64(2) == 240000 &&
                   reader.GetInt32(3) == 1 && reader.GetInt32(4) == 2 &&
                   reader.GetInt64(5) == 3 && reader.GetInt64(6) == 3000,
                "Persisted telemetry fields or synapse linkage are incorrect.");
        }
    }
    finally
    {
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void TestSensorTelemetryDisposal()
{
    var root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "tmp",
        $"sensor-telemetry-disposal-{Guid.NewGuid():N}"));
    var recorder = new SensorTelemetryRecorder(root);
    var sessionId = Guid.NewGuid();
    try
    {
        recorder.StartAsync(new SensorTelemetrySession(
                sessionId,
                DateTime.UtcNow,
                "COM_TEST",
                "test-profile",
                "test-composition",
                "2.0.0-test"))
            .GetAwaiter().GetResult();
        Assert(recorder.TryRecord(new SensorTelemetrySample(
                DateTime.UtcNow, null, "idle", 100, 100, 0, 0, 1, 1000)),
            "Telemetry rejected the disposal test sample.");
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = recorder.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, sample_count
            FROM telemetry_sessions
            WHERE session_id = $sessionId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        using var reader = command.ExecuteReader();
        Assert(reader.Read() && reader.GetString(0) == "stopped" && reader.GetInt64(1) == 1,
            "Disposal did not drain the queue and close the active telemetry session.");
    }
    finally
    {
        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void TestInputArming()
{
    var armer = new TouchInputArmer(requiredConsecutiveSamples: 2);
    Assert(!armer.TryAccept(7, out var first) && first == 0,
        "A single startup spike armed the input.");
    Assert(!armer.TryAccept(1, out var changed) && changed == 0,
        "A changed startup level armed the input.");
    Assert(armer.TryAccept(1, out var stable) && stable == 1,
        "Two stable packets did not arm the input.");
    Assert(armer.TryAccept(3, out var live) && live == 3,
        "Armed input did not pass through a live level change.");
    armer.Reset();
    Assert(!armer.TryAccept(3, out _), "Reset input remained armed.");
}

static void TestTouchCalibration()
{
    var baseline = Enumerable.Range(0, 20).Select(index => (uint)(54000 + index * 10)).ToArray();
    var contact = Enumerable.Range(0, 20).Select(index => (uint)(230000 + index * 10)).ToArray();
    var profile = TouchCalibrationCalculator.CreateTwoPointProfile(
        "test-profile", baseline, contact, TouchCalibrationProfiles.CreateDefault());

    Assert(profile.Thresholds[0] > baseline.Max(),
        "Calculated touch threshold overlaps the baseline.");
    Assert(profile.Thresholds[0] + profile.Hysteresis < contact.Min(),
        "Calculated activation boundary overlaps the contact signal.");
    Assert(profile.Thresholds.SequenceEqual(profile.Thresholds.Order()),
        "Calculated thresholds are not monotonic.");
}

static void TestHigherLevelCalibration()
{
    var baseProfile = CreateCalibratedFirstLevel();
    var higherSamples = Enumerable.Range(0, 6)
        .Select(stage => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(300000 + stage * 50000 + index * 10))
            .ToArray())
        .ToArray();
    var profile = TouchCalibrationCalculator.CreateHigherLevelProfile(
        "full-profile", baseProfile, higherSamples);

    Assert(profile.Thresholds[0] == baseProfile.Thresholds[0],
        "The validated 0–1 threshold changed during higher-level calibration.");
    Assert(profile.Hysteresis == baseProfile.Hysteresis,
        "The validated hysteresis changed during higher-level calibration.");
    Assert(profile.HigherLevels?.Count == 6,
        "The full profile does not contain six higher-level summaries.");
    Assert(profile.Thresholds.SequenceEqual(profile.Thresholds.Order()),
        "Higher-level thresholds are not monotonic.");
}

static void TestHigherLevelOverlap()
{
    var baseProfile = CreateCalibratedFirstLevel();
    var overlappingSamples = Enumerable.Range(0, 6)
        .Select(stage => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(240000 + stage * 50000 + index * 10))
            .ToArray())
        .ToArray();
    var rejected = false;
    try
    {
        TouchCalibrationCalculator.CreateHigherLevelProfile(
            "overlap-profile", baseProfile, overlappingSamples);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Assert(rejected, "Overlapping level-one and level-two signals were accepted.");
}

static void TestUnifiedCalibration()
{
    var sampleSets = Enumerable.Range(0, 8)
        .Select(level => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(100000 + level * 100000 + index * 10))
            .ToArray())
        .ToArray();
    var profile = TouchCalibrationCalculator.CreateFullProfile("unified", sampleSets);

    Assert(profile.Thresholds.Count == 7,
        "Unified calibration did not create seven thresholds.");
    Assert(profile.Thresholds.SequenceEqual(profile.Thresholds.Order()),
        "Unified calibration thresholds are not monotonic.");
    Assert(profile.Baseline is not null && profile.Contact is not null &&
           profile.HigherLevels?.Count == 6,
        "Unified calibration summaries are incomplete.");
}

static void TestUnifiedCalibrationOverlap()
{
    var sampleSets = Enumerable.Range(0, 8)
        .Select(level => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(100000 + level * 100000 + index * 10))
            .ToArray())
        .ToArray();
    sampleSets[4] = sampleSets[3];
    var rejected = false;
    try
    {
        TouchCalibrationCalculator.CreateFullProfile("overlap", sampleSets);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Assert(rejected, "Overlapping adjacent ranges were accepted by unified calibration.");
}

static void TestPartialCalibration()
{
    var sampleSets = Enumerable.Range(0, 4)
        .Select(level => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(100000 + level * 100000 + index * 10))
            .ToArray())
        .ToArray();
    var profile = TouchCalibrationCalculator.CreateProfile("partial", sampleSets);
    var classifier = new TouchLevelClassifier(profile);

    Assert(profile.Thresholds.Count == 3,
        "A 0–3 calibration did not create exactly three thresholds.");
    Assert(profile.HigherLevels?.Count == 2,
        "A 0–3 calibration did not preserve summaries for levels 2 and 3.");
    Assert(classifier.Update(uint.MaxValue - profile.Hysteresis) == 3,
        "The classifier activated an uncalibrated level above 3.");
}

static void TestCalibrationExtension()
{
    var initialSamples = Enumerable.Range(0, 3)
        .Select(level => (IReadOnlyCollection<uint>)Enumerable.Range(0, 20)
            .Select(index => (uint)(100000 + level * 100000 + index * 10))
            .ToArray())
        .ToArray();
    var existing = TouchCalibrationCalculator.CreateProfile("extend", initialSamples);
    var summaries = TouchCalibrationCalculator.GetRecordedLevelSummaries(existing).ToList();
    summaries.Add(TouchCalibrationCalculator.SummarizeAnyLevel(
        3,
        Enumerable.Range(0, 20).Select(index => (uint)(400000 + index * 10)).ToArray()));
    var extended = TouchCalibrationCalculator.CreateProfileFromSummaries(
        "extend",
        summaries,
        existing,
        new HashSet<int> { 3 });

    Assert(extended.Thresholds.Count == 3,
        "Extending 0–2 with level 3 did not create three thresholds.");
    Assert(extended.Thresholds.Take(2).SequenceEqual(existing.Thresholds),
        "Extending calibration changed previously validated thresholds.");
    Assert(extended.CalibrationLevels?.Count == 4,
        "Extended calibration did not persist all recorded range summaries.");
}

static void TestCalibrationBackup()
{
    var root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "tmp",
        $"calibration-backup-{Guid.NewGuid():N}"));
    var allowedRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tmp")) +
        Path.DirectorySeparatorChar;
    Assert(root.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase),
        "Calibration backup test path escaped the workspace tmp directory.");
    try
    {
        var first = CreateCalibratedFirstLevel();
        TouchCalibrationProfiles.SaveAndActivate(root, first);
        var second = new TouchCalibrationProfile
        {
            Name = "second-profile",
            Thresholds = [200000],
            Hysteresis = 5000,
            Baseline = first.Baseline,
            Contact = first.Contact
        };
        TouchCalibrationProfiles.SaveAndActivate(root, second);

        var history = Path.Combine(root, "history");
        Assert(Directory.GetFiles(history, "active-calibration-*.json").Length == 1,
            "Replacing an active calibration did not create exactly one backup.");
        Assert(TouchCalibrationProfiles.LoadActiveOrDefault(root).Name == "second-profile",
            "The replacement calibration was not activated.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void TestCalibrationLibrary()
{
    var root = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "tmp",
        $"calibration-library-{Guid.NewGuid():N}"));
    var allowedRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tmp")) +
        Path.DirectorySeparatorChar;
    Assert(root.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase),
        "Calibration library test path escaped the workspace tmp directory.");
    try
    {
        var profile = CreateCalibratedFirstLevel();
        TouchCalibrationProfiles.SaveAndActivate(root, profile);
        File.WriteAllText(Path.Combine(root, "invalid.json"), "{ invalid json }");

        var catalog = TouchCalibrationProfiles.DiscoverSaved(root);
        Assert(catalog.Profiles.Count == 1,
            "The calibration library did not return the valid named profile.");
        Assert(catalog.Profiles[0].Profile.Name == profile.Name,
            "The calibration library returned the wrong profile.");
        Assert(catalog.Issues.Count == 1 && catalog.Issues[0].FileName == "invalid.json",
            "The calibration library did not isolate the invalid profile.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static TouchCalibrationProfile CreateCalibratedFirstLevel()
{
    var baseline = Enumerable.Range(0, 20).Select(index => (uint)(54000 + index * 10)).ToArray();
    var contact = Enumerable.Range(0, 20).Select(index => (uint)(230000 + index * 10)).ToArray();
    return TouchCalibrationCalculator.CreateTwoPointProfile(
        "first-level", baseline, contact, TouchCalibrationProfiles.CreateDefault());
}

static void TestTouchClassifier()
{
    var profile = TouchCalibrationProfiles.CreateDefault();
    var classifier = new TouchLevelClassifier(profile);
    Assert(classifier.Update(profile.Thresholds[0] + profile.Hysteresis - 1) == 0,
        "Classifier activated below the upper hysteresis boundary.");
    Assert(classifier.Update(profile.Thresholds[0] + profile.Hysteresis) == 1,
        "Classifier did not activate at the upper hysteresis boundary.");
    Assert(classifier.Update(profile.Thresholds[0] - profile.Hysteresis) == 1,
        "Classifier released too early.");
    Assert(classifier.Update(profile.Thresholds[0] - profile.Hysteresis - 1) == 0,
        "Classifier did not release below the lower hysteresis boundary.");
}

static void TestCalibrationOverflow()
{
    var profile = new TouchCalibrationProfile
    {
        Name = "overflow-test",
        Thresholds = [100000, 200000, 300000, 400000, 500000, 600000, uint.MaxValue],
        Hysteresis = 1000
    };
    var rejected = false;
    try
    {
        TouchCalibrationProfiles.Validate(profile);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Assert(rejected, "A profile that can overflow the activation boundary was accepted.");
}

static void Run(string name, Action test, ICollection<string> failures)
{
    try
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class ConstantSampleProvider(WaveFormat waveFormat, float value, int frames) : ISampleProvider
{
    private int remainingSamples = frames * waveFormat.Channels;
    public WaveFormat WaveFormat { get; } = waveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var samples = Math.Min(count, remainingSamples);
        Array.Fill(buffer, value, offset, samples);
        remainingSamples -= samples;
        return samples;
    }
}
