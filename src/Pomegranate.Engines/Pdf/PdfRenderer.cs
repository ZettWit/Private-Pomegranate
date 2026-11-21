using System.Runtime.InteropServices;
using Pomegranate.Core.Jobs;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Pomegranate.Engines.Pdf;

/// <summary>Windows' built-in PDF renderer (Windows.Data.Pdf): PDF pages to PNG files.</summary>
internal static class PdfRenderer
{
    private const int WrongPassword = unchecked((int)0x8007052B);

    /// <summary>Renders every page to <c>page-N.png</c> in <paramref name="folder"/> at <paramref name="dpi"/>.</summary>
    /// <param name="maxWidth">Optional cap on the pixel width (for slides).</param>
    /// <param name="onPage">Called with each page's 0-based index and file as soon as it's written.</param>
    public static async Task<IReadOnlyList<string>> RenderAsync(
        string pdfPath,
        string folder,
        int dpi,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        int? maxWidth = null,
        Action<int, string>? onPage = null)
    {
        var document = await LoadAsync(pdfPath).ConfigureAwait(false);
        var files = new List<string>();
        var scale = Math.Clamp(dpi, 36, 1200) / 96.0;

        for (uint i = 0; i < document.PageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);
            var width = page.Size.Width * scale;
            var height = page.Size.Height * scale;
            if (maxWidth is { } cap && width > cap)
            {
                height *= cap / width;
                width = cap;
            }

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(width)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(height)),
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };

            var file = Path.Combine(folder, $"page-{i + 1}.png");
            using (var stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken).ConfigureAwait(false);
                stream.Seek(0);
                await using var output = File.Create(file);
                await stream.AsStreamForRead().CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            files.Add(file);
            onPage?.Invoke((int)i, file);
            progress?.Report((i + 1.0) / document.PageCount);
        }

        if (files.Count == 0)
        {
            throw new ConversionException("This PDF has no pages.");
        }

        return files;
    }

    /// <summary>How many pages the PDF has, without rendering any.</summary>
    public static async Task<int> PageCountAsync(string pdfPath) => (int)(await LoadAsync(pdfPath).ConfigureAwait(false)).PageCount;

    private static async Task<PdfDocument> LoadAsync(string pdfPath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            return await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex) when (ex.HResult == WrongPassword)
        {
            throw new ConversionException("This file is password-protected.", ex);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            throw new ConversionException($"Windows couldn't open this PDF: {ex.Message.Split('\n')[0].Trim()}", ex);
        }
    }
}
