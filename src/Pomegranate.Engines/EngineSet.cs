using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Engines.Documents;
using Pomegranate.Engines.Media;
using Pomegranate.Engines.Office;
using Pomegranate.Engines.Pdf;
using Pomegranate.Engines.Sheets;

namespace Pomegranate.Engines;

/// <summary>The converters available on this PC, and a catalog limited to what they can do.</summary>
public sealed class EngineSet
{
    private EngineSet(IReadOnlyList<IConverter> converters, FormatCatalog catalog, EngineLocator locator)
    {
        Converters = converters;
        Catalog = catalog;
        Locator = locator;
    }

    public IReadOnlyList<IConverter> Converters { get; }

    /// <summary>Only the conversions whose engine was found (spec §7, "Missing engines"), with fallbacks applied.</summary>
    public FormatCatalog Catalog { get; }

    public EngineLocator Locator { get; }

    public IEnumerable<EngineKind> Available => Converters.Select(c => c.Engine);

    public static EngineSet Create(EngineLocator locator)
    {
        var converters = new List<IConverter>
        {
            new ImageConverter(),
            new ArchiveConverter(),
            new SubtitleConverter(),
            new SheetConverter(),
            new PdfImageConverter(),
            new PdfTextConverter(),
        };

        if (locator is { FfmpegPath: { } ffmpeg, FfprobePath: { } ffprobe })
        {
            converters.Add(new MediaConverter(ffmpeg, ffprobe));
        }

        if (locator.DnglabPath is { } dnglab)
        {
            converters.Add(new DngConverter(dnglab));
        }

        if (locator.PandocPath is { } pandoc)
        {
            converters.Add(new PandocConverter(pandoc));
            converters.Add(new PdfDocumentConverter(pandoc));
            if (locator.EdgePath is { } edge)
            {
                converters.Add(new WebPdfConverter(edge, pandoc));
            }
        }

        var office = locator.Office;
        if (office.CanDoText)
        {
            converters.Add(new OfficeConverter(EngineKind.OfficeText, office, locator.PandocPath));
        }

        if (office.CanDoSheets)
        {
            converters.Add(new OfficeConverter(EngineKind.OfficeSheet, office, locator.PandocPath));
        }

        if (office.CanDoSlides)
        {
            converters.Add(new OfficeConverter(EngineKind.OfficeSlides, office, locator.PandocPath));
        }

        return new EngineSet(converters, FormatCatalog.Default.WithEngines(converters.Select(c => c.Engine)), locator);
    }

    public JobRunner CreateRunner() => new(Catalog, Converters);
}
