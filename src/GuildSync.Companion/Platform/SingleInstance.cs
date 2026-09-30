using System.IO.Pipes;

namespace GuildSync.Companion.Platform;

public static class SingleInstance
{
    public static Mutex? Claim(string name, Action onShow)
    {
        Mutex mutex;
        bool created;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out created);
        }
        catch (UnauthorizedAccessException)
        {
            TrySignal(name);
            return null;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            TrySignal(name);
            return null;
        }

        if (!created)
        {
            TrySignal(name);
            mutex.Dispose();
            return null;
        }

        _ = Task.Run(() => Listen(name, onShow));
        return mutex;
    }

    private static void Listen(string name, Action onShow)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                server.WaitForConnection();
                onShow();
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private static void TrySignal(string name)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
            client.Connect(400);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }
}
