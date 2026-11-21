using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// A timeline (spec §7.3, Trim and Bleep): a waveform or a strip of frames, with either one kept range between
/// two handles, or several marked ranges. Drag a handle to move it; in marking mode, drag on empty space to
/// mark a range, click one to select it, Delete removes it. Left/Right nudge the active handle (Shift: 1 s).
/// </summary>
internal sealed class RangeBar : FrameworkElement
{
    private const double HandleReach = 8;
    private const double MinLength = 0.05;

    private readonly Brush _accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(SystemParameters.WindowGlassColor);
    private readonly Brush _wave = new SolidColorBrush(Color.FromArgb(0xC0, 0x80, 0x80, 0x80));
    private readonly Brush _dim = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private readonly Brush _mark = new SolidColorBrush(Color.FromArgb(0x70, 0xE0, 0x30, 0x30));
    private readonly Brush _background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80));

    private Grab _grab;
    private double _grabOffset;
    private TimeRange? _drawing;
    private double _drawAnchor;

    public RangeBar()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        MinHeight = 56;
    }

    private enum Grab
    {
        None,
        Start,
        End,
        Mark,
        MarkStart,
        MarkEnd,
        Draw,
    }

    /// <summary>The start or end moved (seconds), for showing the frame there.</summary>
    public event Action<double>? HandleMoved;

    public event Action? Changed;

    public double Duration { get; set; } = 1;

    public double Start { get; set; }

    public double End { get; set; } = 1;

    /// <summary>True for Bleep: several marked ranges instead of one kept range.</summary>
    public bool Marking { get; init; }

    public List<TimeRange> Marks { get; } = [];

    public int SelectedMark { get; private set; } = -1;

    public float[]? Peaks { get; set; }

    public IReadOnlyList<BitmapSource>? Frames { get; set; }

    /// <summary>Where playback is, or null.</summary>
    public double? Playhead { get; set; }

    /// <summary>Which handle the arrow keys move.</summary>
    public bool EndIsActive { get; private set; }

    public void RemoveSelectedMark()
    {
        if (SelectedMark >= 0 && SelectedMark < Marks.Count)
        {
            Marks.RemoveAt(SelectedMark);
            SelectedMark = Marks.Count - 1;
            InvalidateVisual();
            Changed?.Invoke();
        }
    }

    private double X(double seconds) => Duration <= 0 ? 0 : seconds / Duration * ActualWidth;

    private double T(double x) => Math.Clamp(x / Math.Max(1, ActualWidth) * Duration, 0, Duration);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(_background, null, new Rect(0, 0, w, h), 4, 4);

        if (Frames is { Count: > 0 } frames)
        {
            var frameWidth = w / frames.Count;
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var fh = h;
                var fw = f.PixelHeight > 0 ? f.PixelWidth * fh / f.PixelHeight : frameWidth;
                dc.PushClip(new RectangleGeometry(new Rect(i * frameWidth, 0, frameWidth, h)));
                dc.DrawImage(f, new Rect((i * frameWidth) + ((frameWidth - fw) / 2), 0, fw, fh));
                dc.Pop();
            }
        }
        else if (Peaks is { Length: > 0 } peaks)
        {
            var pen = new Pen(_wave, 1);
            var mid = h / 2;
            for (var x = 0; x < (int)w; x++)
            {
                var i = (int)((long)x * peaks.Length / Math.Max(1, (int)w));
                var amplitude = Math.Max(0.5, peaks[Math.Min(i, peaks.Length - 1)] * (h / 2 - 4));
                dc.DrawLine(pen, new Point(x + 0.5, mid - amplitude), new Point(x + 0.5, mid + amplitude));
            }
        }

        if (Marking)
        {
            for (var i = 0; i < Marks.Count; i++)
            {
                var m = Marks[i];
                var rect = new Rect(X(m.Start), 0, Math.Max(1, X(m.End) - X(m.Start)), h);
                dc.DrawRectangle(_mark, i == SelectedMark ? new Pen(_accent, 2) : null, rect);
            }

            if (_drawing is { } d)
            {
                dc.DrawRectangle(_mark, new Pen(_accent, 1), new Rect(X(d.Start), 0, Math.Max(1, X(d.End) - X(d.Start)), h));
            }
        }
        else
        {
            // Dim what gets cut, outline what's kept, and draw the two handles.
            dc.DrawRectangle(_dim, null, new Rect(0, 0, X(Start), h));
            dc.DrawRectangle(_dim, null, new Rect(X(End), 0, Math.Max(0, w - X(End)), h));
            dc.DrawRectangle(null, new Pen(_accent, 2), new Rect(X(Start), 1, Math.Max(1, X(End) - X(Start)), h - 2));
            DrawHandle(dc, X(Start), h, !EndIsActive && IsKeyboardFocused);
            DrawHandle(dc, X(End), h, EndIsActive && IsKeyboardFocused);
        }

        if (Playhead is { } p)
        {
            dc.DrawLine(new Pen(Brushes.White, 2), new Point(X(p), 0), new Point(X(p), h));
        }
    }

    private void DrawHandle(DrawingContext dc, double x, double h, bool active)
    {
        dc.DrawRoundedRectangle(_accent, active ? new Pen(Brushes.White, 2) : null, new Rect(x - 4, 0, 8, h), 3, 3);
        var grip = new Pen(Brushes.White, 1);
        dc.DrawLine(grip, new Point(x, (h / 2) - 6), new Point(x, (h / 2) + 6));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var x = e.GetPosition(this).X;
        var t = T(x);
        CaptureMouse();

        if (!Marking)
        {
            // The nearer handle follows the pointer.
            EndIsActive = Math.Abs(x - X(End)) < Math.Abs(x - X(Start)) || (Math.Abs(x - X(End)) <= HandleReach && x >= X(End));
            _grab = EndIsActive ? Grab.End : Grab.Start;
            MoveHandle(t);
            return;
        }

        for (var i = Marks.Count - 1; i >= 0; i--)
        {
            var m = Marks[i];
            if (Math.Abs(x - X(m.Start)) <= HandleReach)
            {
                SelectedMark = i;
                _grab = Grab.MarkStart;
                InvalidateVisual();
                return;
            }

            if (Math.Abs(x - X(m.End)) <= HandleReach)
            {
                SelectedMark = i;
                _grab = Grab.MarkEnd;
                InvalidateVisual();
                return;
            }

            if (t >= m.Start && t <= m.End)
            {
                SelectedMark = i;
                _grab = Grab.Mark;
                _grabOffset = t - m.Start;
                InvalidateVisual();
                return;
            }
        }

        _grab = Grab.Draw;
        _drawAnchor = t;
        _drawing = new TimeRange(t, t);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (_grab == Grab.None)
        {
            Cursor = !Marking && (Math.Abs(x - X(Start)) <= HandleReach || Math.Abs(x - X(End)) <= HandleReach) ? Cursors.SizeWE : Cursors.IBeam;
            return;
        }

        var t = T(x);
        switch (_grab)
        {
            case Grab.Start or Grab.End:
                MoveHandle(t);
                break;
            case Grab.Draw:
                _drawing = new TimeRange(Math.Min(_drawAnchor, t), Math.Max(_drawAnchor, t));
                HandleMoved?.Invoke(t);
                break;
            case Grab.Mark when SelectedMark >= 0:
            {
                var m = Marks[SelectedMark];
                var start = Math.Clamp(t - _grabOffset, 0, Duration - m.Length);
                Marks[SelectedMark] = new TimeRange(start, start + m.Length);
                break;
            }

            case Grab.MarkStart when SelectedMark >= 0:
                Marks[SelectedMark] = new TimeRange(Math.Min(t, Marks[SelectedMark].End - MinLength), Marks[SelectedMark].End);
                break;
            case Grab.MarkEnd when SelectedMark >= 0:
                Marks[SelectedMark] = new TimeRange(Marks[SelectedMark].Start, Math.Max(t, Marks[SelectedMark].Start + MinLength));
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_grab == Grab.Draw && _drawing is { } d)
        {
            if (d.Length >= MinLength)
            {
                Marks.Add(d);
                SelectedMark = Marks.Count - 1;
            }

            _drawing = null;
        }

        _grab = Grab.None;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Marking)
        {
            RemoveSelectedMark();
            e.Handled = true;
            return;
        }

        if (!Marking && e.Key is Key.Home or Key.End)
        {
            EndIsActive = e.Key == Key.End;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 0.1;
        var delta = e.Key switch
        {
            Key.Left => -step,
            Key.Right => step,
            _ => 0,
        };
        if (delta == 0)
        {
            return;
        }

        if (!Marking)
        {
            MoveHandle((EndIsActive ? End : Start) + delta);
        }
        else if (SelectedMark >= 0)
        {
            var m = Marks[SelectedMark];
            var start = Math.Clamp(m.Start + delta, 0, Duration - m.Length);
            Marks[SelectedMark] = new TimeRange(start, start + m.Length);
            InvalidateVisual();
        }

        Changed?.Invoke();
        e.Handled = true;
    }

    private void MoveHandle(double t)
    {
        if (EndIsActive)
        {
            End = Math.Clamp(t, Start + MinLength, Duration);
            HandleMoved?.Invoke(End);
        }
        else
        {
            Start = Math.Clamp(t, 0, End - MinLength);
            HandleMoved?.Invoke(Start);
        }

        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>"1:05.3", or "1:02:05.3" for long files.</summary>
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : t.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }
}

/// <summary>Picks which video frame an editor shows: a slider, the time, and frame extraction in the background.</summary>
internal sealed class FrameScrubber
{
    private readonly MediaPreview _media;
    private readonly string _path;
    private readonly string _frameFile;
    private readonly ImageStage _stage;
    private readonly CancellationToken _cancel;
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBlock _time = new() { MinWidth = 64, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private bool _busy;
    private double? _pending;

    public FrameScrubber(MediaPreview media, string path, string temp, ImageStage stage, double duration, CancellationToken cancel)
    {
        _media = media;
        _path = path;
        _frameFile = System.IO.Path.Combine(temp, "frame.png");
        _stage = stage;
        _cancel = cancel;
        Slider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(0.1, duration - 0.05),
            SmallChange = 0.1,
            LargeChange = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(Slider, L.Get("Editor_Frame"));
        Slider.ValueChanged += (_, _) =>
        {
            _time.Text = RangeBar.Format(Slider.Value);
            _delay.Stop();
            _delay.Start();
        };
        _delay.Tick += async (_, _) =>
        {
            _delay.Stop();
            await ShowAsync(Slider.Value);
        };

        var dock = new DockPanel { Margin = new Thickness(12, 8, 12, 0) };
        var label = new TextBlock { Text = L.Get("Editor_Frame"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(_time, Dock.Right);
        dock.Children.Add(label);
        dock.Children.Add(_time);
        dock.Children.Add(Slider);
        Panel = dock;
    }

    public Slider Slider { get; }

    public FrameworkElement Panel { get; }

    /// <summary>Extracts and shows the frame at <paramref name="seconds"/>; only the latest request runs.</summary>
    public async Task ShowAsync(double seconds)
    {
        if (_busy)
        {
            _pending = seconds;
            return;
        }

        _busy = true;
        try
        {
            await _media.FrameAsync(_path, seconds, 1600, _frameFile, _cancel);
            _stage.Replace(ImageStage.LoadBitmap(_frameFile));
            _time.Text = RangeBar.Format(seconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Couldn't show the frame at {seconds}s.", ex);
        }
        finally
        {
            _busy = false;
        }

        if (_pending is { } next)
        {
            _pending = null;
            await ShowAsync(next);
        }
    }
}
