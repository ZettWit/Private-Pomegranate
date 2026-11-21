using System.IO;
using System.Windows;
using System.Windows.Controls;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// Redact for images and video (spec §7.3): draw boxes; each is a black box (the default, and the only style
/// that can't be undone), pixelated or blurred. On video the boxes apply between a start and end time.
/// </summary>
internal sealed class RedactEditor(string path, MediaPreview? video) : EditorWindow(L.Get("Tool_Redact"), path)
{
    private readonly ImageStage _stage = new();
    private readonly TextBlock _times = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.8 };
    private BoxLayer? _boxes;
    private RangeBar? _during;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var body = new DockPanel();
        if (video is null)
        {
            var preview = await Task.Run(() => ImagePreview.Create(SourcePath, 2400, Path.Combine(Temp, "preview.png")), cancellationToken);
            _stage.Show(ImageStage.LoadBitmap(preview.PreviewPath), preview.Width, preview.Height);
        }
        else
        {
            var info = await video.ProbeAsync(SourcePath, cancellationToken);
            var duration = info.Duration?.TotalSeconds ?? 1;
            var frame = Path.Combine(Temp, "first.png");
            await video.FrameAsync(SourcePath, Math.Min(1, duration / 2), 1600, frame, cancellationToken);
            _stage.Show(ImageStage.LoadBitmap(frame), info.DisplayWidth, info.DisplayHeight);

            var scrubber = new FrameScrubber(video, SourcePath, Temp, _stage, duration, cancellationToken);
            scrubber.Slider.Value = Math.Min(1, duration / 2);
            _during = new RangeBar { Duration = duration, Start = 0, End = duration, Height = 36, Margin = new Thickness(0, 4, 0, 0) };
            AutomationName(_during, L.Get("Redact_During"));
            _during.Changed += UpdateTimes;
            _during.HandleMoved += t => scrubber.Slider.Value = t;

            var range = new DockPanel { Margin = new Thickness(12, 4, 12, 0) };
            var label = new TextBlock { Text = L.Get("Redact_During"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(label, Dock.Left);
            DockPanel.SetDock(_times, Dock.Right);
            range.Children.Add(label);
            range.Children.Add(_times);
            range.Children.Add(_during);

            var below = new StackPanel { Children = { scrubber.Panel, range } };
            DockPanel.SetDock(below, Dock.Bottom);
            body.Children.Add(below);
            UpdateTimes();
        }

        _boxes = new BoxLayer(_stage, single: false);
        Toolbar.Children.Add(Choice<RedactStyle>(
            [
                ("", L.Get("Redact_Black"), RedactStyle.Black),
                ("", L.Get("Redact_Pixelate"), RedactStyle.Pixelate),
                ("", L.Get("Redact_Blur"), RedactStyle.Blur),
            ],
            RedactStyle.Black,
            style =>
            {
                _boxes.NewStyle = style;
                if (_boxes.Selected is { } box)
                {
                    _boxes.SetStyle(box, style);
                }
            }));
        Toolbar.Children.Add(IconButton("", L.Get("Redact_Remove"), (_, _) =>
        {
            if (_boxes.Selected is { } box)
            {
                _boxes.Remove(box);
            }
        }, "Delete"));

        body.Children.Add(_stage);
        Body = body;
        Hint = L.Get(video is null ? "Redact_Hint" : "Redact_HintVideo");
        _stage.Overlay.Focus();
    }

    protected override void Undo()
    {
        if (_boxes?.Boxes.LastOrDefault() is { } last)
        {
            _boxes.Remove(last);
        }
    }

    private void UpdateTimes()
    {
        if (_during is not null)
        {
            _times.Text = $"{RangeBar.Format(_during.Start)} – {RangeBar.Format(_during.End)}";
        }
    }

    protected override ToolEdit? BuildEdit()
    {
        if (_boxes is null || _boxes.Boxes.Count == 0)
        {
            Hint = L.Get("Redact_NothingYet");
            return null;
        }

        TimeRange? during = null;
        if (_during is not null && (_during.Start > 0.01 || _during.End < _during.Duration - 0.01))
        {
            during = new TimeRange(_during.Start, _during.End);
        }

        return new RedactEdit(_boxes.Boxes.Select(b => new RedactBox(b.ToPixels(), b.Style)).ToArray(), during);
    }
}
