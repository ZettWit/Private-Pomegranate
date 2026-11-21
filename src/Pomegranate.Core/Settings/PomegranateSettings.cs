using System.Text.Json.Serialization;

namespace Pomegranate.Core.Settings;

/// <summary>A key that can open a wheel while dragging.</summary>
public enum ModifierKey
{
    Shift,
    Ctrl,
    Alt,
}

/// <summary>The three wheels.</summary>
public enum WheelKind
{
    Convert,
    Tools,
    Actions,
}

/// <summary>Modifier flags for the global hotkey. Values match Win32 MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global hotkey: modifiers plus a Win32 virtual-key code.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VkC = 0x43;

    /// <summary>
    /// Win+Alt+C ("C" for Convert). Win-based, so it can't clash with AltGr characters (AltGr is Ctrl+Alt),
    /// and Ctrl+Alt+Space was already taken on the first test PC.
    /// </summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Win | HotkeyModifiers.Alt, VkC);
}

/// <summary>Which key opens which wheel. All three must be different.</summary>
public sealed record ActivationKeys(ModifierKey Convert, ModifierKey Tools, ModifierKey Actions)
{
    public static ActivationKeys Default { get; } = new(ModifierKey.Shift, ModifierKey.Ctrl, ModifierKey.Alt);

    [JsonIgnore]
    public bool IsValid => Convert != Tools && Convert != Actions && Tools != Actions;

    public WheelKind WheelFor(ModifierKey key) =>
        key == Convert ? WheelKind.Convert
        : key == Tools ? WheelKind.Tools
        : WheelKind.Actions;
}

/// <summary>Default conversion quality (spec §11). Converting never asks; it always uses these.</summary>
public sealed record QualitySettings
{
    public int JpegQuality { get; init; } = 85;
    public int WebPQuality { get; init; } = 80;
    public int AvifQuality { get; init; } = 60;
    public int HeicQuality { get; init; } = 70;

    /// <summary>H.264 constant rate factor for MP4/MOV/MKV/AVI.</summary>
    public int H264Crf { get; init; } = 23;
    public string H264Preset { get; init; } = "medium";

    /// <summary>VP9 constant rate factor for WebM.</summary>
    public int Vp9Crf { get; init; } = 32;

    public int AacKbps { get; init; } = 192;
    public int OpusKbps { get; init; } = 128;

    /// <summary>LAME VBR quality (0 = best, 9 = smallest). 2 is about 190 kbps.</summary>
    public int Mp3VbrQuality { get; init; } = 2;

    /// <summary>Vorbis quality (−1 to 10). 5 is about 160 kbps.</summary>
    public int VorbisQuality { get; init; } = 5;

    public int WmaKbps { get; init; } = 192;
    public int GifFps { get; init; } = 15;
    public int GifMaxWidth { get; init; } = 640;

    /// <summary>Resolution of PDF pages turned into images (spec §7.2).</summary>
    public int PdfImageDpi { get; init; } = 300;

    /// <summary>Width in pixels of slides turned into images.</summary>
    public int SlideImageWidth { get; init; } = 1920;
}

/// <summary>One number per Compress level (spec §7.3, §11).</summary>
public sealed record CompressLevels(int Light, int Medium, int Strong)
{
    public int For(Tools.ToolOption level) => level switch
    {
        Tools.ToolOption.Light => Light,
        Tools.ToolOption.Strong => Strong,
        _ => Medium,
    };
}

/// <summary>What the Compress levels mean (spec §11). A 0 cap means "no cap".</summary>
public sealed record ToolSettings
{
    /// <summary>JPEG / WebP / AVIF quality.</summary>
    public CompressLevels ImageQuality { get; init; } = new(80, 65, 50);

    public CompressLevels ImageMaxSide { get; init; } = new(0, 2560, 1600);

    /// <summary>H.264 CRF; VP9 adds 6.</summary>
    public CompressLevels VideoCrf { get; init; } = new(26, 28, 31);

    public CompressLevels VideoMaxHeight { get; init; } = new(0, 1080, 720);

    /// <summary>The soundtrack of a compressed video (AAC or Opus).</summary>
    public CompressLevels VideoAudioKbps { get; init; } = new(128, 96, 64);

    /// <summary>MP3, AAC and WMA bitrate. Opus uses 60% of it.</summary>
    public CompressLevels AudioKbps { get; init; } = new(160, 128, 96);

    public CompressLevels VorbisQuality { get; init; } = new(4, 3, 1);

    /// <summary>Highest resolution of a PDF's pictures.</summary>
    public CompressLevels PdfDpi { get; init; } = new(200, 150, 100);

    public CompressLevels PdfJpegQuality { get; init; } = new(80, 70, 55);
}

/// <summary>Everything the Settings window controls (spec §11).</summary>
public sealed record PomegranateSettings
{
    public ActivationKeys Keys { get; init; } = ActivationKeys.Default;
    public int HoldDelayMs { get; init; } = 250;
    public Hotkey Hotkey { get; init; } = Hotkey.Default;

    /// <summary>Null means "next to the original".</summary>
    public string? OutputFolder { get; init; }

    public bool StartWithWindows { get; init; } = true;
    public bool PauseInFullScreen { get; init; } = true;

    /// <summary>Process names (e.g. "game.exe") where the wheel never opens.</summary>
    public IReadOnlyList<string> ExcludedApps { get; init; } = [];

    public QualitySettings Quality { get; init; } = new();

    public ToolSettings Tools { get; init; } = new();

    /// <summary>Folders pinned to the top of the Copy to… / Move to… list.</summary>
    public IReadOnlyList<string> PinnedFolders { get; init; } = [];

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    public bool FirstRunCompleted { get; init; }

    public const int MaxHoldDelayMs = 2000;
    public const int MaxRecentFolders = 8;

    /// <summary>Fixes values a hand-edited or old settings file might have wrong.</summary>
    public PomegranateSettings Normalize() => this with
    {
        Keys = Keys is { IsValid: true } ? Keys : ActivationKeys.Default,
        HoldDelayMs = Math.Clamp(HoldDelayMs, 0, MaxHoldDelayMs),
        Hotkey = Hotkey ?? Hotkey.Default,
        Quality = Quality ?? new QualitySettings(),
        Tools = Normalize(Tools),
        ExcludedApps = ExcludedApps ?? Array.Empty<string>(),
        PinnedFolders = PinnedFolders ?? Array.Empty<string>(),
        RecentFolders = (RecentFolders ?? Array.Empty<string>()).Take(MaxRecentFolders).ToArray(),
    };

    /// <summary>An old settings file has no tool settings, or only some of them.</summary>
    private static ToolSettings Normalize(ToolSettings? tools)
    {
        var defaults = new ToolSettings();
        if (tools is null)
        {
            return defaults;
        }

        return tools with
        {
            ImageQuality = Clamp(tools.ImageQuality ?? defaults.ImageQuality, 1, 100),
            ImageMaxSide = tools.ImageMaxSide ?? defaults.ImageMaxSide,
            VideoCrf = Clamp(tools.VideoCrf ?? defaults.VideoCrf, 0, 51),
            VideoMaxHeight = tools.VideoMaxHeight ?? defaults.VideoMaxHeight,
            VideoAudioKbps = Clamp(tools.VideoAudioKbps ?? defaults.VideoAudioKbps, 16, 512),
            AudioKbps = Clamp(tools.AudioKbps ?? defaults.AudioKbps, 16, 512),
            VorbisQuality = Clamp(tools.VorbisQuality ?? defaults.VorbisQuality, -1, 10),
            PdfDpi = Clamp(tools.PdfDpi ?? defaults.PdfDpi, 36, 600),
            PdfJpegQuality = Clamp(tools.PdfJpegQuality ?? defaults.PdfJpegQuality, 1, 100),
        };

        static CompressLevels Clamp(CompressLevels l, int min, int max) =>
            new(Math.Clamp(l.Light, min, max), Math.Clamp(l.Medium, min, max), Math.Clamp(l.Strong, min, max));
    }

    /// <summary>Moves <paramref name="folder"/> to the front of the recent list.</summary>
    public PomegranateSettings WithRecentFolder(string folder) => this with
    {
        RecentFolders = new[] { folder }
            .Concat(RecentFolders.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxRecentFolders)
            .ToArray(),
    };
}
