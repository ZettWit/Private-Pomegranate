using System.IO;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;

namespace Pomegranate.App.Wheel;

internal enum WheelAction
{
    Share,
    Float,
    CopyTo,
    MoveTo,
    CopyPath,
    Terminal,
}

/// <summary>Something on the wheel.</summary>
internal abstract record WheelItem(string Label, string Glyph, string? Tooltip = null)
{
    public virtual IReadOnlyList<WheelItem> Children => [];
}

internal sealed record ConvertItem(FileFormat Target) : WheelItem(Target.Label, Glyphs.For(Target), null);

internal sealed record ActionItem(WheelAction Action, string Label, string Glyph, IReadOnlyList<WheelItem> Folders)
    : WheelItem(Label, Glyph, null)
{
    public override IReadOnlyList<WheelItem> Children => Folders;
}

/// <summary>
/// A tool on the Tools wheel (spec §7.3). The tool itself carries its options as children; an option is a
/// ToolItem with that option and no children. <see cref="ToolOption.None"/> means the tool's default.
/// </summary>
internal sealed record ToolItem(ToolKind Kind, ToolOption Option, string Label, string Glyph, IReadOnlyList<WheelItem> Options)
    : WheelItem(Label, Glyph, null)
{
    public override IReadOnlyList<WheelItem> Children => Options;
}

/// <summary>A folder under Copy to… / Move to….</summary>
internal sealed record FolderItem(WheelAction Action, string Path)
    : WheelItem(FolderLabel(Path), Glyphs.Folder, Path)
{
    private static string FolderLabel(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

/// <summary>What one wheel shows.</summary>
internal sealed record WheelContent(WheelKind Kind, string Title, IReadOnlyList<WheelItem> Items, string? Message);

/// <summary>Segoe Fluent Icons code points (written as escapes so they're readable in the source).</summary>
internal static class Glyphs
{
    public const string Photo = "\uE91B";
    public const string Video = "\uE714";
    public const string Audio = "\uE8D6";
    public const string Archive = "\uE7B8";
    public const string Document = "\uE8A5";
    public const string Captions = "\uE7F0";
    public const string Slides = "\uE786";
    public const string Sheet = "\uE80A";
    public const string Share = "\uE72D";
    public const string Pin = "\uE718";
    public const string Copy = "\uE8C8";
    public const string Move = "\uE8DE";
    public const string Link = "\uE71B";
    public const string Terminal = "\uE756";
    public const string Folder = "\uE8B7";
    public const string More = "\uE712";
    public const string Cancel = "\uE711";

    public static string For(ToolKind tool) => tool switch
    {
        ToolKind.Compress => "\uE73F",
        ToolKind.Resize => "\uE740",
        ToolKind.Trim => "\uE78A",
        ToolKind.Crop => "\uE7A8",
        ToolKind.Rotate => "\uE7AD",
        ToolKind.Speed => "\uEC4A",
        ToolKind.Annotate => "\uE76D",
        ToolKind.Redact => "\uED1A",
        ToolKind.Bleep => "\uE767",
        ToolKind.Mute => "\uE74F",
        ToolKind.ExtractAudio => "\uEC4F",
        ToolKind.Normalize => "\uE9E9",
        ToolKind.Merge => "\uE8F1",
        ToolKind.Split => "\uE8C6",
        ToolKind.Pages => "\uE80A",
        ToolKind.StripMetadata => "\uE8EC",
        ToolKind.ReadCode => "\uED14",
        ToolKind.CopyText => "\uE8C1",
        ToolKind.Extract => "\uED25",
        ToolKind.Zip => "\uF012",
        _ => Document,
    };

    public static string For(FileFormat format) => format.Category switch
    {
        _ when format == KnownFormats.Pdf => Document,
        _ when format == KnownFormats.Txt => Document,
        FormatCategory.Image => Photo,
        FormatCategory.Video => Video,
        FormatCategory.Audio => Audio,
        FormatCategory.Archive => Archive,
        FormatCategory.Subtitle => Captions,
        FormatCategory.Presentation => Slides,
        FormatCategory.Spreadsheet => Sheet,
        _ => Document,
    };
}

/// <summary>Builds each wheel's content for a set of files (spec §4.3, §6).</summary>
internal sealed class WheelContentBuilder(
    Func<DropAnalyzer> analyzer,
    Func<ToolCatalog> tools,
    Func<FormatCatalog> formats,
    Func<PomegranateSettings> settings)
{
    private const int MaxFolders = 7;

    public WheelContent Build(WheelKind kind, IReadOnlyList<string> paths) => kind switch
    {
        WheelKind.Convert => Convert(paths),
        WheelKind.Tools => Tools(paths),
        _ => Actions(),
    };

    private WheelContent Convert(IReadOnlyList<string> paths)
    {
        var analysis = analyzer().Analyze(paths);
        var title = L.Get("Wheel_Convert");
        if (analysis.CanConvert)
        {
            return new(WheelKind.Convert, title, analysis.ConvertTargets.Select(t => (WheelItem)new ConvertItem(t)).ToArray(), null);
        }

        var message = analysis.Availability switch
        {
            ConvertAvailability.ContainsFolders => L.Get("Wheel_FoldersActionsOnly"),
            ConvertAvailability.NoSharedFormats => L.Get("Wheel_NoSharedFormats"),
            ConvertAvailability.UnsupportedType => L.Get("Wheel_UnsupportedType"),
            _ => L.Get("Wheel_NothingToConvert"),
        };
        return new(WheelKind.Convert, title, [], message);
    }

    private WheelContent Tools(IReadOnlyList<string> paths)
    {
        var offered = tools().Offer(paths, formats());
        var items = offered.Select(d => (WheelItem)new ToolItem(
                d.Kind,
                ToolOption.None,
                ToolLabel(d.Kind),
                Glyphs.For(d.Kind),
                d.Options.Select(o => (WheelItem)new ToolItem(d.Kind, o, OptionLabel(o), Glyphs.For(d.Kind), [])).ToArray()))
            .ToArray();
        return new(WheelKind.Tools, L.Get("Wheel_Tools"), items, items.Length == 0 ? L.Get("Wheel_NoTools") : null);
    }

    public static string ToolLabel(ToolKind kind) => L.Get($"Tool_{kind}");

    public static string OptionLabel(ToolOption option) => L.Get($"Option_{option}");

    private WheelContent Actions()
    {
        var s = settings();
        var folders = s.PinnedFolders.Concat(s.RecentFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .Take(MaxFolders)
            .ToArray();

        WheelItem[] items =
        [
            new ActionItem(WheelAction.Share, L.Get("Action_Share"), Glyphs.Share, []),
            new ActionItem(WheelAction.Float, L.Get("Action_Float"), Glyphs.Pin, []),
            new ActionItem(WheelAction.CopyTo, L.Get("Action_CopyTo"), Glyphs.Copy,
                folders.Select(f => (WheelItem)new FolderItem(WheelAction.CopyTo, f)).ToArray()),
            new ActionItem(WheelAction.MoveTo, L.Get("Action_MoveTo"), Glyphs.Move,
                folders.Select(f => (WheelItem)new FolderItem(WheelAction.MoveTo, f)).ToArray()),
            new ActionItem(WheelAction.CopyPath, L.Get("Action_CopyPath"), Glyphs.Link, []),
            new ActionItem(WheelAction.Terminal, L.Get("Action_Terminal"), Glyphs.Terminal, []),
        ];
        return new(WheelKind.Actions, L.Get("Wheel_Actions"), items, null);
    }
}
