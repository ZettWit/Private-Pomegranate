using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Tools;

namespace Pomegranate.Engines.Tools;

/// <summary>PDF tools through PDFsharp (spec §7.3): merge, split, pages, compress, strip metadata.</summary>
public sealed class PdfTools : ITool
{
    public const string PasswordProtected = "This file is password-protected.";

    public EngineKind Engine => EngineKind.PdfTools;

    public Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(request, progress, cancellationToken), cancellationToken);

    private static ToolOutput Run(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            return request.Kind switch
            {
                ToolKind.Merge => Merge(request, progress, cancellationToken),
                ToolKind.Split => Split(request, progress, cancellationToken),
                ToolKind.Pages => Pages(request),
                ToolKind.Compress => Compress(request, progress, cancellationToken),
                ToolKind.StripMetadata => StripMetadata(request),
                _ => throw new ConversionException($"{request.Kind} isn't a PDF tool."),
            };
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException($"Couldn't read the PDF: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConversionException(PasswordProtected, ex);
        }
    }

    /// <summary>Opens a PDF; fails with a clear message instead of asking for a password (spec §7.2).</summary>
    internal static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        var asked = false;
        try
        {
            return PdfReader.Open(path, mode, args =>
            {
                asked = true;
                args.Abort = true;
            });
        }
        catch (Exception ex) when (asked || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConversionException(PasswordProtected, ex);
        }
    }

    private static ToolOutput Merge(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var output = new PdfDocument();
        for (var i = 0; i < request.Sources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = Open(request.Sources[i], PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages)
            {
                output.AddPage(page);
            }

            progress.Report((i + 1.0) / (request.Sources.Count + 1));
        }

        var path = request.WorkFile("pdf");
        output.Save(path);
        return ToolOutput.File(path);
    }

    private static ToolOutput Split(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var source = Open(request.Source, PdfDocumentOpenMode.Import);
        var count = source.PageCount;
        if (count < 2)
        {
            throw new NothingToDoException("This PDF has only one page.");
        }

        var files = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var single = new PdfDocument();
            single.AddPage(source.Pages[i]);
            var path = request.WorkFile("pdf", $"page-{i + 1:D5}");
            single.Save(path);
            files.Add(path);
            progress.Report((i + 1.0) / count);
        }

        return ToolOutput.Pages(files);
    }

    private static ToolOutput Pages(ToolRequest request)
    {
        if (request.Edit is not PagesEdit edit || edit.Pages.Count == 0)
        {
            throw new ConversionException("No pages were chosen.");
        }

        using var source = Open(request.Source, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        foreach (var page in edit.Pages)
        {
            if (page.SourceIndex < 0 || page.SourceIndex >= source.PageCount)
            {
                throw new ConversionException($"Page {page.SourceIndex + 1} doesn't exist.");
            }

            var added = output.AddPage(source.Pages[page.SourceIndex]);
            added.Rotate = Normalize(added.Rotate + page.ExtraRotation);
        }

        var path = request.WorkFile("pdf");
        output.Save(path);
        return ToolOutput.File(path);
    }

    private static int Normalize(int degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>
    /// Removes the document information (title, author, …) and XMP metadata, plus application data
    /// (PieceInfo) on the document and its pages.
    /// </summary>
    private static ToolOutput StripMetadata(ToolRequest request)
    {
        using var document = Open(request.Source, PdfDocumentOpenMode.Modify);
        document.Info.Elements.Clear();
        document.Internals.Catalog.Elements.Remove("/Metadata");
        document.Internals.Catalog.Elements.Remove("/PieceInfo");
        foreach (var page in document.Pages)
        {
            page.Elements.Remove("/Metadata");
            page.Elements.Remove("/PieceInfo");
        }

        var path = request.WorkFile("pdf");
        document.Save(path);
        return ToolOutput.File(path);
    }

    /// <summary>
    /// Re-encodes the PDF's pictures as JPEG, scaled down so none is sharper than the level's DPI on its page
    /// (spec §7.3). Text, drawings and pictures it can't safely re-encode (masks, CMYK, odd color spaces) stay
    /// as they are.
    /// </summary>
    private static ToolOutput Compress(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var dpi = request.Settings.PdfDpi.For(request.Option);
        var quality = (uint)Math.Clamp(request.Settings.PdfJpegQuality.For(request.Option), 1, 100);

        using var document = Open(request.Source, PdfDocumentOpenMode.Modify);
        document.Options.CompressContentStreams = true;
        document.Options.NoCompression = false;
        document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;

        // The largest size each picture needs: its page's size at the chosen DPI.
        var limits = new Dictionary<PdfDictionary, (double W, double H)>(ReferenceEqualityComparer.Instance);
        foreach (var page in document.Pages)
        {
            var w = page.Width.Point / 72 * dpi;
            var h = page.Height.Point / 72 * dpi;
            if (page.Rotate % 180 != 0)
            {
                (w, h) = (h, w);
            }

            foreach (var image in ImagesOn(page.Resources.Elements, depth: 0))
            {
                var (lw, lh) = limits.GetValueOrDefault(image);
                limits[image] = (Math.Max(lw, Math.Max(w, h)), Math.Max(lh, Math.Max(w, h)));
            }
        }

        var done = 0;
        foreach (var (image, limit) in limits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recompress(image, limit, quality);
            progress.Report(++done / (double)(limits.Count + 1));
        }

        var path = request.WorkFile("pdf");
        document.Save(path);
        if (new FileInfo(path).Length >= new FileInfo(request.Source).Length)
        {
            throw new NothingToDoException(ImageTools.AlreadySmall);
        }

        return ToolOutput.File(path);
    }

    /// <summary>The image XObjects in a resource dictionary, including those inside forms (a few levels deep).</summary>
    private static IEnumerable<PdfDictionary> ImagesOn(PdfDictionary.DictionaryElements resources, int depth)
    {
        if (depth > 4 || resources.GetDictionary("/XObject") is not { } xobjects)
        {
            yield break;
        }

        foreach (var key in xobjects.Elements.Keys)
        {
            if (xobjects.Elements.GetDictionary(key) is not { } xobject)
            {
                continue;
            }

            var subtype = xobject.Elements.GetName("/Subtype");
            if (subtype == "/Image")
            {
                yield return xobject;
            }
            else if (subtype == "/Form" && xobject.Elements.GetDictionary("/Resources") is { } nested)
            {
                foreach (var inner in ImagesOn(nested.Elements, depth + 1))
                {
                    yield return inner;
                }
            }
        }
    }

    private static void Recompress(PdfDictionary image, (double W, double H) limit, uint quality)
    {
        var e = image.Elements;
        if (image.Stream is null || e.GetBoolean("/ImageMask") || e.ContainsKey("/Decode") || e.GetInteger("/BitsPerComponent") != 8)
        {
            return;
        }

        var components = Components(e);
        if (components is not (1 or 3))
        {
            return;
        }

        var width = e.GetInteger("/Width");
        var height = e.GetInteger("/Height");
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var filter = FilterOf(e);
        var parameters = e["/DecodeParms"];
        var original = image.Stream.Value;
        MagickImage? decoded = null;
        try
        {
            if (filter == "/DCTDecode")
            {
                decoded = new MagickImage(original);
            }
            else if (filter is "/FlateDecode" or null)
            {
                if (filter is not null && !image.Stream.TryUncompress())
                {
                    RestoreOriginal(image, original, filter, parameters);
                    return;
                }

                var raw = image.Stream.Value;
                if (raw.Length < width * height * components)
                {
                    RestoreOriginal(image, original, filter, parameters);
                    return;
                }

                var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, components == 3 ? "RGB" : "R");
                decoded = new MagickImage();
                decoded.ReadPixels(raw, settings);
                if (components == 1)
                {
                    decoded.ColorType = ColorType.Grayscale;
                }
            }
            else
            {
                return; // JPX, JBIG2, CCITT, …: leave alone
            }

            var scale = Math.Min(1, Math.Min(limit.W / width, limit.H / height));
            if (scale < 0.98)
            {
                decoded.Resize(new MagickGeometry(
                    (uint)Math.Max(1, Math.Round(width * scale)),
                    (uint)Math.Max(1, Math.Round(height * scale))) { IgnoreAspectRatio = true });
            }

            decoded.Strip();
            decoded.Quality = quality;
            if (components == 1)
            {
                decoded.ColorSpace = ColorSpace.Gray;
            }

            var jpeg = decoded.ToByteArray(MagickFormat.Jpeg);
            if (jpeg.Length >= original.Length)
            {
                // Keep what was there (re-filtered if we unpacked it).
                RestoreOriginal(image, original, filter, parameters);
                return;
            }

            image.Stream.Value = jpeg;
            e.SetName("/Filter", "/DCTDecode");
            e.Remove("/DecodeParms");
            e.SetInteger("/Width", (int)decoded.Width);
            e.SetInteger("/Height", (int)decoded.Height);
            e.SetInteger("/BitsPerComponent", 8);
            if (Resolve(e["/ColorSpace"]) is PdfName)
            {
                e.SetName("/ColorSpace", components == 3 ? "/DeviceRGB" : "/DeviceGray");
            }
        }
        catch (MagickException)
        {
            RestoreOriginal(image, original, filter, parameters);
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    /// <summary>Puts back the stream as it was, including the filter and its parameters that unfiltering removed.</summary>
    private static void RestoreOriginal(PdfDictionary image, byte[] original, string? filter, PdfItem? parameters)
    {
        image.Stream.Value = original;
        if (filter is not null)
        {
            image.Elements.SetName("/Filter", filter);
        }

        if (parameters is not null)
        {
            image.Elements["/DecodeParms"] = parameters;
        }
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

    private static string? FilterOf(PdfDictionary.DictionaryElements e) => Resolve(e["/Filter"]) switch
    {
        PdfName name => name.Value,
        PdfArray { Elements.Count: 1 } array when array.Elements[0] is PdfName only => only.Value,
        PdfArray => "/Unsupported",
        _ => null,
    };

    /// <summary>Color components per pixel for the color spaces this tool handles; 0 for the rest.</summary>
    private static int Components(PdfDictionary.DictionaryElements e)
    {
        switch (Resolve(e["/ColorSpace"]))
        {
            case PdfName { Value: "/DeviceRGB" }:
                return 3;
            case PdfName { Value: "/DeviceGray" }:
                return 1;
            case PdfArray { Elements.Count: 2 } array when array.Elements[0] is PdfName { Value: "/ICCBased" }:
                var profile = Resolve(array.Elements[1]) as PdfDictionary;
                var n = profile?.Elements.GetInteger("/N") ?? 0;
                return n is 1 or 3 ? n : 0;
            default:
                return 0;
        }
    }
}
