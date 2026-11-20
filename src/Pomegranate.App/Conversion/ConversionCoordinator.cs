using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Windows;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.App.Conversion;

/// <summary>Starts conversions, shows their progress and reports the result (spec §8).</summary>
internal sealed class ConversionCoordinator(
    Func<JobRunner> runner,
    SettingsService settings,
    TrayIcon tray,
    ProgressWindow progress,
    Dispatcher dispatcher)
{
    public void Start(IReadOnlyList<string> paths, FileFormat target)
    {
        var s = settings.Current;
        var batch = runner().Start(paths, target, s.Quality, s.OutputFolder);
        Log.Info($"Converting {paths.Count} file(s) to {target.Label}.");
        progress.Track(batch);
        batch.Completion.ContinueWith(_ => dispatcher.BeginInvoke(() => Finished(batch)), TaskScheduler.Default);
    }

    private void Finished(ConversionBatch batch)
    {
        var converted = batch.Jobs.Where(j => j.Status == JobStatus.Succeeded).ToArray();
        var failed = batch.Jobs.Where(j => j.Status == JobStatus.Failed).ToArray();
        var attempted = batch.Jobs.Count(j => j.Status != JobStatus.Skipped);

        foreach (var job in failed)
        {
            Log.Error($"Converting {job.SourcePath} to {job.Target.Label} failed: {job.Error}");
        }

        foreach (var job in converted)
        {
            Log.Info($"Wrote {job.OutputPath}");
        }

        if (failed.Length > 0)
        {
            var first = failed[0];
            tray.Notify(
                L.Format("Notify_SomeFailedTitle", converted.Length, attempted),
                $"{Path.GetFileName(first.SourcePath)}: {first.Error}",
                isError: true,
                onClick: OpenLog);
            return;
        }

        if (converted.Length == 0)
        {
            return; // cancelled, or everything was already in the target format
        }

        var done = converted[0];
        var firstOutput = done.OutputPath!;
        var text = converted.Length > 1
            ? L.Format("Notify_FileCount", converted.Sum(j => j.OutputCount))
            : done.OutputCount > 1 && Directory.Exists(firstOutput)
                ? L.Format("Notify_PageCount", done.OutputCount)
                : done.OutputCount > 1
                    ? L.Format("Notify_FileCount", done.OutputCount)
                    : Path.GetFileName(firstOutput);

        // A pages folder opens; a file is shown selected in its folder.
        Action open = Directory.Exists(firstOutput) ? () => OpenFolder(firstOutput) : () => ShowInExplorer(firstOutput);
        tray.Notify(L.Format("Notify_ConvertedTitle", batch.Target.Label), text, onClick: open);
    }

    public static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Couldn't open Explorer.", ex);
        }
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Couldn't open Explorer.", ex);
        }
    }

    public static void OpenLog()
    {
        try
        {
            var file = File.Exists(Log.CurrentFile) ? Log.CurrentFile : Log.Folder;
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Couldn't open the log.", ex);
        }
    }
}
