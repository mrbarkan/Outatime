using System.IO.Pipes;

namespace Outatime.App;

/// A named mutex says whether another copy runs; a named pipe lets a second launch ask it to show its panel.
public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly string pipe;
    public bool IsFirst { get; }
    public event Action? Activated;

    public SingleInstance(string name)
    {
        pipe = name + "-" + Environment.UserName;
        mutex = new Mutex(true, @"Local\" + pipe, out var created);
        IsFirst = created;
        if (IsFirst) _ = Listen();
    }

    async Task Listen()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                Activated?.Invoke();
            }
            catch (Exception) { await Task.Delay(1000); }
        }
    }

    public void SignalFirst()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(2000);
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
