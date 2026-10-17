using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows.Threading;
using Windock.Core.Logging;

namespace Windock.Composition;

/// Lets a second launch hand a command to the running Windock, e.g. "Windock.exe --pin <file>"
/// from the Explorer context menu. One named pipe per Windows session.
internal sealed class CommandChannel : IDisposable
{
    private static readonly string PipeName = $"Windock.Commands.{Process.GetCurrentProcess().SessionId}";

    private readonly CancellationTokenSource _stop = new();
    private readonly Dispatcher _dispatcher;
    private readonly ILog _log;

    public CommandChannel(Dispatcher dispatcher, ILog log)
    {
        _dispatcher = dispatcher;
        _log = log;
        _ = Task.Run(ListenAsync);
    }

    /// (verb, argument), raised on the UI thread.
    public event Action<string, string>? CommandReceived;

    public static bool Send(string verb, string argument)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{verb}\t{argument}");
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(_stop.Token);
                if (line?.Split('\t', 2) is [var verb, var argument])
                    _ = _dispatcher.BeginInvoke(() => CommandReceived?.Invoke(verb, argument));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                _log.Error("command pipe failed", ex);
                await Task.Delay(500);
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
