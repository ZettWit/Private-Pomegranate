using System.Diagnostics;
using System.Text;

namespace Pomegranate.Engines;

/// <summary>Result of running a child process.</summary>
/// <param name="StandardErrorTail">The last lines the process wrote to stderr, for error messages.</param>
internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardErrorTail);

/// <summary>Runs engine executables without a window, at below-normal priority, killable on cancel.</summary>
internal static class ProcessRunner
{
    private const int MaxErrorLines = 25;

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null,
        bool captureOutput = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var errors = new Queue<string>();
        var outputDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputDone.TrySetResult();
                return;
            }

            onOutputLine?.Invoke(e.Data);
            if (captureOutput)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorDone.TrySetResult();
                return;
            }

            lock (errors)
            {
                errors.Enqueue(e.Data);
                if (errors.Count > MaxErrorLines)
                {
                    errors.Dequeue();
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Couldn't start {Path.GetFileName(fileName)}.");
        }

        TryLowerPriority(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputDone.Task, errorDone.Task).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string errorTail;
        lock (errors)
        {
            errorTail = string.Join(Environment.NewLine, errors);
        }

        return new ProcessResult(process.ExitCode, output.ToString(), errorTail);
    }

    private static void TryLowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process may already have exited.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
