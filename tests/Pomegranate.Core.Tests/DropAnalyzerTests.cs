using Pomegranate.Core.Formats;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Core.Tests;

public class DropAnalyzerTests
{
    private static DropAnalysis Analyze(params string[] paths) =>
        new DropAnalyzer(FormatCatalog.Default, isDirectory: p => p.EndsWith('\\')).Analyze(paths);

    [Fact]
    public void Single_image_offers_its_targets()
    {
        var result = Analyze(@"C:\a\photo.png");

        Assert.True(result.CanConvert);
        Assert.Equal(FormatCatalog.Default.TargetsFor(Png), result.ConvertTargets);
    }

    [Fact]
    public void Mixed_images_offer_formats_that_work_for_all_and_keep_shared_ones()
    {
        var result = Analyze(@"C:\a\one.png", @"C:\a\two.jpg");

        Assert.True(result.CanConvert);
        // JPG is offered: the PNG converts and the JPG is skipped. Same for PNG.
        Assert.Contains(Jpg, result.ConvertTargets);
        Assert.Contains(Png, result.ConvertTargets);
        Assert.Contains(WebP, result.ConvertTargets);
        Assert.Contains(Pdf, result.ConvertTargets);
    }

    [Fact]
    public void Same_format_files_never_offer_their_own_format()
    {
        var result = Analyze(@"C:\a\one.png", @"C:\a\two.png");

        Assert.DoesNotContain(Png, result.ConvertTargets);
    }

    [Fact]
    public void Image_plus_video_has_no_shared_formats()
    {
        var result = Analyze(@"C:\a\one.png", @"C:\a\clip.mp4");

        Assert.Equal(ConvertAvailability.NoSharedFormats, result.Availability);
        Assert.Empty(result.ConvertTargets);
    }

    [Fact]
    public void Video_and_audio_share_mp3()
    {
        var result = Analyze(@"C:\a\clip.mp4", @"C:\a\song.wav");

        Assert.Equal([Mp3], result.ConvertTargets);
    }

    [Fact]
    public void Folders_get_actions_only()
    {
        var result = Analyze(@"C:\a\photo.png", @"C:\a\folder\");

        Assert.Equal(ConvertAvailability.ContainsFolders, result.Availability);
        Assert.False(result.CanConvert);
    }

    [Fact]
    public void Unknown_file_is_unsupported()
    {
        Assert.Equal(ConvertAvailability.UnsupportedType, Analyze(@"C:\a\notes.docx").Availability);
        Assert.Equal(ConvertAvailability.UnsupportedType, Analyze(@"C:\a\paper.pdf").Availability);
    }

    [Fact]
    public void Unknown_file_among_known_ones_means_nothing_shared()
    {
        var result = Analyze(@"C:\a\photo.png", @"C:\a\notes.docx");

        Assert.Equal(ConvertAvailability.NoSharedFormats, result.Availability);
    }

    [Fact]
    public void Nothing_dropped_is_empty()
    {
        Assert.Equal(ConvertAvailability.Empty, Analyze().Availability);
    }
}
