using System.Globalization;
using System.Text.Json;

namespace Pomegranate.Engines.Media;

/// <summary>The parts of ffprobe's report that decide how to convert a file.</summary>
/// <param name="Width">Coded width of the first video stream (before rotation), or 0.</param>
/// <param name="Rotation">Clockwise degrees players turn the picture (0, 90, 180, 270).</param>
public sealed record MediaInfo(
    TimeSpan? Duration,
    string? VideoCodec,
    string? AudioCodec,
    string FormatName,
    int Width = 0,
    int Height = 0,
    int Rotation = 0,
    int SampleRate = 0,
    int Channels = 0,
    string? ChannelLayout = null)
{
    public bool HasVideo => VideoCodec is not null;

    public bool HasAudio => AudioCodec is not null;

    /// <summary>The picture's size as players show it, rotation applied.</summary>
    public int DisplayWidth => Rotation is 90 or 270 ? Height : Width;

    public int DisplayHeight => Rotation is 90 or 270 ? Width : Height;

    /// <summary>Parses <c>ffprobe -print_format json -show_format -show_streams</c> output.</summary>
    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? video = null;
        string? audio = null;
        int width = 0, height = 0, rotation = 0, sampleRate = 0, channels = 0;
        string? layout = null;
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
                    width = Int(stream, "width");
                    height = Int(stream, "height");
                    rotation = RotationOf(stream);
                }
                else if (type == "audio" && audio is null)
                {
                    audio = codec;
                    sampleRate = Int(stream, "sample_rate");
                    channels = Int(stream, "channels");
                    layout = stream.TryGetProperty("channel_layout", out var cl) ? cl.GetString() : null;
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

        return new MediaInfo(duration, video, audio, formatName, width, height, rotation, sampleRate, channels, layout);
    }

    /// <summary>A number that ffprobe writes either as a number or as a string ("sample_rate": "48000").</summary>
    private static int Int(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => 0,
        };
    }

    /// <summary>
    /// The display matrix's rotation (counter-clockwise in ffprobe, e.g. -90 for a phone held upright) or the
    /// old "rotate" tag (clockwise), as clockwise degrees.
    /// </summary>
    private static int RotationOf(JsonElement stream)
    {
        double? counterClockwise = null;
        if (stream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var r) && r.TryGetDouble(out var degrees))
                {
                    counterClockwise = degrees;
                    break;
                }
            }
        }

        double clockwise;
        if (counterClockwise is { } ccw)
        {
            clockwise = -ccw;
        }
        else if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var tag) &&
                 double.TryParse(tag.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cw))
        {
            clockwise = cw;
        }
        else
        {
            return 0;
        }

        var quarter = (int)Math.Round(clockwise / 90.0) * 90;
        return ((quarter % 360) + 360) % 360;
    }
}
