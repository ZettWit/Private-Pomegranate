using Pomegranate.Core.Formats;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;

namespace Pomegranate.Core.Jobs;

/// <summary>A tool to run, with its outer-ring option (None = the tool's default) and an editor's result.</summary>
public sealed record ToolInvocation(ToolKind Kind, ToolOption Option = ToolOption.None, ToolEdit? Edit = null);

/// <summary>
/// Runs file tools (spec §7.3) the way <see cref="JobRunner"/> runs conversions: per-engine limits, results
/// written in a temporary folder and moved to their final name only on success, originals untouched.
/// </summary>
public sealed class ToolRunner
{
    private readonly ToolCatalog _tools;
    private readonly FormatCatalog _formats;
    private readonly Dictionary<EngineKind, ITool> _engines;
    private readonly Dictionary<EngineKind, SemaphoreSlim> _slots;

    public ToolRunner(
        ToolCatalog tools,
        FormatCatalog formats,
        IEnumerable<ITool> engines,
        IReadOnlyDictionary<EngineKind, int>? limits = null)
    {
        _tools = tools;
        _formats = formats;
        _engines = engines.ToDictionary(t => t.Engine);
        limits ??= JobRunner.DefaultLimits();
        _slots = Enum.GetValues<EngineKind>().ToDictionary(
            e => e,
            e => new SemaphoreSlim(Math.Max(1, limits.GetValueOrDefault(e, 2))));
    }

    public ToolCatalog Catalog => _tools;

    /// <summary>Starts a tool on <paramref name="paths"/>: one job per file, or one job for all (Merge, Zip).</summary>
    /// <param name="label">The tool's name as the progress window and notification show it.</param>
    /// <param name="outputFolder">Null to write next to each original.</param>
    public ConversionBatch Start(
        ToolInvocation invocation,
        string label,
        IReadOnlyList<string> paths,
        QualitySettings quality,
        ToolSettings settings,
        string? outputFolder = null)
    {
        var definition = ToolCatalog.Definition(invocation.Kind);
        var resolved = invocation.Option == ToolOption.None ? invocation with { Option = definition.DefaultOption } : invocation;

        var cts = new CancellationTokenSource();
        var batch = new ConversionBatch(label, cts);
        var namer = new OutputNamer();

        IReadOnlyList<string>[] groups = definition.Input == ToolInput.AllTogether
            ? [paths.OrderBy(p => Path.GetFileName(p.TrimEnd('\\', '/')), NaturalComparer.Instance).ToArray()]
            : paths.Select(p => (IReadOnlyList<string>)[p]).ToArray();

        var jobs = groups.Where(g => g.Count > 0).Select(g => new ConversionJob(g, label, cts.Token)).ToArray();
        batch.Jobs = jobs;
        batch.Completion = Task.WhenAll(jobs.Select(job => Task.Run(() => RunSafelyAsync(job, resolved, quality, settings, outputFolder, namer))));
        return batch;
    }

    /// <summary>The output's name without extension (spec §7.3, "Output names").</summary>
    public string OutputName(ToolInvocation invocation, IReadOnlyList<string> sources)
    {
        var first = sources[0];
        var baseName = BaseName(first);
        return invocation.Kind switch
        {
            ToolKind.ExtractAudio or ToolKind.Extract or ToolKind.Split => baseName,
            ToolKind.Zip when sources.Count == 1 => baseName,
            ToolKind.Zip => FolderNameOf(first) is { Length: > 0 } parent ? parent : "Archive",
            _ => $"{baseName} ({Suffix(invocation)})",
        };
    }

    public static string Suffix(ToolInvocation invocation) => invocation.Kind switch
    {
        ToolKind.Compress => "compressed",
        ToolKind.Resize => "resized",
        ToolKind.Rotate => "rotated",
        ToolKind.Crop => "cropped",
        ToolKind.Annotate => "annotated",
        ToolKind.Redact => "redacted",
        ToolKind.StripMetadata => "no metadata",
        ToolKind.Trim => "trimmed",
        ToolKind.Speed => ToolOptions.SpeedText(invocation.Option),
        ToolKind.Mute => "muted",
        ToolKind.Bleep => "bleeped",
        ToolKind.Normalize => "normalized",
        ToolKind.Merge => "merged",
        ToolKind.Pages => invocation.Edit is PagesEdit { Selection: true } ? "selection" : "pages",
        _ => invocation.Kind.ToString().ToLowerInvariant(),
    };

    private string BaseName(string path) =>
        Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\', '/')) : _formats.BaseName(path);

    private static string? FolderNameOf(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd('\\', '/'));
        return parent is null ? null : Path.GetFileName(parent.TrimEnd('\\', '/'));
    }

    private async Task RunSafelyAsync(
        ConversionJob job, ToolInvocation invocation, QualitySettings quality, ToolSettings settings, string? outputFolder, OutputNamer namer)
    {
        try
        {
            await RunAsync(job, invocation, quality, settings, outputFolder, namer).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            job.Finish(JobStatus.Failed, error: ex.Message);
        }
    }

    private async Task RunAsync(
        ConversionJob job, ToolInvocation invocation, QualitySettings quality, ToolSettings settings, string? outputFolder, OutputNamer namer)
    {
        var first = job.SourcePath;
        var format = Directory.Exists(first) ? null : _formats.Detect(first);
        if (_tools.Find(invocation.Kind, format) is not { } rule)
        {
            job.Finish(JobStatus.Failed, error: $"{job.Label} doesn't work on {Path.GetFileName(first)}.");
            return;
        }

        if (!_engines.TryGetValue(rule.Engine, out var tool))
        {
            job.Finish(JobStatus.Failed, error: $"The {rule.Engine} engine isn't available.");
            return;
        }

        var folder = outputFolder
            ?? Path.GetDirectoryName(Path.GetFullPath(first).TrimEnd('\\', '/'))
            ?? ".";
        var work = OutputNamer.TemporaryFolderIn(folder);
        var slot = _slots[rule.Engine];

        try
        {
            await slot.WaitAsync(job.Token).ConfigureAwait(false);
            ToolOutput output;
            try
            {
                job.SetRunning();
                Directory.CreateDirectory(work);
                var request = new ToolRequest(invocation.Kind, invocation.Option, job.Sources, format, work, quality, settings, invocation.Edit);
                output = await tool.RunAsync(request, new JobProgress(job), job.Token).ConfigureAwait(false);
            }
            finally
            {
                slot.Release();
            }

            job.Token.ThrowIfCancellationRequested();
            Commit(job, invocation, output, folder, namer);
        }
        catch (NothingToDoException ex)
        {
            job.Finish(JobStatus.Skipped, note: ex.Message);
        }
        catch (OperationCanceledException)
        {
            job.Finish(JobStatus.Canceled);
        }
        catch (Exception ex)
        {
            job.Finish(JobStatus.Failed, error: ex.Message);
        }
        finally
        {
            OutputFiles.DeleteFolderQuietly(work);
        }
    }

    private void Commit(ConversionJob job, ToolInvocation invocation, ToolOutput output, string folder, OutputNamer namer)
    {
        var name = OutputName(invocation, job.Sources);
        switch (output.Kind)
        {
            case ToolOutputKind.Text:
                job.Finish(JobStatus.Succeeded, text: output.Text ?? "");
                return;

            case ToolOutputKind.Folder:
            {
                var temp = output.Paths[0];
                if (!Directory.Exists(temp))
                {
                    throw new ConversionException("The tool finished but wrote no folder.");
                }

                job.Finish(JobStatus.Succeeded, outputPath: OutputFiles.CommitFolder(temp, folder, name, namer));
                return;
            }

            case ToolOutputKind.Pages when output.Paths.Count > 1:
            {
                RequireFiles(output.Paths);
                var extension = Path.GetExtension(output.Paths[0]).TrimStart('.');
                var pages = OutputFiles.CommitPages(ConversionOutput.Pages(output.Paths).Parts, folder, name, extension, namer);
                job.Finish(JobStatus.Succeeded, outputPath: pages, outputCount: output.Paths.Count);
                return;
            }

            default:
            {
                RequireFiles(output.Paths);
                var temp = output.Paths[0];
                var extension = Path.GetExtension(temp).TrimStart('.');
                var planned = namer.Reserve(folder, name, extension);
                job.Finish(JobStatus.Succeeded, outputPath: OutputFiles.Commit(temp, planned, folder, name, extension, namer));
                return;
            }
        }
    }

    private static void RequireFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || paths.Any(p => !File.Exists(p)))
        {
            throw new ConversionException("The tool finished but some of its files are missing.");
        }
    }

    /// <summary>Reports progress synchronously.</summary>
    private sealed class JobProgress(ConversionJob job) : IProgress<double>
    {
        public void Report(double value) => job.SetProgress(value);
    }
}
