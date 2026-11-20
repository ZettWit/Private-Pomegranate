using System.Globalization;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Media;

/// <summary>Audio and video conversions through FFmpeg, run as a child process.</summary>
public sealed class MediaConverter(string ffmpegPath, string ffprobePath) : IConverter
{
    public EngineKind Engine => EngineKind.Media;

    public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var info = await ProbeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        var arguments = FfmpegArguments.Build(request.SourcePath, request.OutputPath, request.Rule.To, info, request.Quality);

        var result = await ProcessRunner.RunAsync(
            ffmpegPath,
            arguments,
            cancellationToken,
            onOutputLine: line => ReportProgress(line, info.Duration, progress)).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new ConversionException(Describe(result));
        }

        progress.Report(1);
        return ConversionOutput.Single;
    }

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        string[] arguments = ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path];
        var result = await ProcessRunner.RunAsync(ffprobePath, arguments, cancellationToken, captureOutput: true)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new ConversionException($"FFmpeg couldn't read this file: {Describe(result)}");
        }

        return MediaInfo.Parse(result.StandardOutput);
    }

    /// <summary>Turns FFmpeg's <c>-progress</c> lines ("out_time_us=1234567") into 0–1.</summary>
    internal static void ReportProgress(string line, TimeSpan? duration, IProgress<double> progress)
    {
        if (duration is not { TotalMicroseconds: > 0 } total)
        {
            return;
        }

        const string key = "out_time_us=";
        if (line.StartsWith(key, StringComparison.Ordinal) &&
            long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) &&
            micros >= 0)
        {
            progress.Report(Math.Min(0.99, micros / total.TotalMicroseconds));
        }
    }

    private static string Describe(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StandardErrorTail)
            ? $"FFmpeg stopped with exit code {result.ExitCode}."
            : result.StandardErrorTail.Trim();
}
