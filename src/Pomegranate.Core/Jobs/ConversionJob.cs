using Pomegranate.Core.Formats;

namespace Pomegranate.Core.Jobs;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,

    /// <summary>The file was already in the target format.</summary>
    Skipped,
}

/// <summary>One file being converted. Raises <see cref="Changed"/> from worker threads.</summary>
public sealed class ConversionJob
{
    private readonly CancellationTokenSource _cts;
    private readonly Lock _lock = new();

    internal ConversionJob(string sourcePath, FileFormat target, ConversionRule? rule, CancellationToken batchToken)
    {
        SourcePath = sourcePath;
        Target = target;
        Rule = rule;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
    }

    public string SourcePath { get; }

    public FileFormat Target { get; }

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

    internal void Finish(JobStatus status, string? outputPath = null, string? error = null, int outputCount = 0) => Update(() =>
    {
        Status = status;
        OutputPath = outputPath;
        OutputCount = outputPath is null ? 0 : Math.Max(1, outputCount);
        Error = error;
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
