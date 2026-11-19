namespace Pomegranate.Core.Wheel;

/// <summary>
/// Tracks which slot is highlighted as the cursor moves, and which slot's children (if any) are shown
/// on the outer ring. A slot's outer ring opens when the cursor reaches the slot, stays open while the
/// cursor is on it or on the outer ring, and closes when the cursor goes to another slot or the center.
/// </summary>
public sealed class WheelState<T>(WheelLayout<T> layout, WheelMetrics metrics)
{
    public WheelLayout<T> Layout { get; } = layout;

    public WheelMetrics Metrics { get; } = metrics;

    /// <summary>The inner slot whose children are on the outer ring, or -1.</summary>
    public int OpenIndex { get; private set; } = -1;

    public bool OuterOpen => OpenIndex >= 0;

    public IReadOnlyList<T> OuterItems => OpenIndex >= 0 ? Layout.Inner[OpenIndex].Children : [];

    public WheelHit Hit { get; private set; } = WheelHit.Center;

    /// <summary>The item under the cursor, if any. The More slot itself is not an item.</summary>
    public T? SelectedItem => Hit.Zone switch
    {
        WheelZone.Inner when !Layout.Inner[Hit.Index].IsMore => Layout.Inner[Hit.Index].Item,
        WheelZone.Outer => OuterItems[Hit.Index],
        _ => default,
    };

    public bool HasSelection => Hit.Zone switch
    {
        WheelZone.Inner => !Layout.Inner[Hit.Index].IsMore,
        WheelZone.Outer => true,
        _ => false,
    };

    /// <summary>Updates the state for a cursor at (dx, dy) from the center. Returns true if anything changed.</summary>
    public bool Update(double dx, double dy)
    {
        var hit = WheelGeometry.HitTest(
            dx, dy, Metrics, Layout.Inner.Count, OuterItems.Count, OpenIndex, OuterOpen);

        var openIndex = hit.Zone switch
        {
            WheelZone.Inner => Layout.Inner[hit.Index].HasChildren ? hit.Index : -1,
            WheelZone.Center => -1,
            _ => OpenIndex,
        };

        var changed = hit != Hit || openIndex != OpenIndex;
        Hit = hit;
        OpenIndex = openIndex;
        return changed;
    }

    /// <summary>Keyboard: moves the highlight by <paramref name="step"/> slots around the current ring.</summary>
    public void Step(int step)
    {
        if (Hit.Zone == WheelZone.Outer && OuterItems.Count > 0)
        {
            Hit = new(WheelZone.Outer, Mod(Hit.Index + step, OuterItems.Count));
            return;
        }

        if (Layout.Inner.Count == 0)
        {
            return;
        }

        var start = Hit.Zone == WheelZone.Inner ? Hit.Index : (step > 0 ? -1 : 0);
        Hit = new(WheelZone.Inner, Mod(start + step, Layout.Inner.Count));
        OpenIndex = Layout.Inner[Hit.Index].HasChildren ? Hit.Index : -1;
    }

    /// <summary>Keyboard: from a slot with children, moves out to its first child; from the outer ring, back in.</summary>
    public void ToggleRing()
    {
        if (Hit.Zone == WheelZone.Inner && Layout.Inner[Hit.Index].HasChildren)
        {
            OpenIndex = Hit.Index;
            Hit = new(WheelZone.Outer, 0);
        }
        else if (Hit.Zone == WheelZone.Outer)
        {
            Hit = new(WheelZone.Inner, OpenIndex);
        }
    }

    private static int Mod(int value, int m) => ((value % m) + m) % m;
}
