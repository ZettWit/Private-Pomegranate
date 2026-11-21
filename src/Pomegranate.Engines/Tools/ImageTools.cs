using ImageMagick;
using ImageMagick.Drawing;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;

namespace Pomegranate.Engines.Tools;

/// <summary>Image tools through Magick.NET (spec §7.3): compress, resize, rotate, crop, redact, annotate, strip metadata.</summary>
public sealed class ImageTools : ITool
{
    public const string AlreadySmall = "Already as small as it gets.";

    public EngineKind Engine => EngineKind.Image;

    public Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(request, progress, cancellationToken), cancellationToken);

    private static ToolOutput Run(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var format = request.Format ?? throw new ConversionException("Unknown image type.");
        var output = request.WorkFile(request.SourceExtension);

        try
        {
            if (request.Kind == ToolKind.StripMetadata && MetadataStripper.TryStripLosslessly(request.Source, output, format))
            {
                progress.Report(1);
                return ToolOutput.File(output);
            }

            using var images = Load(request.Source);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(0.4);

            var original = images[0].Quality;
            var quality = request.Kind == ToolKind.Compress
                ? CompressQuality(format, request.Settings.ImageQuality.For(request.Option), original)
                : KeepQuality(format, request.Quality, original);

            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                image.AutoOrient();
                Apply(request, format, image);
                if (quality > 0)
                {
                    image.Quality = quality;
                }
            }

            if (request.Kind is ToolKind.StripMetadata or ToolKind.Redact)
            {
                foreach (var image in images)
                {
                    StripKeepingColorProfile(image);
                }
            }

            Write(images, output, format, request);
            progress.Report(1);
        }
        catch (MagickException ex)
        {
            throw new ConversionException(FirstLine(ex.Message), ex);
        }

        if (request.Kind == ToolKind.Compress && new FileInfo(output).Length >= new FileInfo(request.Source).Length)
        {
            throw new NothingToDoException(AlreadySmall);
        }

        return ToolOutput.File(output);
    }

    private static MagickImageCollection Load(string path)
    {
        var images = new MagickImageCollection(path);
        if (images.Count > 1)
        {
            // Animated WebP and the like: give every frame the full canvas, so crops and boxes line up.
            images.Coalesce();
        }

        return images;
    }

    private static void Apply(ToolRequest request, FileFormat format, IMagickImage<ushort> image)
    {
        switch (request.Kind)
        {
            case ToolKind.Compress:
                FitInside(image, request.Settings.ImageMaxSide.For(request.Option));
                if (format == KnownFormats.Png && request.Option != ToolOption.Light)
                {
                    image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                }

                break;

            case ToolKind.Resize:
                Resize(image, request.Option);
                break;

            case ToolKind.Rotate:
                if (request.Option == ToolOption.Flip)
                {
                    image.Flop();
                }
                else
                {
                    image.Rotate(ToolOptions.Degrees(request.Option));
                }

                break;

            case ToolKind.Crop when request.Edit is CropEdit crop:
                var area = Clamp(crop.Area, image);
                image.Crop(new MagickGeometry(area.X, area.Y, (uint)area.Width, (uint)area.Height));
                image.ResetPage();
                break;

            case ToolKind.Redact when request.Edit is RedactEdit redact:
                foreach (var box in redact.Boxes)
                {
                    Redact(image, Clamp(box.Area, image), box.Style);
                }

                break;

            case ToolKind.Annotate when request.Edit is AnnotateEdit annotate:
                using (var overlay = new MagickImage(annotate.OverlayPath))
                {
                    if (overlay.Width != image.Width || overlay.Height != image.Height)
                    {
                        overlay.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
                    }

                    image.Composite(overlay, CompositeOperator.Over);
                }

                break;

            case ToolKind.StripMetadata:
                break;

            default:
                throw new ConversionException($"{request.Kind} isn't an image tool, or its editor result is missing.");
        }
    }

    private static void Resize(IMagickImage<ushort> image, ToolOption option)
    {
        uint width, height;
        if (ToolOptions.LongSide(option) is { } side)
        {
            var longSide = Math.Max(image.Width, image.Height);
            if (longSide <= side)
            {
                throw new NothingToDoException($"Already {side} px or smaller.");
            }

            var scale = side / (double)longSide;
            width = (uint)Math.Max(1, Math.Round(image.Width * scale));
            height = (uint)Math.Max(1, Math.Round(image.Height * scale));
        }
        else
        {
            var scale = ToolOptions.Scale(option) ?? 0.5;
            width = (uint)Math.Max(1, Math.Round(image.Width * scale));
            height = (uint)Math.Max(1, Math.Round(image.Height * scale));
        }

        image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
    }

    private static void FitInside(IMagickImage<ushort> image, int maxSide)
    {
        if (maxSide <= 0 || Math.Max(image.Width, image.Height) <= maxSide)
        {
            return;
        }

        image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
    }

    /// <summary>
    /// Black fills the box. Pixelate and blur work on a copy of the box so nothing outside it changes; both are
    /// strong enough that text inside can't be read back.
    /// </summary>
    private static void Redact(IMagickImage<ushort> image, PixelRect area, RedactStyle style)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        if (style == RedactStyle.Black)
        {
            new Drawables()
                .FillColor(MagickColors.Black)
                .StrokeColor(MagickColors.Transparent)
                .Rectangle(area.X, area.Y, area.X + area.Width - 1, area.Y + area.Height - 1)
                .Draw(image);
            return;
        }

        var geometry = new MagickGeometry(area.X, area.Y, (uint)area.Width, (uint)area.Height);
        using var region = image.CloneArea(geometry);
        if (style == RedactStyle.Pixelate)
        {
            var block = Math.Max(8, Math.Min(area.Width, area.Height) / 6);
            var w = (uint)Math.Max(1, area.Width / block);
            var h = (uint)Math.Max(1, area.Height / block);
            region.FilterType = FilterType.Box;
            region.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
            region.Sample(new MagickGeometry((uint)area.Width, (uint)area.Height) { IgnoreAspectRatio = true });
        }
        else
        {
            // Shrink, blur and grow back: much stronger (and faster) than one huge blur.
            var small = new MagickGeometry((uint)Math.Max(1, area.Width / 12), (uint)Math.Max(1, area.Height / 12)) { IgnoreAspectRatio = true };
            region.Resize(small);
            region.Blur(0, 2);
            region.Resize(new MagickGeometry((uint)area.Width, (uint)area.Height) { IgnoreAspectRatio = true });
            region.Blur(0, Math.Max(2, Math.Min(area.Width, area.Height) / 20.0));
        }

        image.Composite(region, area.X, area.Y, CompositeOperator.Over);
    }

    private static PixelRect Clamp(PixelRect rect, IMagickImage<ushort> image)
    {
        var x = Math.Clamp(rect.X, 0, (int)image.Width - 1);
        var y = Math.Clamp(rect.Y, 0, (int)image.Height - 1);
        var w = Math.Clamp(rect.Width, 1, (int)image.Width - x);
        var h = Math.Clamp(rect.Height, 1, (int)image.Height - y);
        return new(x, y, w, h);
    }

    /// <summary>Removes EXIF (with its thumbnail), XMP, IPTC and comments; keeps the color profile.</summary>
    internal static void StripKeepingColorProfile(IMagickImage<ushort> image)
    {
        var icc = image.GetColorProfile();
        image.Strip();
        if (icc is not null)
        {
            image.SetProfile(icc);
        }
    }

    private static void Write(MagickImageCollection images, string output, FileFormat format, ToolRequest request)
    {
        var magickFormat = ImageConverter.MagickFormatFor(format);
        foreach (var image in images)
        {
            if (format == KnownFormats.Png)
            {
                // Magick would otherwise add creation and modification dates as text chunks.
                image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
            }
            else if (format == KnownFormats.Tiff && request.Kind == ToolKind.Compress)
            {
                image.Settings.Compression = request.Option == ToolOption.Light ? CompressionMethod.LZW : CompressionMethod.JPEG;
            }
        }

        if (images.Count > 1 && format != KnownFormats.Tiff)
        {
            images.Optimize();
        }

        images.Write(output, magickFormat);
    }

    /// <summary>For tools other than Compress: keep a JPEG's own quality; otherwise §11's.</summary>
    private static uint KeepQuality(FileFormat format, QualitySettings quality, uint original)
    {
        if (format == KnownFormats.Jpg)
        {
            return original is > 0 and <= 100 ? original : (uint)quality.JpegQuality;
        }

        return format.Id switch
        {
            "webp" => (uint)quality.WebPQuality,
            "avif" => (uint)quality.AvifQuality,
            "tiff" => (uint)quality.JpegQuality,
            _ => 0,
        };
    }

    /// <summary>The Compress level's quality, never above the JPEG's own (that would only grow the file).</summary>
    private static uint CompressQuality(FileFormat format, int level, uint original)
    {
        var q = (uint)Math.Clamp(level, 1, 100);
        if (format == KnownFormats.Jpg && original is > 0 and <= 100)
        {
            q = Math.Min(q, original);
        }

        return format == KnownFormats.Png ? 0 : q;
    }

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();
}
