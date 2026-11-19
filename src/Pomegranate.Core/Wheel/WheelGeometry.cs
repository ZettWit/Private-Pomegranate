namespace Pomegranate.Core.Wheel;

/// <summary>Radii of the wheel's zones, in device-independent pixels from the center.</summary>
/// <param name="CenterRadius">Inside this is the cancel zone.</param>
/// <param name="InnerRadius">Outer edge of the inner ring.</param>
/// <param name="OuterRadius">Outer edge of the outer ring.</param>
public readonly record struct WheelMetrics(double CenterRadius, double InnerRadius, double OuterRadius)
{
    public static WheelMetrics Default { get; } = new(36, 120, 196);
}

public enum WheelZone
{
    /// <summary>Beyond the wheel. Dropping here cancels.</summary>
    Outside,

    /// <summary>The middle of the wheel. Dropping here cancels.</summary>
    Center,

    Inner,
    Outer,
}

public readonly record struct WheelHit(WheelZone Zone, int Index)
{
    public static WheelHit Outside { get; } = new(WheelZone.Outside, -1);
    public static WheelHit Center { get; } = new(WheelZone.Center, -1);
}

/// <summary>
/// Angle math for the wheel. Angles are in radians, 0 at 12 o'clock, increasing clockwise
/// (screen coordinates: y grows downwards). Inner slot 0 is centered at the top.
/// </summary>
public static class WheelGeometry
{
    private const double FullTurn = 2 * Math.PI;

    /// <summary>Angle of point (dx, dy) relative to the center, in [0, 2π).</summary>
    public static double AngleOf(double dx, double dy)
    {
        var angle = Math.Atan2(dx, -dy);
        return angle < 0 ? angle + FullTurn : angle;
    }

    public static double InnerSlotWidth(int innerCount) => FullTurn / innerCount;

    /// <summary>Center angle of inner slot <paramref name="index"/>.</summary>
    public static double InnerSlotAngle(int index, int innerCount) => index * InnerSlotWidth(innerCount);

    /// <summary>Outer slots are as wide as inner ones, unless there are too many to fit around the circle.</summary>
    public static double OuterSlotWidth(int innerCount, int outerCount) =>
        Math.Min(InnerSlotWidth(innerCount), FullTurn / outerCount);

    /// <summary>Center angle of outer slot <paramref name="index"/>: the outer arc is centered on the More slot.</summary>
    public static double OuterSlotAngle(int index, int innerCount, int outerCount, int moreIndex)
    {
        var width = OuterSlotWidth(innerCount, outerCount);
        var angle = InnerSlotAngle(moreIndex, innerCount) + (index - (outerCount - 1) / 2.0) * width;
        return Normalize(angle);
    }

    /// <summary>Which part of the wheel the point (dx, dy) from the center is over.</summary>
    public static WheelHit HitTest(
        double dx, double dy, WheelMetrics metrics, int innerCount, int outerCount, int moreIndex, bool outerOpen)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < metrics.CenterRadius)
        {
            return WheelHit.Center;
        }

        var angle = AngleOf(dx, dy);

        if (distance < metrics.InnerRadius)
        {
            if (innerCount <= 0)
            {
                return WheelHit.Center;
            }

            var width = InnerSlotWidth(innerCount);
            var index = (int)Math.Floor(Normalize(angle + width / 2) / width) % innerCount;
            return new(WheelZone.Inner, index);
        }

        if (outerOpen && outerCount > 0 && moreIndex >= 0 && distance < metrics.OuterRadius)
        {
            var width = OuterSlotWidth(innerCount, outerCount);
            var delta = SignedDelta(angle, InnerSlotAngle(moreIndex, innerCount));
            var position = delta / width + (outerCount - 1) / 2.0;
            var index = (int)Math.Round(position, MidpointRounding.AwayFromZero);
            if (index >= 0 && index < outerCount && Math.Abs(position - index) <= 0.5)
            {
                return new(WheelZone.Outer, index);
            }
        }

        return WheelHit.Outside;
    }

    private static double Normalize(double angle)
    {
        angle %= FullTurn;
        return angle < 0 ? angle + FullTurn : angle;
    }

    /// <summary>Shortest signed difference a − b, in (−π, π].</summary>
    private static double SignedDelta(double a, double b)
    {
        var d = Normalize(a - b);
        return d > Math.PI ? d - FullTurn : d;
    }
}
