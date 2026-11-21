namespace Pomegranate.Core.Tools;

/// <summary>The file tools on the Tools wheel (spec §7.3), in the order the wheel shows them.</summary>
public enum ToolKind
{
    Compress,
    Resize,
    Trim,
    Crop,
    Rotate,
    Speed,
    Annotate,
    Redact,
    Bleep,
    Mute,
    ExtractAudio,
    Normalize,
    Merge,
    Split,
    Pages,
    StripMetadata,
    ReadCode,
    CopyText,
    Extract,
    Zip,
}

/// <summary>A choice on a tool's outer ring (spec §7.3, "Options on the outer ring").</summary>
public enum ToolOption
{
    None,

    Light,
    Medium,
    Strong,

    Scale50,
    Scale25,
    Long3840,
    Long1920,
    Long1280,

    Speed05,
    Speed15,
    Speed2,
    Speed4,

    RotateLeft,
    RotateRight,
    Rotate180,
    Flip,
}

/// <summary>Whether a tool runs once per file or once for the whole selection.</summary>
public enum ToolInput
{
    EachFile,

    /// <summary>Merge, Zip: every dropped item goes into one result.</summary>
    AllTogether,
}

public static class ToolOptions
{
    public static double SpeedFactor(ToolOption option) => option switch
    {
        ToolOption.Speed05 => 0.5,
        ToolOption.Speed15 => 1.5,
        ToolOption.Speed4 => 4,
        _ => 2,
    };

    /// <summary>"2x", "0.5x": used in output names.</summary>
    public static string SpeedText(ToolOption option) =>
        SpeedFactor(option).ToString(System.Globalization.CultureInfo.InvariantCulture) + "x";

    /// <summary>Resize: the scale factor, or null when the option is a long-side size.</summary>
    public static double? Scale(ToolOption option) => option switch
    {
        ToolOption.Scale25 => 0.25,
        ToolOption.Long3840 or ToolOption.Long1920 or ToolOption.Long1280 => null,
        _ => 0.5,
    };

    /// <summary>Resize: the long side in pixels, or null when the option is a scale factor.</summary>
    public static int? LongSide(ToolOption option) => option switch
    {
        ToolOption.Long3840 => 3840,
        ToolOption.Long1920 => 1920,
        ToolOption.Long1280 => 1280,
        _ => null,
    };

    /// <summary>Rotate: clockwise degrees (0 for Flip).</summary>
    public static int Degrees(ToolOption option) => option switch
    {
        ToolOption.RotateLeft => 270,
        ToolOption.Rotate180 => 180,
        ToolOption.Flip => 0,
        _ => 90,
    };
}
