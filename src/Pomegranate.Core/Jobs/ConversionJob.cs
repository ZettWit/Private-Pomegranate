using Pomegranate.Core.Formats;

namespace Pomegranate.Core.Jobs;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,

    /// <summary>The file was already in the target format, or a tool had nothing to do (see <see cref="ConversionJob.Note"/>).</summary>
    Skipped,
}

/// <summary>
/// One file being converted, or one run of a tool (which may cover several files, e.g. Merge).
/// Raises <see cref="Changed"/> from worker threads.
/// </summary>
public sealed class ConversionJob
{
    private readonly CancellationTokenSource _cts;
    private readonly Lock _lock = new();

    internal ConversionJob(string sourcePath, FileFormat target, ConversionRule? rule, CancellationToken batchToken)
        : this([sourcePath], target.Label, batchToken)
    {
        Target = target;
        Rule = rule;
    }

    internal ConversionJob(IReadOnlyList<string> sources, string label, CancellationToken batchToken)
    {
        Sources = sources;
        Label = label;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
    }

    /// <summary>The first (usually only) input.</summary>
    public string SourcePath => Sources[0];

    public IReadOnlyList<string> Sources { get; }

    /// <summary>What the job makes: the target format's label ("PNG") or the tool's name ("Compress").</summary>
    public string Label { get; }

    /// <summary>The target format of a conversion; null for a tool.</summary>
    public FileFormat? Target { get; }

    /// <summary>Null when the job is skipped or no rule exists.</summary>
    public ConversionRule? Rule { get; }

    public JobStatus Status { get; private set; } = JobStatus.Queued;

    /// <summary>0 to 1.</summary>
    public double Progress { get; private set; }

    /// <summary>
    /// The final output once the job has succeeded: the file, or for several pages the folder holding them,
    /// or for several named files (sheets) the first of them.
    /// </summary>
    public string? OutputPath { get; private set; }

    /// <summary>How many files the job produced (pages, sheets); 1 for an ordinary conversion.</summary>
    public int OutputCount { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Why a tool skipped the file ("already as small as it gets"), or null.</summary>
    public string? Note { get; private set; }

    /// <summary>A tool's text result (Read QR code, Copy text), or null.</summary>
    public string? ResultText { get; private set; }

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled or JobStatus.Skipped;

    public event EventHandler? Changed;

    internal CancellationToken Token => _cts.Token;

    public void Cancel()
    {
        if (!IsFinished)
        {
            _cts.Cancel();
        }
    }

    internal void SetRunning() => Update(() => Status = JobStatus.Running);

    internal void SetProgress(double value) => Update(() => Progress = Math.Clamp(value, 0, 1));

    internal void Finish(
        JobStatus status,
        string? outputPath = null,
        string? error = null,
        int outputCount = 0,
        string? note = null,
        string? text = null) => Update(() =>
    {
        Status = status;
        OutputPath = outputPath;
        OutputCount = outputPath is null ? 0 : Math.Max(1, outputCount);
        Error = error;
        Note = note;
        ResultText = text;
        if (status == JobStatus.Succeeded)
        {
            Progress = 1;
        }
    });

    private void Update(Action change)
    {
        lock (_lock)
        {
            if (IsFinished)
            {
                return;
            }

            change();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
