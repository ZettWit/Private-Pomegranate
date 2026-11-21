namespace Pomegranate.Core.Tools;

/// <summary>What an editor window hands to its tool (spec §7.3, "Editors").</summary>
public abstract record ToolEdit;

/// <summary>A stretch of time in seconds.</summary>
public sealed record TimeRange(double Start, double End)
{
    public double Length => End - Start;
}

/// <summary>A rectangle in pixels of the upright image or video frame (rotation already applied).</summary>
public sealed record PixelRect(int X, int Y, int Width, int Height);

public sealed record CropEdit(PixelRect Area) : ToolEdit;

public sealed record TrimEdit(TimeRange Keep) : ToolEdit;

/// <param name="Silence">True: the ranges go quiet. False: they become a 1 kHz tone.</param>
public sealed record BleepEdit(IReadOnlyList<TimeRange> Ranges, bool Silence) : ToolEdit;

/// <summary>One page of the result: which source page (0-based) and how much to turn it clockwise.</summary>
public sealed record PageEdit(int SourceIndex, int ExtraRotation);

/// <param name="Selection">True when the user chose "Save selection": named "(selection)" instead of "(pages)".</param>
public sealed record PagesEdit(IReadOnlyList<PageEdit> Pages, bool Selection) : ToolEdit;

public enum RedactStyle
{
    Black,
    Pixelate,
    Blur,
}

public sealed record RedactBox(PixelRect Area, RedactStyle Style);

/// <param name="During">Video only: when the boxes apply. Null means the whole clip.</param>
public sealed record RedactEdit(IReadOnlyList<RedactBox> Boxes, TimeRange? During) : ToolEdit;

/// <param name="OverlayPath">A transparent PNG the size of the upright image, drawn by the editor.</param>
public sealed record AnnotateEdit(string OverlayPath) : ToolEdit;
