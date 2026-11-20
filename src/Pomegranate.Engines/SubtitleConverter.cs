using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines;

/// <summary>One subtitle cue.</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>SRT ↔ VTT and both → TXT, in Pomegranate's own code.</summary>
public sealed partial class SubtitleConverter : IConverter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public EngineKind Engine => EngineKind.Subtitle;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        var cues = request.Rule.From == KnownFormats.Vtt ? ParseVtt(text) : ParseSrt(text);
        if (cues.Count == 0)
        {
            throw new ConversionException("No subtitles found in this file.");
        }

        progress.Report(0.5);
        var output = request.Rule.To.Id switch
        {
            "srt" => WriteSrt(cues),
            "vtt" => WriteVtt(cues),
            "txt" => WriteText(cues),
            _ => throw new ConversionException($"{request.Rule.To.Label} isn't a subtitle target."),
        };

        await File.WriteAllTextAsync(request.OutputPath, output, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        progress.Report(1);
        return ConversionOutput.Single;
    }

    public static IReadOnlyList<SubtitleCue> ParseSrt(string text) => ParseBlocks(text, isVtt: false);

    public static IReadOnlyList<SubtitleCue> ParseVtt(string text) => ParseBlocks(text, isVtt: true);

    public static string WriteSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            sb.Append(i + 1).Append("\r\n");
            sb.Append(Time(cues[i].Start, ',')).Append(" --> ").Append(Time(cues[i].End, ',')).Append("\r\n");
            sb.Append(KeepBasicTags(cues[i].Text).Replace("\n", "\r\n")).Append("\r\n\r\n");
        }

        return sb.ToString();
    }

    public static string WriteVtt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            sb.Append(Time(cue.Start, '.')).Append(" --> ").Append(Time(cue.End, '.')).Append('\n');

            // "-->" isn't allowed in VTT cue text.
            sb.Append(KeepBasicTags(cue.Text).Replace("-->", "->")).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>Plain text: tags removed, one line per cue line, repeated lines collapsed.</summary>
    public static string WriteText(IReadOnlyList<SubtitleCue> cues)
    {
        var lines = new List<string>();
        foreach (var cue in cues)
        {
            foreach (var raw in StripTags(cue.Text).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0 && (lines.Count == 0 || lines[^1] != line))
                {
                    lines.Add(line);
                }
            }
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static List<SubtitleCue> ParseBlocks(string text, bool isVtt)
    {
        var cues = new List<SubtitleCue>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
        var blocks = BlankLines().Split(normalized);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n').ToList();
            var timeIndex = lines.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
            if (timeIndex < 0 || timeIndex > 1)
            {
                // WEBVTT header, NOTE, STYLE and REGION blocks, or junk.
                continue;
            }

            var match = TimeLine().Match(lines[timeIndex]);
            if (!match.Success)
            {
                continue;
            }

            var start = ParseTime(match.Groups["start"].Value);
            var end = ParseTime(match.Groups["end"].Value);
            if (start is null || end is null)
            {
                continue;
            }

            var body = string.Join('\n', lines.Skip(timeIndex + 1)).Trim('\n');
            if (!isVtt)
            {
                // SRT allows <font color=…>; only keep tags both formats understand.
                body = KeepBasicTags(body);
            }

            cues.Add(new SubtitleCue(start.Value, end.Value, body));
        }

        return cues;
    }

    /// <summary>Parses "HH:MM:SS,mmm", "HH:MM:SS.mmm" or VTT's short "MM:SS.mmm".</summary>
    internal static TimeSpan? ParseTime(string value)
    {
        var parts = value.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var hours = parts.Length == 3 ? parts[0] : "0";
        var minutes = parts[^2];
        var seconds = parts[^1];
        if (!int.TryParse(hours, NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !double.TryParse(seconds, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var s))
        {
            return null;
        }

        return new TimeSpan(0, h, m, 0) + TimeSpan.FromMilliseconds(Math.Round(s * 1000));
    }

    private static string Time(TimeSpan t, char separator) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}");

    private static string KeepBasicTags(string text) => AnyTag().Replace(text, m =>
        BasicTag().IsMatch(m.Value) ? m.Value.ToLowerInvariant() : "");

    private static string StripTags(string text) => AnyTag().Replace(text, "")
        .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");

    // A blank line may contain stray spaces or tabs.
    [GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)*")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^\s*(?<start>[\d:.,]+)\s*-->\s*(?<end>[\d:.,]+)")]
    private static partial Regex TimeLine();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"^</?[biu]>$", RegexOptions.IgnoreCase)]
    private static partial Regex BasicTag();
}
