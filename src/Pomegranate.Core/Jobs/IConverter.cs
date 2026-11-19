using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Jobs;

/// <summary>What an engine is asked to do: read <see cref="SourcePath"/>, write <see cref="OutputPath"/>.</summary>
/// <param name="OutputPath">A temporary path in the output folder. The runner renames it when the engine succeeds.</param>
public sealed record ConversionRequest(
    string SourcePath,
    string OutputPath,
    ConversionRule Rule,
    QualitySettings Quality);

/// <summary>One conversion engine (Magick.NET, FFmpeg, …).</summary>
public interface IConverter
{
    EngineKind Engine { get; }

    /// <summary>
    /// Converts the file. Reports progress from 0 to 1 when it can. Throws on failure and
    /// <see cref="OperationCanceledException"/> when cancelled. Must not touch the source file.
    /// </summary>
    Task ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken);
}

/// <summary>An engine failure with a message fit for the "Details" view.</summary>
public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
