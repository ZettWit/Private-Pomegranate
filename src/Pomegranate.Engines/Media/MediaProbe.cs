using System.Globalization;
using System.Text.Json;

namespace Pomegranate.Engines.Media;

/// <summary>The parts of ffprobe's report that decide how to convert a file.</summary>
public sealed record MediaInfo(
    TimeSpan? Duration,
    string? VideoCodec,
    string? AudioCodec,
    string FormatName)
{
    public bool HasVideo => VideoCodec is not null;

    public bool HasAudio => AudioCodec is not null;

    /// <summary>Parses <c>ffprobe -print_format json -show_format -show_streams</c> output.</summary>
    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? video = null;
        string? audio = null;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null;

                // Cover art in audio files shows up as a one-frame "video" stream; it isn't real video.
                var isAttachedPicture = stream.TryGetProperty("disposition", out var d) &&
                    d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;

                if (type == "video" && video is null && !isAttachedPicture)
                {
                    video = codec;
                }
                else if (type == "audio" && audio is null)
                {
                    audio = codec;
                }
            }
        }

        TimeSpan? duration = null;
        var formatName = "";
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var dur) &&
                double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            formatName = format.TryGetProperty("format_name", out var fn) ? fn.GetString() ?? "" : "";
        }

        return new MediaInfo(duration, video, audio, formatName);
    }
}
