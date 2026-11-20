using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Pdf;

/// <summary>A line of text on a PDF page, rebuilt from word positions.</summary>
internal sealed record PdfLine(IReadOnlyList<Word> Words, double Top, double Bottom, double FontSize)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));

    public double Left => Words[0].BoundingBox.Left;

    public double Height => Math.Max(1, Top - Bottom);
}

/// <summary>
/// Turns PdfPig's words back into lines, paragraphs, headings and table cells. PDFs store positioned
/// glyphs, not structure, so this is a best effort: editable text with a simplified layout (spec §7.2).
/// </summary>
internal static class PdfLayout
{
    public static PdfDocument Open(string path)
    {
        try
        {
            return PdfDocument.Open(path);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new ConversionException("This file is password-protected.", ex);
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or InvalidOperationException)
        {
            throw new ConversionException($"Couldn't read this PDF: {ex.Message}", ex);
        }
    }

    /// <summary>Groups a page's words into lines, top to bottom.</summary>
    public static IReadOnlyList<PdfLine> Lines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var height = Math.Max(1, word.BoundingBox.Height);
            var line = lines.LastOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < height * 0.5);
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        return lines
            .Select(l => l.OrderBy(w => w.BoundingBox.Left).ToList())
            .Select(l => new PdfLine(
                l,
                l.Max(w => w.BoundingBox.Top),
                l.Min(w => w.BoundingBox.Bottom),
                l.SelectMany(w => w.Letters).Select(c => c.PointSize).DefaultIfEmpty(10).Average()))
            .OrderByDescending(l => l.Bottom)
            .ToList();
    }

    /// <summary>Plain text: one line per line, a blank line between paragraphs and between pages.</summary>
    public static string ToText(PdfDocument document)
    {
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            foreach (var paragraph in Paragraphs(Lines(page)))
            {
                sb.Append(string.Join("\r\n", paragraph.Select(l => l.Text))).Append("\r\n\r\n");
            }
        }

        return sb.ToString().TrimEnd() + "\r\n";
    }

    /// <summary>
    /// Markdown: paragraphs joined into single lines, bigger text as headings, bullets as lists.
    /// <paramref name="imagesForPage"/> may return Markdown image links to add after each page's text.
    /// </summary>
    public static string ToMarkdown(PdfDocument document, Func<Page, IEnumerable<string>>? imagesForPage = null)
    {
        var pages = document.GetPages().ToList();
        var allLines = pages.SelectMany(Lines).ToList();
        var body = allLines.Count == 0 ? 10 : Median(allLines.Select(l => l.FontSize));

        var sb = new StringBuilder();
        foreach (var page in pages)
        {
            foreach (var paragraph in Paragraphs(Lines(page)))
            {
                var size = paragraph.Average(l => l.FontSize);
                var text = string.Join(' ', paragraph.Select(l => l.Text)).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (IsBullet(text, out var rest))
                {
                    foreach (var line in paragraph)
                    {
                        sb.Append("- ").Append(Escape(IsBullet(line.Text, out var item) ? item : line.Text)).Append('\n');
                    }

                    sb.Append('\n');
                    _ = rest;
                    continue;
                }

                var prefix = size >= body * 1.6 ? "# " : size >= body * 1.25 ? "## " : "";
                sb.Append(prefix).Append(Escape(text)).Append("\n\n");
            }

            foreach (var image in imagesForPage?.Invoke(page) ?? [])
            {
                sb.Append(image).Append("\n\n");
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Table-ish rows for a page: each line split into cells where the gap between words is wide, and the
    /// cells lined up into columns across the page.
    /// </summary>
    public static List<List<string>> Rows(Page page)
    {
        var lines = Lines(page);
        var cells = new List<List<(double Left, string Text)>>();
        foreach (var line in lines)
        {
            var row = new List<(double Left, string Text)>();
            var current = new List<Word> { line.Words[0] };
            for (var i = 1; i < line.Words.Count; i++)
            {
                var gap = line.Words[i].BoundingBox.Left - line.Words[i - 1].BoundingBox.Right;
                if (gap > line.FontSize * 1.2)
                {
                    row.Add((current[0].BoundingBox.Left, string.Join(' ', current.Select(w => w.Text))));
                    current = [];
                }

                current.Add(line.Words[i]);
            }

            row.Add((current[0].BoundingBox.Left, string.Join(' ', current.Select(w => w.Text))));
            cells.Add(row);
        }

        // Column positions: cluster the left edges of all cells.
        var tolerance = lines.Count == 0 ? 10 : Median(lines.Select(l => l.FontSize)) * 1.5;
        var columns = new List<double>();
        foreach (var left in cells.SelectMany(r => r.Select(c => c.Left)).Order())
        {
            if (columns.Count == 0 || left - columns[^1] > tolerance)
            {
                columns.Add(left);
            }
        }

        var rows = new List<List<string>>();
        foreach (var row in cells)
        {
            var values = new List<string>(Enumerable.Repeat(string.Empty, Math.Max(1, columns.Count)));
            foreach (var (left, text) in row)
            {
                var index = 0;
                for (var c = 0; c < columns.Count; c++)
                {
                    if (Math.Abs(columns[c] - left) < Math.Abs(columns[index] - left))
                    {
                        index = c;
                    }
                }

                values[index] = values[index].Length == 0 ? text : values[index] + " " + text;
            }

            rows.Add(values);
        }

        return rows;
    }

    /// <summary>Lines close together (less than about one line of space between them) form a paragraph.</summary>
    private static IEnumerable<List<PdfLine>> Paragraphs(IReadOnlyList<PdfLine> lines)
    {
        var current = new List<PdfLine>();
        foreach (var line in lines)
        {
            if (current.Count > 0)
            {
                var previous = current[^1];
                var gap = previous.Bottom - line.Top;
                var sizeChange = Math.Abs(previous.FontSize - line.FontSize) > previous.FontSize * 0.15;
                if (gap > previous.Height * 0.9 || sizeChange || IsBullet(line.Text, out _) != IsBullet(previous.Text, out _))
                {
                    yield return current;
                    current = [];
                }
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static bool IsBullet(string text, out string rest)
    {
        var trimmed = text.TrimStart();
        foreach (var marker in new[] { "•", "◦", "▪", "‣", "–", "-", "*" })
        {
            if (trimmed.StartsWith(marker + " ", StringComparison.Ordinal))
            {
                rest = trimmed[(marker.Length + 1)..].TrimStart();
                return true;
            }
        }

        rest = text;
        return false;
    }

    /// <summary>Escapes characters that would otherwise turn PDF text into Markdown links, HTML or formatting.</summary>
    internal static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '!')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        var result = sb.ToString();

        // "1. " at the start would become a numbered list item.
        if (result.Length > 2 && char.IsDigit(result[0]))
        {
            var dot = result.IndexOf(". ", StringComparison.Ordinal);
            if (dot > 0 && result[..dot].All(char.IsDigit))
            {
                result = result[..dot] + "\\" + result[dot..];
            }
        }

        return result;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    /// <summary>A number if the text is plainly one (no leading zeros like postcodes), otherwise the text.</summary>
    public static object CellValue(string text)
    {
        var trimmed = text.Trim();
        var leadingZero = trimmed.Length > 1 && trimmed[0] == '0' && trimmed[1] != '.';
        if (!leadingZero && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return trimmed;
    }
}
