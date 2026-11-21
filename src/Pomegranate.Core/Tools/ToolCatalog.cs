using Pomegranate.Core.Formats;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Core.Tools;

/// <summary>One tool's fixed properties.</summary>
/// <param name="Options">The outer ring, in order; empty when the tool has none.</param>
/// <param name="DefaultOption">Used when the file is dropped on the tool itself.</param>
public sealed record ToolDefinition(
    ToolKind Kind,
    ToolInput Input,
    bool IsEditor,
    IReadOnlyList<ToolOption> Options,
    ToolOption DefaultOption);

/// <summary>A tool works on a format through an engine. A null format means "any file or folder" (Zip).</summary>
public sealed record ToolRule(ToolKind Kind, FileFormat? Format, EngineKind Engine);

/// <summary>Which tools exist, which formats each works on, and what a drop is offered (spec §7.3).</summary>
public sealed class ToolCatalog
{
    private static readonly FileFormat[] WritableImages = [Jpg, Png, WebP, Tiff, Avif, Bmp];
    private static readonly FileFormat[] ReadableImages = [Jpg, Png, WebP, Heic, Avif, Tiff, Bmp, Svg];
    private static readonly FileFormat[] Videos = [Mp4, Mov, Mkv, WebM, Avi, Wmv];
    private static readonly FileFormat[] CompressibleVideos = [Mp4, Mov, Mkv, WebM];
    private static readonly FileFormat[] Audio = [Mp3, M4a, Wav, Flac, Ogg, Opus, Aiff, Wma];
    private static readonly FileFormat[] LossyAudio = [Mp3, M4a, Ogg, Opus, Wma];
    private static readonly FileFormat[] Archives = [Zip, Tar, TarGz, Rar];

    private readonly IReadOnlyList<ToolRule> _rules;
    private readonly Dictionary<(ToolKind, FileFormat?), ToolRule> _byKey;

    public ToolCatalog(IEnumerable<ToolRule> rules)
    {
        _rules = rules.ToArray();
        _byKey = [];
        foreach (var rule in _rules)
        {
            if (!_byKey.TryAdd((rule.Kind, rule.Format), rule))
            {
                throw new ArgumentException($"Duplicate tool rule {rule.Kind} for {rule.Format}.", nameof(rules));
            }
        }
    }

    public static IReadOnlyList<ToolDefinition> Definitions { get; } = CreateDefinitions();

    public static ToolCatalog Default { get; } = new(CreateRules());

    public IReadOnlyList<ToolRule> Rules => _rules;

    public static ToolDefinition Definition(ToolKind kind) => Definitions[(int)kind];

    /// <summary>The catalog without the tools whose engine isn't available (e.g. no FFmpeg).</summary>
    public ToolCatalog WithEngines(IEnumerable<EngineKind> available)
    {
        var set = available.ToHashSet();
        return new(_rules.Where(r => set.Contains(r.Engine)));
    }

    /// <summary>The rule for a tool on a format, falling back to an "any file" rule.</summary>
    public ToolRule? Find(ToolKind kind, FileFormat? format) =>
        format is not null && _byKey.TryGetValue((kind, format), out var rule) ? rule
        : _byKey.TryGetValue((kind, null), out var any) ? any
        : null;

    /// <summary>
    /// The tools a drop is offered, in wheel order. A tool is offered when it works for every dropped item
    /// (spec §7.3). Editors need exactly one file; Merge needs two or more; folders get Zip only.
    /// </summary>
    public IReadOnlyList<ToolDefinition> Offer(IReadOnlyList<string> paths, FormatCatalog formats, Func<string, bool>? isDirectory = null)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        isDirectory ??= Directory.Exists;
        var hasFolders = paths.Any(isDirectory);
        var detected = hasFolders ? [] : paths.Select(formats.Detect).ToArray();

        var offered = new List<ToolDefinition>();
        foreach (var definition in Definitions)
        {
            var kind = definition.Kind;
            if (kind == ToolKind.Zip)
            {
                if (Find(kind, null) is not null)
                {
                    offered.Add(definition);
                }

                continue;
            }

            if (hasFolders || detected.Any(f => f is null))
            {
                continue;
            }

            if (definition.IsEditor && paths.Count != 1)
            {
                continue;
            }

            if (kind == ToolKind.Merge && paths.Count < 2)
            {
                continue;
            }

            if (detected.All(f => _byKey.ContainsKey((kind, f))))
            {
                offered.Add(definition);
            }
        }

        return offered;
    }

    private static IReadOnlyList<ToolDefinition> CreateDefinitions()
    {
        ToolOption[] compress = [ToolOption.Light, ToolOption.Medium, ToolOption.Strong];
        ToolOption[] resize = [ToolOption.Scale50, ToolOption.Scale25, ToolOption.Long3840, ToolOption.Long1920, ToolOption.Long1280];
        ToolOption[] speed = [ToolOption.Speed05, ToolOption.Speed15, ToolOption.Speed2, ToolOption.Speed4];
        ToolOption[] rotate = [ToolOption.RotateLeft, ToolOption.RotateRight, ToolOption.Rotate180, ToolOption.Flip];

        ToolDefinition Instant(ToolKind kind, ToolOption[]? options = null, ToolOption fallback = ToolOption.None) =>
            new(kind, ToolInput.EachFile, false, options ?? [], fallback);
        ToolDefinition Editor(ToolKind kind) => new(kind, ToolInput.EachFile, true, [], ToolOption.None);

        var all = new Dictionary<ToolKind, ToolDefinition>
        {
            [ToolKind.Compress] = Instant(ToolKind.Compress, compress, ToolOption.Medium),
            [ToolKind.Resize] = Instant(ToolKind.Resize, resize, ToolOption.Scale50),
            [ToolKind.Trim] = Editor(ToolKind.Trim),
            [ToolKind.Crop] = Editor(ToolKind.Crop),
            [ToolKind.Rotate] = Instant(ToolKind.Rotate, rotate, ToolOption.RotateRight),
            [ToolKind.Speed] = Instant(ToolKind.Speed, speed, ToolOption.Speed2),
            [ToolKind.Annotate] = Editor(ToolKind.Annotate),
            [ToolKind.Redact] = Editor(ToolKind.Redact),
            [ToolKind.Bleep] = Editor(ToolKind.Bleep),
            [ToolKind.Mute] = Instant(ToolKind.Mute),
            [ToolKind.ExtractAudio] = Instant(ToolKind.ExtractAudio),
            [ToolKind.Normalize] = Instant(ToolKind.Normalize),
            [ToolKind.Merge] = new(ToolKind.Merge, ToolInput.AllTogether, false, [], ToolOption.None),
            [ToolKind.Split] = Instant(ToolKind.Split),
            [ToolKind.Pages] = Editor(ToolKind.Pages),
            [ToolKind.StripMetadata] = Instant(ToolKind.StripMetadata),
            [ToolKind.ReadCode] = Instant(ToolKind.ReadCode),
            [ToolKind.CopyText] = Instant(ToolKind.CopyText),
            [ToolKind.Extract] = Instant(ToolKind.Extract),
            [ToolKind.Zip] = new(ToolKind.Zip, ToolInput.AllTogether, false, [], ToolOption.None),
        };

        // Indexed by the enum's value, which is also the wheel order.
        return Enum.GetValues<ToolKind>().Select(k => all[k]).ToArray();
    }

    private static IEnumerable<ToolRule> CreateRules()
    {
        IEnumerable<ToolRule> On(ToolKind kind, EngineKind engine, params IEnumerable<FileFormat> formats) =>
            formats.Select(f => new ToolRule(kind, f, engine));

        var rules = new List<ToolRule>();

        // Images (Magick.NET); reading codes and text works on anything Magick can read.
        rules.AddRange(On(ToolKind.Compress, EngineKind.Image, WritableImages.Where(f => f != Bmp)));
        foreach (var kind in new[] { ToolKind.Resize, ToolKind.Crop, ToolKind.Rotate, ToolKind.Annotate, ToolKind.Redact, ToolKind.StripMetadata })
        {
            rules.AddRange(On(kind, EngineKind.Image, WritableImages));
        }

        rules.AddRange(On(ToolKind.ReadCode, EngineKind.Recognition, ReadableImages));
        rules.AddRange(On(ToolKind.CopyText, EngineKind.Recognition, ReadableImages));

        // Video (FFmpeg).
        rules.AddRange(On(ToolKind.Compress, EngineKind.Media, CompressibleVideos));
        foreach (var kind in new[] { ToolKind.Trim, ToolKind.Crop, ToolKind.Redact, ToolKind.Speed, ToolKind.Rotate, ToolKind.Mute, ToolKind.ExtractAudio, ToolKind.StripMetadata })
        {
            rules.AddRange(On(kind, EngineKind.Media, Videos));
        }

        // Audio (FFmpeg).
        rules.AddRange(On(ToolKind.Compress, EngineKind.Media, LossyAudio));
        foreach (var kind in new[] { ToolKind.Trim, ToolKind.Bleep, ToolKind.Speed, ToolKind.Normalize, ToolKind.StripMetadata })
        {
            rules.AddRange(On(kind, EngineKind.Media, Audio));
        }

        // PDF (PDFsharp).
        foreach (var kind in new[] { ToolKind.Compress, ToolKind.Merge, ToolKind.Split, ToolKind.Pages, ToolKind.StripMetadata })
        {
            rules.Add(new(kind, Pdf, EngineKind.PdfTools));
        }

        // Archives.
        rules.AddRange(On(ToolKind.Extract, EngineKind.Archive, Archives));
        rules.Add(new(ToolKind.Zip, null, EngineKind.Archive));
        return rules;
    }
}
