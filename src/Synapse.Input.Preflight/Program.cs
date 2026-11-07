using Synapse.Input;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Synapse.Input.Preflight <COM port>");
    return 2;
}

var portName = args[0];
using var client = new SerialTouchClient();
var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var messages = new List<TouchStateMessage>();
var invalidCount = 0;

client.MessageReceived += message =>
{
    messages.Add(message);
    Console.WriteLine(
        $"PASS: level={message.ParticipationLevel}, raw={message.RawValue}, " +
        $"smooth={message.SmoothValue}, seq={message.Sequence}, uptime={message.UptimeMilliseconds}");
    if (messages.Count >= 3)
    {
        completion.TrySetResult();
    }
};
client.InvalidLineReceived += _ => invalidCount++;
client.ConnectionFaulted += exception => completion.TrySetException(exception);

try
{
    client.Connect(portName);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    await completion.Task.WaitAsync(timeout.Token);
    if (messages.Any(message => message.ParticipationLevel is < 0 or > 7))
    {
        throw new InvalidDataException("A participation level was outside 0-7.");
    }
    Console.WriteLine($"SERIAL PREFLIGHT: PASS ({messages.Count} packets, {invalidCount} ignored lines)");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SERIAL PREFLIGHT: FAIL - {exception.Message}");
    return 1;
}
