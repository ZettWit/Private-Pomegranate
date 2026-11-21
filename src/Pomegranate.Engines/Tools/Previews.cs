using System.Buffers.Binary;
using System.Globalization;
using ImageMagick;
using Pomegranate.Core.Jobs;
using Pomegranate.Engines.Media;

namespace Pomegranate.Engines.Tools;

/// <summary>An upright picture for an editor to show, and the full image's upright size.</summary>
public sealed record ImagePreviewResult(string PreviewPath, int Width, int Height);

/// <summary>Previews for the editor windows (spec §7.3, "Editors").</summary>
public static class ImagePreview
{
    /// <summary>
    /// Writes an upright PNG no larger than <paramref name="maxSide"/> (so the editor stays quick) and returns
    /// the original's upright size, which is what editor coordinates refer to.
    /// </summary>
    public static ImagePreviewResult Create(string path, int maxSide, string outputPng)
    {
        try
        {
            using var image = new MagickImage(path);
            image.AutoOrient();
            var width = (int)image.Width;
            var height = (int)image.Height;
            if (Math.Max(width, height) > maxSide)
            {
                image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
            }

            image.Write(outputPng, MagickFormat.Png32);
            return new(outputPng, width, height);
        }
        catch (MagickException ex)
        {
            throw new ConversionException(ex.Message.Split('\n', 2)[0].Trim(), ex);
        }
    }
}

/// <summary>Video frames, audio waveforms and playable audio for the editors, through FFmpeg.</summary>
public sealed class MediaPreview(string ffmpegPath, string ffprobePath)
{
    /// <summary>Samples per second of the audio decoded for waveforms.</summary>
    private const int PeakRate = 4000;

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken) =>
        new MediaConverter(ffmpegPath, ffprobePath).ProbeAsync(path, cancellationToken);

    /// <summary>The frame at <paramref name="seconds"/>, upright, as a PNG at most <paramref name="maxWidth"/> wide.</summary>
    public async Task FrameAsync(string path, double seconds, int maxWidth, string outputPng, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-ss", Num(Math.Max(0, seconds)), "-i", path,
            "-frames:v", "1", "-an", "-sn", "-dn",
            "-vf", $"scale='min({maxWidth},iw)':-2",
            "-f", "image2", "-c:v", "png", outputPng,
        ];
        var result = await ProcessRunner.RunAsync(ffmpegPath, args, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPng))
        {
            throw new ConversionException($"Couldn't read a frame: {result.StandardErrorTail.Trim()}");
        }
    }

    /// <summary>
    /// The loudest sample (0–1) in each of <paramref name="buckets"/> equal slices of the first audio track,
    /// for drawing a waveform.
    /// </summary>
    public async Task<float[]> PeaksAsync(string path, int buckets, string workFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workFolder);
        var raw = Path.Combine(workFolder, $"peaks-{Guid.NewGuid():N}.raw");
        try
        {
            string[] args =
            [
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", path,
                "-map", "0:a:0", "-ac", "1", "-ar", Num(PeakRate), "-f", "s16le", "-c:a", "pcm_s16le", raw,
            ];
            var result = await ProcessRunner.RunAsync(ffmpegPath, args, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(raw))
            {
                throw new ConversionException($"Couldn't read the sound: {result.StandardErrorTail.Trim()}");
            }

            var data = await File.ReadAllBytesAsync(raw, cancellationToken).ConfigureAwait(false);
            return Peaks(data, buckets);
        }
        finally
        {
            try
            {
                File.Delete(raw);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static float[] Peaks(ReadOnlySpan<byte> samples16, int buckets)
    {
        var count = samples16.Length / 2;
        var peaks = new float[Math.Max(1, buckets)];
        if (count == 0)
        {
            return peaks;
        }

        for (var b = 0; b < peaks.Length; b++)
        {
            var start = (int)((long)b * count / peaks.Length);
            var end = (int)Math.Min(count, (long)(b + 1) * count / peaks.Length);
            var max = 0;
            for (var i = start; i < end; i++)
            {
                var value = Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(samples16[(i * 2)..]));
                if (value > max)
                {
                    max = value;
                }
            }

            peaks[b] = Math.Min(1f, max / 32768f);
        }

        return peaks;
    }

    /// <summary>A WAV copy of the sound that Windows' media player can always play and seek in.</summary>
    public async Task PlayableWavAsync(string path, string outputWav, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", path,
            "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "22050", "-c:a", "pcm_s16le", "-f", "wav", outputWav,
        ];
        var result = await ProcessRunner.RunAsync(ffmpegPath, args, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConversionException($"Couldn't prepare the sound for playing: {result.StandardErrorTail.Trim()}");
        }
    }

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Page thumbnails for the Pages editor, through Windows' PDF renderer.</summary>
public static class PdfPreview
{
    public static Task<int> PageCountAsync(string path) => Pdf.PdfRenderer.PageCountAsync(path);

    /// <summary>Renders every page about <paramref name="width"/> pixels wide, reporting each as soon as it's ready.</summary>
    public static Task<IReadOnlyList<string>> ThumbnailsAsync(
        string path, string folder, int width, Action<int, string> onPage, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        return Pdf.PdfRenderer.RenderAsync(path, folder, dpi: 96, progress: null, cancellationToken, maxWidth: width, onPage: onPage);
    }
}
