using Pomegranate.Core.Wheel;

namespace Pomegranate.Core.Tests;

public class WheelLayoutTests
{
    [Fact]
    public void Up_to_eight_items_fit_on_the_inner_ring()
    {
        var layout = WheelLayout<int>.Create(Enumerable.Range(0, 8).ToArray());

        Assert.Equal(8, layout.Inner.Count);
        Assert.Equal(-1, layout.MoreIndex);
        Assert.DoesNotContain(layout.Inner, s => s.IsMore || s.HasChildren);
    }

    [Fact]
    public void Extra_items_go_behind_More()
    {
        var layout = WheelLayout<int>.Create(Enumerable.Range(0, 10).ToArray());

        Assert.Equal(8, layout.Inner.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], layout.Inner.Take(7).Select(s => s.Item));
        Assert.True(layout.Inner[7].IsMore);
        Assert.Equal(7, layout.MoreIndex);
        Assert.Equal([7, 8, 9], layout.Inner[7].Children);
    }

    [Fact]
    public void Items_can_have_their_own_children()
    {
        var layout = WheelLayout<string>.Create(["Copy to", "Share"], i => i == "Copy to" ? ["A", "B"] : []);

        Assert.Equal(["A", "B"], layout.Inner[0].Children);
        Assert.False(layout.Inner[1].HasChildren);
    }

    [Fact]
    public void Empty_list_gives_an_empty_wheel()
    {
        Assert.Empty(WheelLayout<int>.Create([]).Inner);
    }
}

public class WheelGeometryTests
{
    private static readonly WheelMetrics M = new(CenterRadius: 30, InnerRadius: 100, OuterRadius: 160);

    [Theory]
    [InlineData(0, -60, 0)]   // straight up
    [InlineData(60, 0, 2)]    // right (90°)
    [InlineData(0, 60, 4)]    // down (180°)
    [InlineData(-60, 0, 6)]   // left (270°)
    [InlineData(-20, -60, 0)] // slightly left of top still slot 0 (within ±22.5°)
    [InlineData(-60, -60, 7)] // up-left (315°)
    public void Inner_slots_are_numbered_clockwise_from_the_top(double dx, double dy, int expected)
    {
        var hit = WheelGeometry.HitTest(dx, dy, M, innerCount: 8, outerCount: 0, moreIndex: -1, outerOpen: false);

        Assert.Equal(new WheelHit(WheelZone.Inner, expected), hit);
    }

    [Fact]
    public void Center_and_outside_are_cancel_zones()
    {
        Assert.Equal(WheelHit.Center, WheelGeometry.HitTest(5, 5, M, 8, 0, -1, false));
        Assert.Equal(WheelHit.Outside, WheelGeometry.HitTest(0, -130, M, 8, 0, -1, false));
    }

    [Fact]
    public void Outer_ring_only_counts_when_open()
    {
        // More is slot 7 (315°, up-left). Outer slot 1 of 3 is centered on it.
        var (dx, dy) = Point(315, 130);

        Assert.Equal(WheelHit.Outside, WheelGeometry.HitTest(dx, dy, M, 8, 3, 7, outerOpen: false));
        Assert.Equal(new WheelHit(WheelZone.Outer, 1), WheelGeometry.HitTest(dx, dy, M, 8, 3, 7, outerOpen: true));
    }

    [Fact]
    public void Outer_slots_spread_either_side_of_More()
    {
        var (lx, ly) = Point(270, 130); // one slot width (45°) counter-clockwise from More
        var (rx, ry) = Point(0, 130);   // one slot width clockwise from More

        Assert.Equal(new WheelHit(WheelZone.Outer, 0), WheelGeometry.HitTest(lx, ly, M, 8, 3, 7, true));
        Assert.Equal(new WheelHit(WheelZone.Outer, 2), WheelGeometry.HitTest(rx, ry, M, 8, 3, 7, true));
    }

    [Fact]
    public void Outer_angles_past_the_arc_are_outside()
    {
        var (dx, dy) = Point(135, 130); // opposite side of the wheel from More

        Assert.Equal(WheelHit.Outside, WheelGeometry.HitTest(dx, dy, M, 8, 3, 7, true));
    }

    [Fact]
    public void Many_outer_items_shrink_to_fit_the_circle()
    {
        Assert.Equal(2 * Math.PI / 8, WheelGeometry.OuterSlotWidth(8, 3), 6);
        Assert.Equal(2 * Math.PI / 20, WheelGeometry.OuterSlotWidth(8, 20), 6);
    }

    private static (double Dx, double Dy) Point(double degrees, double radius)
    {
        var rad = degrees * Math.PI / 180;
        return (Math.Sin(rad) * radius, -Math.Cos(rad) * radius);
    }
}

public class WheelStateTests
{
    private static readonly WheelMetrics M = new(30, 100, 160);

    private static WheelState<string> Create(int count) =>
        new(WheelLayout<string>.Create(Enumerable.Range(0, count).Select(i => $"item{i}").ToArray()), M);

    [Fact]
    public void Hovering_More_opens_the_outer_ring_and_another_slot_closes_it()
    {
        var state = Create(10);

        state.Update(-60, -60); // slot 7 = More
        Assert.True(state.OuterOpen);
        Assert.False(state.HasSelection);

        state.Update(-92, -92); // outward into the outer ring, still around 315°
        Assert.True(state.OuterOpen);
        Assert.Equal("item8", state.SelectedItem);

        state.Update(0, -60); // back to inner slot 0
        Assert.False(state.OuterOpen);
        Assert.Equal("item0", state.SelectedItem);
    }

    [Fact]
    public void Center_has_no_selection()
    {
        var state = Create(4);

        state.Update(0, 0);

        Assert.False(state.HasSelection);
        Assert.Null(state.SelectedItem);
    }

    [Fact]
    public void Keyboard_steps_wrap_around()
    {
        var state = Create(3);

        state.Step(1);
        Assert.Equal("item0", state.SelectedItem);
        state.Step(-1);
        Assert.Equal("item2", state.SelectedItem);
        state.Step(1);
        Assert.Equal("item0", state.SelectedItem);
    }

    [Fact]
    public void Slot_with_children_is_selectable_itself_and_opens_its_children()
    {
        var layout = WheelLayout<string>.Create(["Share", "Copy to", "Move to", "Path"], i => i == "Copy to" ? ["A", "B", "C"] : []);
        var state = new WheelState<string>(layout, M);

        state.Update(60, 0); // slot 1 of 4 is at 90° (right)
        Assert.Equal("Copy to", state.SelectedItem);
        Assert.Equal(["A", "B", "C"], state.OuterItems);

        state.Update(130, 0); // straight out: the middle child
        Assert.Equal("B", state.SelectedItem);

        state.Update(0, 60); // slot 2 (180°) has no children: outer ring closes
        Assert.False(state.OuterOpen);
        Assert.Equal("Move to", state.SelectedItem);
    }

    [Fact]
    public void Keyboard_can_enter_and_leave_the_outer_ring()
    {
        var state = Create(10);

        state.Step(-1); // from nothing, -1 lands on the last slot = More
        Assert.True(state.OuterOpen);
        state.ToggleRing();
        Assert.Equal("item7", state.SelectedItem);
        state.Step(2);
        Assert.Equal("item9", state.SelectedItem);
        state.ToggleRing();
        Assert.Equal(new WheelHit(WheelZone.Inner, 7), state.Hit);
    }
}
