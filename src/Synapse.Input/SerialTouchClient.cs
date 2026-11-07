using System.IO.Ports;
using System.Text;
namespace Synapse.Input;

public sealed class SerialTouchClient : IDisposable
{
    private readonly object sync = new();
    private readonly StringBuilder receiveBuffer = new();
    private readonly TouchSequenceTracker sequenceTracker = new();
    private SerialPort? port;
    private bool disposed;
    private bool hasReceivedValidPacket;

    public event Action<TouchStateMessage>? MessageReceived;
    public event Action<Exception>? ConnectionFaulted;
    public event Action<string>? InvalidLineReceived;

    public bool IsConnected
    {
        get
        {
            lock (sync)
            {
                return port?.IsOpen == true;
            }
        }
    }

    public DateTime LastValidMessageUtc { get; private set; } = DateTime.MinValue;

    public static IReadOnlyList<string> GetAvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Connect(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DisconnectCore();

            var availablePorts = SerialPort.GetPortNames();
            if (!availablePorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Serial port is not available: {portName}");
            }

            var candidate = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            candidate.DataReceived += PortOnDataReceived;
            try
            {
                candidate.Open();
                port = candidate;
                receiveBuffer.Clear();
                sequenceTracker.Reset();
                hasReceivedValidPacket = false;
                LastValidMessageUtc = DateTime.UtcNow;
            }
            catch
            {
                candidate.DataReceived -= PortOnDataReceived;
                candidate.Dispose();
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (sync)
        {
            DisconnectCore();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            DisconnectCore();
            disposed = true;
        }
    }

    private void PortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        List<TouchStateMessage> messages = [];
        List<string> invalidLines = [];
        Exception? fault = null;
        lock (sync)
        {
            if (port is null || sender != port || !port.IsOpen)
            {
                return;
            }

            try
            {
                receiveBuffer.Append(port.ReadExisting());
                if (receiveBuffer.Length > 2048)
                {
                    invalidLines.Add("Serial receive buffer exceeded its safe limit.");
                    receiveBuffer.Clear();
                }

                while (TryTakeLine(receiveBuffer, out var line))
                {
                    if (TouchMessageParser.TryParse(line, out var message) && message is not null)
                    {
                        var sequenceResult = sequenceTracker.Accept(message);
                        if (sequenceResult is TouchSequenceResult.Duplicate or TouchSequenceResult.OutOfOrder)
                        {
                            invalidLines.Add($"{sequenceResult} serial sequence ignored.");
                            continue;
                        }
                        LastValidMessageUtc = DateTime.UtcNow;
                        hasReceivedValidPacket = true;
                        messages.Add(message);
                    }
                    else if (hasReceivedValidPacket && !string.IsNullOrWhiteSpace(line))
                    {
                        invalidLines.Add(line.Length <= TouchMessageParser.MaximumLineLength
                            ? line
                            : line[..TouchMessageParser.MaximumLineLength]);
                    }
                }
            }
            catch (Exception exception)
            {
                fault = exception;
            }
        }

        foreach (var line in invalidLines)
        {
            InvalidLineReceived?.Invoke(line);
        }
        foreach (var message in messages)
        {
            MessageReceived?.Invoke(message);
        }
        if (fault is not null)
        {
            ConnectionFaulted?.Invoke(fault);
        }
    }

    private void DisconnectCore()
    {
        var closingPort = port;
        if (closingPort is null)
        {
            return;
        }

        port = null;
        receiveBuffer.Clear();
        sequenceTracker.Reset();
        hasReceivedValidPacket = false;
        LastValidMessageUtc = DateTime.MinValue;

        closingPort.DataReceived -= PortOnDataReceived;
        try
        {
            if (closingPort.IsOpen)
            {
                closingPort.Close();
            }
        }
        finally
        {
            closingPort.Dispose();
        }
    }

    private static bool TryTakeLine(StringBuilder buffer, out string line)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\n')
            {
                continue;
            }

            line = buffer.ToString(0, index).TrimEnd('\r');
            buffer.Remove(0, index + 1);
            return true;
        }

        line = string.Empty;
        return false;
    }
}
