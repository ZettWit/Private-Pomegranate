using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Engines.Documents;
using Pomegranate.Engines.Pdf;
using Pomegranate.Engines.Sheets;

namespace Pomegranate.Engines.Office;

/// <summary>
/// Conversions that need a full office suite (spec §7.2): layout-faithful PDFs, slides to images,
/// OpenDocument files, and the old DOC/XLS/PPT formats. Microsoft Office handles its own formats and RTF;
/// LibreOffice handles OpenDocument files. Each falls back to the other when only one is installed.
/// One instance per app: Word-type (<see cref="EngineKind.OfficeText"/>), Excel-type and PowerPoint-type.
/// </summary>
public sealed class OfficeConverter(EngineKind kind, OfficeApps apps, string? pandocPath) : IConverter
{
    private static readonly HashSet<string> OpenDocument = ["odt", "ods", "odp"];

    public EngineKind Engine { get; } = kind is EngineKind.OfficeText or EngineKind.OfficeSheet or EngineKind.OfficeSlides
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(kind));

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var from = request.Rule.From;
        var to = request.Rule.To;

        // Word → HTML writes pictures into a side folder; going through DOCX and Pandoc gives one self-contained file.
        if (to == KnownFormats.Html)
        {
            if (pandocPath is null)
            {
                throw new ConversionException("Pandoc is needed for HTML output and isn't installed.");
            }

            var docx = Path.Combine(request.WorkFolder, "intermediate.docx");
            await ConvertWithSuiteAsync(request with { OutputPath = docx, Rule = request.Rule with { To = KnownFormats.Docx } }, progress, cancellationToken)
                .ConfigureAwait(false);
            await PandocConverter.RunAsync(
                pandocPath,
                ["--sandbox", "--from", "docx", "--to", "html5", "--standalone", "--embed-resources",
                 "--metadata", $"pagetitle={Path.GetFileNameWithoutExtension(request.SourcePath)}", "--output", request.OutputPath, docx],
                cancellationToken).ConfigureAwait(false);
            return ConversionOutput.Single;
        }

        // ODS → CSV/TSV: the suite writes an XLSX, then every sheet becomes its own file.
        if (to == KnownFormats.Csv || to == KnownFormats.Tsv)
        {
            var xlsx = Path.Combine(request.WorkFolder, "intermediate.xlsx");
            await ConvertWithSuiteAsync(request with { OutputPath = xlsx, Rule = request.Rule with { To = KnownFormats.Xlsx } }, progress, cancellationToken)
                .ConfigureAwait(false);
            var tables = SheetData.Read(xlsx, KnownFormats.Xlsx);
            File.Delete(xlsx);
            return SheetData.WriteDelimited(tables, to == KnownFormats.Tsv ? '\t' : ',', request.OutputPath, request.WorkFolder);
        }

        return await ConvertWithSuiteAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    private Task<ConversionOutput> ConvertWithSuiteAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var microsoftHasIt = Engine switch
        {
            EngineKind.OfficeText => apps.Word,
            EngineKind.OfficeSheet => apps.Excel,
            _ => apps.PowerPoint,
        };

        var preferLibreOffice = OpenDocument.Contains(request.Rule.From.Id);
        var useLibreOffice = apps.LibreOffice is not null && (preferLibreOffice || !microsoftHasIt);

        if (useLibreOffice)
        {
            return LibreOffice.ConvertAsync(apps.LibreOffice!, Engine, request, progress, cancellationToken);
        }

        if (!microsoftHasIt)
        {
            throw new ConversionException("This needs Microsoft Office or LibreOffice, and neither is installed.");
        }

        return MicrosoftOffice.ConvertAsync(Engine, request, progress, cancellationToken);
    }

    /// <summary>Slide or page images from a PDF the suite made, at the slide width from the settings.</summary>
    internal static async Task<ConversionOutput> ImagesFromPdfAsync(
        string pdf, ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateDirectory(Path.Combine(request.WorkFolder, "pages")).FullName;
        var pages = await PdfRenderer.RenderAsync(
            pdf, folder, 300, progress, cancellationToken, maxWidth: request.Quality.SlideImageWidth).ConfigureAwait(false);
        if (request.Rule.To != KnownFormats.Jpg)
        {
            return ConversionOutput.Pages(pages);
        }

        var jpegs = new List<string>();
        foreach (var png in pages)
        {
            var jpeg = Path.ChangeExtension(png, ".jpg");
            using (var image = new ImageMagick.MagickImage(png))
            {
                image.BackgroundColor = ImageMagick.MagickColors.White;
                image.Alpha(ImageMagick.AlphaAction.Remove);
                image.Quality = (uint)Math.Clamp(request.Quality.JpegQuality, 1, 100);
                image.Write(jpeg, ImageMagick.MagickFormat.Jpeg);
            }

            jpegs.Add(jpeg);
        }

        return ConversionOutput.Pages(jpegs);
    }
}
