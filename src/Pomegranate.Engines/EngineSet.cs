using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Engines.Media;

namespace Pomegranate.Engines;

/// <summary>The converters available on this PC, and a catalog limited to what they can do.</summary>
public sealed class EngineSet
{
    private EngineSet(IReadOnlyList<IConverter> converters, FormatCatalog catalog)
    {
        Converters = converters;
        Catalog = catalog;
    }

    public IReadOnlyList<IConverter> Converters { get; }

    /// <summary>Only the conversions whose engine was found (spec §7, "Missing engines").</summary>
    public FormatCatalog Catalog { get; }

    public IEnumerable<EngineKind> Available => Converters.Select(c => c.Engine);

    public static EngineSet Create(EngineLocator locator)
    {
        var converters = new List<IConverter>
        {
            new ImageConverter(),
            new ArchiveConverter(),
            new SubtitleConverter(),
        };

        if (locator is { FfmpegPath: { } ffmpeg, FfprobePath: { } ffprobe })
        {
            converters.Add(new MediaConverter(ffmpeg, ffprobe));
        }

        if (locator.DnglabPath is { } dnglab)
        {
            converters.Add(new DngConverter(dnglab));
        }

        return new EngineSet(converters, FormatCatalog.Default.WithEngines(converters.Select(c => c.Engine)));
    }

    public JobRunner CreateRunner() => new(Catalog, Converters);
}
