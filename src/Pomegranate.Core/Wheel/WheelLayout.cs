namespace Pomegranate.Core.Wheel;

/// <summary>
/// One slot on the inner ring. A slot can have children, shown on the outer ring while the slot is
/// hovered (e.g. "Copy to…" → recent folders). The "More" slot holds the options that didn't fit.
/// </summary>
public readonly record struct WheelSlot<T>(T? Item, bool IsMore, IReadOnlyList<T> Children)
{
    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// Splits a list of options into the inner ring (at most <see cref="MaxInnerSlots"/> slots). When
/// there are too many, the last slot becomes "More" and holds the rest (spec §5).
/// </summary>
public sealed class WheelLayout<T>
{
    public const int MaxInnerSlots = 8;

    private WheelLayout(IReadOnlyList<WheelSlot<T>> inner, int moreIndex)
    {
        Inner = inner;
        MoreIndex = moreIndex;
    }

    public IReadOnlyList<WheelSlot<T>> Inner { get; }

    /// <summary>Index of the "More" slot on the inner ring, or -1.</summary>
    public int MoreIndex { get; }

    public static WheelLayout<T> Create(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<T>>? childrenOf = null,
        int maxInnerSlots = MaxInnerSlots)
    {
        if (maxInnerSlots < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInnerSlots), "Need room for at least one item and More.");
        }

        WheelSlot<T> Slot(T item) => new(item, false, childrenOf?.Invoke(item) ?? []);

        if (items.Count <= maxInnerSlots)
        {
            return new(items.Select(Slot).ToArray(), -1);
        }

        var inner = items.Take(maxInnerSlots - 1).Select(Slot)
            .Append(new WheelSlot<T>(default, true, items.Skip(maxInnerSlots - 1).ToArray()))
            .ToArray();
        return new(inner, inner.Length - 1);
    }
}
