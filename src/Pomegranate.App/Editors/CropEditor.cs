using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>Crop for images and video (spec §7.3): drag the frame or its handles; aspect presets.</summary>
internal sealed class CropEditor(string path, MediaPreview? video) : EditorWindow(L.Get("Tool_Crop"), path)
{
    private readonly ImageStage _stage = new();
    private readonly TextBlock _size = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 6), Opacity = 0.75 };
    private BoxLayer? _boxes;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        int width, height;
        FrameworkElement? below = null;
        if (video is null)
        {
            var preview = await Task.Run(() => ImagePreview.Create(SourcePath, 2400, Path.Combine(Temp, "preview.png")), cancellationToken);
            width = preview.Width;
            height = preview.Height;
            _stage.Show(ImageStage.LoadBitmap(preview.PreviewPath), width, height);
        }
        else
        {
            var info = await video.ProbeAsync(SourcePath, cancellationToken);
            width = info.DisplayWidth;
            height = info.DisplayHeight;
            var duration = info.Duration?.TotalSeconds ?? 1;
            var frame = Path.Combine(Temp, "first.png");
            await video.FrameAsync(SourcePath, Math.Min(1, duration / 2), 1600, frame, cancellationToken);
            _stage.Show(ImageStage.LoadBitmap(frame), width, height);
            var scrubber = new FrameScrubber(video, SourcePath, Temp, _stage, duration, cancellationToken);
            scrubber.Slider.Value = Math.Min(1, duration / 2);
            below = scrubber.Panel;
        }

        _boxes = new BoxLayer(_stage, single: true);
        _boxes.Add(new Rect(0, 0, width, height));
        _boxes.Changed += UpdateSize;

        var original = (double)width / height;
        Toolbar.Children.Add(Choice<double?>(
            [
                ("", L.Get("Crop_Free"), null),
                ("", L.Get("Crop_Original"), original),
                ("", "1:1", 1.0),
                ("", "4:3", 4.0 / 3),
                ("", "16:9", 16.0 / 9),
                ("", "9:16", 9.0 / 16),
            ],
            null,
            aspect => _boxes.ApplyAspect(aspect)));
        Toolbar.Children.Add(IconButton("", L.Get("Crop_Reset"), (_, _) =>
        {
            _boxes.Selected!.Rect = new Rect(0, 0, width, height);
            _boxes.ApplyAspect(_boxes.Aspect);
            UpdateSize();
        }));
        Toolbar.Children.Add(_size);

        var body = new DockPanel();
        if (below is not null)
        {
            DockPanel.SetDock(below, Dock.Bottom);
            body.Children.Add(below);
        }

        body.Children.Add(_stage);
        Body = body;
        Hint = L.Get(video is null ? "Crop_Hint" : "Crop_HintVideo");
        UpdateSize();
        _stage.Overlay.Focus();
    }

    private void UpdateSize()
    {
        if (_boxes?.Selected is { } box)
        {
            var r = box.ToPixels();
            _size.Text = string.Create(CultureInfo.CurrentCulture, $"{r.Width} × {r.Height}");
        }
    }

    protected override ToolEdit? BuildEdit()
    {
        if (_boxes?.Selected is not { } box)
        {
            return null;
        }

        var area = box.ToPixels();
        if (area.X == 0 && area.Y == 0 && area.Width >= _stage.PixelWidth && area.Height >= _stage.PixelHeight)
        {
            Hint = L.Get("Crop_NothingYet");
            return null;
        }

        return new CropEdit(area);
    }
}
