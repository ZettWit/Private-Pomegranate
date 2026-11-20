using System.Formats.Tar;
using System.IO.Compression;
using ImageMagick;
using ImageMagick.Drawing;
using Pomegranate.Core.Formats;
using Pomegranate.Engines;

[assembly: AssemblyFixture(typeof(Pomegranate.Engines.Tests.Samples))]

namespace Pomegranate.Engines.Tests;

/// <summary>
/// Generates one small sample file per input format, once per test run. Formats no bundled engine can
/// write (HEIC, camera RAW, DNG, RAR) come from <c>tests/samples/</c> if present; their tests skip otherwise.
/// </summary>
public sealed partial class Samples : IAsyncLifetime
{
    private readonly Dictionary<FileFormat, string> _files = [];

    public string Folder { get; } = Directory.CreateTempSubdirectory("pomegranate-samples-").FullName;

    public EngineLocator Engines { get; } = EngineLocator.FindDefault();

    public EngineSet EngineSet => Pomegranate.Engines.EngineSet.Create(Engines);

    /// <summary>The archive content every archive sample holds.</summary>
    public static IReadOnlyDictionary<string, string> ArchiveContent { get; } = new Dictionary<string, string>
    {
        ["docs/readme.txt"] = "Hello from Pomegranate.",
        ["img/data.bin"] = new string('x', 5000),
    };

    public const string Srt = "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello <i>world</i>\r\n\r\n2\r\n00:01:03,250 --> 00:01:04,000\r\n<font color=\"red\">Second</font> line\r\nand more\r\n\r\n";

    public const string Vtt = "WEBVTT\n\nNOTE a comment\n\nintro\n00:01.000 --> 00:02.500 align:start\nHello <i>world</i>\n\n01:03.250 --> 01:04.000\n<v Ann>Second</v> line\nand more\n";

    public string? PathFor(FileFormat format) => _files.GetValueOrDefault(format);

    public async ValueTask InitializeAsync()
    {
        MakeImages();
        MakeArchives();
        await File.WriteAllTextAsync(Add(KnownFormats.Srt, "sample.srt"), Srt);
        await File.WriteAllTextAsync(Add(KnownFormats.Vtt, "sample.vtt"), Vtt);
        if (Engines.FfmpegPath is not null)
        {
            await MakeMediaAsync();
        }

        await MakeDocumentsAsync();
        MakeSheets();
        await MakeOfficeSamplesAsync();
        AddExternalSamples();
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private string Add(FileFormat format, string name)
    {
        var path = Path.Combine(Folder, name);
        _files[format] = path;
        return path;
    }

    private void MakeImages()
    {
        // A red circle on a transparent background, so alpha handling gets exercised.
        using var image = new MagickImage(MagickColors.Transparent, 64, 48);
        new Drawables().FillColor(MagickColors.Red).Circle(32, 24, 32, 8).Draw(image);

        image.Write(Add(KnownFormats.Png, "sample.png"), MagickFormat.Png);
        image.Write(Add(KnownFormats.WebP, "sample.webp"), MagickFormat.WebP);
        image.Write(Add(KnownFormats.Avif, "sample.avif"), MagickFormat.Avif);
        image.Write(Add(KnownFormats.Tiff, "sample.tiff"), MagickFormat.Tiff);
        image.Write(Add(KnownFormats.Bmp, "sample.bmp"), MagickFormat.Bmp);

        using var opaque = image.Clone();
        opaque.BackgroundColor = MagickColors.White;
        opaque.Alpha(AlphaAction.Remove);
        opaque.Write(Add(KnownFormats.Jpg, "sample.jpg"), MagickFormat.Jpeg);

        File.WriteAllText(
            Add(KnownFormats.Svg, "sample.svg"),
            "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='30'><circle cx='20' cy='15' r='10' fill='red'/></svg>");
    }

    private void MakeArchives()
    {
        var tree = Directory.CreateDirectory(Path.Combine(Folder, "tree")).FullName;
        foreach (var (name, content) in ArchiveContent)
        {
            var path = Path.Combine(tree, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        ZipFile.CreateFromDirectory(tree, Add(KnownFormats.Zip, "sample.zip"));
        TarFile.CreateFromDirectory(tree, Add(KnownFormats.Tar, "sample.tar"), includeBaseDirectory: false);

        using var tarGz = File.Create(Add(KnownFormats.TarGz, "sample.tar.gz"));
        using var gzip = new GZipStream(tarGz, CompressionLevel.Fastest);
        TarFile.CreateFromDirectory(tree, gzip, includeBaseDirectory: false);
    }

    private async Task MakeMediaAsync()
    {
        string[] sine = ["-f", "lavfi", "-i", "sine=frequency=440:duration=1:sample_rate=44100"];
        var audio = new (FileFormat Format, string Name, string[] Codec)[]
        {
            (KnownFormats.Mp3, "sample.mp3", ["-c:a", "libmp3lame"]),
            (KnownFormats.M4a, "sample.m4a", ["-c:a", "aac"]),
            (KnownFormats.Wav, "sample.wav", ["-c:a", "pcm_s16le"]),
            (KnownFormats.Flac, "sample.flac", ["-c:a", "flac"]),
            (KnownFormats.Ogg, "sample.ogg", ["-c:a", "libvorbis"]),
            (KnownFormats.Opus, "sample.opus", ["-c:a", "libopus"]),
            (KnownFormats.Aiff, "sample.aiff", ["-c:a", "pcm_s16be"]),
            (KnownFormats.Wma, "sample.wma", ["-c:a", "wmav2"]),
        };

        string[] video = ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=15:duration=1", .. sine, "-shortest"];
        string[] h264 = ["-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p"];
        var videos = new (FileFormat Format, string Name, string[] Codec)[]
        {
            (KnownFormats.Mp4, "sample.mp4", [.. h264, "-c:a", "aac"]),
            (KnownFormats.Mov, "sample.mov", [.. h264, "-c:a", "aac"]),
            (KnownFormats.Mkv, "sample.mkv", [.. h264, "-c:a", "flac"]),
            (KnownFormats.WebM, "sample.webm", ["-c:v", "libvpx-vp9", "-deadline", "realtime", "-c:a", "libopus"]),
            (KnownFormats.Avi, "sample.avi", ["-c:v", "mpeg4", "-c:a", "libmp3lame"]),
            (KnownFormats.Wmv, "sample.wmv", ["-c:v", "wmv2", "-c:a", "wmav2"]),
        };

        var jobs = audio.Select(a => Ffmpeg([.. sine, .. a.Codec, Add(a.Format, a.Name)]))
            .Concat(videos.Select(v => Ffmpeg([.. video, .. v.Codec, Add(v.Format, v.Name)])))
            .Append(Ffmpeg(["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=10:duration=1", Add(KnownFormats.Gif, "sample.gif")]));

        await Task.WhenAll(jobs);
    }

    private async Task Ffmpeg(string[] arguments)
    {
        var result = await ProcessRunner.RunAsync(
            Engines.FfmpegPath!, ["-hide_banner", "-loglevel", "error", "-y", .. arguments], CancellationToken.None);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Making a sample failed: {string.Join(' ', arguments)}\n{result.StandardErrorTail}");
        }
    }

    /// <summary>Picks up real-world samples from tests/samples/ (sample.heic, sample.cr2, sample.dng, sample.rar).</summary>
    private void AddExternalSamples()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var samples = Path.Combine(dir.FullName, "tests", "samples");
            if (!Directory.Exists(samples))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(samples))
            {
                var format = FormatCatalog.Default.Detect(file);
                if (format is not null && !_files.ContainsKey(format))
                {
                    _files[format] = file;
                }
            }

            return;
        }
    }
}
