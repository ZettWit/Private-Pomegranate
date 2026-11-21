using Pomegranate.App.Editors;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Tools;
using Pomegranate.Engines;

namespace Pomegranate.App.Tools;

/// <summary>Opens the editor window for a tool (spec §7.3, "Editors") and hands its result back.</summary>
internal sealed class EditorLauncher(EngineSet engines)
{
    public void Open(ToolKind kind, string path, Action<ToolEdit> onSave)
    {
        var isVideo = engines.Catalog.Detect(path)?.Category == FormatCategory.Video;
        var media = engines.MediaPreview;
        if ((isVideo || kind is ToolKind.Trim or ToolKind.Bleep) && media is null)
        {
            Log.Error($"{kind} needs FFmpeg, which isn't installed.");
            return;
        }

        EditorWindow window = kind switch
        {
            ToolKind.Crop => new CropEditor(path, isVideo ? media : null),
            ToolKind.Trim => new TrimEditor(path, media!, isVideo, bleep: false),
            ToolKind.Bleep => new TrimEditor(path, media!, isVideo: false, bleep: true),
            ToolKind.Pages => new PagesEditor(path),
            ToolKind.Redact => new RedactEditor(path, isVideo ? media : null),
            ToolKind.Annotate => new AnnotateEditor(path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This tool has no editor."),
        };

        window.Saved += onSave;
        window.ShowInFront();
        Log.Info($"Opened the {kind} editor for {path}.");
    }
}
