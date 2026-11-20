using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Office;

/// <summary>
/// LibreOffice, run headless with a throwaway profile so it never touches the user's own LibreOffice setup.
/// Slide images go through PDF and Windows' renderer, because LibreOffice's own image export only does the
/// first slide.
/// </summary>
internal static class LibreOffice
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static async Task<ConversionOutput> ConvertAsync(
        string soffice, EngineKind kind, ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var to = request.Rule.To;
        if (to == KnownFormats.Png || to == KnownFormats.Jpg)
        {
            var pdf = Path.Combine(request.WorkFolder, "slides.pdf");
            await ConvertAsync(soffice, kind, request with { OutputPath = pdf, Rule = request.Rule with { To = KnownFormats.Pdf } }, progress, cancellationToken)
                .ConfigureAwait(false);
            return await OfficeConverter.ImagesFromPdfAsync(pdf, request, progress, cancellationToken).ConfigureAwait(false);
        }

        var outFolder = Directory.CreateDirectory(Path.Combine(request.WorkFolder, "lo-out")).FullName;
        var profile = new Uri(Path.Combine(request.WorkFolder, "lo-profile") + Path.DirectorySeparatorChar).AbsoluteUri;
        var arguments = new List<string>
        {
            "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
            $"-env:UserInstallation={profile}",
        };

        var from = request.Rule.From;
        if (from == KnownFormats.Csv)
        {
            arguments.Add("--infilter=CSV:44,34,76,1");
        }
        else if (from == KnownFormats.Tsv)
        {
            arguments.Add("--infilter=CSV:9,34,76,1");
        }

        arguments.AddRange(["--convert-to", Filter(kind, to), "--outdir", outFolder, request.SourcePath]);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(soffice, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConversionException("LibreOffice stopped responding.");
        }

        var produced = Directory.EnumerateFiles(outFolder).FirstOrDefault();
        if (produced is null)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardErrorTail) ? $"exit code {result.ExitCode}" : result.StandardErrorTail.Trim();
            throw new ConversionException($"LibreOffice couldn't convert this file: {detail}");
        }

        File.Move(produced, request.OutputPath, overwrite: true);
        progress.Report(1);
        return ConversionOutput.Single;
    }

    private static string Filter(EngineKind kind, FileFormat to) => (kind, to.Id) switch
    {
        (EngineKind.OfficeText, "pdf") => "pdf:writer_pdf_Export",
        (EngineKind.OfficeSheet, "pdf") => "pdf:calc_pdf_Export",
        (EngineKind.OfficeSlides, "pdf") => "pdf:impress_pdf_Export",
        (_, "docx") => "docx:MS Word 2007 XML",
        (_, "odt") => "odt",
        (_, "rtf") => "rtf",
        (_, "txt") => "txt:Text (encoded):UTF8",
        (_, "xlsx") => "xlsx:Calc MS Excel 2007 XML",
        (_, "ods") => "ods",
        (_, "pptx") => "pptx:Impress MS PowerPoint 2007 XML",
        (_, "odp") => "odp",
        _ => throw new ConversionException($"LibreOffice can't write {to.Label} here."),
    };
}
