using System.Text;
using ImageMagick;
using MiniExcelLibs;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Engines.Documents;

namespace Pomegranate.Engines.Pdf;

/// <summary>PDF → JPG/PNG with Windows' built-in renderer, one image per page (spec §7.2).</summary>
public sealed class PdfImageConverter : IConverter
{
    public EngineKind Engine => EngineKind.PdfImage;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var pages = await PdfRenderer.RenderAsync(
            request.SourcePath, request.WorkFolder, request.Quality.PdfImageDpi, new Scaled(progress, 0, 0.8), cancellationToken)
            .ConfigureAwait(false);

        if (request.Rule.To != KnownFormats.Jpg)
        {
            return ConversionOutput.Pages(pages);
        }

        var jpegs = new List<string>();
        foreach (var png in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jpeg = Path.ChangeExtension(png, ".jpg");
            using (var image = new MagickImage(png))
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
                image.Quality = (uint)Math.Clamp(request.Quality.JpegQuality, 1, 100);
                image.Write(jpeg, MagickFormat.Jpeg);
            }

            File.Delete(png);
            jpegs.Add(jpeg);
        }

        progress.Report(1);
        return ConversionOutput.Pages(jpegs);
    }
}

/// <summary>PDF → TXT, Markdown, XLSX with PdfPig: the text, with a simplified layout (spec §7.2).</summary>
public sealed class PdfTextConverter : IConverter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public EngineKind Engine => EngineKind.PdfText;

    public Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                using var document = PdfLayout.Open(request.SourcePath);
                progress.Report(0.3);
                switch (request.Rule.To.Id)
                {
                    case "txt":
                        File.WriteAllText(request.OutputPath, PdfLayout.ToText(document), Utf8NoBom);
                        break;
                    case "md":
                        File.WriteAllText(request.OutputPath, PdfLayout.ToMarkdown(document), Utf8NoBom);
                        break;
                    case "xlsx":
                        WriteWorkbook(document, request.OutputPath, cancellationToken);
                        break;
                    default:
                        throw new ConversionException($"PDF can't become {request.Rule.To.Label} this way.");
                }

                progress.Report(1);
                return ConversionOutput.Single;
            },
            cancellationToken);

    /// <summary>One sheet per page, text placed in rows and columns by position.</summary>
    private static void WriteWorkbook(UglyToad.PdfPig.PdfDocument document, string path, CancellationToken cancellationToken)
    {
        var sheets = new Dictionary<string, object>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = PdfLayout.Rows(page)
                .Select(row => (IDictionary<string, object>)row
                    .Select((value, i) => (Key: ColumnName(i), Value: PdfLayout.CellValue(value)))
                    .ToDictionary(c => c.Key, c => c.Value))
                .ToList();
            sheets[$"Page {page.Number}"] = rows;
        }

        if (sheets.Count == 0)
        {
            throw new ConversionException("This PDF has no pages.");
        }

        MiniExcel.SaveAs(path, sheets, printHeader: false, excelType: ExcelType.XLSX);
    }

    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }
}

/// <summary>
/// PDF → DOCX and EPUB (text and pictures through PdfPig, then Pandoc), and PDF → PPTX (each page as a
/// picture slide, through Windows' renderer and Pandoc). Spec §7.2.
/// </summary>
public sealed class PdfDocumentConverter(string pandocPath) : IConverter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public EngineKind Engine => EngineKind.PdfDocument;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var markdownPath = Path.Combine(request.WorkFolder, "document.md");
        var media = Directory.CreateDirectory(Path.Combine(request.WorkFolder, "media")).FullName;

        if (request.Rule.To == KnownFormats.Pptx)
        {
            var pages = await PdfRenderer.RenderAsync(
                request.SourcePath, media, 150, new Scaled(progress, 0, 0.6), cancellationToken, maxWidth: request.Quality.SlideImageWidth)
                .ConfigureAwait(false);

            // One slide per page, the page picture filling it.
            var slides = pages.Select(p => $"## {{.unnumbered}}\n\n![]({Relative(p, request.WorkFolder)}){{width=100%}}");
            await File.WriteAllTextAsync(markdownPath, string.Join("\n\n", slides) + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var markdown = await Task.Run(
                () =>
                {
                    using var document = PdfLayout.Open(request.SourcePath);
                    return PdfLayout.ToMarkdown(document, page => SavePictures(page, media, request.WorkFolder));
                },
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(markdownPath, markdown, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(0.7);
        var title = Path.GetFileNameWithoutExtension(request.SourcePath).Replace("\"", "'", StringComparison.Ordinal);
        var metadata = Path.Combine(request.WorkFolder, "metadata.yaml");
        await File.WriteAllTextAsync(metadata, $"title: \"{title}\"\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);

        // No --sandbox here: the Markdown is Pomegranate's own, its text escaped by PdfLayout, and it only
        // points at pictures in the work folder.
        await PandocConverter.RunAsync(
            pandocPath,
            ["--from", "markdown", "--to", PandocConverter.WriterFor(request.Rule.To), "--standalone",
             "--resource-path", request.WorkFolder, "--metadata-file", metadata, "--output", request.OutputPath, markdownPath],
            cancellationToken).ConfigureAwait(false);

        progress.Report(1);
        return ConversionOutput.Single;
    }

    /// <summary>Saves the pictures on a page as PNG files and returns Markdown links to them.</summary>
    private static IEnumerable<string> SavePictures(UglyToad.PdfPig.Content.Page page, string media, string workFolder)
    {
        var links = new List<string>();
        var index = 0;
        foreach (var image in page.GetImages())
        {
            index++;
            if (!image.TryGetPng(out var png) || png is null || png.Length == 0)
            {
                continue;
            }

            var file = Path.Combine(media, $"page{page.Number}-{index}.png");
            File.WriteAllBytes(file, png);
            links.Add($"![]({Relative(file, workFolder)})");
        }

        return links;
    }

    private static string Relative(string path, string root) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

/// <summary>Maps an inner step's 0–1 progress onto part of the job's progress bar.</summary>
internal sealed class Scaled(IProgress<double> inner, double from, double to) : IProgress<double>
{
    public void Report(double value) => inner.Report(from + ((to - from) * Math.Clamp(value, 0, 1)));
}
