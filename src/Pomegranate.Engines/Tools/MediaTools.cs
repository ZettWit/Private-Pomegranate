using System.Globalization;
using System.Text.Json;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Media;

namespace Pomegranate.Engines.Tools;

/// <summary>
/// Audio and video tools through FFmpeg (spec §7.3): compress, trim, crop, redact, speed, rotate, mute,
/// extract audio, bleep, normalize, strip metadata. The output keeps the source's container.
/// </summary>
public sealed class MediaTools(string ffmpegPath, string ffprobePath) : ITool
{
    private const double BleepVolume = 0.25;

    public EngineKind Engine => EngineKind.Media;

    public async Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var format = request.Format ?? throw new ConversionException("Unknown media type.");
        var info = await new MediaConverter(ffmpegPath, ffprobePath).ProbeAsync(request.Source, cancellationToken).ConfigureAwait(false);
        var isVideo = format.Category == FormatCategory.Video;
        if (isVideo && !info.HasVideo)
        {
            throw new ConversionException("This file has no video track.");
        }

        if (!isVideo && !info.HasAudio)
        {
            throw new ConversionException("This file has no audio track.");
        }

        if (request.Kind == ToolKind.Normalize)
        {
            return await NormalizeAsync(request, format, info, progress, cancellationToken).ConfigureAwait(false);
        }

        var output = request.WorkFile(request.SourceExtension);
        var duration = info.Duration;
        var args = Start();
        switch (request.Kind)
        {
            case ToolKind.Compress:
                args.AddRange(["-i", request.Source]);
                args.AddRange(isVideo ? CompressVideo(format, info, request) : CompressAudio(format, request));
                break;

            case ToolKind.Speed:
            {
                var factor = ToolOptions.SpeedFactor(request.Option);
                duration = duration / factor;
                args.AddRange(["-i", request.Source]);
                args.AddRange(isVideo ? SpeedVideo(format, info, factor, request.Quality) : SpeedAudio(format, factor, request.Quality));
                break;
            }

            case ToolKind.Rotate:
                args.AddRange(["-i", request.Source, "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn"]);
                args.AddRange(VideoEncode(format, request.Quality, [RotateFilter(request.Option)]));
                args.AddRange(["-c:a", "copy"]);
                break;

            case ToolKind.Mute:
                if (!info.HasAudio)
                {
                    throw new NothingToDoException("This video has no sound.");
                }

                args.AddRange(["-i", request.Source, "-map", "0", "-map", "-0:a", "-c", "copy"]);
                break;

            case ToolKind.ExtractAudio:
                return await ExtractAudioAsync(request, info, progress, cancellationToken).ConfigureAwait(false);

            case ToolKind.StripMetadata:
                // Real video (not cover art), sound and subtitles, copied as they are; every tag, chapter and
                // encoder name dropped. Rotation lives in the stream itself, so it stays.
                args.AddRange(["-i", request.Source, "-map", "0:V?", "-map", "0:a?", "-map", "0:s?", "-c", "copy",
                    "-map_metadata", "-1", "-map_chapters", "-1",
                    "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact"]);
                foreach (var (kind, count) in new[] { ("v", info.HasVideo ? 1 : 0), ("a", info.HasAudio ? 1 : 0) })
                {
                    if (count > 0)
                    {
                        args.AddRange([$"-map_metadata:s:{kind}", "-1"]);
                    }
                }

                break;

            case ToolKind.Trim when request.Edit is TrimEdit trim:
            {
                var keep = Clamp(trim.Keep, info);
                duration = TimeSpan.FromSeconds(keep.Length);
                args.AddRange(["-ss", Num(keep.Start), "-i", request.Source, "-t", Num(keep.Length)]);
                if (isVideo)
                {
                    args.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn"]);
                    args.AddRange(VideoEncode(format, request.Quality, []));
                    if (info.HasAudio)
                    {
                        args.AddRange(VideoAudioEncode(format, request.Quality, kbps: null));
                    }
                }
                else
                {
                    args.AddRange(["-map", "0:a:0", "-vn"]);
                    args.AddRange(AudioEncode(format, request.Quality));
                }

                break;
            }

            case ToolKind.Crop when request.Edit is CropEdit crop:
            {
                var area = Even(crop.Area, info);
                args.AddRange(["-i", request.Source, "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn"]);
                args.AddRange(VideoEncode(format, request.Quality, [$"crop={area.Width}:{area.Height}:{area.X}:{area.Y}"], evenSize: false));
                args.AddRange(["-c:a", "copy"]);
                break;
            }

            case ToolKind.Redact when request.Edit is RedactEdit redact:
                args.AddRange(["-i", request.Source]);
                args.AddRange(RedactVideo(format, info, redact, request.Quality));
                break;

            case ToolKind.Bleep when request.Edit is BleepEdit bleep:
                args.AddRange(["-i", request.Source]);
                args.AddRange(Bleep(format, info, bleep, request.Quality));
                break;

            default:
                throw new ConversionException($"{request.Kind} isn't a media tool, or its editor result is missing.");
        }

        if (format == KnownFormats.Mp4 || format == KnownFormats.Mov)
        {
            args.AddRange(["-movflags", "+faststart"]);
        }

        args.AddRange(["-f", FfmpegArguments.MuxerFor(format), output]);
        await RunFfmpegAsync(args, duration, progress, cancellationToken).ConfigureAwait(false);

        if (request.Kind == ToolKind.Compress && new FileInfo(output).Length >= new FileInfo(request.Source).Length)
        {
            throw new NothingToDoException(ImageTools.AlreadySmall);
        }

        return ToolOutput.File(output);
    }

    // ---- Tools ----------------------------------------------------------------------------------

    private static IEnumerable<string> CompressVideo(FileFormat format, MediaInfo info, ToolRequest request)
    {
        var s = request.Settings;
        var maxHeight = s.VideoMaxHeight.For(request.Option);
        var filters = new List<string>();
        if (maxHeight > 0 && info.DisplayHeight > maxHeight)
        {
            filters.Add($"scale=-2:{maxHeight}");
        }

        var args = new List<string> { "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn" };
        var crf = s.VideoCrf.For(request.Option);
        if (format == KnownFormats.WebM)
        {
            args.AddRange(["-c:v", "libvpx-vp9", "-crf", Num(crf + 6), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p"]);
            if (filters.Count > 0)
            {
                args.AddRange(["-vf", string.Join(",", filters)]);
            }
        }
        else
        {
            args.AddRange(X264(crf, request.Quality.H264Preset, filters, evenSize: filters.Count == 0));
        }

        if (info.HasAudio)
        {
            args.AddRange(VideoAudioEncode(format, request.Quality, s.VideoAudioKbps.For(request.Option)));
        }

        return args;
    }

    private static IEnumerable<string> CompressAudio(FileFormat format, ToolRequest request)
    {
        var s = request.Settings;
        var kbps = s.AudioKbps.For(request.Option);
        var args = new List<string> { "-map", "0:a:0" };

        // MP3 and M4A keep their cover art; it's copied as it is.
        if (format == KnownFormats.Mp3 || format == KnownFormats.M4a)
        {
            args.AddRange(["-map", "0:v?", "-c:v", "copy"]);
        }
        else
        {
            args.Add("-vn");
        }

        args.AddRange(format.Id switch
        {
            "mp3" => ["-c:a", "libmp3lame", "-b:a", Kbps(kbps)],
            "m4a" => ["-c:a", "aac", "-b:a", Kbps(kbps)],
            "wma" => ["-c:a", "wmav2", "-b:a", Kbps(kbps)],
            "opus" => ["-c:a", "libopus", "-b:a", Kbps(OpusKbps(kbps))],
            "ogg" => ["-c:a", "libvorbis", "-q:a", Num(s.VorbisQuality.For(request.Option))],
            _ => throw new ConversionException($"{format.Label} can't be compressed further."),
        });
        return args;
    }

    /// <summary>Opus is efficient: 60% of the MP3 bitrate, in steps of 8.</summary>
    internal static int OpusKbps(int mp3Kbps) => Math.Max(16, (int)Math.Round(mp3Kbps * 0.6 / 8) * 8);

    private static IEnumerable<string> SpeedVideo(FileFormat format, MediaInfo info, double factor, QualitySettings q)
    {
        var video = $"[0:v:0]setpts=PTS/{Num(factor)}{EvenFilter(format)}[v]";
        var graph = info.HasAudio ? $"{video};[0:a:0]atempo={Num(factor)}[a]" : video;
        var args = new List<string> { "-filter_complex", graph, "-map", "[v]" };
        if (info.HasAudio)
        {
            args.AddRange(["-map", "[a]"]);
        }

        args.AddRange(VideoEncode(format, q, [], evenSize: false));
        if (info.HasAudio)
        {
            args.AddRange(VideoAudioEncode(format, q, kbps: null));
        }

        return args;
    }

    /// <summary>
    /// The even-size scaling H.264 needs, for the end of a complex filter graph (FFmpeg won't take a separate
    /// -vf for a stream that comes out of one).
    /// </summary>
    private static string EvenFilter(FileFormat format) =>
        format == KnownFormats.WebM || format == KnownFormats.Wmv ? "" : ",scale=trunc(iw/2)*2:trunc(ih/2)*2";

    private static IEnumerable<string> SpeedAudio(FileFormat format, double factor, QualitySettings q) =>
        new[] { "-map", "0:a:0", "-vn", "-af", $"atempo={Num(factor)}" }.Concat(AudioEncode(format, q));

    private static string RotateFilter(ToolOption option) => option switch
    {
        ToolOption.RotateLeft => "transpose=2",
        ToolOption.Rotate180 => "hflip,vflip",
        ToolOption.Flip => "hflip",
        _ => "transpose=1",
    };

    /// <summary>
    /// Boxes over the picture, each black, pixelated or blurred, between the edit's start and end (or the
    /// whole clip). Numbers are worked out here, so the filter needs no escaping.
    /// </summary>
    private static IEnumerable<string> RedactVideo(FileFormat format, MediaInfo info, RedactEdit edit, QualitySettings q)
    {
        if (edit.Boxes.Count == 0)
        {
            throw new ConversionException("No boxes were drawn.");
        }

        var enable = edit.During is { } during ? $":enable='between(t,{Num(during.Start)},{Num(during.End)})'" : "";
        var parts = new List<string>();
        var current = "[0:v:0]";
        for (var i = 0; i < edit.Boxes.Count; i++)
        {
            var box = edit.Boxes[i];
            var a = Even(box.Area, info);
            var next = $"[r{i}]";
            if (box.Style == RedactStyle.Black)
            {
                parts.Add($"{current}drawbox=x={a.X}:y={a.Y}:w={a.Width}:h={a.Height}:color=black:t=fill{enable}{next}");
            }
            else
            {
                var region = box.Style == RedactStyle.Pixelate
                    ? $"scale={Math.Max(1, a.Width / Block(a))}:{Math.Max(1, a.Height / Block(a))}:flags=area,scale={a.Width}:{a.Height}:flags=neighbor"
                    : $"scale={Math.Max(2, a.Width / 12)}:{Math.Max(2, a.Height / 12)}:flags=area,gblur=sigma=1.5,scale={a.Width}:{a.Height}:flags=bicubic,gblur=sigma={Num(Math.Max(2, Math.Min(a.Width, a.Height) / 20.0))}";
                parts.Add($"{current}split[b{i}][c{i}]");
                parts.Add($"[c{i}]crop={a.Width}:{a.Height}:{a.X}:{a.Y},{region}[p{i}]");
                parts.Add($"[b{i}][p{i}]overlay={a.X}:{a.Y}{enable}{next}");
            }

            current = next;
        }

        var even = EvenFilter(format);
        if (even.Length > 0)
        {
            parts.Add($"{current}{even[1..]}[even]");
            current = "[even]";
        }

        var args = new List<string> { "-filter_complex", string.Join(";", parts), "-map", current, "-map", "0:a:0?", "-sn", "-dn" };
        args.AddRange(VideoEncode(format, q, [], evenSize: false));
        args.AddRange(["-c:a", "copy"]);
        return args;

        static int Block(PixelRect r) => Math.Max(8, Math.Min(r.Width, r.Height) / 6);
    }

    /// <summary>The ranges go quiet; for a bleep, a 1 kHz tone plays in them instead.</summary>
    private static IEnumerable<string> Bleep(FileFormat format, MediaInfo info, BleepEdit edit, QualitySettings q)
    {
        if (edit.Ranges.Count == 0)
        {
            throw new ConversionException("No ranges were marked.");
        }

        var inRanges = string.Join("+", edit.Ranges.Select(r => $"between(t,{Num(r.Start)},{Num(r.End)})"));
        var args = new List<string>();
        if (edit.Silence)
        {
            args.AddRange(["-map", "0:a:0", "-vn", "-af", $"volume=enable='{inRanges}':volume=0"]);
        }
        else
        {
            var rate = info.SampleRate > 0 ? info.SampleRate : 48000;
            var layout = string.IsNullOrEmpty(info.ChannelLayout) ? (info.Channels == 1 ? "mono" : "stereo") : info.ChannelLayout;
            var seconds = info.Duration?.TotalSeconds ?? edit.Ranges.Max(r => r.End);
            var graph =
                $"[0:a:0]volume=enable='{inRanges}':volume=0[m];" +
                $"aevalsrc=exprs='{Num(BleepVolume)}*sin(2*PI*1000*t)':s={rate}:d={Num(seconds)}," +
                $"volume=enable='not({inRanges})':volume=0,aformat=sample_rates={rate}:channel_layouts={layout}[b];" +
                "[m][b]amix=inputs=2:normalize=0:duration=first[out]";
            args.AddRange(["-filter_complex", graph, "-map", "[out]", "-vn"]);
        }

        args.AddRange(AudioEncode(format, q));
        return args;
    }

    private async Task<ToolOutput> ExtractAudioAsync(ToolRequest request, MediaInfo info, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!info.HasAudio)
        {
            throw new NothingToDoException("This video has no sound.");
        }

        var (extension, muxer, codec) = info.AudioCodec switch
        {
            "aac" or "alac" => ("m4a", "ipod", "copy"),
            "mp3" => ("mp3", "mp3", "copy"),
            "opus" => ("opus", "opus", "copy"),
            "vorbis" => ("ogg", "ogg", "copy"),
            "flac" => ("flac", "flac", "copy"),
            "pcm_s16le" or "pcm_s24le" or "pcm_f32le" => ("wav", "wav", "copy"),
            { } pcm when pcm.StartsWith("pcm_", StringComparison.Ordinal) => ("wav", "wav", "pcm_s16le"),
            "wmav1" or "wmav2" or "wmapro" => ("wma", "asf", "copy"),
            _ => ("mka", "matroska", "copy"),
        };

        var output = request.WorkFile(extension);
        var args = Start();
        args.AddRange(["-i", request.Source, "-map", "0:a:0", "-vn", "-sn", "-dn", "-c:a", codec, "-f", muxer, output]);
        await RunFfmpegAsync(args, info.Duration, progress, cancellationToken).ConfigureAwait(false);
        return ToolOutput.File(output);
    }

    /// <summary>
    /// Two-pass EBU R128 loudness normalization to −16 LUFS: the first pass measures, the second applies one
    /// steady gain (linear mode), so quiet and loud parts keep their balance.
    /// </summary>
    private async Task<ToolOutput> NormalizeAsync(
        ToolRequest request, FileFormat format, MediaInfo info, IProgress<double> progress, CancellationToken cancellationToken)
    {
        const string target = "I=-16:TP=-1.5:LRA=11";
        string[] measure =
        [
            "-hide_banner", "-nostdin", "-nostats", "-loglevel", "info", "-i", request.Source,
            "-map", "0:a:0", "-vn", "-af", $"loudnorm={target}:print_format=json", "-f", "null", "-",
        ];
        var first = await ProcessRunner.RunAsync(ffmpegPath, measure, cancellationToken).ConfigureAwait(false);
        if (first.ExitCode != 0)
        {
            throw new ConversionException(Describe(first));
        }

        var json = first.StandardErrorTail;
        var start = json.LastIndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new ConversionException("FFmpeg didn't report the loudness.");
        }

        using var doc = JsonDocument.Parse(json[start..(end + 1)]);
        var root = doc.RootElement;
        string Value(string name) => root.GetProperty(name).GetString() ?? "";
        var measuredI = Value("input_i");
        if (!double.TryParse(measuredI, NumberStyles.Float, CultureInfo.InvariantCulture, out var loudness) || double.IsInfinity(loudness))
        {
            throw new NothingToDoException("This file is silent.");
        }

        progress.Report(0.45);
        var filter = $"loudnorm={target}:measured_I={measuredI}:measured_TP={Value("input_tp")}:measured_LRA={Value("input_lra")}" +
                     $":measured_thresh={Value("input_thresh")}:offset={Value("target_offset")}:linear=true";
        var output = request.WorkFile(request.SourceExtension);
        var args = Start();
        args.AddRange(["-i", request.Source, "-map", "0:a:0", "-vn", "-af", filter]);

        // loudnorm works at 192 kHz; go back to the file's own rate.
        args.AddRange(["-ar", Num(info.SampleRate > 0 ? info.SampleRate : 48000)]);
        args.AddRange(AudioEncode(format, request.Quality));
        args.AddRange(["-f", FfmpegArguments.MuxerFor(format), output]);
        var second = new Progress<double>(p => progress.Report(0.45 + p * 0.55));
        await RunFfmpegAsync(args, info.Duration, second, cancellationToken).ConfigureAwait(false);
        return ToolOutput.File(output);
    }

    // ---- Encoding -------------------------------------------------------------------------------

    private static List<string> Start() =>
        ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-progress", "pipe:1", "-nostats"];

    /// <summary>Re-encodes the picture in the source's own format with §11's settings.</summary>
    private static IEnumerable<string> VideoEncode(FileFormat format, QualitySettings q, IReadOnlyList<string> filters, bool evenSize = true) =>
        format.Id switch
        {
            "webm" => new[] { "-c:v", "libvpx-vp9", "-crf", Num(q.Vp9Crf), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p" }
                .Concat(filters.Count > 0 ? ["-vf", string.Join(",", filters)] : []),
            "wmv" => new[] { "-c:v", "wmv2", "-q:v", "3" }.Concat(filters.Count > 0 ? ["-vf", string.Join(",", filters)] : []),
            _ => X264(q.H264Crf, q.H264Preset, filters, evenSize),
        };

    private static IEnumerable<string> X264(int crf, string preset, IReadOnlyList<string> filters, bool evenSize)
    {
        var chain = filters.ToList();
        if (evenSize)
        {
            chain.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        }

        var args = new List<string> { "-c:v", "libx264", "-crf", Num(crf), "-preset", preset, "-pix_fmt", "yuv420p" };
        if (chain.Count > 0)
        {
            args.AddRange(["-vf", string.Join(",", chain)]);
        }

        return args;
    }

    /// <summary>A video's soundtrack, re-encoded: AAC (Opus in WebM, MP3 in AVI, WMA in WMV).</summary>
    private static IEnumerable<string> VideoAudioEncode(FileFormat format, QualitySettings q, int? kbps) => format.Id switch
    {
        "webm" => ["-c:a", "libopus", "-b:a", Kbps(kbps is { } k ? OpusKbps(k) : q.OpusKbps)],
        "avi" => kbps is { } a ? ["-c:a", "libmp3lame", "-b:a", Kbps(a)] : ["-c:a", "libmp3lame", "-q:a", Num(q.Mp3VbrQuality)],
        "wmv" => ["-c:a", "wmav2", "-b:a", Kbps(kbps ?? q.WmaKbps)],
        _ => ["-c:a", "aac", "-b:a", Kbps(kbps ?? q.AacKbps)],
    };

    /// <summary>An audio file re-encoded in its own format with §11's settings.</summary>
    private static IEnumerable<string> AudioEncode(FileFormat format, QualitySettings q) => format.Id switch
    {
        "mp3" => ["-c:a", "libmp3lame", "-q:a", Num(q.Mp3VbrQuality)],
        "m4a" => ["-c:a", "aac", "-b:a", Kbps(q.AacKbps)],
        "wav" => ["-c:a", "pcm_s16le"],
        "flac" => ["-c:a", "flac"],
        "ogg" => ["-c:a", "libvorbis", "-q:a", Num(q.VorbisQuality)],
        "opus" => ["-c:a", "libopus", "-b:a", Kbps(q.OpusKbps)],
        "aiff" => ["-c:a", "pcm_s16be"],
        "wma" => ["-c:a", "wmav2", "-b:a", Kbps(q.WmaKbps)],
        _ => throw new ConversionException($"{format.Label} isn't an audio format."),
    };

    /// <summary>A rectangle inside the picture with even position and size (H.264 and most players need them).</summary>
    internal static PixelRect Even(PixelRect rect, MediaInfo info)
    {
        var width = info.DisplayWidth > 0 ? info.DisplayWidth : rect.X + rect.Width;
        var height = info.DisplayHeight > 0 ? info.DisplayHeight : rect.Y + rect.Height;
        var x = Math.Clamp(rect.X, 0, Math.Max(0, width - 2)) & ~1;
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 2)) & ~1;
        var w = Math.Clamp(rect.Width, 2, width - x) & ~1;
        var h = Math.Clamp(rect.Height, 2, height - y) & ~1;
        return new(x, y, Math.Max(2, w), Math.Max(2, h));
    }

    private static TimeRange Clamp(TimeRange range, MediaInfo info)
    {
        var total = info.Duration?.TotalSeconds ?? double.MaxValue;
        var start = Math.Clamp(range.Start, 0, total);
        var end = Math.Clamp(range.End, start, total);
        if (end - start < 0.05)
        {
            throw new ConversionException("The part to keep is too short.");
        }

        return new(start, end);
    }

    private async Task RunFfmpegAsync(List<string> args, TimeSpan? duration, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            ffmpegPath,
            args,
            cancellationToken,
            onOutputLine: line => MediaConverter.ReportProgress(line, duration, progress)).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConversionException(Describe(result));
        }

        progress.Report(1);
    }

    private static string Describe(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardErrorTail)
            ? $"FFmpeg stopped with exit code {result.ExitCode}."
            : result.StandardErrorTail.Trim();

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Kbps(int value) => Num(value) + "k";
}
