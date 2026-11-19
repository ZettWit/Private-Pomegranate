using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Wheel;

namespace Pomegranate.App.Wheel;

/// <summary>The user picked something on the wheel.</summary>
internal sealed record WheelChoice(WheelKind Kind, WheelItem Item, IReadOnlyList<string> Paths);

/// <summary>
/// The wheel overlay (spec §5, §12.2). One window is created at startup and reused.
/// <para>
/// Drag mode: the window appears under the cursor without taking focus and becomes the drop target
/// for the drag in progress. The wheel is drawn only once <c>DragEnter</c> confirms files are being
/// dragged. A drop is always reported as Copy, so Explorer never deletes the source.
/// </para>
/// <para>Click mode (hotkey, right-click menu): the window takes focus and works with mouse and keyboard.</para>
/// </summary>
internal sealed class WheelWindow : Window
{
    private static readonly WheelMetrics Metrics = new(CenterRadius: 44, InnerRadius: 132, OuterRadius: 204);
    private const double RingMargin = 14;
    private const double Size = 2 * (204 + RingMargin);

    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly Func<WheelKind, IReadOnlyList<string>, WheelContent> _contentFor;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size, IsHitTestVisible = false };
    private readonly Grid _root = new();
    private readonly TextBlock _announcer = new() { Opacity = 0, Width = 1, Height = 1 };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _enterTimeout = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly Point _center = new(Size / 2, Size / 2);

    private WheelState<WheelItem>? _state;
    private WheelContent? _content;
    private IReadOnlyList<string> _paths = [];
    private WheelKind _kind;
    private bool _clickMode;
    private bool _isOpen;
    private Palette _palette = Palette.Current();

    // Visuals that change on hover.
    private readonly List<Path> _innerSlices = [];
    private readonly List<Path> _outerSlices = [];
    private readonly List<TextBlock[]> _innerLabels = [];
    private readonly List<TextBlock[]> _outerLabels = [];
    private Ellipse? _centerDisc;
    private TextBlock? _centerGlyph;
    private Canvas? _outerLayer;

    public WheelWindow(Func<WheelKind, IReadOnlyList<string>, WheelContent> contentFor)
    {
        _contentFor = contentFor;

        // An empty explicit style keeps the app's Fluent window style (which uses Mica) off this layered window.
        Style = new Style(typeof(Window));
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = Size;
        Height = Size;
        Title = "Pomegranate";

        // Alpha 1/255: invisible, but still hit-testable, so the whole square is one drop target.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        AllowDrop = true;
        FontFamily = TextFont;

        _root.Children.Add(_canvas);
        _root.Children.Add(_announcer);
        AutomationProperties.SetLiveSetting(_announcer, AutomationLiveSetting.Assertive);
        Content = _root;

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Deactivated += (_, _) =>
        {
            if (_clickMode && _isOpen)
            {
                Close(raise: true);
            }
        };

        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            Close(raise: true);
        };
        _enterTimeout.Tick += (_, _) =>
        {
            // No OLE drag arrived: the user was dragging a window or drawing a selection box.
            _enterTimeout.Stop();
            if (_content is null)
            {
                Close(raise: true);
            }
        };
    }

    public event EventHandler<WheelChoice>? Chosen;

    /// <summary>The wheel went away without a choice (or after one).</summary>
    public event EventHandler? Dismissed;

    public bool IsOpen => _isOpen;

    public void Prepare()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetNoActivate(handle, true);
    }

    /// <summary>Drag mode. Paths arrive with DragEnter.</summary>
    public void OpenForDrag(WheelKind kind)
    {
        _clickMode = false;
        _kind = kind;
        _paths = [];
        _content = null;
        _canvas.Children.Clear();
        _palette = Palette.Current();
        PlaceAtCursor(activate: false);
        _isOpen = true;
        _enterTimeout.Start();
        Nudge();
    }

    /// <summary>Click mode, for files picked by the hotkey or the right-click menu.</summary>
    public void OpenForClick(WheelKind kind, IReadOnlyList<string> paths)
    {
        _clickMode = true;
        _kind = kind;
        _paths = paths;
        _palette = Palette.Current();
        PlaceAtCursor(activate: true);
        _isOpen = true;
        ShowContent(kind);
        Activate();
        Focus();
    }

    /// <summary>The user pressed another activation key mid-drag.</summary>
    public void SwitchTo(WheelKind kind)
    {
        _kind = kind;
        if (_content is not null)
        {
            ShowContent(kind);
        }
    }

    public void Close(bool raise)
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        _leaveTimer.Stop();
        _enterTimeout.Stop();
        _content = null;
        _state = null;
        Hide();
        if (raise)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Drag mode ----------------------------------------------------------------------------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _leaveTimer.Stop();
        e.Handled = true;
        if (_content is not null)
        {
            UpdateFromDrag(e);
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _enterTimeout.Stop();
            _paths = files;
            ShowContent(_kind);
            UpdateFromDrag(e);
            return;
        }

        if (e.Data.GetDataPresent("FileGroupDescriptorW"))
        {
            // Outlook attachments and some browser downloads: files that don't exist on disk yet.
            _enterTimeout.Stop();
            _paths = [];
            Render(new WheelContent(_kind, Title, [], L.Get("Wheel_VirtualFiles")));
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.None;
        Close(raise: true);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        _leaveTimer.Stop();
        e.Handled = true;
        UpdateFromDrag(e);
    }

    private void UpdateFromDrag(DragEventArgs e)
    {
        if (_state is null || _paths.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var p = e.GetPosition(_canvas);
        if (_state.Update(p.X - _center.X, p.Y - _center.Y))
        {
            RefreshHighlight();
        }

        // Never Move: the source must keep its file whatever the user picks.
        e.Effects = _state.HasSelection && (e.AllowedEffects & DragDropEffects.Copy) != 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // Moving between elements inside the window can raise a leave and an enter back-to-back.
        _leaveTimer.Start();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var item = _state?.SelectedItem;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [.. _paths];
        e.Effects = item is null ? DragDropEffects.None : DragDropEffects.Copy;
        var kind = _kind;
        Close(raise: true);

        if (item is not null)
        {
            // Let the drag finish before doing the work.
            Dispatcher.BeginInvoke(() => Chosen?.Invoke(this, new WheelChoice(kind, item, paths)));
        }
    }

    // ---- Click mode ---------------------------------------------------------------------------

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_clickMode || _state is null)
        {
            return;
        }

        var p = e.GetPosition(_canvas);
        if (_state.Update(p.X - _center.X, p.Y - _center.Y))
        {
            RefreshHighlight();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_clickMode || _state is null)
        {
            return;
        }

        var p = e.GetPosition(_canvas);
        _state.Update(p.X - _center.X, p.Y - _center.Y);
        if (_state.HasSelection)
        {
            Choose();
        }
        else if (_state.Hit.Zone is WheelZone.Center or WheelZone.Outside)
        {
            Close(raise: true);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_clickMode || _state is null)
        {
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                Close(raise: true);
                return;
            case Key.Right:
                _state.Step(1);
                break;
            case Key.Left:
                _state.Step(-1);
                break;
            case Key.Up:
            case Key.Down:
                _state.ToggleRing();
                break;
            case Key.Enter:
            case Key.Space:
                if (_state.HasSelection)
                {
                    Choose();
                }

                return;
            case Key.Tab:
                var next = (int)_kind + ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 2 : 1);
                _kind = (WheelKind)(next % 3);
                ShowContent(_kind);
                return;
            default:
                e.Handled = false;
                return;
        }

        RefreshHighlight();
    }

    private void Choose()
    {
        var item = _state?.SelectedItem;
        var paths = _paths;
        var kind = _kind;
        Close(raise: true);
        if (item is not null)
        {
            Chosen?.Invoke(this, new WheelChoice(kind, item, paths));
        }
    }

    // ---- Drawing --------------------------------------------------------------------------------

    private void ShowContent(WheelKind kind)
    {
        Render(_contentFor(kind, _paths));
    }

    private void Render(WheelContent content)
    {
        _content = content;
        var layout = WheelLayout<WheelItem>.Create(content.Items, item => item.Children);
        _state = new WheelState<WheelItem>(layout, Metrics);

        _canvas.Children.Clear();
        _innerSlices.Clear();
        _innerLabels.Clear();

        // Ring background with a soft shadow.
        var ring = new Path
        {
            Data = Ring(Metrics.CenterRadius - 2, Metrics.InnerRadius),
            Fill = _palette.Panel,
            Stroke = _palette.Border,
            StrokeThickness = 1,
            Effect = _palette.HighContrast ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Direction = 270 },
        };
        _canvas.Children.Add(ring);

        var count = layout.Inner.Count;
        for (var i = 0; i < count; i++)
        {
            var width = WheelGeometry.InnerSlotWidth(count);
            var angle = WheelGeometry.InnerSlotAngle(i, count);
            var slice = new Path
            {
                Data = Sector(Metrics.CenterRadius + 3, Metrics.InnerRadius - 3, angle - width / 2 + Gap(count), angle + width / 2 - Gap(count)),
                Fill = Brushes.Transparent,
            };
            _innerSlices.Add(slice);
            _canvas.Children.Add(slice);

            var slot = layout.Inner[i];
            var label = slot.IsMore ? L.Get("Wheel_More") : slot.Item!.Label;
            var glyph = slot.IsMore ? Glyphs.More : slot.Item!.Glyph;
            _innerLabels.Add(AddLabel(glyph, label, (Metrics.CenterRadius + Metrics.InnerRadius) / 2, angle, 92));

            if (slot.HasChildren && !slot.IsMore)
            {
                // A small chevron on the rim says "more behind this one".
                var tip = Point(Metrics.InnerRadius - 9, angle);
                var chevron = Glyph("", 10, _palette.SubtleText);
                chevron.RenderTransform = new RotateTransform(angle * 180 / Math.PI - 90, 5, 5);
                Canvas.SetLeft(chevron, tip.X - 5);
                Canvas.SetTop(chevron, tip.Y - 7);
                _canvas.Children.Add(chevron);
            }
        }

        if (count == 0 && content.Message is not null)
        {
            var message = new TextBlock
            {
                Text = content.Message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Width = 150,
                FontSize = 13,
                Foreground = _palette.Text,
            };
            message.Measure(new System.Windows.Size(150, double.PositiveInfinity));
            Canvas.SetLeft(message, _center.X - 75);
            Canvas.SetTop(message, _center.Y + Metrics.CenterRadius + ((Metrics.InnerRadius - Metrics.CenterRadius - message.DesiredSize.Height) / 2) - 4);
            _canvas.Children.Add(message);
        }

        // Center: the wheel's name, or a cancel mark while the cursor is on it.
        _centerDisc = new Ellipse
        {
            Width = 2 * (Metrics.CenterRadius - 4),
            Height = 2 * (Metrics.CenterRadius - 4),
            Fill = _palette.CenterFill,
        };
        Canvas.SetLeft(_centerDisc, _center.X - Metrics.CenterRadius + 4);
        Canvas.SetTop(_centerDisc, _center.Y - Metrics.CenterRadius + 4);
        _canvas.Children.Add(_centerDisc);

        var title = new TextBlock { Text = content.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Text };
        title.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(title, _center.X - title.DesiredSize.Width / 2);
        Canvas.SetTop(title, _center.Y - title.DesiredSize.Height / 2 + 8);
        _canvas.Children.Add(title);

        _centerGlyph = Glyph(Glyphs.Cancel, 12, _palette.SubtleText);
        Canvas.SetLeft(_centerGlyph, _center.X - 6);
        Canvas.SetTop(_centerGlyph, _center.Y - 18);
        _canvas.Children.Add(_centerGlyph);

        _outerLayer = new Canvas { Width = Size, Height = Size };
        _canvas.Children.Add(_outerLayer);

        AutomationProperties.SetName(this, content.Title);
        Animate();
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        if (_state is null)
        {
            return;
        }

        for (var i = 0; i < _innerSlices.Count; i++)
        {
            var hot = _state.Hit.Zone == WheelZone.Inner && _state.Hit.Index == i;
            var open = _state.OpenIndex == i;
            _innerSlices[i].Fill = hot ? _palette.Accent : open ? _palette.AccentSoft : Brushes.Transparent;
            foreach (var text in _innerLabels[i])
            {
                text.Foreground = hot ? _palette.OnAccent : _palette.Text;
            }
        }

        var cancelHot = _state.Hit.Zone == WheelZone.Center;
        if (_centerGlyph is not null)
        {
            _centerGlyph.Foreground = cancelHot ? _palette.Text : _palette.SubtleText;
        }

        if (_centerDisc is not null)
        {
            _centerDisc.Fill = cancelHot ? _palette.CenterHot : _palette.CenterFill;
        }

        DrawOuterRing();
        Announce();
    }

    private void DrawOuterRing()
    {
        if (_outerLayer is null || _state is null)
        {
            return;
        }

        _outerLayer.Children.Clear();
        _outerSlices.Clear();
        _outerLabels.Clear();
        var items = _state.OuterItems;
        if (items.Count == 0)
        {
            return;
        }

        var innerCount = _state.Layout.Inner.Count;
        var width = WheelGeometry.OuterSlotWidth(innerCount, items.Count);
        var r0 = Metrics.InnerRadius + 6;
        var r1 = Metrics.OuterRadius;
        var first = WheelGeometry.OuterSlotAngle(0, innerCount, items.Count, _state.OpenIndex) - width / 2;
        var last = WheelGeometry.OuterSlotAngle(items.Count - 1, innerCount, items.Count, _state.OpenIndex) + width / 2;
        if (last < first)
        {
            last += 2 * Math.PI;
        }

        _outerLayer.Children.Add(new Path
        {
            Data = Sector(r0, r1, first, last),
            Fill = _palette.Panel,
            Stroke = _palette.Border,
            StrokeThickness = 1,
        });

        for (var j = 0; j < items.Count; j++)
        {
            var angle = WheelGeometry.OuterSlotAngle(j, innerCount, items.Count, _state.OpenIndex);
            var hot = _state.Hit.Zone == WheelZone.Outer && _state.Hit.Index == j;
            var slice = new Path
            {
                Data = Sector(r0 + 3, r1 - 3, angle - width / 2 + Gap(items.Count), angle + width / 2 - Gap(items.Count)),
                Fill = hot ? _palette.Accent : Brushes.Transparent,
            };
            _outerSlices.Add(slice);
            _outerLayer.Children.Add(slice);
            var labels = AddLabel(items[j].Glyph, items[j].Label, (r0 + r1) / 2, angle, 70, _outerLayer);
            foreach (var text in labels)
            {
                text.Foreground = hot ? _palette.OnAccent : _palette.Text;
            }

            _outerLabels.Add(labels);
        }
    }

    private void Announce()
    {
        var text = _state?.SelectedItem is { } item ? item.Tooltip ?? item.Label : _content?.Message ?? _content?.Title ?? "";
        if (_announcer.Text == text)
        {
            return;
        }

        _announcer.Text = text;
        UIElementAutomationPeer.FromElement(_announcer)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private TextBlock[] AddLabel(string glyph, string label, double radius, double angle, double maxWidth, Canvas? into = null)
    {
        var target = into ?? _canvas;
        var center = Point(radius, angle);
        var icon = Glyph(glyph, 16, _palette.Text);
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = _palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = maxWidth,
            TextAlignment = TextAlignment.Center,
        };
        icon.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        text.Measure(new System.Windows.Size(maxWidth, double.PositiveInfinity));
        Canvas.SetLeft(icon, center.X - icon.DesiredSize.Width / 2);
        Canvas.SetTop(icon, center.Y - icon.DesiredSize.Height - 1);
        Canvas.SetLeft(text, center.X - text.DesiredSize.Width / 2);
        Canvas.SetTop(text, center.Y + 1);
        target.Children.Add(icon);
        target.Children.Add(text);
        return [icon, text];
    }

    private static TextBlock Glyph(string glyph, double size, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        Foreground = brush,
    };

    private void Animate()
    {
        if (!SystemTheme.AnimationsEnabled)
        {
            _canvas.Opacity = 1;
            _canvas.RenderTransform = Transform.Identity;
            return;
        }

        var scale = new ScaleTransform(0.88, 0.88, _center.X, _center.Y);
        _canvas.RenderTransform = scale;
        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, duration) { EasingFunction = ease });
        _canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private static double Gap(int count) => count <= 1 ? 0 : 0.012;

    private Point Point(double radius, double angle) =>
        new(_center.X + radius * Math.Sin(angle), _center.Y - radius * Math.Cos(angle));

    private Geometry Ring(double r0, double r1) => new CombinedGeometry(
        GeometryCombineMode.Exclude,
        new EllipseGeometry(_center, r1, r1),
        new EllipseGeometry(_center, r0, r0));

    /// <summary>A ring sector between radii r0..r1 and angles a0..a1 (radians, 0 at the top, clockwise).</summary>
    private Geometry Sector(double r0, double r1, double a0, double a1)
    {
        if (a1 - a0 >= 2 * Math.PI - 1e-6)
        {
            return Ring(r0, r1);
        }

        var large = a1 - a0 > Math.PI;
        var figure = new PathFigure { StartPoint = Point(r1, a0), IsClosed = true };
        figure.Segments.Add(new ArcSegment(Point(r1, a1), new System.Windows.Size(r1, r1), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(Point(r0, a1), true));
        figure.Segments.Add(new ArcSegment(Point(r0, a0), new System.Windows.Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    // ---- Placement ------------------------------------------------------------------------------

    private void PlaceAtCursor(bool activate)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetNoActivate(handle, !activate);
        ShowActivated = activate;

        Native.GetCursorPos(out var cursor);
        var monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        var scale = DpiScale(monitor);
        var px = (int)Math.Round(Size * scale);

        // Keep the whole wheel on screen; near an edge the wheel shifts and the cursor is off-center.
        var left = Math.Clamp(cursor.X - px / 2, info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - px));
        var top = Math.Clamp(cursor.Y - px / 2, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - px));

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, px, px, Native.SWP_NOACTIVATE);
        Show();
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, px, px, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private static double DpiScale(IntPtr monitor)
    {
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private static void SetNoActivate(IntPtr handle, bool noActivate)
    {
        var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
        style |= Native.WS_EX_TOOLWINDOW;
        style = noActivate ? style | Native.WS_EX_NOACTIVATE : style & ~(long)Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>A one-pixel wiggle so Windows' drag loop notices the new window under the cursor right away.</summary>
    private static void Nudge()
    {
        var inputs = new[]
        {
            new Native.INPUT { type = Native.INPUT_MOUSE, mi = new Native.MOUSEINPUT { dx = 1, dy = 0, dwFlags = Native.MOUSEEVENTF_MOVE } },
            new Native.INPUT { type = Native.INPUT_MOUSE, mi = new Native.MOUSEINPUT { dx = -1, dy = 0, dwFlags = Native.MOUSEEVENTF_MOVE } },
        };
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    /// <summary>Brushes for the current theme (spec §5: light/dark, accent color, high contrast).</summary>
    private sealed record Palette(
        Brush Panel, Brush Border, Brush Text, Brush SubtleText, Brush Accent, Brush AccentSoft, Brush OnAccent,
        Brush CenterFill, Brush CenterHot, bool HighContrast)
    {
        public static Palette Current()
        {
            if (SystemTheme.HighContrast)
            {
                return new(
                    SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush,
                    SystemColors.GrayTextBrush, SystemColors.HighlightBrush, SystemColors.ControlBrush,
                    SystemColors.HighlightTextBrush, SystemColors.WindowBrush, SystemColors.ControlBrush, true);
            }

            var accent = SystemTheme.Accent;
            var dark = SystemTheme.IsDark;
            return new(
                Panel: Frozen(dark ? Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF5, 0xF9, 0xF9, 0xF9)),
                Border: Frozen(dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00)),
                Text: Frozen(dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A)),
                SubtleText: Frozen(dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                Accent: Frozen(accent),
                AccentSoft: Frozen(Color.FromArgb(0x40, accent.R, accent.G, accent.B)),
                OnAccent: Frozen(SystemTheme.TextOn(accent)),
                CenterFill: Frozen(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xEE, 0xEE, 0xEE)),
                CenterHot: Frozen(dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xDD, 0xDD, 0xDD)),
                HighContrast: false);
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
