using System.Globalization;
using System.IO;

namespace Pomegranate.App.Infrastructure;

/// <summary>
/// A plain text log in the app's data folder (<see cref="AppPaths"/>), one file per day, kept for 14 days.
/// It never leaves the PC (spec §2, "Network").
/// </summary>
internal static class Log
{
    private static readonly Lock Gate = new();

    /// <summary>A path File Explorer can open too, also for the installed app (see <see cref="AppPaths"/>).</summary>
    public static string Folder => AppPaths.LogFolder;

    public static string CurrentFile => Path.Combine(Folder, $"pomegranate-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(Folder, "pomegranate-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
