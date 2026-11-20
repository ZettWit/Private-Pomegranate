using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Jobs;

/// <summary>A set of files dropped together onto one target format.</summary>
public sealed class ConversionBatch
{
    private readonly CancellationTokenSource _cts;

    internal ConversionBatch(FileFormat target, CancellationTokenSource cts)
    {
        Target = target;
        _cts = cts;
    }

    public FileFormat Target { get; }

    public IReadOnlyList<ConversionJob> Jobs { get; internal set; } = [];

    /// <summary>Completes when every job has finished. Never faults.</summary>
    public Task Completion { get; internal set; } = Task.CompletedTask;

    public int Succeeded => Jobs.Count(j => j.Status == JobStatus.Succeeded);

    public int Failed => Jobs.Count(j => j.Status == JobStatus.Failed);

    public void CancelAll() => _cts.Cancel();
}

/// <summary>
/// Runs conversions with a per-engine limit on how many run at once (spec §8, §12.4).
/// Output goes to a temporary file first and is renamed only on success; failures leave nothing behind.
/// </summary>
public sealed class JobRunner
{
    private readonly FormatCatalog _catalog;
    private readonly Dictionary<EngineKind, IConverter> _converters;
    private readonly Dictionary<EngineKind, SemaphoreSlim> _slots;

    public JobRunner(
        FormatCatalog catalog,
        IEnumerable<IConverter> converters,
        IReadOnlyDictionary<EngineKind, int>? limits = null)
    {
        _catalog = catalog;
        _converters = converters.ToDictionary(c => c.Engine);
        limits ??= DefaultLimits();
        _slots = Enum.GetValues<EngineKind>().ToDictionary(
            e => e,
            e => new SemaphoreSlim(Math.Max(1, limits.GetValueOrDefault(e, 2))));
    }

    public static IReadOnlyDictionary<EngineKind, int> DefaultLimits() => new Dictionary<EngineKind, int>
    {
        [EngineKind.Image] = Environment.ProcessorCount,
        [EngineKind.Dng] = 2,
        [EngineKind.Media] = 2,
        [EngineKind.Archive] = 2,
        [EngineKind.Subtitle] = 4,
        [EngineKind.Pandoc] = 2,

        // Office apps are heavy and partly single-instance (PowerPoint): one conversion at a time per app.
        [EngineKind.OfficeText] = 1,
        [EngineKind.OfficeSheet] = 1,
        [EngineKind.OfficeSlides] = 1,
        [EngineKind.PdfImage] = 2,
        [EngineKind.PdfText] = 2,
        [EngineKind.PdfDocument] = 2,
        [EngineKind.Sheet] = 2,
        [EngineKind.WebPdf] = 1,
    };

    /// <summary>Starts converting <paramref name="sources"/> to <paramref name="target"/>.</summary>
    /// <param name="outputFolder">Null to write next to each original.</param>
    public ConversionBatch Start(
        IReadOnlyList<string> sources,
        FileFormat target,
        QualitySettings quality,
        string? outputFolder = null)
    {
        var cts = new CancellationTokenSource();
        var batch = new ConversionBatch(target, cts);
        var namer = new OutputNamer();

        var jobs = sources.Select(source =>
        {
            var format = _catalog.Detect(source);
            var rule = format is null || format == target ? null : _catalog.FindRule(format, target);
            return new ConversionJob(source, target, rule, cts.Token);
        }).ToArray();

        batch.Jobs = jobs;
        batch.Completion = Task.WhenAll(jobs.Select(job => Task.Run(() => RunSafelyAsync(job, quality, outputFolder, namer))));
        return batch;
    }

    private async Task RunSafelyAsync(ConversionJob job, QualitySettings quality, string? outputFolder, OutputNamer namer)
    {
        try
        {
            await RunAsync(job, quality, outputFolder, namer).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // E.g. an invalid output folder. Keep the batch's Completion task from faulting.
            job.Finish(JobStatus.Failed, error: ex.Message);
        }
    }

    private async Task RunAsync(ConversionJob job, QualitySettings quality, string? outputFolder, OutputNamer namer)
    {
        var format = _catalog.Detect(job.SourcePath);
        if (format == job.Target)
        {
            job.Finish(JobStatus.Skipped);
            return;
        }

        if (job.Rule is not { } rule)
        {
            job.Finish(JobStatus.Failed, error: $"Can't convert {Path.GetFileName(job.SourcePath)} to {job.Target.Label}.");
            return;
        }

        if (!_converters.TryGetValue(rule.Engine, out var converter))
        {
            job.Finish(JobStatus.Failed, error: $"The {rule.Engine} engine isn't available.");
            return;
        }

        var folder = outputFolder ?? Path.GetDirectoryName(Path.GetFullPath(job.SourcePath)) ?? ".";
        var baseName = _catalog.BaseName(job.SourcePath);
        var extension = job.Target.PrimaryExtension;
        var planned = namer.Reserve(folder, baseName, extension);
        var temp = OutputNamer.TemporaryPathFor(planned);
        var work = OutputNamer.TemporaryFolderIn(folder);
        var slot = _slots[rule.Engine];

        try
        {
            await slot.WaitAsync(job.Token).ConfigureAwait(false);
            ConversionOutput output;
            try
            {
                job.SetRunning();
                Directory.CreateDirectory(work);
                var request = new ConversionRequest(job.SourcePath, temp, rule, quality, work);
                output = await converter.ConvertAsync(request, new JobProgress(job), job.Token).ConfigureAwait(false);
            }
            finally
            {
                slot.Release();
            }

            var (final, count) = CommitOutput(output, temp, planned, folder, baseName, extension, namer);
            job.Finish(JobStatus.Succeeded, outputPath: final, outputCount: count);
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(temp);
            namer.Release(planned);
            job.Finish(JobStatus.Canceled);
        }
        catch (Exception ex)
        {
            DeleteQuietly(temp);
            namer.Release(planned);
            job.Finish(JobStatus.Failed, error: ex.Message);
        }
        finally
        {
            DeleteFolderQuietly(work);
        }
    }

    /// <summary>
    /// Moves what the engine wrote to its final place (spec §7.2): one file next to the original; several
    /// pages in a new "&lt;name&gt; (pages)" folder; or several named files ("&lt;name&gt; - Sheet1.csv") next to it.
    /// Returns the output to show the user and how many files there are.
    /// </summary>
    private static (string Path, int Count) CommitOutput(
        ConversionOutput output, string temp, string planned, string folder, string baseName, string extension, OutputNamer namer)
    {
        if (output.Parts.Count == 0)
        {
            if (!File.Exists(temp))
            {
                throw new ConversionException("The engine finished but wrote no file.");
            }

            return (Commit(temp, planned, folder, baseName, extension, namer), 1);
        }

        foreach (var part in output.Parts)
        {
            if (!File.Exists(part.TempPath))
            {
                throw new ConversionException("The engine finished but some of its files are missing.");
            }
        }

        if (output.Parts.Count == 1)
        {
            return (Commit(output.Parts[0].TempPath, planned, folder, baseName, extension, namer), 1);
        }

        namer.Release(planned);
        if (output.GroupInFolder)
        {
            var pages = namer.Reserve(folder, $"{baseName} (pages)", extension: null);
            Directory.CreateDirectory(pages);
            foreach (var part in output.Parts)
            {
                File.Move(part.TempPath, Path.Combine(pages, $"{baseName} - {SafeName(part.Suffix)}.{extension}"));
            }

            return (pages, output.Parts.Count);
        }

        string? first = null;
        foreach (var part in output.Parts)
        {
            var name = $"{baseName} - {SafeName(part.Suffix)}";
            var target = namer.Reserve(folder, name, extension);
            var final = Commit(part.TempPath, target, folder, name, extension, namer);
            first ??= final;
        }

        return (first!, output.Parts.Count);
    }

    /// <summary>Sheet names and the like can contain characters Windows doesn't allow in file names.</summary>
    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "part" : cleaned;
    }

    /// <summary>Renames the temporary file to its final name, choosing a new name if another file took it meanwhile.</summary>
    private static string Commit(string temp, string planned, string folder, string baseName, string extension, OutputNamer namer)
    {
        var target = planned;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                target = namer.Reserve(folder, baseName, extension);
            }
        }

        throw new ConversionException("Couldn't find a free file name for the output.");
    }

    private static void DeleteFolderQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reports progress synchronously (unlike <see cref="Progress{T}"/>, which posts to a sync context).</summary>
    private sealed class JobProgress(ConversionJob job) : IProgress<double>
    {
        public void Report(double value) => job.SetProgress(value);
    }
}
