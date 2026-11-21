using System.Runtime.InteropServices.WindowsRuntime;
using ImageMagick;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Tools;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;

namespace Pomegranate.Engines.Tools;

/// <summary>
/// Read QR code (ZXing.Net) and Copy text (Windows' built-in text recognition), spec §7.3. Both return text
/// for the clipboard; nothing is ever opened.
/// </summary>
public sealed class RecognitionTools : ITool
{
    public const string NoLanguage =
        "Windows has no text-recognition language installed. Add one in Settings → Time & language → Language & region.";

    /// <summary>Codes are found more reliably in a picture of moderate size.</summary>
    private const int MaxCodeSide = 2400;

    public EngineKind Engine => EngineKind.Recognition;

    public Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        request.Kind switch
        {
            ToolKind.ReadCode => Task.Run(() => ReadCodes(request.Source, cancellationToken), cancellationToken),
            ToolKind.CopyText => ReadTextAsync(request.Source, cancellationToken),
            _ => throw new ConversionException($"{request.Kind} isn't a recognition tool."),
        };

    private static ToolOutput ReadCodes(string path, CancellationToken cancellationToken)
    {
        var texts = new List<string>();
        try
        {
            using var image = Load(path, MaxCodeSide, upscaleSmall: false);
            cancellationToken.ThrowIfCancellationRequested();
            using var pixels = image.GetPixels();
            var rgb = pixels.ToByteArray(PixelMapping.RGB) ?? throw new ConversionException("Couldn't read the picture.");
            var source = new RGBLuminanceSource(rgb, (int)image.Width, (int)image.Height, RGBLuminanceSource.BitmapFormat.RGB24);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, TryInverted = true },
            };

            var results = reader.DecodeMultiple(source) ?? [];
            texts.AddRange(results.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct());
        }
        catch (MagickException ex)
        {
            throw new ConversionException(ex.Message.Split('\n', 2)[0].Trim(), ex);
        }

        if (texts.Count == 0)
        {
            throw new NothingToDoException("No QR code or barcode found.");
        }

        return ToolOutput.FromText(string.Join(Environment.NewLine, texts));
    }

    private static async Task<ToolOutput> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new ConversionException(NoLanguage);
        SoftwareBitmap bitmap;
        try
        {
            using var image = Load(path, (int)OcrEngine.MaxImageDimension, upscaleSmall: true);
            image.Alpha(AlphaAction.Opaque);
            using var pixels = image.GetPixels();
            var bgra = pixels.ToByteArray(PixelMapping.BGRA) ?? throw new ConversionException("Couldn't read the picture.");
            bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, (int)image.Width, (int)image.Height);
        }
        catch (MagickException ex)
        {
            throw new ConversionException(ex.Message.Split('\n', 2)[0].Trim(), ex);
        }

        using (bitmap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
            if (text.Length == 0)
            {
                throw new NothingToDoException("No text found.");
            }

            return ToolOutput.FromText(text);
        }
    }

    /// <summary>The picture upright, on white (transparent codes would read as black), within a size range.</summary>
    private static MagickImage Load(string path, int maxSide, bool upscaleSmall)
    {
        var image = new MagickImage(path);
        try
        {
            image.AutoOrient();
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            var longSide = Math.Max(image.Width, image.Height);
            if (longSide > maxSide)
            {
                image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
            }
            else if (upscaleSmall && longSide < 1500)
            {
                // Small screenshots: text recognition works better on larger letters.
                var scale = Math.Min(2.0, maxSide / (double)longSide);
                image.Resize(new MagickGeometry((uint)(image.Width * scale), (uint)(image.Height * scale)));
            }

            image.Depth = 8;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
