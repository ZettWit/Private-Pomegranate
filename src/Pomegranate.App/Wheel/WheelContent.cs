using System.IO;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

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

/// <summary>Segoe Fluent Icons code points.</summary>
internal static class Glyphs
{
    public const string Photo = "";
    public const string Video = "";
    public const string Audio = "";
    public const string Archive = "";
    public const string Document = "";
    public const string Captions = "";
    public const string Share = "";
    public const string Pin = "";
    public const string Copy = "";
    public const string Move = "";
    public const string Link = "";
    public const string Terminal = "";
    public const string Folder = "";
    public const string More = "";
    public const string Cancel = "";

    public static string For(FileFormat format) => format.Category switch
    {
        _ when format == KnownFormats.Pdf => Document,
        _ when format == KnownFormats.Txt => Document,
        FormatCategory.Image => Photo,
        FormatCategory.Video => Video,
        FormatCategory.Audio => Audio,
        FormatCategory.Archive => Archive,
        FormatCategory.Subtitle => Captions,
        _ => Document,
    };
}

/// <summary>Builds each wheel's content for a set of files (spec §4.3, §6).</summary>
internal sealed class WheelContentBuilder(Func<DropAnalyzer> analyzer, Func<PomegranateSettings> settings)
{
    private const int MaxFolders = 7;

    public WheelContent Build(WheelKind kind, IReadOnlyList<string> paths) => kind switch
    {
        WheelKind.Convert => Convert(paths),
        WheelKind.Tools => new(kind, L.Get("Wheel_Tools"), [], L.Get("Wheel_ToolsComingSoon")),
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
