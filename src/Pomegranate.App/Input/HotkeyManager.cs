using System.Runtime.InteropServices;
using System.Windows.Interop;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Settings;

namespace Pomegranate.App.Input;

/// <summary>The global hotkey (spec §4.2), registered on a hidden message-only window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x504D; // "PM"
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _registered;

    public HotkeyManager(Action onPressed)
    {
        _onPressed = onPressed;
        _source = new HwndSource(new HwndSourceParameters("Pomegranate hotkey") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Registers <paramref name="hotkey"/>, replacing any earlier one. Returns false if another app owns it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = Native.RegisterHotKey(
            _source.Handle, HotkeyId, (uint)hotkey.Modifiers | ModNoRepeat, (uint)hotkey.VirtualKey);
        if (!_registered)
        {
            Log.Error($"Couldn't register the hotkey {HotkeyText.Describe(hotkey)} (error {Marshal.GetLastPInvokeError()}).");
        }

        return _registered;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void Unregister()
    {
        if (_registered)
        {
            Native.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }
}

/// <summary>"Ctrl + Alt + Space" and friends.</summary>
internal static class HotkeyText
{
    public static string Describe(Hotkey hotkey)
    {
        var parts = new List<string>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(hotkey.VirtualKey);
        parts.Add(key == System.Windows.Input.Key.Space ? "Space" : key.ToString());
        return string.Join(" + ", parts);
    }
}
