using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Jobs;

/// <summary>What an engine is asked to do: read <see cref="SourcePath"/>, write <see cref="OutputPath"/>.</summary>
/// <param name="OutputPath">A temporary path in the output folder. The runner renames it when the engine succeeds.</param>
/// <param name="WorkFolder">
/// An empty temporary folder in the output folder, for engines that produce several files (pages, sheets)
/// or need scratch space. The runner deletes it afterwards.
/// </param>
public sealed record ConversionRequest(
    string SourcePath,
    string OutputPath,
    ConversionRule Rule,
    QualitySettings Quality,
    string WorkFolder = "");

/// <summary>One file of a conversion that produced several.</summary>
/// <param name="TempPath">Where the engine wrote it (normally inside <see cref="ConversionRequest.WorkFolder"/>).</param>
/// <param name="Suffix">What tells it apart: "01" for page 1, "Sheet1" for a sheet.</param>
public sealed record OutputPart(string TempPath, string Suffix);

/// <summary>
/// What an engine produced. Usually one file at <see cref="ConversionRequest.OutputPath"/>; sometimes several
/// (spec §7.2, "Files with several pages").
/// </summary>
public sealed class ConversionOutput
{
    private ConversionOutput(IReadOnlyList<OutputPart> parts, bool groupInFolder)
    {
        Parts = parts;
        GroupInFolder = groupInFolder;
    }

    /// <summary>One file, written to <see cref="ConversionRequest.OutputPath"/>.</summary>
    public static ConversionOutput Single { get; } = new([], false);

    /// <summary>Empty for a single file.</summary>
    public IReadOnlyList<OutputPart> Parts { get; }

    /// <summary>
    /// True: the parts go into a new folder "&lt;name&gt; (pages)" as "&lt;name&gt; - 01.png", …
    /// False: they go next to the original as "&lt;name&gt; - Sheet1.csv", ….
    /// </summary>
    public bool GroupInFolder { get; }

    /// <summary>Numbered pages: 01, 02, … (more digits when there are 100 or more).</summary>
    public static ConversionOutput Pages(IReadOnlyList<string> files)
    {
        if (files.Count <= 1)
        {
            return files.Count == 0 ? Single : new([new(files[0], "01")], true);
        }

        var digits = Math.Max(2, files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var parts = files.Select((f, i) => new OutputPart(f, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0')));
        return new(parts.ToArray(), true);
    }

    /// <summary>Named parts next to the original, e.g. one CSV per sheet.</summary>
    public static ConversionOutput Named(IEnumerable<OutputPart> parts) => new(parts.ToArray(), false);
}

/// <summary>One conversion engine (Magick.NET, FFmpeg, …).</summary>
public interface IConverter
{
    EngineKind Engine { get; }

    /// <summary>
    /// Converts the file. Reports progress from 0 to 1 when it can. Throws on failure and
    /// <see cref="OperationCanceledException"/> when cancelled. Must not touch the source file.
    /// </summary>
    Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken);
}

/// <summary>An engine failure with a message fit for the "Details" view.</summary>
public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
