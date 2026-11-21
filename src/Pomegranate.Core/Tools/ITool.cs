using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Tools;

/// <summary>What a tool is asked to do.</summary>
/// <param name="Sources">The file (or, for Merge and Zip, every item) to work on. Never modified.</param>
/// <param name="Format">The format of the first source, or null (folders, unknown files).</param>
/// <param name="WorkFolder">
/// An empty temporary folder in the output folder. The tool writes its result here and returns it in a
/// <see cref="ToolOutput"/>; the runner moves it to its final name and deletes the folder afterwards.
/// </param>
public sealed record ToolRequest(
    ToolKind Kind,
    ToolOption Option,
    IReadOnlyList<string> Sources,
    FileFormat? Format,
    string WorkFolder,
    QualitySettings Quality,
    ToolSettings Settings,
    ToolEdit? Edit = null)
{
    public string Source => Sources[0];

    /// <summary>A path in the work folder for the result, keeping the given extension (FFmpeg picks the container from it).</summary>
    public string WorkFile(string extension, string name = "result") =>
        Path.Combine(WorkFolder, $"{name}.{extension.TrimStart('.')}");

    /// <summary>The source's own extension without the dot ("jpeg" stays "jpeg").</summary>
    public string SourceExtension => Path.GetExtension(Source).TrimStart('.');
}

public enum ToolOutputKind
{
    File,
    Pages,
    Folder,
    Text,
}

/// <summary>What a tool produced.</summary>
public sealed class ToolOutput
{
    private ToolOutput(ToolOutputKind kind, IReadOnlyList<string> paths, string? text)
    {
        Kind = kind;
        Paths = paths;
        Text = text;
    }

    public ToolOutputKind Kind { get; }

    /// <summary>The file, the folder, or the pages in order.</summary>
    public IReadOnlyList<string> Paths { get; }

    public string? Text { get; }

    /// <summary>One file in the work folder. Its extension becomes the output's.</summary>
    public static ToolOutput File(string path) => new(ToolOutputKind.File, [path], null);

    /// <summary>One file per page; they go into a "&lt;name&gt; (pages)" folder (spec §7.2).</summary>
    public static ToolOutput Pages(IReadOnlyList<string> files) => new(ToolOutputKind.Pages, files, null);

    /// <summary>A folder in the work folder (Extract).</summary>
    public static ToolOutput Folder(string path) => new(ToolOutputKind.Folder, [path], null);

    /// <summary>No file: text for the clipboard (Read QR code, Copy text).</summary>
    public static ToolOutput FromText(string text) => new(ToolOutputKind.Text, [], text);
}

/// <summary>The tools one engine provides (Magick.NET, FFmpeg, PDFsharp, …).</summary>
public interface ITool
{
    EngineKind Engine { get; }

    /// <summary>
    /// Runs the tool. Reports progress from 0 to 1 when it can. Throws <see cref="NothingToDoException"/> when
    /// there's nothing to change, another exception on failure, and <see cref="OperationCanceledException"/>
    /// when cancelled. Must not touch the sources.
    /// </summary>
    Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken);
}

/// <summary>
/// The tool had nothing to do (e.g. the file is already as small as it gets). Not a failure: the job is
/// skipped and the message shown.
/// </summary>
public sealed class NothingToDoException(string message) : Exception(message);
