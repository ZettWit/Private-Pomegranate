using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Engines.Media;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Engines.Tests;

public class FfmpegArgumentsTests
{
    private static readonly QualitySettings Q = new();

    private static IReadOnlyList<string> Args(FileFormat target, string? video, string? audio) =>
        FfmpegArguments.Build("in", "out", target, new MediaInfo(TimeSpan.FromSeconds(10), video, audio, "x"), Q);

    private static string? After(IReadOnlyList<string> args, string flag)
    {
        var i = args.ToList().IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    [Fact]
    public void H264_and_aac_are_copied_into_mp4()
    {
        var args = Args(Mp4, "h264", "aac");

        Assert.Equal("copy", After(args, "-c:v"));
        Assert.Equal("copy", After(args, "-c:a"));
        Assert.Equal("mp4", After(args, "-f"));
    }

    [Fact]
    public void Incompatible_audio_is_reencoded_while_video_is_copied()
    {
        var args = Args(Mp4, "h264", "flac");

        Assert.Equal("copy", After(args, "-c:v"));
        Assert.Equal("aac", After(args, "-c:a"));
        Assert.Equal("192k", After(args, "-b:a"));
    }

    [Fact]
    public void Vp9_to_mp4_is_reencoded_with_spec_defaults()
    {
        var args = Args(Mp4, "vp9", "opus");

        Assert.Equal("libx264", After(args, "-c:v"));
        Assert.Equal("23", After(args, "-crf"));
        Assert.Equal("medium", After(args, "-preset"));
        Assert.Equal("yuv420p", After(args, "-pix_fmt"));
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public void Hevc_gets_the_hvc1_tag_in_mp4()
    {
        Assert.Equal("hvc1", After(Args(Mov, "hevc", null), "-tag:v"));
    }

    [Fact]
    public void Webm_uses_vp9_and_opus()
    {
        var args = Args(WebM, "h264", "aac");

        Assert.Equal("libvpx-vp9", After(args, "-c:v"));
        Assert.Equal("32", After(args, "-crf"));
        Assert.Equal("libopus", After(args, "-c:a"));
    }

    [Fact]
    public void Avi_pairs_h264_with_mp3()
    {
        var args = Args(Avi, "vp9", "aac");

        Assert.Equal("libx264", After(args, "-c:v"));
        Assert.Equal("libmp3lame", After(args, "-c:a"));
    }

    [Fact]
    public void Video_without_audio_maps_audio_optionally_and_sets_no_audio_codec()
    {
        var args = Args(Mp4, "gif", null);

        Assert.Contains("0:a:0?", args);
        Assert.Null(After(args, "-c:a"));
    }

    [Fact]
    public void Gif_uses_a_generated_palette()
    {
        var args = Args(Gif, "h264", "aac");

        Assert.Contains("palettegen", After(args, "-filter_complex"));
        Assert.Contains("fps=15", After(args, "-filter_complex"));
        Assert.Contains("-an", args);
    }

    [Fact]
    public void Video_to_mp3_drops_the_picture()
    {
        var args = Args(Mp3, "h264", "aac");

        Assert.Contains("-vn", args);
        Assert.Equal("libmp3lame", After(args, "-c:a"));
        Assert.Equal("2", After(args, "-q:a"));
    }

    [Fact]
    public void Opus_moves_between_ogg_and_opus_without_reencoding()
    {
        Assert.Equal("copy", After(Args(Opus, null, "opus"), "-c:a"));
        Assert.Equal("copy", After(Args(Ogg, null, "opus"), "-c:a"));
        Assert.Equal("libopus", After(Args(Opus, null, "vorbis"), "-c:a"));
    }

    [Fact]
    public void Audio_target_without_audio_track_is_a_clear_error()
    {
        var ex = Assert.Throws<ConversionException>(() => Args(Mp3, "h264", null));
        Assert.Contains("no audio", ex.Message);
    }

    [Fact]
    public void Every_media_target_has_a_muxer()
    {
        var targets = FormatCatalog.Default.AllRules.Where(r => r.Engine == EngineKind.Media).Select(r => r.To).Distinct();

        Assert.All(targets, t => Assert.False(string.IsNullOrEmpty(FfmpegArguments.MuxerFor(t))));
    }
}

public class MediaInfoTests
{
    [Fact]
    public void Parses_streams_and_duration_and_ignores_cover_art()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "mjpeg", "disposition": { "attached_pic": 1 } },
                { "codec_type": "audio", "codec_name": "mp3" }
              ],
              "format": { "format_name": "mp3", "duration": "12.500000" }
            }
            """;

        var info = MediaInfo.Parse(json);

        Assert.False(info.HasVideo);
        Assert.Equal("mp3", info.AudioCodec);
        Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
    }

    [Fact]
    public void Progress_lines_become_fractions()
    {
        var values = new List<double>();
        var progress = new SyncProgress(values);

        MediaConverter.ReportProgress("out_time_us=2500000", TimeSpan.FromSeconds(10), progress);
        MediaConverter.ReportProgress("frame=12", TimeSpan.FromSeconds(10), progress);
        MediaConverter.ReportProgress("out_time_us=99000000", TimeSpan.FromSeconds(10), progress);
        MediaConverter.ReportProgress("out_time_us=1000", null, progress);

        Assert.Equal([0.25, 0.99], values);
    }

    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}

public class SubtitleParsingTests
{
    [Fact]
    public void Srt_with_bom_crlf_and_extra_blank_lines_parses()
    {
        var cues = SubtitleConverter.ParseSrt("﻿1\r\n00:00:01,000 --> 00:00:02,000\r\nOne\r\n\r\n \r\n\r\n2\r\n00:00:03,000 --> 00:00:04,000\r\nTwo\r\n");

        Assert.Equal(2, cues.Count);
        Assert.Equal("Two", cues[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(3), cues[1].Start);
    }

    [Fact]
    public void Vtt_header_notes_and_short_times_are_handled()
    {
        var cues = SubtitleConverter.ParseVtt("WEBVTT - title\n\nNOTE skip me\n\nSTYLE\n::cue { color: red }\n\nid-1\n01:02.345 --> 01:03.000 line:0\nText\n");

        var cue = Assert.Single(cues);
        Assert.Equal(new TimeSpan(0, 0, 1, 2, 345), cue.Start);
        Assert.Equal("Text", cue.Text);
    }

    [Theory]
    [InlineData("00:00:01,500", 1500)]
    [InlineData("01:00:00.000", 3_600_000)]
    [InlineData("02:03.004", 123_004)]
    public void Times_parse(string value, int ms)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(ms), SubtitleConverter.ParseTime(value));
    }

    [Fact]
    public void Text_output_collapses_repeated_lines()
    {
        var cues = new[]
        {
            new SubtitleCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "<b>Same</b>"),
            new SubtitleCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Same"),
            new SubtitleCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Tom &amp; Jerry"),
        };

        Assert.Equal("Same\r\nTom & Jerry\r\n", SubtitleConverter.WriteText(cues));
    }
}

public class ArchiveNameTests
{
    [Theory]
    [InlineData(@"docs\readme.txt", "docs/readme.txt")]
    [InlineData("/abs/path.txt", "abs/path.txt")]
    [InlineData("../../evil.txt", "evil.txt")]
    [InlineData("a/./b/../c.txt", "a/b/c.txt")]
    [InlineData(null, "")]
    public void Entry_names_are_made_safe(string? key, string expected)
    {
        Assert.Equal(expected, ArchiveConverter.NormalizeName(key));
    }
}

public class EngineLocatorTests
{
    [Fact]
    public void Missing_folder_means_no_external_engines()
    {
        var locator = new EngineLocator(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));
        var set = EngineSet.Create(locator);

        Assert.Null(locator.FfmpegPath);
        Assert.DoesNotContain(EngineKind.Media, set.Available);
        Assert.Empty(set.Catalog.TargetsFor(Mp4));
        Assert.NotEmpty(set.Catalog.TargetsFor(Png));
    }
}
