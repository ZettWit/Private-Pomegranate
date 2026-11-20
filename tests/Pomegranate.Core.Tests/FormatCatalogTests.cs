using Pomegranate.Core.Formats;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Core.Tests;

public class FormatCatalogTests
{
    private readonly FormatCatalog _catalog = FormatCatalog.Default;

    [Theory]
    [InlineData(@"C:\a\photo.jpg", "jpg")]
    [InlineData(@"C:\a\PHOTO.JPEG", "jpg")]
    [InlineData(@"C:\a\shot.HEIF", "heic")]
    [InlineData(@"C:\a\backup.tar.gz", "targz")]
    [InlineData(@"C:\a\backup.tgz", "targz")]
    [InlineData(@"C:\a\backup.tar", "tar")]
    [InlineData(@"C:\a\IMG_0001.CR3", "raw")]
    [InlineData(@"C:\a\IMG_0001.dng", "dng")]
    [InlineData(@"C:\a\clip.m4v", "mp4")]
    [InlineData(@"C:\a\subs.vtt", "vtt")]
    [InlineData(@"C:\a\my.photo.v2.png", "png")]
    public void Detects_format_from_extension(string path, string expectedId)
    {
        Assert.Equal(expectedId, _catalog.Detect(path)?.Id);
    }

    [Theory]
    [InlineData(@"C:\a\notes.pages")]
    [InlineData(@"C:\a\README")]
    [InlineData(@"C:\a\.png")]
    [InlineData(@"C:\a\archive.gz")]
    public void Unknown_files_have_no_format(string path)
    {
        Assert.Null(_catalog.Detect(path));
    }

    [Fact]
    public void Images_convert_to_other_rasters_and_pdf_but_not_themselves()
    {
        var targets = _catalog.TargetsFor(Png);

        Assert.DoesNotContain(Png, targets);
        Assert.Equal([Jpg, WebP, Avif, Tiff, Bmp, Pdf], targets);
    }

    [Fact]
    public void Heic_is_input_only()
    {
        Assert.NotEmpty(_catalog.TargetsFor(Heic));
        Assert.DoesNotContain(_catalog.AllRules, r => r.To == Heic);
    }

    [Fact]
    public void Catalog_can_drop_conversions_for_missing_engines()
    {
        var withoutDng = _catalog.WithEngines([EngineKind.Image, EngineKind.Media]);

        Assert.DoesNotContain(Dng, withoutDng.TargetsFor(Raw));
        Assert.Contains(Jpg, withoutDng.TargetsFor(Raw));
        Assert.Empty(withoutDng.TargetsFor(Zip));
        Assert.Equal(Zip, withoutDng.Detect("a.zip")); // formats are still recognised
    }

    [Fact]
    public void Only_raw_files_can_become_dng()
    {
        Assert.Contains(Dng, _catalog.TargetsFor(Raw));
        Assert.DoesNotContain(Dng, _catalog.TargetsFor(Dng));
        Assert.DoesNotContain(Dng, _catalog.TargetsFor(Jpg));
        Assert.Equal(EngineKind.Dng, _catalog.FindRule(Raw, Dng)?.Engine);
        Assert.Equal(EngineKind.Image, _catalog.FindRule(Raw, Jpg)?.Engine);
    }

    [Fact]
    public void Input_only_formats_are_never_targets()
    {
        var allTargets = _catalog.AllRules.Select(r => r.To).Distinct().ToArray();

        Assert.DoesNotContain(Svg, allTargets);
        Assert.DoesNotContain(Rar, allTargets);
        Assert.DoesNotContain(Raw, allTargets);
        Assert.DoesNotContain(Heic, allTargets);
        Assert.DoesNotContain(Doc, allTargets);
        Assert.DoesNotContain(Ppt, allTargets);
        Assert.DoesNotContain(Xls, allTargets);
    }

    [Fact]
    public void Video_converts_to_other_video_and_mp3()
    {
        var targets = _catalog.TargetsFor(Mov);

        Assert.Contains(Mp4, targets);
        Assert.Contains(Gif, targets);
        Assert.Contains(Mp3, targets);
        Assert.DoesNotContain(Mov, targets);
        Assert.DoesNotContain(Wav, targets);
        Assert.DoesNotContain(Mp3, _catalog.TargetsFor(Gif));
    }

    [Fact]
    public void Subtitles_convert_between_each_other_and_to_text()
    {
        Assert.Equal([Vtt, Txt], _catalog.TargetsFor(Srt));
        Assert.Equal([Srt, Txt], _catalog.TargetsFor(Vtt));
    }

    [Fact]
    public void Rule_counts_match_the_spec()
    {
        int Count(FormatCategory category) =>
            _catalog.AllRules.Count(r => r.From.Category == category);

        // Rasters: 6 inputs × (5 others + PDF) + HEIC × (6 + PDF); SVG 7; RAW 8 (incl. DNG); DNG 7.
        Assert.Equal(36 + 7 + 7 + 8 + 7, Count(FormatCategory.Image));
        Assert.Equal(56, Count(FormatCategory.Audio));
        Assert.Equal(48, Count(FormatCategory.Video)); // 7 × 6 + MP3 from every video except GIF
        Assert.Equal(9, Count(FormatCategory.Archive));
        Assert.Equal(4, Count(FormatCategory.Subtitle));

        // v2: DOCX/ODT/RTF × 7, Markdown/HTML/EPUB/TXT × 7, DOC × 6, PDF × 8.
        Assert.Equal(21 + 28 + 6 + 8, Count(FormatCategory.Document));
        Assert.Equal(4 + 5 + 4, Count(FormatCategory.Presentation));
        Assert.Equal(4 + 5 + 4 + 4 + 4, Count(FormatCategory.Spreadsheet));
        Assert.Equal(182 + 63 + 13 + 21, _catalog.AllRules.Count());
    }

    [Fact]
    public void Office_is_preferred_and_falls_back_where_a_fallback_exists()
    {
        Assert.Equal(EngineKind.OfficeText, _catalog.FindRule(Docx, Odt)?.Engine);
        Assert.Equal(EngineKind.OfficeSheet, _catalog.FindRule(Xls, Xlsx)?.Engine);

        EngineKind[] office = [EngineKind.OfficeText, EngineKind.OfficeSheet, EngineKind.OfficeSlides];
        var withoutOffice = _catalog.WithEngines(Enum.GetValues<EngineKind>().Except(office));

        Assert.Equal(EngineKind.Pandoc, withoutOffice.FindRule(Docx, Odt)?.Engine);
        Assert.Equal(EngineKind.Sheet, withoutOffice.FindRule(Xls, Xlsx)?.Engine);
        Assert.Null(withoutOffice.FindRule(Docx, Pdf));
        Assert.Null(withoutOffice.FindRule(Pptx, Pdf));
        Assert.Empty(withoutOffice.TargetsFor(Ods));
        Assert.Contains(Csv, withoutOffice.TargetsFor(Xlsx));
        Assert.Equal(EngineKind.WebPdf, withoutOffice.FindRule(Markdown, Pdf)?.Engine);
    }

    [Fact]
    public void Pdf_offers_documents_images_and_text()
    {
        Assert.Equal([Docx, Jpg, Png, Txt, Markdown, Epub, Pptx, Xlsx], _catalog.TargetsFor(Pdf));
    }

    [Theory]
    [InlineData(@"C:\a\photo.jpg", "photo")]
    [InlineData(@"C:\a\backup.tar.gz", "backup")]
    [InlineData(@"C:\a\my.photo.v2.PNG", "my.photo.v2")]
    [InlineData(@"C:\a\notes.docx", "notes")]
    public void Base_name_strips_the_whole_format_extension(string path, string expected)
    {
        Assert.Equal(expected, _catalog.BaseName(path));
    }

    [Fact]
    public void Every_extension_belongs_to_exactly_one_format()
    {
        var duplicates = KnownFormats.All
            .SelectMany(f => f.Extensions)
            .GroupBy(e => e)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }
}
