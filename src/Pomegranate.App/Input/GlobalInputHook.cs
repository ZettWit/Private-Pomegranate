using System.Runtime.InteropServices;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Input;
using Pomegranate.Core.Settings;

namespace Pomegranate.App.Input;

/// <summary>
/// Low-level mouse and keyboard hooks on a dedicated thread (spec §12.2). The hook callbacks feed a
/// <see cref="DragGestureTracker"/> and post its signals to the UI. Nothing is ever swallowed:
/// every event continues to the app it was meant for.
/// </summary>
internal sealed class GlobalInputHook : IDisposable
{
    private const uint TickIntervalMs = 25;

    private readonly DragGestureTracker _tracker;
    private readonly Action<GestureSignal, WheelKind?> _onSignal;
    private readonly Action _onEscape;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();

    // Delegates passed to SetWindowsHookEx must stay alive as long as the hooks do.
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyboardProc;

    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private uint _threadId;
    private bool _ticking;

    // With no window, SetTimer ignores the id we pass and returns its own.
    private nuint _timerId;

    public GlobalInputHook(ActivationKeys keys, int holdDelayMs, Action<GestureSignal, WheelKind?> onSignal, Action onEscape)
    {
        _tracker = new DragGestureTracker(
            keys,
            holdDelayMs,
            Native.GetSystemMetrics(Native.SM_CXDRAG),
            Native.GetSystemMetrics(Native.SM_CYDRAG));
        _onSignal = onSignal;
        _onEscape = onEscape;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "Pomegranate input hook" };
    }

    public void Start()
    {
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Thread-safe: settings can change from the UI thread.</summary>
    public void Configure(ActivationKeys keys, int holdDelayMs)
    {
        lock (_tracker)
        {
            _tracker.Configure(keys, holdDelayMs);
        }
    }

    /// <summary>The UI closed the wheel (drop, Esc, cancel, or the drag left it).</summary>
    public void WheelClosed()
    {
        lock (_tracker)
        {
            _tracker.WheelClosed();
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            Native.PostThreadMessageW(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _started.Dispose();
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        var module = Native.GetModuleHandleW(null);
        _mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            Log.Error($"Couldn't install input hooks (error {Marshal.GetLastPInvokeError()}). Drag activation won't work.");
        }

        _started.Set();

        while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == Native.WM_TIMER && _timerId != 0 && (nuint)msg.wParam == _timerId)
            {
                Process(t => t.Tick(Environment.TickCount64));
                continue;
            }

            Native.TranslateMessage(in msg);
            Native.DispatchMessageW(in msg);
        }

        Native.UnhookWindowsHookEx(_mouseHook);
        Native.UnhookWindowsHookEx(_keyboardHook);
    }

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var now = Environment.TickCount64;
            switch ((int)wParam)
            {
                case Native.WM_LBUTTONDOWN:
                    Process(t => t.OnLeftButtonDown(data.pt.X, data.pt.Y));
                    break;
                case Native.WM_MOUSEMOVE:
                    Process(t => t.OnMouseMove(data.pt.X, data.pt.Y, now));
                    break;
                case Native.WM_LBUTTONUP:
                    Process(t => t.OnLeftButtonUp());
                    break;
            }
        }

        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            if (ToModifier((int)data.vkCode) is { } key && (isDown || isUp))
            {
                var now = Environment.TickCount64;
                Process(t => t.OnKey(key, isDown, now));
            }
            else if (isDown && data.vkCode == Native.VK_ESCAPE)
            {
                bool open;
                lock (_tracker)
                {
                    open = _tracker.IsWheelOpen;
                }

                if (open)
                {
                    _onEscape();
                }
            }
        }

        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>Runs a tracker step under the lock, keeps the tick timer in sync, and forwards any signal.</summary>
    private void Process(Func<DragGestureTracker, GestureSignal> step)
    {
        GestureSignal signal;
        WheelKind? wheel;
        bool dragging;
        lock (_tracker)
        {
            signal = step(_tracker);
            wheel = _tracker.ActiveWheel;
            dragging = _tracker.IsDragging && !_tracker.IsWheelOpen;
        }

        // Tick only while a drag is waiting for the hold delay, so an idle PC isn't woken 40 times a second.
        if (dragging != _ticking)
        {
            _ticking = dragging;
            if (dragging)
            {
                _timerId = Native.SetTimer(IntPtr.Zero, 0, TickIntervalMs, IntPtr.Zero);
            }
            else if (_timerId != 0)
            {
                Native.KillTimer(IntPtr.Zero, _timerId);
                _timerId = 0;
            }
        }

        if (signal != GestureSignal.None)
        {
            _onSignal(signal, wheel);
        }
    }

    private static ModifierKey? ToModifier(int vk) => vk switch
    {
        Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT => ModifierKey.Shift,
        Native.VK_CONTROL or Native.VK_LCONTROL or Native.VK_RCONTROL => ModifierKey.Ctrl,
        Native.VK_MENU or Native.VK_LMENU or Native.VK_RMENU => ModifierKey.Alt,
        _ => null,
    };
}
