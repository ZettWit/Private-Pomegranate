using ImageMagick;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;

namespace Pomegranate.Engines;

/// <summary>Image conversions through Magick.NET (in-process).</summary>
public sealed class ImageConverter : IConverter
{
    public EngineKind Engine => EngineKind.Image;

    public Task ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(() => Convert(request, progress, cancellationToken), cancellationToken);

    private static void Convert(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var target = request.Rule.To;
        var format = MagickFormatFor(target);

        try
        {
            // PDF and TIFF can hold several pages, so keep every frame. Other targets take the first one.
            if (target == KnownFormats.Pdf || target == KnownFormats.Tiff)
            {
                using var images = new MagickImageCollection(request.SourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(0.5);
                foreach (var image in images)
                {
                    Prepare(image, target, request.Quality);
                }

                images.Write(request.OutputPath, format);
            }
            else
            {
                using var image = new MagickImage(request.SourcePath);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(0.5);
                Prepare(image, target, request.Quality);
                image.Write(request.OutputPath, format);
            }
        }
        catch (MagickException ex)
        {
            throw new ConversionException(FirstLine(ex.Message), ex);
        }

        progress.Report(1);
    }

    private static void Prepare(IMagickImage<ushort> image, FileFormat target, QualitySettings quality)
    {
        // Photos from phones store rotation in EXIF; bake it in so every viewer shows it upright.
        image.AutoOrient();

        // JPEG and BMP have no transparency: put transparent areas on white instead of black.
        if ((target == KnownFormats.Jpg || target == KnownFormats.Bmp) && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }

        if (target == KnownFormats.Jpg)
        {
            image.Quality = (uint)Math.Clamp(quality.JpegQuality, 1, 100);
        }
        else if (target == KnownFormats.WebP)
        {
            image.Quality = (uint)Math.Clamp(quality.WebPQuality, 1, 100);
        }
        else if (target == KnownFormats.Avif)
        {
            image.Quality = (uint)Math.Clamp(quality.AvifQuality, 1, 100);
        }
        else if (target == KnownFormats.Heic)
        {
            image.Quality = (uint)Math.Clamp(quality.HeicQuality, 1, 100);
        }
    }

    internal static MagickFormat MagickFormatFor(FileFormat format) => format.Id switch
    {
        "jpg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "heic" => MagickFormat.Heic,
        "avif" => MagickFormat.Avif,
        "tiff" => MagickFormat.Tiff,
        "bmp" => MagickFormat.Bmp,
        "pdf" => MagickFormat.Pdf,
        _ => throw new ConversionException($"{format.Label} isn't an image format Pomegranate can write."),
    };

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();
}
