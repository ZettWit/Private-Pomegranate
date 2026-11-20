using System.IO.Compression;
using System.Text;
using ImageMagick;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Engines.Documents;
using Pomegranate.Engines.Pdf;
using Pomegranate.Engines.Sheets;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Engines.Tests;

public sealed class DocumentConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Document);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        V2Assert.Is(output, Parts, KnownFormats.All.Single(f => f.Id == to), expectMarker: true);
    }
}

public sealed class PresentationConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Presentation);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        V2Assert.Is(output, Parts, KnownFormats.All.Single(f => f.Id == to), expectMarker: true);
        if (to is "png" or "jpg")
        {
            Assert.Equal(3, Parts.Count); // the sample has a title slide and two content slides
            Assert.Equal(1920u, new MagickImageInfo(Parts[0]).Width);
        }
    }
}

public sealed class SpreadsheetConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Spreadsheet);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        V2Assert.Is(output, Parts, KnownFormats.All.Single(f => f.Id == to), expectMarker: true);

        // Workbooks (two sheets) become one CSV/TSV per sheet; CSV and TSV samples have one sheet.
        if (to is "csv" or "tsv")
        {
            Assert.Equal(from is "csv" or "tsv" ? 1 : 2, Parts.Count);
        }
    }
}

/// <summary>The fallback engines, tested directly so they're covered even on a PC with Office.</summary>
public sealed class FallbackConversionTests(Samples samples)
{
    public static TheoryData<string, string> WordTypePairs => new()
    {
        { "docx", "odt" }, { "docx", "rtf" }, { "odt", "docx" }, { "odt", "rtf" }, { "rtf", "docx" }, { "rtf", "odt" },
    };

    [Theory]
    [MemberData(nameof(WordTypePairs))]
    public async Task Pandoc_converts_word_type_documents_without_office(string from, string to)
    {
        Assert.SkipWhen(samples.Engines.PandocPath is null, "Pandoc isn't installed (run tools/fetch-engines.ps1).");
        var output = await Run(new PandocConverter(samples.Engines.PandocPath!), from, to, EngineKind.Pandoc);
        V2Assert.Is(output, [output], KnownFormats.All.Single(f => f.Id == to), expectMarker: true);
    }

    [Fact]
    public async Task Built_in_reader_converts_xls_to_xlsx_without_office()
    {
        Assert.SkipWhen(samples.PathFor(Xls) is null, "No XLS sample (needs Excel to create one).");
        var output = await Run(new SheetConverter(), "xls", "xlsx", EngineKind.Sheet);
        V2Assert.Is(output, [output], Xlsx, expectMarker: true);
    }

    private async Task<string> Run(IConverter converter, string fromId, string toId, EngineKind engine)
    {
        var from = KnownFormats.All.Single(f => f.Id == fromId);
        var to = KnownFormats.All.Single(f => f.Id == toId);
        var source = samples.PathFor(from);
        Assert.SkipWhen(source is null, $"No {from.Label} sample.");
        var folder = Directory.CreateDirectory(Path.Combine(samples.Folder, "fallback", $"{fromId}-to-{toId}")).FullName;
        var work = Directory.CreateDirectory(Path.Combine(folder, "work")).FullName;
        var output = Path.Combine(folder, $"out.{to.PrimaryExtension}");
        await converter.ConvertAsync(
            new ConversionRequest(source!, output, new ConversionRule(from, to, engine), new QualitySettings(), work),
            new Progress<double>(),
            TestContext.Current.CancellationToken);
        return output;
    }
}

public class V2UnitTests
{
    [Fact]
    public void Csv_round_trips_quotes_commas_and_new_lines()
    {
        object?[][] rows = [["name", "note"], ["Fig, dried", "said \"hi\"\nthen left"], ["01234", 12.5]];

        var text = SheetData.FormatDelimited(rows, ',');
        var parsed = SheetData.ParseDelimited(text, ',');

        Assert.Equal("name,note\r\n\"Fig, dried\",\"said \"\"hi\"\"\nthen left\"\r\n01234,12.5\r\n", text);
        Assert.Equal(3, parsed.Count);
        Assert.Equal("said \"hi\"\nthen left", parsed[1][1]);
        Assert.Equal("01234", SheetData.CellValue("01234")); // postcodes keep their leading zero
        Assert.Equal(12.5, SheetData.CellValue("12.5"));
    }

    [Fact]
    public void Pdf_text_is_escaped_so_it_cannot_become_links_or_html()
    {
        Assert.Equal(@"\!\[x\](http://evil) \<img\>", PdfLayout.Escape("![x](http://evil) <img>"));
        Assert.Equal(@"1\. not a list", PdfLayout.Escape("1. not a list"));
    }

    [Fact]
    public void Plain_text_becomes_simple_html()
    {
        var html = PandocConverter.TextToHtml("Line one\nline two\n\n<b>not bold</b>");

        Assert.Contains("<p>Line one<br/>line two</p>", html);
        Assert.Contains("<p>&lt;b&gt;not bold&lt;/b&gt;</p>", html);
    }

    [Fact]
    public void Extracted_images_are_inlined_and_other_links_left_alone()
    {
        var media = Directory.CreateTempSubdirectory("pomegranate-media-").FullName;
        try
        {
            var image = Path.Combine(media, "a.png");
            File.WriteAllBytes(image, [1, 2, 3]);

            var result = PandocConverter.InlineImages($"![one]({image}) ![two](http://example.com/b.png)", media);

            Assert.Contains("![one](data:image/png;base64,AQID)", result);
            Assert.Contains("![two](http://example.com/b.png)", result);
        }
        finally
        {
            Directory.Delete(media, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    public void Spreadsheet_column_names(int index, string expected)
    {
        Assert.Equal(expected, SheetData.ColumnName(index));
    }
}

/// <summary>Checks an output really is the target format, and (optionally) that the sample's text survived.</summary>
internal static class V2Assert
{
    public static void Is(string output, IReadOnlyList<string> parts, FileFormat target, bool expectMarker)
    {
        switch (target.Id)
        {
            case "pdf":
                Assert.StartsWith("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
                using (var pdf = UglyToad.PdfPig.PdfDocument.Open(output))
                {
                    Assert.True(pdf.NumberOfPages >= 1);
                    if (expectMarker)
                    {
                        Assert.Contains(Samples.Marker, string.Join(' ', pdf.GetPages().Select(p => p.Text)));
                    }
                }

                return;
            case "docx":
                AssertZipEntry(output, "word/document.xml");
                break;
            case "pptx":
                AssertZipEntry(output, "ppt/presentation.xml");
                return; // slide text lives in many parts
            case "xlsx":
                AssertZipEntry(output, "xl/workbook.xml");
                break;
            case "odt":
                AssertMimetype(output, "application/vnd.oasis.opendocument.text");
                break;
            case "ods":
                AssertMimetype(output, "application/vnd.oasis.opendocument.spreadsheet");
                break;
            case "odp":
                AssertMimetype(output, "application/vnd.oasis.opendocument.presentation");
                return;
            case "epub":
                AssertMimetype(output, "application/epub+zip");
                break;
            case "rtf":
                Assert.StartsWith(@"{\rtf", File.ReadAllText(output));
                break;
            case "html":
                Assert.Contains("<html", File.ReadAllText(output), StringComparison.OrdinalIgnoreCase);
                break;
            case "png":
            case "jpg":
                foreach (var part in parts)
                {
                    Assert.Equal(target.Id == "png" ? MagickFormat.Png : MagickFormat.Jpeg, new MagickImageInfo(part).Format);
                }

                return;
        }

        if (expectMarker)
        {
            Assert.Contains(Samples.Marker, AllText(output, target));
        }
    }

    private static string AllText(string path, FileFormat format)
    {
        if (format.Id is "txt" or "md" or "html" or "csv" or "tsv" or "rtf")
        {
            return File.ReadAllText(path);
        }

        // Zip-based formats: look through the XML/XHTML inside.
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal) ||
                                                     e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            sb.Append(reader.ReadToEnd());
        }

        return sb.ToString();
    }

    private static void AssertZipEntry(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry(entry));
    }

    private static void AssertMimetype(string path, string mimetype)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("mimetype");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal(mimetype, reader.ReadToEnd().Trim());
    }
}
