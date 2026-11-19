using ImageMagick;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Engines.Media;
using SharpCompress.Readers;

namespace Pomegranate.Engines.Tests;

/// <summary>
/// Runs every conversion in the v1 table on a real sample file and checks the output really is the
/// target format. One class per category so the categories run in parallel.
/// </summary>
public abstract class ConversionMatrixTests(Samples samples)
{
    protected Samples Samples { get; } = samples;

    public static TheoryData<string, string> RulesFor(FormatCategory category)
    {
        var data = new TheoryData<string, string>();
        foreach (var rule in FormatCatalog.Default.AllRules.Where(r => r.From.Category == category))
        {
            data.Add(rule.From.Id, rule.To.Id);
        }

        return data;
    }

    protected async Task<string> ConvertAsync(string fromId, string toId)
    {
        var from = KnownFormats.All.Single(f => f.Id == fromId);
        var to = KnownFormats.All.Single(f => f.Id == toId);
        var rule = FormatCatalog.Default.FindRule(from, to)!;

        var source = Samples.PathFor(from);
        Assert.SkipWhen(source is null, $"No {from.Label} sample. Put one in tests/samples/ to run this test.");

        var converter = Samples.EngineSet.Converters.SingleOrDefault(c => c.Engine == rule.Engine);
        Assert.SkipWhen(converter is null, $"The {rule.Engine} engine isn't installed (run tools/fetch-engines.ps1).");

        var outFolder = Directory.CreateDirectory(Path.Combine(Samples.Folder, "out", $"{fromId}-to-{toId}")).FullName;
        var output = Path.Combine(outFolder, $".pomegranate-test.{to.PrimaryExtension}");
        var request = new ConversionRequest(source!, output, rule, new QualitySettings());
        var reported = new List<double>();

        await converter!.ConvertAsync(request, new Progress(reported), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(output), "No output file was written.");
        Assert.True(new FileInfo(output).Length > 0, "The output file is empty.");
        Assert.Equal(1, reported.LastOrDefault());
        return output;
    }

    private sealed class Progress(List<double> values) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (values)
            {
                values.Add(value);
            }
        }
    }
}

public sealed class ImageConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Image);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);

        if (to == "pdf")
        {
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
            return;
        }

        if (to == "dng")
        {
            var header = File.ReadAllBytes(output).AsSpan(0, 4).ToArray();
            Assert.True(header is [0x49, 0x49, 0x2A, 0x00] or [0x4D, 0x4D, 0x00, 0x2A], "DNG output isn't a TIFF-based file.");
            return;
        }

        var target = KnownFormats.All.Single(f => f.Id == to);
        var info = new MagickImageInfo(output);
        Assert.Equal(ImageConverter.MagickFormatFor(target), info.Format);
    }

    [Fact]
    public async Task Transparent_areas_become_white_in_jpg()
    {
        var output = await ConvertAsync("png", "jpg");

        using var image = new MagickImage(output);
        var corner = image.GetPixels().GetPixel(0, 0).ToColor()!;
        Assert.True(corner.R > 60000 && corner.G > 60000 && corner.B > 60000, $"Corner is {corner}, expected white.");
    }
}

public sealed class AudioConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Audio);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        await MediaAssert.IsAsync(Samples, output, KnownFormats.All.Single(f => f.Id == to));
    }
}

public sealed class VideoConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Video);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        await MediaAssert.IsAsync(Samples, output, KnownFormats.All.Single(f => f.Id == to));
    }
}

public sealed class ArchiveConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Archive);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);

        var files = new Dictionary<string, string>();
        using (var stream = File.OpenRead(output))
        using (var reader = ReaderFactory.OpenReader(stream))
        {
            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory)
                {
                    using var entry = new StreamReader(reader.OpenEntryStream());
                    files[ArchiveConverter.NormalizeName(reader.Entry.Key)] = await entry.ReadToEndAsync(TestContext.Current.CancellationToken);
                }
            }
        }

        if (from == "rar")
        {
            Assert.NotEmpty(files); // a real-world sample with its own content
            return;
        }

        Assert.Equal(Samples.ArchiveContent.OrderBy(p => p.Key), files.OrderBy(p => p.Key));
    }
}

public sealed class SubtitleConversionTests(Samples samples) : ConversionMatrixTests(samples)
{
    public static TheoryData<string, string> Rules => RulesFor(FormatCategory.Subtitle);

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Converts(string from, string to)
    {
        var output = await ConvertAsync(from, to);
        var text = await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken);

        switch (to)
        {
            case "vtt":
                Assert.Equal(
                    "WEBVTT\n\n00:00:01.000 --> 00:00:02.500\nHello <i>world</i>\n\n00:01:03.250 --> 00:01:04.000\nSecond line\nand more\n\n",
                    text);
                break;
            case "srt":
                Assert.Equal(
                    "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello <i>world</i>\r\n\r\n2\r\n00:01:03,250 --> 00:01:04,000\r\nSecond line\r\nand more\r\n\r\n",
                    text);
                break;
            case "txt":
                Assert.Equal("Hello world\r\nSecond line\r\nand more\r\n", text);
                break;
        }
    }
}

internal static class MediaAssert
{
    private static readonly Dictionary<string, (string Muxer, string Codec)> Expected = new()
    {
        ["mp3"] = ("mp3", "mp3"),
        ["m4a"] = ("mp4", "aac"),
        ["wav"] = ("wav", "pcm_s16le"),
        ["flac"] = ("flac", "flac"),
        ["ogg"] = ("ogg", ""),       // vorbis, or opus copied from an .opus file
        ["opus"] = ("ogg", "opus"),
        ["aiff"] = ("aiff", "pcm_s16be"),
        ["wma"] = ("asf", "wmav2"),
        ["mp4"] = ("mp4", ""),
        ["mov"] = ("mov", ""),
        ["mkv"] = ("matroska", ""),
        ["webm"] = ("webm", ""),
        ["avi"] = ("avi", ""),
        ["wmv"] = ("asf", "wmv2"),
        ["gif"] = ("gif", "gif"),
    };

    public static async Task IsAsync(Samples samples, string path, FileFormat target)
    {
        var probe = new MediaConverter(samples.Engines.FfmpegPath!, samples.Engines.FfprobePath!);
        var info = await probe.ProbeAsync(path, TestContext.Current.CancellationToken);
        var (muxer, codec) = Expected[target.Id];

        Assert.Contains(muxer, info.FormatName);
        if (target.Category == FormatCategory.Audio)
        {
            Assert.True(info.HasAudio, "Audio output has no audio.");
            Assert.False(info.HasVideo, "Audio output has a video track.");
            if (codec.Length > 0)
            {
                Assert.Equal(codec, info.AudioCodec);
            }
        }
        else
        {
            Assert.True(info.HasVideo, "Video output has no video.");
            if (codec.Length > 0)
            {
                Assert.Equal(codec, info.VideoCodec);
            }

            if (target == KnownFormats.WebM)
            {
                Assert.Contains(info.VideoCodec, new[] { "vp8", "vp9", "av1" });
            }
        }
    }
}
