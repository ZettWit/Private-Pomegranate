using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Pomegranate.Core.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// Rectangles on an <see cref="ImageStage"/>: one crop frame (with the outside dimmed and an optional aspect
/// ratio) or any number of redaction boxes. Drag inside to move, drag a handle to resize, drag elsewhere to draw
/// a new one. Arrow keys nudge the selected box (Shift for bigger steps); Delete removes it.
/// </summary>
internal sealed class BoxLayer
{
    private const double HandleScreenSize = 12;
    private const double MinSize = 4;

    private readonly ImageStage _stage;
    private readonly bool _single;
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), IsHitTestVisible = false };
    private readonly List<Rectangle> _handles = [];
    private readonly List<Line> _thirds = [];
    private readonly Brush _accent;

    private Drag _drag;
    private Point _dragStart;
    private Rect _dragOriginal;

    public BoxLayer(ImageStage stage, bool single)
    {
        _stage = stage;
        _single = single;
        _accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(SystemParameters.WindowGlassColor);
        if (_single)
        {
            stage.Overlay.Children.Add(_shade);
            for (var i = 0; i < 4; i++)
            {
                var line = new Line { Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
                _thirds.Add(line);
                stage.Overlay.Children.Add(line);
            }
        }

        stage.Overlay.MouseLeftButtonDown += OnDown;
        stage.Overlay.MouseMove += OnMove;
        stage.Overlay.MouseLeftButtonUp += OnUp;
        stage.Overlay.KeyDown += OnKey;
        stage.SizeChanged += (_, _) => Render();
    }

    private enum Drag
    {
        None,
        Draw,
        Move,
        Left,
        Top,
        Right,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public event Action? Changed;

    public List<Box> Boxes { get; } = [];

    public Box? Selected { get; private set; }

    /// <summary>Width / height to keep while resizing, or null for free.</summary>
    public double? Aspect { get; set; }

    /// <summary>The style new boxes get (redaction).</summary>
    public RedactStyle NewStyle { get; set; } = RedactStyle.Black;

    private Rect Bounds => new(0, 0, _stage.PixelWidth, _stage.PixelHeight);

    public Box Add(Rect rect, RedactStyle style = RedactStyle.Black)
    {
        var box = new Box(rect, style, new Rectangle { StrokeThickness = 1 });
        Boxes.Add(box);
        _stage.Overlay.Children.Add(box.Visual);
        Select(box);
        return box;
    }

    public void Remove(Box box)
    {
        Boxes.Remove(box);
        _stage.Overlay.Children.Remove(box.Visual);
        if (Selected == box)
        {
            Selected = Boxes.LastOrDefault();
        }

        Render();
        Changed?.Invoke();
    }

    public void Select(Box? box)
    {
        Selected = box;
        Render();
    }

    public void SetStyle(Box box, RedactStyle style)
    {
        box.Style = style;
        Render();
        Changed?.Invoke();
    }

    /// <summary>Fits the largest rectangle of <paramref name="aspect"/> inside the current one, centered.</summary>
    public void ApplyAspect(double? aspect)
    {
        Aspect = aspect;
        if (aspect is not { } ratio || Selected is null)
        {
            Render();
            return;
        }

        var r = Selected.Rect;
        var width = r.Width;
        var height = width / ratio;
        if (height > r.Height)
        {
            height = r.Height;
            width = height * ratio;
        }

        Selected.Rect = new Rect(r.X + ((r.Width - width) / 2), r.Y + ((r.Height - height) / 2), width, height);
        Render();
        Changed?.Invoke();
    }

    public void Render()
    {
        var scale = _stage.Scale;
        var handle = HandleScreenSize / scale;
        var stroke = 1.5 / scale;

        foreach (var box in Boxes)
        {
            Place(box.Visual, box.Rect);
            box.Visual.StrokeThickness = (box == Selected ? 2.5 : 1.5) / scale;
            box.Visual.Stroke = box == Selected ? _accent : Brushes.White;
            box.Visual.Fill = _single ? Brushes.Transparent : FillFor(box.Style);
        }

        if (_single && Selected is { } crop)
        {
            var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(Bounds), new RectangleGeometry(crop.Rect));
            _shade.Data = geometry;
            var r = crop.Rect;
            Third(_thirds[0], r.Left + (r.Width / 3), r.Top, r.Left + (r.Width / 3), r.Bottom, stroke);
            Third(_thirds[1], r.Left + (2 * r.Width / 3), r.Top, r.Left + (2 * r.Width / 3), r.Bottom, stroke);
            Third(_thirds[2], r.Left, r.Top + (r.Height / 3), r.Right, r.Top + (r.Height / 3), stroke);
            Third(_thirds[3], r.Left, r.Top + (2 * r.Height / 3), r.Right, r.Top + (2 * r.Height / 3), stroke);
        }

        foreach (var h in _handles)
        {
            _stage.Overlay.Children.Remove(h);
        }

        _handles.Clear();
        if (Selected is { } selected)
        {
            foreach (var point in HandlePoints(selected.Rect))
            {
                var rect = new Rectangle
                {
                    Width = handle,
                    Height = handle,
                    Fill = Brushes.White,
                    Stroke = _accent,
                    StrokeThickness = stroke,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(rect, point.X - (handle / 2));
                Canvas.SetTop(rect, point.Y - (handle / 2));
                _handles.Add(rect);
                _stage.Overlay.Children.Add(rect);
            }
        }
    }

    private static void Third(Line line, double x1, double y1, double x2, double y2, double thickness)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        line.StrokeThickness = thickness;
    }

    private static void Place(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
    }

    /// <summary>How each redaction style looks while editing: black, a coarse checker, or frosted.</summary>
    private static Brush FillFor(RedactStyle style) => style switch
    {
        RedactStyle.Black => Brushes.Black,
        RedactStyle.Pixelate => Checker,
        _ => new SolidColorBrush(Color.FromArgb(0xB0, 0xE8, 0xE8, 0xE8)),
    };

    private static readonly Brush Checker = CreateChecker();

    private static Brush CreateChecker()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8)), null, new RectangleGeometry(new Rect(1, 1, 1, 1))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 0.1, 0.1), ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
        brush.Freeze();
        return brush;
    }

    private static Point[] HandlePoints(Rect r) =>
    [
        r.TopLeft, new(r.Left + (r.Width / 2), r.Top), r.TopRight, new(r.Right, r.Top + (r.Height / 2)),
        r.BottomRight, new(r.Left + (r.Width / 2), r.Bottom), r.BottomLeft, new(r.Left, r.Top + (r.Height / 2)),
    ];

    private static readonly Drag[] HandleDrags =
        [Drag.TopLeft, Drag.Top, Drag.TopRight, Drag.Right, Drag.BottomRight, Drag.Bottom, Drag.BottomLeft, Drag.Left];

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _stage.Overlay.Focus();
        var p = Clamp(e.GetPosition(_stage.Overlay));
        var reach = HandleScreenSize / _stage.Scale;

        if (Selected is { } selected)
        {
            var points = HandlePoints(selected.Rect);
            for (var i = 0; i < points.Length; i++)
            {
                if ((points[i] - p).Length <= reach)
                {
                    Begin(HandleDrags[i], p, selected.Rect);
                    return;
                }
            }
        }

        var hit = Boxes.LastOrDefault(b => b.Rect.Contains(p));
        if (hit is not null)
        {
            Select(hit);
            Begin(Drag.Move, p, hit.Rect);
            return;
        }

        // Draw a new box (for a crop, a new frame replaces the old one).
        if (_single && Selected is { } frame)
        {
            frame.Rect = new Rect(p, new Size(0, 0));
            Begin(Drag.Draw, p, frame.Rect);
        }
        else if (!_single)
        {
            var box = Add(new Rect(p, new Size(0, 0)), NewStyle);
            Begin(Drag.Draw, p, box.Rect);
        }

        e.Handled = true;
    }

    private void Begin(Drag drag, Point p, Rect original)
    {
        _drag = drag;
        _dragStart = p;
        _dragOriginal = original;
        _stage.Overlay.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = Clamp(e.GetPosition(_stage.Overlay));
        if (_drag == Drag.None)
        {
            _stage.Overlay.Cursor = CursorFor(p);
            return;
        }

        if (Selected is null)
        {
            return;
        }

        var r = _dragOriginal;
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        Rect next;
        switch (_drag)
        {
            case Drag.Move:
                next = new Rect(
                    Math.Clamp(r.X + dx, 0, Bounds.Width - r.Width),
                    Math.Clamp(r.Y + dy, 0, Bounds.Height - r.Height),
                    r.Width,
                    r.Height);
                break;
            case Drag.Draw:
                next = FromCorners(_dragStart, p);
                break;
            default:
                next = Resize(r, _drag, dx, dy);
                break;
        }

        Selected.Rect = Constrain(next, _drag);
        Render();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == Drag.None)
        {
            return;
        }

        _stage.Overlay.ReleaseMouseCapture();
        var wasDrawing = _drag == Drag.Draw;
        _drag = Drag.None;
        if (Selected is { } box && (box.Rect.Width < MinSize || box.Rect.Height < MinSize))
        {
            if (_single)
            {
                box.Rect = _dragOriginal.Width >= MinSize ? _dragOriginal : Bounds; // a click, not a drag: keep the old frame
            }
            else if (wasDrawing)
            {
                Remove(box);
                return;
            }
        }

        Render();
        Changed?.Invoke();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Selected is not { } box)
        {
            return;
        }

        if (e.Key == Key.Delete && !_single)
        {
            Remove(box);
            e.Handled = true;
            return;
        }

        var step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) / _stage.Scale;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var r = box.Rect;
        box.Rect = new Rect(Math.Clamp(r.X + dx, 0, Bounds.Width - r.Width), Math.Clamp(r.Y + dy, 0, Bounds.Height - r.Height), r.Width, r.Height);
        Render();
        Changed?.Invoke();
        e.Handled = true;
    }

    private Cursor CursorFor(Point p)
    {
        if (Selected is { } selected)
        {
            var reach = HandleScreenSize / _stage.Scale;
            var points = HandlePoints(selected.Rect);
            for (var i = 0; i < points.Length; i++)
            {
                if ((points[i] - p).Length <= reach)
                {
                    return HandleDrags[i] switch
                    {
                        Drag.TopLeft or Drag.BottomRight => Cursors.SizeNWSE,
                        Drag.TopRight or Drag.BottomLeft => Cursors.SizeNESW,
                        Drag.Left or Drag.Right => Cursors.SizeWE,
                        _ => Cursors.SizeNS,
                    };
                }
            }
        }

        return Boxes.Any(b => b.Rect.Contains(p)) ? Cursors.SizeAll : Cursors.Cross;
    }

    private static Rect FromCorners(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Rect Resize(Rect r, Drag drag, double dx, double dy)
    {
        double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        if (drag is Drag.Left or Drag.TopLeft or Drag.BottomLeft)
        {
            left = Math.Min(r.Left + dx, r.Right - MinSize);
        }

        if (drag is Drag.Right or Drag.TopRight or Drag.BottomRight)
        {
            right = Math.Max(r.Right + dx, r.Left + MinSize);
        }

        if (drag is Drag.Top or Drag.TopLeft or Drag.TopRight)
        {
            top = Math.Min(r.Top + dy, r.Bottom - MinSize);
        }

        if (drag is Drag.Bottom or Drag.BottomLeft or Drag.BottomRight)
        {
            bottom = Math.Max(r.Bottom + dy, r.Top + MinSize);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Keeps the box inside the picture and, for a crop with a fixed ratio, at that ratio.</summary>
    private Rect Constrain(Rect r, Drag drag)
    {
        r.Intersect(Bounds);
        if (r.IsEmpty)
        {
            return new Rect(0, 0, 0, 0);
        }

        if (!_single || Aspect is not { } ratio || drag == Drag.Move || r.Width < 1 || r.Height < 1)
        {
            return r;
        }

        // Width leads unless only the height is being dragged.
        var width = r.Width;
        var height = width / ratio;
        if (drag is Drag.Top or Drag.Bottom)
        {
            height = r.Height;
            width = height * ratio;
        }

        var anchorRight = drag is Drag.Left or Drag.TopLeft or Drag.BottomLeft;
        var anchorBottom = drag is Drag.Top or Drag.TopLeft or Drag.TopRight || (drag == Drag.Draw && _dragStart.Y > r.Top + 0.5);
        if (drag == Drag.Draw && _dragStart.X > r.Left + 0.5)
        {
            anchorRight = true;
        }

        var x = anchorRight ? r.Right - width : r.Left;
        var y = anchorBottom ? r.Bottom - height : r.Top;
        var fitted = new Rect(x, y, width, height);
        if (!Bounds.Contains(fitted))
        {
            // Shrink to fit, keeping the anchored corner.
            var maxW = anchorRight ? r.Right : Bounds.Width - r.Left;
            var maxH = anchorBottom ? r.Bottom : Bounds.Height - r.Top;
            var scale = Math.Min(1, Math.Min(maxW / width, maxH / height));
            width *= scale;
            height *= scale;
            fitted = new Rect(anchorRight ? r.Right - width : r.Left, anchorBottom ? r.Bottom - height : r.Top, width, height);
        }

        return fitted;
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));

    /// <summary>A rectangle and how it should be hidden (redaction only).</summary>
    public sealed class Box(Rect rect, RedactStyle style, Rectangle visual)
    {
        public Rect Rect { get; set; } = rect;

        public RedactStyle Style { get; set; } = style;

        public Rectangle Visual { get; } = visual;

        public PixelRect ToPixels() => new(
            (int)Math.Round(Rect.X),
            (int)Math.Round(Rect.Y),
            Math.Max(1, (int)Math.Round(Rect.Width)),
            Math.Max(1, (int)Math.Round(Rect.Height)));
    }
}
