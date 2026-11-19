using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Pomegranate.App.Infrastructure;

/// <summary>A request from a second launch (or the right-click menu) to the running app.</summary>
internal sealed record AppCommand(string Command, IReadOnlyList<string> Paths)
{
    public const string OpenWheel = "open-wheel";
    public const string ShowSettings = "show-settings";

    /// <summary>
    /// Command line: <c>--wheel &lt;paths…&gt;</c>, <c>--wheel-list &lt;file&gt;</c> (one path per line, used by the
    /// right-click menu for very long selections; the file is deleted after reading), <c>--settings</c>,
    /// <c>--background</c>, or nothing.
    /// </summary>
    public static AppCommand? FromArgs(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && args[0].Equals("--wheel", StringComparison.OrdinalIgnoreCase))
        {
            return new(OpenWheel, args.Skip(1).ToArray());
        }

        if (args.Count > 1 && args[0].Equals("--wheel-list", StringComparison.OrdinalIgnoreCase))
        {
            return new(OpenWheel, ReadList(args[1]));
        }

        if (args.Count > 0 && args[0].Equals("--background", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new(ShowSettings, []);
    }

    private static string[] ReadList(string file)
    {
        try
        {
            // Only accept list files the shell extension wrote to the temp folder.
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            var paths = File.ReadAllLines(full).Where(l => l.Length > 0).ToArray();
            File.Delete(full);
            return paths;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error("Couldn't read the selection list.", ex);
            return [];
        }
    }
}

/// <summary>
/// Only one Pomegranate runs per user. A second launch sends its command to the first over a named
/// pipe that only the same user can open, then exits (spec §10).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Id = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    private static readonly string PipeName = "Pomegranate-" + Id;

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns null if another instance already owns the mutex.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\Pomegranate-" + Id, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static bool TrySend(AppCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            JsonSerializer.Serialize(client, command);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Error("Couldn't reach the running Pomegranate.", ex);
            return false;
        }
    }

    /// <summary>Listens for commands on a background task and hands each one to <paramref name="onCommand"/>.</summary>
    public void Listen(Action<AppCommand> onCommand)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    var command = await JsonSerializer.DeserializeAsync<AppCommand>(server, cancellationToken: _stop.Token);
                    if (command is not null)
                    {
                        onCommand(command);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    Log.Error("Bad message on the command pipe.", ex);
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
