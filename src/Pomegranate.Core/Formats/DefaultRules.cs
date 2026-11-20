using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Core.Formats;

/// <summary>The v1 conversion table (spec §7).</summary>
internal static class DefaultRules
{
    // Display order on the wheel, most used first.
    // HEIC is input only: the bundled ImageMagick build has no HEIC encoder (spec §13, engine check 2026-11-18).
    private static readonly FileFormat[] RasterInputs = [Jpg, Png, WebP, Heic, Avif, Tiff, Bmp];
    private static readonly FileFormat[] RasterTargets = [Jpg, Png, WebP, Avif, Tiff, Bmp];
    private static readonly FileFormat[] AudioFormats = [Mp3, M4a, Wav, Flac, Ogg, Opus, Aiff, Wma];
    private static readonly FileFormat[] VideoFormats = [Mp4, Mov, Mkv, WebM, Gif, Avi, Wmv];
    private static readonly FileFormat[] ArchiveTargets = [Zip, Tar, TarGz];

    public static IEnumerable<ConversionRule> Create()
    {
        // Raster images → every other raster format, plus PDF.
        foreach (var from in RasterInputs)
        {
            foreach (var to in RasterTargets.Where(t => t != from))
            {
                yield return new(from, to, EngineKind.Image);
            }

            yield return new(from, Pdf, EngineKind.Image);
        }

        // SVG is input only.
        foreach (var to in RasterTargets)
        {
            yield return new(Svg, to, EngineKind.Image);
        }

        yield return new(Svg, Pdf, EngineKind.Image);

        // Camera RAW → rasters, PDF, and DNG through dnglab.
        foreach (var to in RasterTargets)
        {
            yield return new(Raw, to, EngineKind.Image);
        }

        yield return new(Raw, Pdf, EngineKind.Image);
        yield return new(Raw, Dng, EngineKind.Dng);

        // DNG is already a RAW format, so it converts like RAW minus DNG itself.
        foreach (var to in RasterTargets)
        {
            yield return new(Dng, to, EngineKind.Image);
        }

        yield return new(Dng, Pdf, EngineKind.Image);

        // Audio ↔ audio.
        foreach (var from in AudioFormats)
        {
            foreach (var to in AudioFormats.Where(t => t != from))
            {
                yield return new(from, to, EngineKind.Media);
            }
        }

        // Video ↔ video, plus MP3 export.
        foreach (var from in VideoFormats)
        {
            foreach (var to in VideoFormats.Where(t => t != from))
            {
                yield return new(from, to, EngineKind.Media);
            }

            // GIF never has sound, so it can't become MP3.
            if (from != Gif)
            {
                yield return new(from, Mp3, EngineKind.Media);
            }
        }

        // Archives: ZIP, TAR, TAR.GZ between each other; RAR is input only.
        foreach (var from in ArchiveTargets)
        {
            foreach (var to in ArchiveTargets.Where(t => t != from))
            {
                yield return new(from, to, EngineKind.Archive);
            }
        }

        foreach (var to in ArchiveTargets)
        {
            yield return new(Rar, to, EngineKind.Archive);
        }

        // Subtitles.
        yield return new(Srt, Vtt, EngineKind.Subtitle);
        yield return new(Srt, Txt, EngineKind.Subtitle);
        yield return new(Vtt, Srt, EngineKind.Subtitle);
        yield return new(Vtt, Txt, EngineKind.Subtitle);

        foreach (var rule in Documents().Concat(Pdfs()).Concat(Slides()).Concat(Spreadsheets()))
        {
            yield return rule;
        }
    }

    // ---- v2 (spec §7.2). Where a pair lists two engines, the first is preferred and the second is the
    // fallback when the first isn't installed.

    // Word-type documents keep their layout through Office; Pandoc converts their content.
    private static readonly FileFormat[] WordTypes = [Docx, Odt, Rtf];
    private static readonly FileFormat[] TextTypes = [Markdown, Html, Epub, Txt];

    private static IEnumerable<ConversionRule> Documents()
    {
        FileFormat[] order = [Docx, Pdf, Odt, Rtf, Markdown, Html, Epub, Txt];

        foreach (var from in WordTypes)
        {
            foreach (var to in order.Where(t => t != from))
            {
                if (to == Pdf)
                {
                    yield return new(from, to, EngineKind.OfficeText);
                }
                else if (WordTypes.Contains(to))
                {
                    yield return new(from, to, EngineKind.OfficeText);
                    yield return new(from, to, EngineKind.Pandoc);
                }
                else
                {
                    yield return new(from, to, EngineKind.Pandoc);
                }
            }
        }

        foreach (var from in TextTypes)
        {
            foreach (var to in order.Where(t => t != from))
            {
                yield return new(from, to, to == Pdf ? EngineKind.WebPdf : EngineKind.Pandoc);
            }
        }

        // DOC (Word 97–2003) is input only and needs Word or LibreOffice to read it.
        foreach (var to in new[] { Docx, Pdf, Odt, Rtf, Html, Txt })
        {
            yield return new(Doc, to, EngineKind.OfficeText);
        }
    }

    private static IEnumerable<ConversionRule> Pdfs()
    {
        yield return new(Pdf, Docx, EngineKind.PdfDocument);
        yield return new(Pdf, Jpg, EngineKind.PdfImage);
        yield return new(Pdf, Png, EngineKind.PdfImage);
        yield return new(Pdf, Txt, EngineKind.PdfText);
        yield return new(Pdf, Markdown, EngineKind.PdfText);
        yield return new(Pdf, Epub, EngineKind.PdfDocument);
        yield return new(Pdf, Pptx, EngineKind.PdfDocument);
        yield return new(Pdf, Xlsx, EngineKind.PdfText);
    }

    private static IEnumerable<ConversionRule> Slides()
    {
        foreach (var from in new[] { Pptx, Ppt, Odp })
        {
            yield return new(from, Pdf, EngineKind.OfficeSlides);
            yield return new(from, Jpg, EngineKind.OfficeSlides);
            yield return new(from, Png, EngineKind.OfficeSlides);
            foreach (var to in new[] { Pptx, Odp }.Where(t => t != from))
            {
                yield return new(from, to, EngineKind.OfficeSlides);
            }
        }
    }

    private static IEnumerable<ConversionRule> Spreadsheets()
    {
        FileFormat[] order = [Xlsx, Csv, Pdf, Ods, Tsv];
        foreach (var from in new[] { Xlsx, Xls, Ods, Csv, Tsv })
        {
            foreach (var to in order.Where(t => t != from))
            {
                if (to == Pdf || to == Ods || from == Ods)
                {
                    // Layout (PDF) and OpenDocument need an office suite.
                    yield return new(from, to, EngineKind.OfficeSheet);
                }
                else if (from == Xls && to == Xlsx)
                {
                    // Excel keeps formatting and formulas; the built-in reader keeps values only.
                    yield return new(from, to, EngineKind.OfficeSheet);
                    yield return new(from, to, EngineKind.Sheet);
                }
                else
                {
                    yield return new(from, to, EngineKind.Sheet);
                }
            }
        }
    }
}
