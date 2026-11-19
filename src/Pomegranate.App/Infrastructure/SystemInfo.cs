using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Pomegranate.App.Infrastructure;

/// <summary>Colors and settings the wheel follows: accent color, light/dark, high contrast, animations.</summary>
internal static class SystemTheme
{
    public static bool IsDark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
    }

    public static bool HighContrast => SystemParameters.HighContrast;

    /// <summary>Windows' "Animation effects" setting.</summary>
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public static Color Accent
    {
        get
        {
            try
            {
                var c = new UISettings().GetColorValue(UIColorType.Accent);
                return Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return Color.FromRgb(0x00, 0x67, 0xC0);
            }
        }
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    public static Color TextOn(Color background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 150 ? Colors.Black : Colors.White;
    }
}

/// <summary>What's in the foreground: used to pause the wheel in full-screen apps and excluded apps.</summary>
internal static class Foreground
{
    /// <summary>True while a full-screen game, video or presentation is running.</summary>
    public static bool IsFullScreenApp()
    {
        if (Native.SHQueryUserNotificationState(out var state) != 0)
        {
            return false;
        }

        return state is Native.UserNotificationState.Busy
            or Native.UserNotificationState.RunningD3DFullScreen
            or Native.UserNotificationState.PresentationMode;
    }

    /// <summary>File name of the foreground window's process, e.g. "explorer.exe".</summary>
    public static string? ProcessName()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Where the app runs from and whether it's an installed MSIX package.</summary>
internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string ExePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pomegranate.exe");

    public static bool IsPackaged { get; } = DetectPackage();

    private static bool DetectPackage()
    {
        try
        {
            return global::Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
