using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Input;

/// <summary>What the tracker wants the app to do after an input event or a tick.</summary>
public enum GestureSignal
{
    None,

    /// <summary>Open the wheel named in <see cref="DragGestureTracker.ActiveWheel"/>.</summary>
    Open,

    /// <summary>The wheel is already open; switch it to <see cref="DragGestureTracker.ActiveWheel"/>.</summary>
    Switch,

    /// <summary>The drag ended; hide the wheel if it's still showing.</summary>
    Close,
}

/// <summary>
/// Decides when a wheel should open during a drag (spec §4.1), from raw mouse and keyboard events.
/// Pure logic with caller-supplied timestamps so it can be unit-tested; the low-level hooks feed it.
/// <para>
/// Rules: the left button is down and the mouse has moved past the drag threshold; an activation key
/// was pressed <em>after</em> the drag started (a key held from before the drag never opens a wheel,
/// so Ctrl-drag copy and Shift-drag move keep working); and the key stays down for the hold delay.
/// Once a wheel is open, pressing another activation key switches wheels at once.
/// </para>
/// </summary>
public sealed class DragGestureTracker
{
    private readonly int _thresholdX;
    private readonly int _thresholdY;

    private ActivationKeys _keys;
    private long _holdDelayMs;

    private bool _buttonDown;
    private bool _dragging;
    private int _originX;
    private int _originY;

    // The key currently counting towards the hold delay, and when it went down.
    private ModifierKey? _pendingKey;
    private long _pendingSince;

    // Keys that were already down when the drag started; they don't count until released and pressed again.
    private readonly HashSet<ModifierKey> _staleKeys = [];
    private readonly HashSet<ModifierKey> _downKeys = [];

    public DragGestureTracker(ActivationKeys keys, int holdDelayMs, int dragThresholdX = 4, int dragThresholdY = 4)
    {
        _keys = keys;
        _holdDelayMs = holdDelayMs;
        _thresholdX = Math.Max(1, dragThresholdX);
        _thresholdY = Math.Max(1, dragThresholdY);
    }

    public bool IsDragging => _dragging;

    public bool IsWheelOpen => ActiveWheel is not null;

    public WheelKind? ActiveWheel { get; private set; }

    public void Configure(ActivationKeys keys, int holdDelayMs)
    {
        _keys = keys;
        _holdDelayMs = holdDelayMs;
    }

    public GestureSignal OnLeftButtonDown(int x, int y)
    {
        Reset();
        _buttonDown = true;
        _originX = x;
        _originY = y;
        return GestureSignal.None;
    }

    public GestureSignal OnMouseMove(int x, int y, long nowMs)
    {
        if (_buttonDown && !_dragging &&
            (Math.Abs(x - _originX) >= _thresholdX || Math.Abs(y - _originY) >= _thresholdY))
        {
            _dragging = true;
            _staleKeys.Clear();
            _staleKeys.UnionWith(_downKeys);
        }

        return Tick(nowMs);
    }

    public GestureSignal OnLeftButtonUp()
    {
        var wasOpen = IsWheelOpen;
        Reset();
        return wasOpen ? GestureSignal.Close : GestureSignal.None;
    }

    public GestureSignal OnKey(ModifierKey key, bool isDown, long nowMs)
    {
        if (isDown)
        {
            if (!_downKeys.Add(key))
            {
                // Auto-repeat of a key that's already down.
                return Tick(nowMs);
            }

            if (!_dragging)
            {
                return GestureSignal.None;
            }

            if (IsWheelOpen)
            {
                var wheel = _keys.WheelFor(key);
                if (wheel == ActiveWheel)
                {
                    return GestureSignal.None;
                }

                ActiveWheel = wheel;
                return GestureSignal.Switch;
            }

            if (!_staleKeys.Contains(key))
            {
                _pendingKey = key;
                _pendingSince = nowMs;
            }

            return Tick(nowMs);
        }

        _downKeys.Remove(key);
        _staleKeys.Remove(key);
        if (_pendingKey == key)
        {
            // Released before the hold delay: a quick press. Leave the drag to Explorer.
            _pendingKey = null;
        }

        return GestureSignal.None;
    }

    /// <summary>Call on a timer while dragging so the wheel opens even if the mouse stops moving.</summary>
    public GestureSignal Tick(long nowMs)
    {
        if (!_dragging || IsWheelOpen || _pendingKey is not { } key)
        {
            return GestureSignal.None;
        }

        if (nowMs - _pendingSince < _holdDelayMs)
        {
            return GestureSignal.None;
        }

        ActiveWheel = _keys.WheelFor(key);
        _pendingKey = null;
        return GestureSignal.Open;
    }

    /// <summary>The wheel was dismissed by the app (drop handled, Esc, cancel).</summary>
    public void WheelClosed()
    {
        ActiveWheel = null;
        _pendingKey = null;
    }

    private void Reset()
    {
        _buttonDown = false;
        _dragging = false;
        _pendingKey = null;
        _staleKeys.Clear();
        ActiveWheel = null;
    }
}
