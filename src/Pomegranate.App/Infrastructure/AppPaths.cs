using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace Pomegranate.App.Infrastructure;

/// <summary>
/// Where Pomegranate keeps files the user may open in File Explorer (logs, the welcome sample).
/// <para>
/// An installed MSIX app's writes to %LOCALAPPDATA% are quietly redirected into the package's own
/// folder, but File Explorer runs outside the package and sees the real %LOCALAPPDATA%. So for the
/// installed app these paths point straight at the redirected location
/// (…\Packages\&lt;family&gt;\LocalCache\Local\Pomegranate), which both the app and Explorer can see.
/// </para>
/// </summary>
internal static class AppPaths
{
    public static string DataFolder { get; } = Resolve();

    public static string LogFolder => Path.Combine(DataFolder, "logs");

    public static string TryItFolder => Path.Combine(DataFolder, "Try it");

    private static string Resolve()
    {
        if (AppInfo.IsPackaged)
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Local", "Pomegranate");
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or UnauthorizedAccessException)
            {
                // Fall back to the normal location; the app still works, Explorer may just show an empty folder.
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pomegranate");
    }
}
