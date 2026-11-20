using System.Text;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Documents;

/// <summary>
/// Markdown, HTML, EPUB and TXT to PDF through the PC's own Microsoft Edge, run headless (spec §7.2).
/// Pandoc first turns Markdown/EPUB into one self-contained HTML page; Edge prints it.
/// <para>
/// Edge runs with a throwaway profile and every network name lookup blocked, so printing never reaches the
/// internet, even if a page links to remote images (spec §2, "Network").
/// </para>
/// </summary>
public sealed class WebPdfConverter(string edgePath, string pandocPath) : IConverter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Readable print styles for pages Pandoc generates.</summary>
    private const string PrintCss = """
        body { font-family: "Segoe UI", Arial, sans-serif; font-size: 11pt; line-height: 1.45; max-width: none; margin: 0; padding: 0; }
        h1, h2, h3 { line-height: 1.2; }
        pre, code { font-family: Consolas, monospace; font-size: 9.5pt; }
        pre { white-space: pre-wrap; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid #999; padding: 3px 8px; }
        img { max-width: 100%; }
        @page { margin: 2cm; }
        """;

    public EngineKind Engine => EngineKind.WebPdf;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var from = request.Rule.From;
        string page;
        if (from == KnownFormats.Html)
        {
            page = request.SourcePath; // print as is, so relative pictures next to it still load
        }
        else
        {
            page = Path.Combine(request.WorkFolder, "page.html");
            var css = Path.Combine(request.WorkFolder, "print.css");
            await File.WriteAllTextAsync(css, PrintCss, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            var input = request.SourcePath;
            var reader = from == KnownFormats.Txt ? "html" : PandocConverter.ReaderFor(from);
            if (from == KnownFormats.Txt)
            {
                input = Path.Combine(request.WorkFolder, "input.html");
                var text = await File.ReadAllTextAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(input, PandocConverter.TextToHtml(text), Utf8NoBom, cancellationToken).ConfigureAwait(false);
            }

            var title = Path.GetFileNameWithoutExtension(request.SourcePath);
            await PandocConverter.RunAsync(
                pandocPath,
                ["--sandbox", "--from", reader, "--to", "html5", "--standalone", "--embed-resources", "--css", css,
                 "--metadata", $"pagetitle={title}", "--output", page, input],
                cancellationToken).ConfigureAwait(false);
        }

        progress.Report(0.4);
        await PrintAsync(page, request.OutputPath, request.WorkFolder, cancellationToken).ConfigureAwait(false);
        progress.Report(1);
        return ConversionOutput.Single;
    }

    private async Task PrintAsync(string htmlPath, string pdfPath, string workFolder, CancellationToken cancellationToken)
    {
        var profile = Path.Combine(workFolder, "edge-profile");
        var url = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        string[] arguments =
        [
            "--headless",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            "--disable-sync",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-features=msSmartScreenProtection,Translate,OptimizationHints,MediaRouter",
            "--host-resolver-rules=MAP * ~NOTFOUND",
            $"--user-data-dir={profile}",
            "--no-pdf-header-footer",
            $"--print-to-pdf={pdfPath}",
            url,
        ];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var result = await ProcessRunner.RunAsync(edgePath, arguments, timeout.Token).ConfigureAwait(false);
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.StandardErrorTail) ? $"exit code {result.ExitCode}" : result.StandardErrorTail.Trim();
                throw new ConversionException($"Microsoft Edge couldn't print this to PDF: {detail}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConversionException("Microsoft Edge took too long to print this to PDF.");
        }
    }
}
