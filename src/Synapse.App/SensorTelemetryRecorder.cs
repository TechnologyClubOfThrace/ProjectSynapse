using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Synapse.App;

internal sealed record SensorTelemetrySession(
    Guid SessionId,
    DateTime StartedUtc,
    string SerialPort,
    string CalibrationProfile,
    string Composition,
    string AppVersion);

internal sealed record SensorTelemetrySample(
    DateTime RecordedUtc,
    Guid? SynapseId,
    string Phase,
    uint RawValue,
    uint SmoothValue,
    int FirmwareLevel,
    int AppLevel,
    uint? Sequence,
    ulong? UptimeMilliseconds);

/// <summary>
/// Writes optional sensor diagnostics on a dedicated background task. A full
/// or unavailable database must never block the serial, UI or audio paths.
/// </summary>
internal sealed class SensorTelemetryRecorder : IAsyncDisposable
{
    private const int QueueCapacity = 4096;
    private const int MaximumBatchSize = 100;
    private readonly string databasePath;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Stopwatch elapsed = new();
    private Channel<SensorTelemetrySample>? sampleChannel;
    private Task? writerTask;
    private long droppedSamples;
    private volatile bool isRecording;
    private bool disposed;

    public SensorTelemetryRecorder(string runtimeDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDataDirectory);
        DatabasePath = Path.Combine(
            Path.GetFullPath(runtimeDataDirectory),
            "sensor-telemetry.sqlite");
        databasePath = DatabasePath;
    }

    public event Action<Exception>? Faulted;

    public string DatabasePath { get; }

    public bool IsRecording => isRecording;

    public TimeSpan Elapsed => elapsed.Elapsed;

    public long DroppedSamples => Interlocked.Read(ref droppedSamples);

    public async Task StartAsync(SensorTelemetrySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateSession(session);
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (isRecording || writerTask is not null)
            {
                throw new InvalidOperationException("Sensor telemetry is already recording.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            Interlocked.Exchange(ref droppedSamples, 0);
            var channel = Channel.CreateBounded<SensorTelemetrySample>(new BoundedChannelOptions(
                QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            var ready = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            sampleChannel = channel;
            writerTask = Task.Run(() => RunWriterAsync(session, channel.Reader, ready));
            try
            {
                await ready.Task.ConfigureAwait(false);
            }
            catch
            {
                channel.Writer.TryComplete();
                await writerTask.ConfigureAwait(false);
                writerTask = null;
                sampleChannel = null;
                throw;
            }

            elapsed.Restart();
            isRecording = true;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public bool TryRecord(SensorTelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!isRecording)
        {
            return false;
        }
        ValidateSample(sample);

        var writer = sampleChannel?.Writer;
        if (writer is not null && writer.TryWrite(sample))
        {
            return true;
        }

        Interlocked.Increment(ref droppedSamples);
        return false;
    }

    public async Task StopAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        isRecording = false;
        elapsed.Stop();
        var channel = sampleChannel;
        var task = writerTask;
        sampleChannel = null;
        writerTask = null;
        channel?.Writer.TryComplete();
        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }
    }

    private async Task RunWriterAsync(
        SensorTelemetrySession session,
        ChannelReader<SensorTelemetrySample> reader,
        TaskCompletionSource ready)
    {
        SqliteConnection? connection = null;
        var initialized = false;
        long writtenSamples = 0;
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();
            connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await InitializeDatabaseAsync(connection, session).ConfigureAwait(false);
            initialized = true;
            ready.TrySetResult();

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                await using var transaction = await connection.BeginTransactionAsync()
                    .ConfigureAwait(false);
                await using var command = CreateInsertCommand(connection, transaction);
                var batchCount = 0;
                while (batchCount < MaximumBatchSize && reader.TryRead(out var sample))
                {
                    BindSample(command, session.SessionId, sample);
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    writtenSamples++;
                    batchCount++;
                }
                await transaction.CommitAsync().ConfigureAwait(false);
            }

            await FinishSessionAsync(
                connection,
                session.SessionId,
                "stopped",
                writtenSamples,
                DroppedSamples,
                null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
            if (initialized && connection is not null)
            {
                try
                {
                    await FinishSessionAsync(
                        connection,
                        session.SessionId,
                        "faulted",
                        writtenSamples,
                        DroppedSamples,
                        LimitError(exception.Message)).ConfigureAwait(false);
                }
                catch
                {
                    // The original database failure is the useful diagnostic.
                }
            }
            isRecording = false;
            elapsed.Stop();
            if (initialized)
            {
                Faulted?.Invoke(exception);
            }
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task InitializeDatabaseAsync(
        SqliteConnection connection,
        SensorTelemetrySession session)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA synchronous=FULL;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys=ON;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout=5000;")
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS telemetry_sessions (
                session_id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                stopped_utc TEXT,
                serial_port TEXT NOT NULL,
                calibration_profile TEXT NOT NULL,
                composition TEXT NOT NULL,
                app_version TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('active', 'stopped', 'faulted', 'interrupted')),
                sample_count INTEGER NOT NULL DEFAULT 0 CHECK (sample_count >= 0),
                dropped_sample_count INTEGER NOT NULL DEFAULT 0 CHECK (dropped_sample_count >= 0),
                last_error TEXT
            );

            CREATE TABLE IF NOT EXISTS telemetry_samples (
                sample_id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                synapse_id TEXT,
                recorded_utc TEXT NOT NULL,
                phase TEXT NOT NULL,
                raw_value INTEGER NOT NULL CHECK (raw_value >= 0),
                smooth_value INTEGER NOT NULL CHECK (smooth_value >= 0),
                firmware_level INTEGER NOT NULL CHECK (firmware_level BETWEEN 0 AND 7),
                app_level INTEGER NOT NULL CHECK (app_level BETWEEN 0 AND 7),
                sequence INTEGER,
                uptime_ms INTEGER,
                FOREIGN KEY (session_id) REFERENCES telemetry_sessions(session_id)
            );

            CREATE INDEX IF NOT EXISTS ix_telemetry_samples_session_time
                ON telemetry_samples(session_id, recorded_utc);
            CREATE INDEX IF NOT EXISTS ix_telemetry_samples_synapse
                ON telemetry_samples(synapse_id) WHERE synapse_id IS NOT NULL;
            PRAGMA user_version=1;
            """).ConfigureAwait(false);

        var now = DateTime.UtcNow.ToString("O");
        await using (var interrupted = connection.CreateCommand())
        {
            interrupted.CommandText = """
                UPDATE telemetry_sessions
                SET status = 'interrupted', stopped_utc = COALESCE(stopped_utc, $stoppedUtc)
                WHERE status = 'active';
                """;
            interrupted.Parameters.AddWithValue("$stoppedUtc", now);
            await interrupted.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO telemetry_sessions (
                session_id, started_utc, serial_port, calibration_profile,
                composition, app_version, status)
            VALUES (
                $sessionId, $startedUtc, $serialPort, $calibrationProfile,
                $composition, $appVersion, 'active');
            """;
        insert.Parameters.AddWithValue("$sessionId", session.SessionId.ToString("D"));
        insert.Parameters.AddWithValue("$startedUtc", session.StartedUtc.ToUniversalTime().ToString("O"));
        insert.Parameters.AddWithValue("$serialPort", session.SerialPort);
        insert.Parameters.AddWithValue("$calibrationProfile", session.CalibrationProfile);
        insert.Parameters.AddWithValue("$composition", session.Composition);
        insert.Parameters.AddWithValue("$appVersion", session.AppVersion);
        await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static SqliteCommand CreateInsertCommand(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO telemetry_samples (
                session_id, synapse_id, recorded_utc, phase, raw_value,
                smooth_value, firmware_level, app_level, sequence, uptime_ms)
            VALUES (
                $sessionId, $synapseId, $recordedUtc, $phase, $rawValue,
                $smoothValue, $firmwareLevel, $appLevel, $sequence, $uptimeMs);
            """;
        command.Parameters.Add("$sessionId", SqliteType.Text);
        command.Parameters.Add("$synapseId", SqliteType.Text);
        command.Parameters.Add("$recordedUtc", SqliteType.Text);
        command.Parameters.Add("$phase", SqliteType.Text);
        command.Parameters.Add("$rawValue", SqliteType.Integer);
        command.Parameters.Add("$smoothValue", SqliteType.Integer);
        command.Parameters.Add("$firmwareLevel", SqliteType.Integer);
        command.Parameters.Add("$appLevel", SqliteType.Integer);
        command.Parameters.Add("$sequence", SqliteType.Integer);
        command.Parameters.Add("$uptimeMs", SqliteType.Integer);
        return command;
    }

    private static void BindSample(
        SqliteCommand command,
        Guid sessionId,
        SensorTelemetrySample sample)
    {
        command.Parameters["$sessionId"].Value = sessionId.ToString("D");
        command.Parameters["$synapseId"].Value = sample.SynapseId?.ToString("D") ??
            (object)DBNull.Value;
        command.Parameters["$recordedUtc"].Value = sample.RecordedUtc.ToUniversalTime().ToString("O");
        command.Parameters["$phase"].Value = sample.Phase;
        command.Parameters["$rawValue"].Value = (long)sample.RawValue;
        command.Parameters["$smoothValue"].Value = (long)sample.SmoothValue;
        command.Parameters["$firmwareLevel"].Value = sample.FirmwareLevel;
        command.Parameters["$appLevel"].Value = sample.AppLevel;
        command.Parameters["$sequence"].Value = sample.Sequence is uint sequence
            ? (long)sequence
            : DBNull.Value;
        command.Parameters["$uptimeMs"].Value = sample.UptimeMilliseconds is ulong uptime &&
            uptime <= long.MaxValue
                ? (long)uptime
                : DBNull.Value;
    }

    private static async Task FinishSessionAsync(
        SqliteConnection connection,
        Guid sessionId,
        string status,
        long sampleCount,
        long droppedSampleCount,
        string? error)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE telemetry_sessions
            SET stopped_utc = $stoppedUtc,
                status = $status,
                sample_count = $sampleCount,
                dropped_sample_count = $droppedSampleCount,
                last_error = $lastError
            WHERE session_id = $sessionId;
            """;
        command.Parameters.AddWithValue("$stoppedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$sampleCount", sampleCount);
        command.Parameters.AddWithValue("$droppedSampleCount", droppedSampleCount);
        command.Parameters.AddWithValue("$lastError", error ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void ValidateSession(SensorTelemetrySession session)
    {
        if (session.SessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(session.SerialPort) ||
            string.IsNullOrWhiteSpace(session.CalibrationProfile) ||
            string.IsNullOrWhiteSpace(session.Composition) ||
            string.IsNullOrWhiteSpace(session.AppVersion))
        {
            throw new ArgumentException("Telemetry session metadata is incomplete.", nameof(session));
        }
    }

    private static void ValidateSample(SensorTelemetrySample sample)
    {
        if (sample.FirmwareLevel is < 0 or > 7 || sample.AppLevel is < 0 or > 7 ||
            string.IsNullOrWhiteSpace(sample.Phase) || sample.Phase.Length > 32)
        {
            throw new ArgumentException("Telemetry sample is invalid.", nameof(sample));
        }
    }

    private static string LimitError(string message) =>
        message.Length <= 500 ? message : message[..500];
}
