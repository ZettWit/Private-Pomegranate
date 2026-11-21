using System.Buffers.Binary;
using ImageMagick;
using Pomegranate.Core.Formats;

namespace Pomegranate.Engines.Tools;

/// <summary>
/// Strips metadata from JPEG and PNG without re-encoding (spec §7.3). Used only when the image needs no
/// rotation; otherwise Magick re-encodes it upright.
/// </summary>
internal static class MetadataStripper
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // Everything a PNG needs to look the same. Text, time, EXIF and unknown private chunks are dropped.
    private static readonly HashSet<string> PngKeep =
    [
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "bKGD", "pHYs", "hIST",
        "cICP", "mDCV", "cLLI", "acTL", "fcTL", "fdAT",
    ];

    /// <summary>Returns false when the file needs re-encoding instead (it's rotated, or not a plain JPEG/PNG).</summary>
    public static bool TryStripLosslessly(string source, string output, FileFormat format)
    {
        if (format != KnownFormats.Jpg && format != KnownFormats.Png)
        {
            return false;
        }

        if (NeedsRotation(source))
        {
            return false;
        }

        var data = File.ReadAllBytes(source);
        var stripped = format == KnownFormats.Jpg ? StripJpeg(data) : StripPng(data);
        if (stripped is null)
        {
            return false;
        }

        File.WriteAllBytes(output, stripped);
        return true;
    }

    private static bool NeedsRotation(string path)
    {
        using var image = new MagickImage();
        image.Ping(path);
        return image.Orientation is not (Orientation.Undefined or Orientation.TopLeft);
    }

    /// <summary>
    /// Keeps the image data, quantization and Huffman tables, JFIF, the ICC profile and Adobe's color marker.
    /// Drops EXIF and XMP (APP1), Photoshop/IPTC (APP13), multi-picture data, comments and anything after the
    /// image ends (phones append depth maps and previews there). Null if the file isn't a JPEG we understand.
    /// </summary>
    internal static byte[]? StripJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        using var output = new MemoryStream(data.Length);
        output.Write([0xFF, 0xD8]);
        var i = 2;
        while (i + 2 <= data.Length)
        {
            if (data[i] != 0xFF)
            {
                return null;
            }

            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            if (marker == 0xD9)
            {
                output.Write([0xFF, 0xD9]);
                return output.ToArray();
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                output.Write(data.Slice(i, 2));
                i += 2;
                continue;
            }

            if (i + 4 > data.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
            var end = i + 2 + length;
            if (length < 2 || end > data.Length)
            {
                return null;
            }

            var segment = data[i..end];
            if (Keep(marker, segment))
            {
                output.Write(segment);
            }

            i = end;
            if (marker == 0xDA)
            {
                // Entropy-coded data follows until the next real marker.
                var start = i;
                while (i + 1 < data.Length && !(data[i] == 0xFF && data[i + 1] != 0x00 && data[i + 1] is not (>= 0xD0 and <= 0xD7)))
                {
                    i++;
                }

                output.Write(data[start..i]);
            }
        }

        return null; // no end-of-image marker
    }

    private static bool Keep(byte marker, ReadOnlySpan<byte> segment)
    {
        var payload = segment[4..];
        return marker switch
        {
            0xE0 => payload.StartsWith("JFIF\0"u8) || payload.StartsWith("JFXX\0"u8),
            0xE2 => payload.StartsWith("ICC_PROFILE\0"u8),
            0xEE => payload.StartsWith("Adobe"u8), // tells decoders how the colors were transformed
            >= 0xE1 and <= 0xEF => false,           // EXIF, XMP, MPF, IPTC, other app data
            0xFE => false,                          // comment
            _ => true,                              // tables, frame and scan headers
        };
    }

    /// <summary>Keeps the chunks that affect how the PNG looks; null if the file isn't a PNG.</summary>
    internal static byte[]? StripPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngSignature.Length || !data[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return null;
        }

        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);
        var i = PngSignature.Length;
        while (i + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
            if (length > int.MaxValue - 12 || i + 12 + (int)length > data.Length)
            {
                return null;
            }

            var end = i + 12 + (int)length;
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(i + 4, 4));
            if (PngKeep.Contains(type))
            {
                output.Write(data[i..end]);
            }

            i = end;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        return null;
    }
}
