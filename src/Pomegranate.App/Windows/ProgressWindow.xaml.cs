using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Jobs;

namespace Pomegranate.App.Windows;

/// <summary>A small window above the tray listing running conversions, with Cancel (spec §8).</summary>
public partial class ProgressWindow : Window
{
    private readonly ObservableCollection<JobRow> _rows = [];
    private readonly List<ConversionBatch> _batches = [];
    private readonly DispatcherTimer _showDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _hideDelay = new() { Interval = TimeSpan.FromSeconds(2.5) };

    public ProgressWindow()
    {
        InitializeComponent();
        Jobs.ItemsSource = _rows;

        // Quick jobs finish before this fires, so the window only appears for conversions that take a while.
        _showDelay.Tick += (_, _) =>
        {
            _showDelay.Stop();
            if (_rows.Any(r => !r.Job.IsFinished))
            {
                PlaceAboveTray();
                Show();
            }
        };
        _hideDelay.Tick += (_, _) =>
        {
            _hideDelay.Stop();
            if (_rows.All(r => r.Job.IsFinished))
            {
                Hide();
                _rows.Clear();
                _batches.Clear();
            }
        };
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    internal void Track(ConversionBatch batch)
    {
        _hideDelay.Stop();
        _batches.Add(batch);
        foreach (var job in batch.Jobs)
        {
            var row = new JobRow(job);
            _rows.Add(row);
            job.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                row.Refresh();
                CheckAllDone();
            });
        }

        if (!IsVisible)
        {
            _showDelay.Start();
        }
    }

    private void CheckAllDone()
    {
        var done = _rows.All(r => r.Job.IsFinished);
        CancelButton.IsEnabled = !done;
        if (done)
        {
            _showDelay.Stop();
            _hideDelay.Start();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        foreach (var batch in _batches)
        {
            batch.CancelAll();
        }
    }

    private void PlaceAboveTray()
    {
        var area = SystemParameters.WorkArea;
        UpdateLayout();
        var height = double.IsNaN(ActualHeight) || ActualHeight < 1 ? 160 : ActualHeight;
        Left = area.Right - Width - 12;
        Top = area.Bottom - height - 12;
    }

    internal sealed class JobRow(ConversionJob job) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ConversionJob Job { get; } = job;

        public string Name => $"{Path.GetFileName(Job.SourcePath)} → {Job.Target.Label}";

        public double Percent => Job.Progress * 100;

        public bool IsIndeterminate => Job.Status == JobStatus.Running && Job.Progress <= 0;

        public string Status => Job.Status switch
        {
            JobStatus.Queued => L.Get("Progress_Queued"),
            JobStatus.Running => $"{Job.Progress:P0}",
            JobStatus.Succeeded => L.Get("Progress_Done"),
            JobStatus.Failed => L.Get("Progress_Failed"),
            JobStatus.Canceled => L.Get("Progress_Canceled"),
            JobStatus.Skipped => L.Get("Progress_Skipped"),
            _ => "",
        };

        public void Refresh()
        {
            Raise(nameof(Percent));
            Raise(nameof(Status));
            Raise(nameof(IsIndeterminate));
        }

        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
