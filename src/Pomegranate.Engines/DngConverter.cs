using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines;

/// <summary>Camera RAW → DNG through dnglab.</summary>
public sealed class DngConverter(string dnglabPath) : IConverter
{
    public EngineKind Engine => EngineKind.Dng;

    public async Task ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0.1);
        string[] arguments = ["convert", "--loglevel", "error", request.SourcePath, request.OutputPath];
        var result = await ProcessRunner.RunAsync(dnglabPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardErrorTail) ? $"exit code {result.ExitCode}" : result.StandardErrorTail;
            throw new ConversionException($"dnglab couldn't convert this file: {detail}");
        }

        progress.Report(1);
    }
}
