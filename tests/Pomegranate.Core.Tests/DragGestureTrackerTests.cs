using Pomegranate.Core.Input;
using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Tests;

public class DragGestureTrackerTests
{
    private static DragGestureTracker Create() => new(ActivationKeys.Default, holdDelayMs: 250);

    private static DragGestureTracker StartDrag()
    {
        var t = Create();
        t.OnLeftButtonDown(100, 100);
        t.OnMouseMove(120, 100, 0);
        Assert.True(t.IsDragging);
        return t;
    }

    [Fact]
    public void Holding_shift_during_a_drag_opens_convert_after_the_delay()
    {
        var t = StartDrag();

        Assert.Equal(GestureSignal.None, t.OnKey(ModifierKey.Shift, isDown: true, nowMs: 1000));
        Assert.Equal(GestureSignal.None, t.Tick(1249));
        Assert.Equal(GestureSignal.Open, t.Tick(1250));
        Assert.Equal(WheelKind.Convert, t.ActiveWheel);
    }

    [Fact]
    public void Ctrl_opens_tools_and_alt_opens_actions()
    {
        var t = StartDrag();
        t.OnKey(ModifierKey.Ctrl, true, 0);
        t.Tick(300);
        Assert.Equal(WheelKind.Tools, t.ActiveWheel);

        t = StartDrag();
        t.OnKey(ModifierKey.Alt, true, 0);
        t.OnMouseMove(130, 100, 300);
        Assert.Equal(WheelKind.Actions, t.ActiveWheel);
    }

    [Fact]
    public void A_quick_press_leaves_the_drag_to_explorer()
    {
        var t = StartDrag();

        t.OnKey(ModifierKey.Shift, true, 1000);
        t.OnKey(ModifierKey.Shift, false, 1100);

        Assert.Equal(GestureSignal.None, t.Tick(2000));
        Assert.False(t.IsWheelOpen);
    }

    [Fact]
    public void A_key_held_from_before_the_drag_does_not_open_a_wheel()
    {
        var t = Create();
        t.OnKey(ModifierKey.Ctrl, true, 0);
        t.OnLeftButtonDown(100, 100);
        t.OnMouseMove(120, 100, 10);

        Assert.Equal(GestureSignal.None, t.Tick(5000));

        // Releasing and pressing it again during the drag does count.
        t.OnKey(ModifierKey.Ctrl, false, 5000);
        t.OnKey(ModifierKey.Ctrl, true, 5100);
        Assert.Equal(GestureSignal.Open, t.Tick(5350));
    }

    [Fact]
    public void Pressing_a_key_without_dragging_does_nothing()
    {
        var t = Create();
        t.OnLeftButtonDown(100, 100);
        t.OnMouseMove(101, 101, 0); // below the drag threshold

        t.OnKey(ModifierKey.Shift, true, 0);

        Assert.Equal(GestureSignal.None, t.Tick(1000));
        Assert.False(t.IsDragging);
    }

    [Fact]
    public void Another_key_switches_an_open_wheel_immediately()
    {
        var t = StartDrag();
        t.OnKey(ModifierKey.Shift, true, 0);
        t.Tick(250);

        Assert.Equal(GestureSignal.Switch, t.OnKey(ModifierKey.Alt, true, 260));
        Assert.Equal(WheelKind.Actions, t.ActiveWheel);
        Assert.Equal(GestureSignal.None, t.OnKey(ModifierKey.Alt, true, 270)); // auto-repeat
    }

    [Fact]
    public void Wheel_stays_open_after_the_key_is_released()
    {
        var t = StartDrag();
        t.OnKey(ModifierKey.Shift, true, 0);
        t.Tick(250);

        t.OnKey(ModifierKey.Shift, false, 400);

        Assert.True(t.IsWheelOpen);
    }

    [Fact]
    public void Releasing_the_mouse_closes_and_resets()
    {
        var t = StartDrag();
        t.OnKey(ModifierKey.Shift, true, 0);
        t.Tick(250);

        Assert.Equal(GestureSignal.Close, t.OnLeftButtonUp());
        Assert.False(t.IsWheelOpen);
        Assert.False(t.IsDragging);
        Assert.Equal(GestureSignal.None, t.OnLeftButtonUp());
    }

    [Fact]
    public void Custom_keys_are_respected()
    {
        var t = new DragGestureTracker(new ActivationKeys(ModifierKey.Alt, ModifierKey.Shift, ModifierKey.Ctrl), 0);
        t.OnLeftButtonDown(0, 0);
        t.OnMouseMove(50, 0, 0);

        t.OnKey(ModifierKey.Alt, true, 0);

        Assert.Equal(WheelKind.Convert, t.ActiveWheel);
    }
}
