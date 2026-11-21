using System.IO.Compression;
using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Media;
using Pomegranate.Engines.Tools;
using ZXing;
using ZXing.Common;

namespace Pomegranate.Engines.Tests;

/// <summary>Sample files for the tool tests (spec §7.3): big enough that compressing them is meaningful.</summary>
public sealed class ToolSamples : IAsyncLifetime
{
    public const string QrText = "https://example.com/pomegranate?tool=qr";
    public const string OcrText = "POMEGRANATE TEXT TEST";

    public string Folder { get; } = Directory.CreateTempSubdirectory("pomegranate-tools-").FullName;

    public EngineLocator Engines { get; } = EngineLocator.FindDefault();

    public string File(string name) => Path.Combine(Folder, name);

    public async ValueTask InitializeAsync()
    {
        MakeImages();
        MakePdfs();
        MakeArchives();
        if (Engines.FfmpegPath is { } ffmpeg)
        {
            await RunAsync(ffmpeg,
                "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=4",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=4:sample_rate=44100",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k",
                "-metadata", "title=Secret title", "-metadata", "artist=Someone", "-shortest", File("video.mp4"));
            await RunAsync(ffmpeg,
                "-f", "lavfi", "-i", "sine=frequency=440:duration=4:sample_rate=44100", "-af", "volume=-30dB",
                "-c:a", "libmp3lame", "-b:a", "320k", "-metadata", "title=Secret title", File("quiet.mp3"));
        }
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

    private void MakeImages()
    {
        // A photo-like picture, stored sideways (EXIF orientation 6), with camera details and an sRGB profile.
        using (var photo = new MagickImage("plasma:red-blue", new MagickReadSettings { Width = 2000, Height = 1500 }))
        {
            photo.SetProfile(ColorProfiles.SRGB);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "TestCam");
            exif.SetValue(ExifTag.Artist, "Someone");
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            photo.SetProfile(exif);
            photo.Orientation = Orientation.RightTop;
            photo.Quality = 95;
            photo.Write(File("sideways.jpg"), MagickFormat.Jpeg);

            exif.SetValue(ExifTag.Orientation, (ushort)1);
            photo.SetProfile(exif);
            photo.Orientation = Orientation.TopLeft;
            photo.Resize(800, 600);
            photo.Write(File("upright.jpg"), MagickFormat.Jpeg);
            photo.Write(File("photo.png"), MagickFormat.Png);
        }

        using (var tagged = new MagickImage("gradient:white-navy", new MagickReadSettings { Width = 300, Height = 200 }))
        {
            tagged.SetAttribute("comment", "secret comment");
            tagged.Write(File("tagged.png"), MagickFormat.Png);
        }

        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 360, Height = 360, Margin = 2 },
        };
        var matrix = writer.Encode(QrText);
        var pixels = new byte[matrix.Width * matrix.Height * 3];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                var value = matrix[x, y] ? (byte)0 : (byte)255;
                var i = ((y * matrix.Width) + x) * 3;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            }
        }

        using (var qr = new MagickImage())
        {
            qr.ReadPixels(pixels, new PixelReadSettings((uint)matrix.Width, (uint)matrix.Height, StorageType.Char, PixelMapping.RGB));
            qr.Write(File("qr.png"), MagickFormat.Png);
        }

        using var text = new MagickImage(MagickColors.White, 1400, 220);
        new Drawables().Font("Arial").FontPointSize(72).FillColor(MagickColors.Black).Text(40, 140, OcrText).Draw(text);
        text.Write(File("text.png"), MagickFormat.Png);

        using var blank = new MagickImage(MagickColors.White, 400, 300);
        blank.Write(File("blank.png"), MagickFormat.Png);
    }

    private void MakePdfs()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        WritePdf(File("three.pdf"), pages: 3, withPhoto: true);
        WritePdf(File("other.pdf"), pages: 2, withPhoto: false);
        WritePdf(File("one.pdf"), pages: 1, withPhoto: false);
    }

    private void WritePdf(string path, int pages, bool withPhoto)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Secret title";
        document.Info.Author = "Someone";
        var font = new XFont("Arial", 20);
        for (var i = 1; i <= pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Pomegranate page {i}", font, XBrushes.Black, new XPoint(72, 72));
            if (withPhoto && i == 1)
            {
                using var photo = XImage.FromFile(File("upright.jpg"));
                gfx.DrawImage(photo, 72, 100, 400, 300);
            }
        }

        document.Save(path);
    }

    private void MakeArchives()
    {
        var tree = Directory.CreateDirectory(File("tree")).FullName;
        Directory.CreateDirectory(Path.Combine(tree, "inner"));
        System.IO.File.WriteAllText(Path.Combine(tree, "a.txt"), "alpha");
        System.IO.File.WriteAllText(Path.Combine(tree, "inner", "b.txt"), "beta");

        // Everything inside one top folder, so Extract should drop that level.
        ZipFile.CreateFromDirectory(tree, File("wrapped.zip"), CompressionLevel.Fastest, includeBaseDirectory: true);

        // An entry that tries to climb out of the target folder.
        using var zip = ZipFile.Open(File("evil.zip"), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("../evil.txt").Open()))
        {
            writer.Write("should not escape");
        }

        using (var writer = new StreamWriter(zip.CreateEntry("ok/good.txt").Open()))
        {
            writer.Write("fine");
        }
    }

    private static async Task RunAsync(string ffmpeg, params string[] args)
    {
        var result = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", .. args], CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }
}

public sealed class ToolEngineTests(ToolSamples samples) : IClassFixture<ToolSamples>
{
    private static readonly QualitySettings Quality = new();
    private static readonly ToolSettings Settings = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ToolOutput> RunAsync(ITool tool, ToolKind kind, string file, ToolOption option = ToolOption.None, ToolEdit? edit = null, string[]? more = null)
    {
        var source = samples.File(file);
        var sources = more is null ? [source] : new[] { source }.Concat(more.Select(samples.File)).ToArray();
        var work = Directory.CreateTempSubdirectory("pomegranate-tool-work-").FullName;
        var definition = ToolCatalog.Definition(kind);
        if (option == ToolOption.None)
        {
            option = definition.DefaultOption;
        }

        var request = new ToolRequest(kind, option, sources, FormatCatalog.Default.Detect(source), work, Quality, Settings, edit);
        return await tool.RunAsync(request, new Progress<double>(), Token);
    }

    private MediaTools Media()
    {
        Assert.SkipWhen(samples.Engines.FfmpegPath is null, "FFmpeg isn't installed (run tools/fetch-engines.ps1).");
        return new MediaTools(samples.Engines.FfmpegPath!, samples.Engines.FfprobePath!);
    }

    private Task<MediaInfo> ProbeAsync(string path) =>
        new MediaConverter(samples.Engines.FfmpegPath!, samples.Engines.FfprobePath!).ProbeAsync(path, Token);

    // ---- Images ---------------------------------------------------------------------------------

    [Fact]
    public async Task Compress_shrinks_a_photo_and_keeps_its_format()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Compress, "sideways.jpg", ToolOption.Strong);
        var path = output.Paths.Single();
        Assert.True(new FileInfo(path).Length < new FileInfo(samples.File("sideways.jpg")).Length);
        using var image = new MagickImage(path);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal(1600u, Math.Max(image.Width, image.Height)); // Strong caps the long side
        Assert.Equal(1200u, image.Width); // upright: it was stored sideways
    }

    [Fact]
    public async Task Compress_turns_a_PNG_into_256_colors()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Compress, "photo.png");
        Assert.True(new FileInfo(output.Paths[0]).Length < new FileInfo(samples.File("photo.png")).Length);
    }

    [Fact]
    public async Task Compressing_twice_has_nothing_to_do()
    {
        var first = await RunAsync(new ImageTools(), ToolKind.Compress, "blank.png", ToolOption.Light);
        var again = Path.Combine(samples.Folder, "blank-compressed.png");
        System.IO.File.Copy(first.Paths[0], again, overwrite: true);
        var ex = await Assert.ThrowsAsync<NothingToDoException>(() => RunAsync(new ImageTools(), ToolKind.Compress, "blank-compressed.png", ToolOption.Light));
        Assert.Equal(ImageTools.AlreadySmall, ex.Message);
    }

    [Fact]
    public void The_sideways_sample_really_is_sideways()
    {
        using var image = new MagickImage();
        image.Ping(samples.File("sideways.jpg"));
        Assert.Equal(Orientation.RightTop, image.Orientation);
    }

    [Theory]
    [InlineData(ToolOption.Scale50, 400u, 300u)]
    [InlineData(ToolOption.Scale25, 200u, 150u)]
    public async Task Resize_scales(ToolOption option, uint width, uint height)
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Resize, "upright.jpg", option);
        using var image = new MagickImage(output.Paths[0]);
        Assert.Equal((width, height), (image.Width, image.Height));
    }

    [Fact]
    public async Task Resize_to_a_long_side_only_shrinks()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Resize, "sideways.jpg", ToolOption.Long1280);
        using var image = new MagickImage(output.Paths[0]);
        Assert.Equal((960u, 1280u), (image.Width, image.Height));

        var ex = await Assert.ThrowsAsync<NothingToDoException>(() => RunAsync(new ImageTools(), ToolKind.Resize, "upright.jpg", ToolOption.Long1920));
        Assert.Contains("1920", ex.Message);
    }

    [Theory]
    [InlineData(ToolOption.RotateRight, 2000u, 1500u)]
    [InlineData(ToolOption.Rotate180, 1500u, 2000u)]
    [InlineData(ToolOption.Flip, 1500u, 2000u)]
    public async Task Rotate_works_on_the_upright_picture(ToolOption option, uint width, uint height)
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Rotate, "sideways.jpg", option);
        using var image = new MagickImage(output.Paths[0]);
        Assert.Equal((width, height), (image.Width, image.Height));
        Assert.True(image.Orientation is Orientation.TopLeft or Orientation.Undefined);
    }

    [Fact]
    public async Task Strip_metadata_on_a_rotated_photo_bakes_in_the_rotation_and_keeps_the_color_profile()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.StripMetadata, "sideways.jpg");
        using var image = new MagickImage(output.Paths[0]);
        Assert.Null(image.GetExifProfile());
        Assert.NotNull(image.GetColorProfile());
        Assert.Equal((1500u, 2000u), (image.Width, image.Height));
    }

    [Fact]
    public async Task Strip_metadata_on_an_upright_JPEG_keeps_every_pixel()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.StripMetadata, "upright.jpg");
        using var before = new MagickImage(samples.File("upright.jpg"));
        using var after = new MagickImage(output.Paths[0]);
        Assert.NotNull(before.GetExifProfile());
        Assert.Null(after.GetExifProfile());
        Assert.NotNull(after.GetColorProfile());
        Assert.Equal(before.Signature, after.Signature);
        Assert.DoesNotContain("TestCam", System.Text.Encoding.Latin1.GetString(await System.IO.File.ReadAllBytesAsync(output.Paths[0], Token)));
    }

    [Fact]
    public async Task Strip_metadata_on_a_PNG_drops_text_chunks()
    {
        Assert.Contains("secret comment", System.Text.Encoding.Latin1.GetString(await System.IO.File.ReadAllBytesAsync(samples.File("tagged.png"), Token)));
        var output = await RunAsync(new ImageTools(), ToolKind.StripMetadata, "tagged.png");
        Assert.DoesNotContain("secret comment", System.Text.Encoding.Latin1.GetString(await System.IO.File.ReadAllBytesAsync(output.Paths[0], Token)));
        using var before = new MagickImage(samples.File("tagged.png"));
        using var after = new MagickImage(output.Paths[0]);
        Assert.Equal(before.Signature, after.Signature);
    }

    [Fact]
    public async Task Crop_uses_upright_coordinates()
    {
        var output = await RunAsync(new ImageTools(), ToolKind.Crop, "sideways.jpg", edit: new CropEdit(new PixelRect(100, 200, 640, 480)));
        using var image = new MagickImage(output.Paths[0]);
        Assert.Equal((640u, 480u), (image.Width, image.Height));
    }

    [Fact]
    public async Task Redact_paints_boxes_and_drops_metadata()
    {
        var edit = new RedactEdit(
            [new RedactBox(new PixelRect(10, 10, 200, 100), RedactStyle.Black),
             new RedactBox(new PixelRect(300, 300, 200, 150), RedactStyle.Pixelate),
             new RedactBox(new PixelRect(500, 50, 200, 150), RedactStyle.Blur)],
            null);
        var output = await RunAsync(new ImageTools(), ToolKind.Redact, "upright.jpg", edit: edit);
        using var image = new MagickImage(output.Paths[0]);
        Assert.Null(image.GetExifProfile());
        using var pixels = image.GetPixels();
        var color = pixels.GetPixel(100, 50).ToColor()!;
        Assert.True(color.R < 2000 && color.G < 2000 && color.B < 2000, $"Expected black, got {color}");
    }

    [Fact]
    public async Task Annotate_draws_the_overlay()
    {
        var overlay = Path.Combine(samples.Folder, "overlay.png");
        using (var layer = new MagickImage(MagickColors.Transparent, 800, 600))
        {
            new Drawables().FillColor(MagickColors.Lime).Rectangle(100, 100, 200, 200).Draw(layer);
            layer.Write(overlay, MagickFormat.Png);
        }

        var output = await RunAsync(new ImageTools(), ToolKind.Annotate, "upright.jpg", edit: new AnnotateEdit(overlay));
        using var image = new MagickImage(output.Paths[0]);
        using var pixels = image.GetPixels();
        var color = pixels.GetPixel(150, 150).ToColor()!;
        Assert.True(color.G > 60000 && color.R < 4000, $"Expected green, got {color}");
    }

    // ---- Recognition ----------------------------------------------------------------------------

    [Fact]
    public async Task Read_QR_code_returns_its_text()
    {
        var output = await RunAsync(new RecognitionTools(), ToolKind.ReadCode, "qr.png");
        Assert.Equal(ToolOutputKind.Text, output.Kind);
        Assert.Equal(ToolSamples.QrText, output.Text);
    }

    [Fact]
    public async Task A_picture_without_a_code_has_nothing_to_read()
    {
        await Assert.ThrowsAsync<NothingToDoException>(() => RunAsync(new RecognitionTools(), ToolKind.ReadCode, "blank.png"));
    }

    [Fact]
    public async Task Copy_text_reads_printed_text()
    {
        Assert.SkipWhen(
            global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages() is null,
            "No text-recognition language is installed in Windows.");
        var output = await RunAsync(new RecognitionTools(), ToolKind.CopyText, "text.png");
        Assert.Contains("POMEGRANATE", output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TEST", output.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- PDF ------------------------------------------------------------------------------------

    private static int PageCount(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public async Task Merge_joins_every_page()
    {
        var output = await RunAsync(new PdfTools(), ToolKind.Merge, "three.pdf", more: ["other.pdf"]);
        Assert.Equal(5, PageCount(output.Paths[0]));
    }

    [Fact]
    public async Task Split_writes_one_PDF_per_page()
    {
        var output = await RunAsync(new PdfTools(), ToolKind.Split, "three.pdf");
        Assert.Equal(ToolOutputKind.Pages, output.Kind);
        Assert.Equal(3, output.Paths.Count);
        Assert.All(output.Paths, p => Assert.Equal(1, PageCount(p)));

        await Assert.ThrowsAsync<NothingToDoException>(() => RunAsync(new PdfTools(), ToolKind.Split, "one.pdf"));
    }

    [Fact]
    public async Task Pages_reorders_rotates_and_drops()
    {
        var edit = new PagesEdit([new PageEdit(2, 90), new PageEdit(0, 0)], Selection: false);
        var output = await RunAsync(new PdfTools(), ToolKind.Pages, "three.pdf", edit: edit);
        using var document = PdfReader.Open(output.Paths[0], PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(90, document.Pages[0].Rotate);
        Assert.Equal(0, document.Pages[1].Rotate);

        using var pig = UglyToad.PdfPig.PdfDocument.Open(output.Paths[0]);
        Assert.Contains("page 3", pig.GetPage(1).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page 1", pig.GetPage(2).Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compress_shrinks_a_PDF_with_a_photo_and_keeps_its_text()
    {
        var output = await RunAsync(new PdfTools(), ToolKind.Compress, "three.pdf", ToolOption.Strong);
        Assert.True(new FileInfo(output.Paths[0]).Length < new FileInfo(samples.File("three.pdf")).Length);
        using var pig = UglyToad.PdfPig.PdfDocument.Open(output.Paths[0]);
        Assert.Equal(3, pig.NumberOfPages);
        Assert.Contains("page 1", pig.GetPage(1).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Single(pig.GetPage(1).GetImages());
    }

    [Fact]
    public async Task Strip_metadata_clears_the_PDF_information()
    {
        var output = await RunAsync(new PdfTools(), ToolKind.StripMetadata, "three.pdf");
        using var document = PdfReader.Open(output.Paths[0], PdfDocumentOpenMode.Import);
        Assert.True(string.IsNullOrEmpty(document.Info.Title));
        Assert.True(string.IsNullOrEmpty(document.Info.Author));
        Assert.Equal(3, document.PageCount);
    }

    // ---- Archives -------------------------------------------------------------------------------

    [Fact]
    public async Task Zip_packs_files_and_folders()
    {
        var output = await RunAsync(new ArchiveTools(), ToolKind.Zip, "tree", more: ["qr.png"]);
        using var zip = ZipFile.OpenRead(output.Paths[0]);
        var names = zip.Entries.Select(e => e.FullName).Order().ToArray();
        Assert.Equal(["qr.png", "tree/a.txt", "tree/inner/b.txt"], names);
    }

    [Fact]
    public async Task Extract_drops_a_single_top_folder()
    {
        var output = await RunAsync(new ArchiveTools(), ToolKind.Extract, "wrapped.zip");
        Assert.Equal(ToolOutputKind.Folder, output.Kind);
        var folder = output.Paths[0];
        Assert.Equal("alpha", await System.IO.File.ReadAllTextAsync(Path.Combine(folder, "a.txt"), Token));
        Assert.Equal("beta", await System.IO.File.ReadAllTextAsync(Path.Combine(folder, "inner", "b.txt"), Token));
    }

    [Fact]
    public async Task Extract_never_writes_outside_its_folder()
    {
        var output = await RunAsync(new ArchiveTools(), ToolKind.Extract, "evil.zip");
        var folder = output.Paths[0];

        // "../evil.txt" loses its "..": it stays inside the new folder instead of landing next to it.
        Assert.Equal("should not escape", await System.IO.File.ReadAllTextAsync(Path.Combine(folder, "evil.txt"), Token));
        Assert.False(System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "evil.txt")));
        Assert.Equal("fine", await System.IO.File.ReadAllTextAsync(Path.Combine(folder, "ok", "good.txt"), Token));
    }

    // ---- Video ----------------------------------------------------------------------------------

    [Fact]
    public async Task Video_compress_shrinks()
    {
        var output = await RunAsync(Media(), ToolKind.Compress, "video.mp4", ToolOption.Strong);
        Assert.True(new FileInfo(output.Paths[0]).Length < new FileInfo(samples.File("video.mp4")).Length);
        var info = await ProbeAsync(output.Paths[0]);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
    }

    [Fact]
    public async Task Video_trim_keeps_the_range()
    {
        var output = await RunAsync(Media(), ToolKind.Trim, "video.mp4", edit: new TrimEdit(new TimeRange(1, 3)));
        var info = await ProbeAsync(output.Paths[0]);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.8, 2.2);
    }

    [Fact]
    public async Task Video_crop_rotate_and_speed()
    {
        var crop = await RunAsync(Media(), ToolKind.Crop, "video.mp4", edit: new CropEdit(new PixelRect(21, 11, 161, 121)));
        var cropped = await ProbeAsync(crop.Paths[0]);
        Assert.Equal((160, 120), (cropped.Width, cropped.Height)); // even sizes

        var rotate = await RunAsync(Media(), ToolKind.Rotate, "video.mp4", ToolOption.RotateRight);
        var rotated = await ProbeAsync(rotate.Paths[0]);
        Assert.Equal((240, 320), (rotated.DisplayWidth, rotated.DisplayHeight));

        var speed = await RunAsync(Media(), ToolKind.Speed, "video.mp4", ToolOption.Speed2);
        var faster = await ProbeAsync(speed.Paths[0]);
        Assert.InRange(faster.Duration!.Value.TotalSeconds, 1.8, 2.3);
        Assert.True(faster.HasAudio);
    }

    [Fact]
    public async Task Mute_and_extract_audio_copy_streams()
    {
        var mute = await RunAsync(Media(), ToolKind.Mute, "video.mp4");
        var muted = await ProbeAsync(mute.Paths[0]);
        Assert.True(muted.HasVideo);
        Assert.False(muted.HasAudio);

        var extract = await RunAsync(Media(), ToolKind.ExtractAudio, "video.mp4");
        Assert.Equal(".m4a", Path.GetExtension(extract.Paths[0]));
        var audio = await ProbeAsync(extract.Paths[0]);
        Assert.False(audio.HasVideo);
        Assert.Equal("aac", audio.AudioCodec);
    }

    [Fact]
    public async Task Video_redact_paints_a_black_box()
    {
        var edit = new RedactEdit(
            [new RedactBox(new PixelRect(40, 40, 80, 60), RedactStyle.Black),
             new RedactBox(new PixelRect(180, 100, 80, 60), RedactStyle.Pixelate),
             new RedactBox(new PixelRect(180, 20, 60, 40), RedactStyle.Blur)],
            new TimeRange(0, 4));
        var output = await RunAsync(Media(), ToolKind.Redact, "video.mp4", edit: edit);
        var frame = Path.Combine(samples.Folder, "redacted-frame.png");
        await new MediaPreview(samples.Engines.FfmpegPath!, samples.Engines.FfprobePath!).FrameAsync(output.Paths[0], 1, 320, frame, Token);
        using var image = new MagickImage(frame);
        using var pixels = image.GetPixels();
        var color = pixels.GetPixel(80, 70).ToColor()!;
        Assert.True(color.R < 6000 && color.G < 6000 && color.B < 6000, $"Expected black, got {color}");
    }

    [Fact]
    public async Task Video_strip_metadata_drops_tags()
    {
        Assert.Contains("Secret title", await FormatTagsAsync(samples.File("video.mp4")));
        var output = await RunAsync(Media(), ToolKind.StripMetadata, "video.mp4");
        var tags = await FormatTagsAsync(output.Paths[0]);
        Assert.DoesNotContain("Secret title", tags);
        Assert.DoesNotContain("Someone", tags);
        var info = await ProbeAsync(output.Paths[0]);
        Assert.True(info.HasVideo && info.HasAudio);
    }

    private async Task<string> FormatTagsAsync(string path)
    {
        var result = await ProcessRunner.RunAsync(
            samples.Engines.FfprobePath!, ["-v", "error", "-show_entries", "format_tags:stream_tags", "-of", "json", path], Token, captureOutput: true);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }

    // ---- Audio ----------------------------------------------------------------------------------

    private async Task<float[]> PeaksAsync(string path, int buckets) =>
        await new MediaPreview(samples.Engines.FfmpegPath!, samples.Engines.FfprobePath!)
            .PeaksAsync(path, buckets, Path.Combine(samples.Folder, "peaks"), Token);

    [Fact]
    public async Task Audio_compress_trim_and_speed()
    {
        var compress = await RunAsync(Media(), ToolKind.Compress, "quiet.mp3", ToolOption.Medium);
        Assert.True(new FileInfo(compress.Paths[0]).Length < new FileInfo(samples.File("quiet.mp3")).Length);

        var trim = await RunAsync(Media(), ToolKind.Trim, "quiet.mp3", edit: new TrimEdit(new TimeRange(0.5, 2.5)));
        Assert.InRange((await ProbeAsync(trim.Paths[0])).Duration!.Value.TotalSeconds, 1.9, 2.2);

        var speed = await RunAsync(Media(), ToolKind.Speed, "quiet.mp3", ToolOption.Speed05);
        Assert.InRange((await ProbeAsync(speed.Paths[0])).Duration!.Value.TotalSeconds, 7.8, 8.3);
    }

    [Fact]
    public async Task Normalize_makes_a_quiet_file_louder()
    {
        var output = await RunAsync(Media(), ToolKind.Normalize, "quiet.mp3");
        var before = (await PeaksAsync(samples.File("quiet.mp3"), 8)).Max();
        var after = (await PeaksAsync(output.Paths[0], 8)).Max();
        Assert.True(after > before * 4, $"Peak went from {before} to {after}.");
    }

    [Fact]
    public async Task Bleep_silences_or_beeps_the_ranges()
    {
        var silence = await RunAsync(Media(), ToolKind.Bleep, "quiet.mp3", edit: new BleepEdit([new TimeRange(1, 2)], Silence: true));
        // FFmpeg's test tone is at 1/8 of full scale; at -30 dB its peaks are about 0.004.
        var quiet = await PeaksAsync(silence.Paths[0], 40);
        Assert.True(quiet[15] < 0.0008, $"Expected silence, got {quiet[15]}");
        Assert.True(quiet[30] > 0.002, $"Expected the tone, got {quiet[30]}");

        var beep = await RunAsync(Media(), ToolKind.Bleep, "quiet.mp3", edit: new BleepEdit([new TimeRange(1, 2)], Silence: false));
        var loud = await PeaksAsync(beep.Paths[0], 40);
        Assert.True(loud[15] > 0.15, $"Expected the bleep, got {loud[15]}");
        Assert.True(loud[30] < 0.01, $"Expected the quiet tone, got {loud[30]}");
    }

    [Fact]
    public async Task Audio_strip_metadata_drops_tags()
    {
        var output = await RunAsync(Media(), ToolKind.StripMetadata, "quiet.mp3");
        Assert.DoesNotContain("Secret title", await FormatTagsAsync(output.Paths[0]));
    }

    [Fact]
    public void Opus_bitrate_is_sixty_percent_in_steps_of_eight()
    {
        Assert.Equal(96, MediaTools.OpusKbps(160));
        Assert.Equal(80, MediaTools.OpusKbps(128));
        Assert.Equal(56, MediaTools.OpusKbps(96));
    }

    [Fact]
    public void Peaks_find_the_loudest_sample_per_slice()
    {
        var samples16 = new byte[8];
        BitConverter.TryWriteBytes(samples16.AsSpan(0), (short)16384);
        BitConverter.TryWriteBytes(samples16.AsSpan(6), (short)-32768);
        var peaks = MediaPreview.Peaks(samples16, 2);
        Assert.Equal([0.5f, 1f], peaks);
    }

    [Fact]
    public void Media_info_reads_rotation()
    {
        const string json = """
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,
              "side_data_list":[{"side_data_type":"Display Matrix","rotation":-90}]},
             {"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":2,"channel_layout":"stereo"}],
             "format":{"duration":"3.5","format_name":"mov,mp4"}}
            """;
        var info = MediaInfo.Parse(json);
        Assert.Equal(90, info.Rotation);
        Assert.Equal((1080, 1920), (info.DisplayWidth, info.DisplayHeight));
        Assert.Equal((48000, 2, "stereo"), (info.SampleRate, info.Channels, info.ChannelLayout));
    }
}
