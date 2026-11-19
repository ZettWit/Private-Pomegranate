using System.Globalization;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

namespace Pomegranate.Engines.Media;

/// <summary>
/// Builds FFmpeg command lines (spec §7, §11). Streams whose codec the target container already
/// supports are copied without re-encoding, which makes container changes near-instant.
/// </summary>
public static class FfmpegArguments
{
    private static readonly HashSet<string> Mp4Video = ["h264", "hevc", "av1", "mpeg4"];
    private static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "ac3", "eac3", "alac"];
    private static readonly HashSet<string> MkvVideo = ["h264", "hevc", "av1", "vp8", "vp9", "mpeg4", "mpeg2video"];
    private static readonly HashSet<string> MkvAudio = ["aac", "mp3", "opus", "vorbis", "flac", "ac3", "eac3", "alac", "pcm_s16le", "pcm_s24le"];
    private static readonly HashSet<string> WebmVideo = ["vp8", "vp9", "av1"];
    private static readonly HashSet<string> WebmAudio = ["opus", "vorbis"];

    public static IReadOnlyList<string> Build(
        string input, string output, FileFormat target, MediaInfo source, QualitySettings q)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-loglevel", "error",
            "-progress", "pipe:1", "-nostats",
            "-i", input,
        };

        if (target.Category == FormatCategory.Audio)
        {
            if (!source.HasAudio)
            {
                throw new Pomegranate.Core.Jobs.ConversionException("This file has no audio track.");
            }

            args.AddRange(["-map", "0:a:0", "-vn", "-sn", "-dn"]);
            args.AddRange(AudioCodec(target, source.AudioCodec, q));
        }
        else if (target == KnownFormats.Gif)
        {
            RequireVideo(source);
            // Labelled input and output pads: the filter reads the first video stream and its result is what gets mapped.
            var filter = string.Create(CultureInfo.InvariantCulture,
                $"[0:v:0]fps={q.GifFps},scale='min({q.GifMaxWidth},iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5[gif]");
            args.AddRange(["-filter_complex", filter, "-map", "[gif]", "-an", "-sn", "-loop", "0"]);
        }
        else
        {
            RequireVideo(source);
            args.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn"]);
            args.AddRange(VideoCodec(target, source.VideoCodec, q));
            if (source.HasAudio)
            {
                args.AddRange(VideoAudioCodec(target, source.AudioCodec, q));
            }

            if (target == KnownFormats.Mp4 || target == KnownFormats.Mov)
            {
                args.AddRange(["-movflags", "+faststart"]);
            }
        }

        args.AddRange(["-f", MuxerFor(target), output]);
        return args;
    }

    /// <summary>FFmpeg's muxer name for a target, so the temporary file's name doesn't matter.</summary>
    public static string MuxerFor(FileFormat target) => target.Id switch
    {
        "mp4" => "mp4",
        "mov" => "mov",
        "mkv" => "matroska",
        "webm" => "webm",
        "avi" => "avi",
        "wmv" => "asf",
        "gif" => "gif",
        "mp3" => "mp3",
        "m4a" => "ipod",
        "wav" => "wav",
        "flac" => "flac",
        "ogg" => "ogg",
        "opus" => "opus",
        "aiff" => "aiff",
        "wma" => "asf",
        _ => throw new ArgumentException($"{target.Label} isn't an FFmpeg target.", nameof(target)),
    };

    private static void RequireVideo(MediaInfo source)
    {
        if (!source.HasVideo)
        {
            throw new Pomegranate.Core.Jobs.ConversionException("This file has no video track.");
        }
    }

    private static string[] AudioCodec(FileFormat target, string? sourceCodec, QualitySettings q)
    {
        // Same codec, different container (e.g. Opus in .ogg → .opus): copy instead of re-encoding.
        if ((target == KnownFormats.Opus && sourceCodec == "opus") ||
            (target == KnownFormats.Ogg && sourceCodec is "vorbis" or "opus"))
        {
            return ["-c:a", "copy"];
        }

        return target.Id switch
        {
            "mp3" => ["-c:a", "libmp3lame", "-q:a", Num(q.Mp3VbrQuality)],
            "m4a" => ["-c:a", "aac", "-b:a", Kbps(q.AacKbps)],
            "wav" => ["-c:a", "pcm_s16le"],
            "flac" => ["-c:a", "flac"],
            "ogg" => ["-c:a", "libvorbis", "-q:a", Num(q.VorbisQuality)],
            "opus" => ["-c:a", "libopus", "-b:a", Kbps(q.OpusKbps)],
            "aiff" => ["-c:a", "pcm_s16be"],
            "wma" => ["-c:a", "wmav2", "-b:a", Kbps(q.WmaKbps)],
            _ => throw new ArgumentException($"{target.Label} isn't an audio target.", nameof(target)),
        };
    }

    private static string[] VideoCodec(FileFormat target, string? sourceCodec, QualitySettings q)
    {
        var codec = sourceCodec ?? "";
        switch (target.Id)
        {
            case "mp4" or "mov" when Mp4Video.Contains(codec):
                // HEVC needs the hvc1 tag to play in Apple's and Windows' built-in players.
                return codec == "hevc" ? ["-c:v", "copy", "-tag:v", "hvc1"] : ["-c:v", "copy"];
            case "mkv" when MkvVideo.Contains(codec):
                return ["-c:v", "copy"];
            case "webm" when WebmVideo.Contains(codec):
                return ["-c:v", "copy"];
            case "mp4" or "mov" or "mkv" or "avi":
                // yuv420p and even dimensions keep H.264 playable everywhere.
                return
                [
                    "-c:v", "libx264", "-crf", Num(q.H264Crf), "-preset", q.H264Preset,
                    "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                ];
            case "webm":
                return
                [
                    "-c:v", "libvpx-vp9", "-crf", Num(q.Vp9Crf), "-b:v", "0",
                    "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p",
                ];
            case "wmv":
                return ["-c:v", "wmv2", "-q:v", "3"];
            default:
                throw new ArgumentException($"{target.Label} isn't a video target.", nameof(target));
        }
    }

    private static string[] VideoAudioCodec(FileFormat target, string? sourceCodec, QualitySettings q)
    {
        var codec = sourceCodec ?? "";
        return target.Id switch
        {
            "mp4" or "mov" when Mp4Audio.Contains(codec) => ["-c:a", "copy"],
            "mkv" when MkvAudio.Contains(codec) => ["-c:a", "copy"],
            "webm" when WebmAudio.Contains(codec) => ["-c:a", "copy"],
            "mp4" or "mov" or "mkv" => ["-c:a", "aac", "-b:a", Kbps(q.AacKbps)],
            "webm" => ["-c:a", "libopus", "-b:a", Kbps(q.OpusKbps)],

            // AAC in AVI plays badly in many players; MP3 is the usual pairing.
            "avi" => ["-c:a", "libmp3lame", "-q:a", Num(q.Mp3VbrQuality)],
            "wmv" => ["-c:a", "wmav2", "-b:a", Kbps(q.WmaKbps)],
            _ => throw new ArgumentException($"{target.Label} isn't a video target.", nameof(target)),
        };
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Kbps(int value) => Num(value) + "k";
}
