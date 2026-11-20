using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Documents;

/// <summary>
/// Text documents through Pandoc (spec §7.2): DOCX, ODT, RTF, Markdown, HTML, EPUB and TXT between each other.
/// Pandoc converts content (headings, lists, tables, images), not page layout; Word-type ↔ Word-type goes through
/// Office when it's installed.
/// <para>
/// Pandoc runs with <c>--sandbox</c>, so it reads only the input file and never fetches anything from the
/// internet (spec §2, "Network"). Images linked from Markdown or HTML by path or URL are therefore not pulled in.
/// </para>
/// </summary>
public sealed partial class PandocConverter(string pandocPath) : IConverter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public EngineKind Engine => EngineKind.Pandoc;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var from = request.Rule.From;
        var to = request.Rule.To;
        var input = request.SourcePath;

        // Pandoc has no plain-text reader; wrap TXT as simple HTML first.
        if (from == KnownFormats.Txt)
        {
            input = Path.Combine(request.WorkFolder, "input.html");
            var text = await File.ReadAllTextAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(input, TextToHtml(text), Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(0.2);
        var media = Path.Combine(request.WorkFolder, "media");
        var arguments = new List<string>
        {
            "--sandbox",
            "--from", from == KnownFormats.Txt ? "html" : ReaderFor(from),
            "--to", WriterFor(to),
            "--output", request.OutputPath,
        };

        if (to == KnownFormats.Html)
        {
            // One self-contained file, pictures included.
            arguments.AddRange(["--standalone", "--embed-resources"]);
        }
        else if (to == KnownFormats.Rtf || to == KnownFormats.Epub || to == KnownFormats.Odt || to == KnownFormats.Docx)
        {
            arguments.Add("--standalone");
        }

        if (to == KnownFormats.Markdown)
        {
            arguments.AddRange(["--extract-media", media, "--wrap", "none"]);
        }
        else if (to == KnownFormats.Txt)
        {
            arguments.AddRange(["--wrap", "none"]);
        }

        // EPUB and HTML want a title. A metadata file has the lowest priority, so a document's own title wins.
        var metadata = Path.Combine(request.WorkFolder, "metadata.yaml");
        var title = Path.GetFileNameWithoutExtension(request.SourcePath).Replace("\"", "'", StringComparison.Ordinal);
        await File.WriteAllTextAsync(metadata, $"title: \"{title}\"\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        arguments.AddRange(["--metadata-file", metadata]);

        arguments.Add(input);
        await RunAsync(pandocPath, arguments, cancellationToken).ConfigureAwait(false);

        if (to == KnownFormats.Markdown && Directory.Exists(media))
        {
            // Markdown can't hold pictures; put the extracted ones inline as data URIs so the file stands alone.
            var markdown = await File.ReadAllTextAsync(request.OutputPath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(request.OutputPath, InlineImages(markdown, media), Utf8NoBom, cancellationToken)
                .ConfigureAwait(false);
        }

        progress.Report(1);
        return ConversionOutput.Single;
    }

    /// <summary>Runs Pandoc and turns a failure into a readable message.</summary>
    internal static async Task RunAsync(string pandocPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(pandocPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardErrorTail) ? $"exit code {result.ExitCode}" : result.StandardErrorTail.Trim();
            throw new ConversionException($"Pandoc couldn't convert this file: {detail}");
        }
    }

    internal static string ReaderFor(FileFormat format) => format.Id switch
    {
        "docx" => "docx",
        "odt" => "odt",
        "rtf" => "rtf",
        "md" => "markdown",
        "html" => "html",
        "epub" => "epub",
        _ => throw new ConversionException($"Pandoc can't read {format.Label}."),
    };

    internal static string WriterFor(FileFormat format) => format.Id switch
    {
        "docx" => "docx",
        "odt" => "odt",
        "rtf" => "rtf",
        "md" => "gfm",
        "html" => "html5",
        "epub" => "epub3",
        "txt" => "plain",
        "pptx" => "pptx",
        _ => throw new ConversionException($"Pandoc can't write {format.Label}."),
    };

    /// <summary>Plain text as HTML: blank lines separate paragraphs, single line breaks are kept.</summary>
    internal static string TextToHtml(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('﻿');
        var paragraphs = BlankLines().Split(normalized)
            .Where(p => p.Trim().Length > 0)
            .Select(p => "<p>" + string.Join("<br/>", p.Split('\n').Select(WebUtility.HtmlEncode)) + "</p>");
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>\n" + string.Join("\n", paragraphs) + "\n</body></html>\n";
    }

    /// <summary>Replaces image links into <paramref name="mediaFolder"/> with base64 data URIs.</summary>
    internal static string InlineImages(string markdown, string mediaFolder)
    {
        var root = Path.GetFullPath(mediaFolder);
        return ImageLink().Replace(markdown, match =>
        {
            var target = match.Groups["path"].Value;
            string full;
            try
            {
                full = Path.GetFullPath(target);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return match.Value;
            }

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                return match.Value;
            }

            var mime = Path.GetExtension(full).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
            var data = Convert.ToBase64String(File.ReadAllBytes(full));
            return $"{match.Groups["prefix"].Value}data:{mime};base64,{data}{match.Groups["suffix"].Value}";
        });
    }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLines();

    // ![alt](path) or <img src="path">
    [GeneratedRegex("""(?<prefix>!\[[^\]]*\]\(|<img [^>]*src=")(?<path>[^)"\s]+)(?<suffix>[)"])""")]
    private static partial Regex ImageLink();
}
