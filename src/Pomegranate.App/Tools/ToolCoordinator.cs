using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pomegranate.App.Conversion;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Wheel;
using Pomegranate.App.Windows;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Tools;
using Pomegranate.Engines;

namespace Pomegranate.App.Tools;

/// <summary>Runs file tools: opens an editor when the tool has one, then runs it and reports the result (spec §7.3).</summary>
internal sealed class ToolCoordinator(
    ToolRunner runner,
    EngineSet engines,
    SettingsService settings,
    TrayIcon tray,
    ProgressWindow progress,
    Dispatcher dispatcher)
{
    private const int MaxNotifiedText = 240;

    private readonly EditorLauncher _editors = new(engines);

    public void Start(ToolItem item, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var definition = ToolCatalog.Definition(item.Kind);
        if (definition.IsEditor)
        {
            _editors.Open(item.Kind, paths[0], edit => Run(new ToolInvocation(item.Kind, item.Option, edit), paths));
            return;
        }

        Run(new ToolInvocation(item.Kind, item.Option), paths);
    }

    private void Run(ToolInvocation invocation, IReadOnlyList<string> paths)
    {
        var s = settings.Current;
        var label = WheelContentBuilder.ToolLabel(invocation.Kind);
        var batch = runner.Start(invocation, label, paths, s.Quality, s.Tools, s.OutputFolder);
        Log.Info($"Tool {invocation.Kind} ({invocation.Option}) on {paths.Count} item(s).");
        progress.Track(batch);
        batch.Completion.ContinueWith(_ => dispatcher.BeginInvoke(() => Finished(batch, invocation)), TaskScheduler.Default);
    }

    private void Finished(ConversionBatch batch, ToolInvocation invocation)
    {
        // The annotation layer the editor drew is only needed while the tool runs.
        if (invocation.Edit is AnnotateEdit annotate)
        {
            try
            {
                File.Delete(annotate.OverlayPath);
            }
            catch (IOException)
            {
            }
        }

        var done = batch.Jobs.Where(j => j.Status == JobStatus.Succeeded).ToArray();
        var failed = batch.Jobs.Where(j => j.Status == JobStatus.Failed).ToArray();
        var skipped = batch.Jobs.Where(j => j.Status == JobStatus.Skipped && j.Note is not null).ToArray();

        foreach (var job in failed)
        {
            Log.Error($"{invocation.Kind} on {job.SourcePath} failed: {job.Error}");
        }

        foreach (var job in skipped)
        {
            Log.Info($"{invocation.Kind} skipped {job.SourcePath}: {job.Note}");
        }

        foreach (var job in done.Where(j => j.OutputPath is not null))
        {
            Log.Info($"Wrote {job.OutputPath}");
        }

        if (failed.Length > 0)
        {
            var first = failed[0];
            tray.Notify(
                L.Format("Notify_ToolSomeFailedTitle", batch.Label, done.Length, batch.Jobs.Count),
                $"{Path.GetFileName(first.SourcePath)}: {first.Error}",
                isError: true,
                onClick: ConversionCoordinator.OpenLog);
            return;
        }

        var texts = done.Where(j => j.ResultText is not null).Select(j => j.ResultText!).ToArray();
        if (texts.Length > 0)
        {
            ReportText(invocation.Kind, texts);
            return;
        }

        if (done.Length == 0)
        {
            if (skipped.Length > 0)
            {
                var note = skipped.Length == 1 ? skipped[0].Note! : $"{skipped[0].Note} ({L.Format("Notify_FileCount", skipped.Length)})";
                tray.Notify(batch.Label, note);
            }

            return; // cancelled
        }

        var firstDone = done[0];
        var output = firstDone.OutputPath!;
        var text = done.Length > 1
            ? L.Format("Notify_FileCount", done.Sum(j => j.OutputCount))
            : firstDone.OutputCount > 1
                ? L.Format("Notify_PageCount", firstDone.OutputCount)
                : Path.GetFileName(output);

        Action open = Directory.Exists(output) ? () => ConversionCoordinator.OpenFolder(output) : () => ConversionCoordinator.ShowInExplorer(output);
        tray.Notify(L.Format("Notify_ToolDoneTitle", batch.Label), text, onClick: open);
    }

    /// <summary>Read QR code and Copy text: the result goes on the clipboard and into the notification. Links are never opened.</summary>
    private void ReportText(ToolKind kind, IReadOnlyList<string> texts)
    {
        var combined = string.Join(Environment.NewLine + Environment.NewLine, texts.Where(t => t.Length > 0));
        var copied = combined.Length > 0 && TrySetClipboard(combined);
        var title = kind == ToolKind.ReadCode ? L.Get("Notify_CodeCopiedTitle") : L.Get("Notify_TextCopiedTitle");
        if (!copied)
        {
            title = L.Get("Notify_ClipboardBusyTitle");
        }

        var shown = combined.Length > MaxNotifiedText ? combined[..MaxNotifiedText] + "…" : combined;
        tray.Notify(title, shown);
    }

    private static bool TrySetClipboard(string text)
    {
        // Another app can hold the clipboard open for a moment; try a few times.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 4)
            {
                Thread.Sleep(60);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Log.Error("Couldn't copy to the clipboard.", ex);
            }
        }

        return false;
    }
}
