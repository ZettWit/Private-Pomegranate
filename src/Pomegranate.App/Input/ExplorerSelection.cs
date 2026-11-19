using System.Runtime.InteropServices;
using Pomegranate.App.Infrastructure;

namespace Pomegranate.App.Input;

/// <summary>
/// Reads which files are selected in the foreground File Explorer window or on the desktop
/// (for the hotkey, spec §12.3), through the Shell's automation objects.
/// </summary>
internal static class ExplorerSelection
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;

    /// <summary>Must run on an STA thread (the UI thread).</summary>
    public static IReadOnlyList<string> Get(IntPtr foreground)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return [];
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                var className = Native.ClassName(foreground);
                if (className is "Progman" or "WorkerW")
                {
                    return FromDesktop(shell);
                }

                if (className is "CabinetWClass" or "ExploreWClass")
                {
                    return FromExplorerWindow(shell, foreground);
                }

                return [];
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Error("Couldn't read the Explorer selection.", ex);
            return [];
        }
    }

    private static List<string> FromExplorerWindow(dynamic shell, IntPtr hwnd)
    {
        // With tabs, every tab of the window shows up with the same HWND. The active tab's
        // folder name is what the window title starts with.
        var title = WindowTitle(hwnd);
        List<string>? fallback = null;
        foreach (var window in shell.Windows())
        {
            try
            {
                if ((long)window.HWND != hwnd.ToInt64())
                {
                    continue;
                }

                var selection = Selected(window.Document);
                string location = window.LocationName ?? "";
                if (location.Length > 0 && title.StartsWith(location, StringComparison.OrdinalIgnoreCase))
                {
                    return selection;
                }

                fallback ??= selection;
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // A window that's closing, or not a folder view (e.g. Home); skip it.
            }
        }

        return fallback ?? [];
    }

    private static List<string> FromDesktop(dynamic shell)
    {
        var windows = shell.Windows();
        object location = CsidlDesktop;
        object root = Type.Missing;
        var desktop = windows.FindWindowSW(ref location, ref root, SwcDesktop, out int _, SwfoNeedDispatch);
        return desktop is null ? [] : Selected(desktop.Document);
    }

    private static List<string> Selected(dynamic document)
    {
        var paths = new List<string>();
        foreach (var item in document.SelectedItems())
        {
            string path = item.Path;
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var buffer = new char[512];
        var length = GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);
}
