using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// Annotate (spec §7.3): arrow, rectangle, highlighter, pen and text in six colors and three sizes. What's drawn
/// is saved as a transparent layer the size of the picture; the image tool puts it on top.
/// </summary>
internal sealed class AnnotateEditor(string path) : EditorWindow(L.Get("Tool_Annotate"), path)
{
    private static readonly (string Key, Color Color)[] Colors =
    [
        ("Color_Red", Color.FromRgb(0xE8, 0x11, 0x23)),
        ("Color_Yellow", Color.FromRgb(0xFF, 0xC8, 0x00)),
        ("Color_Green", Color.FromRgb(0x10, 0x89, 0x3E)),
        ("Color_Blue", Color.FromRgb(0x00, 0x78, 0xD4)),
        ("Color_Black", Color.FromRgb(0x10, 0x10, 0x10)),
        ("Color_White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    private readonly ImageStage _stage = new();
    private readonly List<UIElement> _drawn = [];
    private readonly Stack<UIElement> _redo = new();
    private Tool _tool = Tool.Arrow;
    private Color _color = Colors[0].Color;
    private double _sizeFactor = 2;
    private double _base = 2;
    private UIElement? _current;
    private Point _start;
    private TextBox? _typing;

    private enum Tool
    {
        Arrow,
        Rectangle,
        Highlighter,
        Pen,
        Text,
    }

    private double Thickness => _base * _sizeFactor;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var preview = await Task.Run(() => ImagePreview.Create(SourcePath, 2400, System.IO.Path.Combine(Temp, "preview.png")), cancellationToken);
        _stage.Show(ImageStage.LoadBitmap(preview.PreviewPath), preview.Width, preview.Height);
        _base = Math.Max(2, Math.Max(preview.Width, preview.Height) / 400.0);

        Toolbar.Children.Add(Choice<Tool>(
            [
                ("", L.Get("Annotate_Arrow"), Tool.Arrow),
                ("", L.Get("Annotate_Rectangle"), Tool.Rectangle),
                ("", L.Get("Annotate_Highlighter"), Tool.Highlighter),
                ("", L.Get("Annotate_Pen"), Tool.Pen),
                ("", L.Get("Annotate_Text"), Tool.Text),
            ],
            Tool.Arrow,
            tool =>
            {
                CommitText();
                _tool = tool;
                _stage.Overlay.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            }));

        Toolbar.Children.Add(Swatches());
        Toolbar.Children.Add(Choice<double>(
            [("", L.Get("Annotate_Small"), 1), ("", L.Get("Annotate_Medium"), 2), ("", L.Get("Annotate_Large"), 4)],
            2,
            size => _sizeFactor = size));
        var undo = IconButton("", "", (_, _) => Undo(), $"{L.Get("Editor_Undo")} (Ctrl+Z)");
        var redo = IconButton("", "", (_, _) => Redo(), $"{L.Get("Editor_Redo")} (Ctrl+Y)");
        AutomationName(undo, L.Get("Editor_Undo"));
        AutomationName(redo, L.Get("Editor_Redo"));
        Toolbar.Children.Add(undo);
        Toolbar.Children.Add(redo);

        _stage.Overlay.Cursor = Cursors.Cross;
        _stage.Overlay.MouseLeftButtonDown += OnDown;
        _stage.Overlay.MouseMove += OnMove;
        _stage.Overlay.MouseLeftButtonUp += OnUp;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control && _typing is null)
            {
                Redo();
                e.Handled = true;
            }
        };

        Body = _stage;
        Hint = L.Get("Annotate_Hint");
        _stage.Overlay.Focus();
    }

    /// <summary>
    /// Color buttons. The chosen one gets a ring in the text color rather than the accent fill, which would hide
    /// a swatch of the same color as the accent.
    /// </summary>
    private StackPanel Swatches()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
        var rings = new List<Border>();
        foreach (var (key, color) in Colors)
        {
            var ring = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Child = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                },
            };
            ring.SetResourceReference(Border.BorderBrushProperty, SystemColors.WindowTextBrushKey);
            ring.BorderThickness = new Thickness(color == _color ? 2 : 0);
            rings.Add(ring);

            var button = new Button { Content = ring, Padding = new Thickness(4), Margin = new Thickness(0, 0, 2, 0), ToolTip = L.Get(key) };
            AutomationName(button, L.Get(key));
            button.Click += (_, _) =>
            {
                _color = color;
                foreach (var r in rings)
                {
                    r.BorderThickness = new Thickness(ReferenceEquals(r, ring) ? 2 : 0);
                }
            };
            panel.Children.Add(button);
        }

        return panel;
    }

    private Brush Fill(byte alpha = 0xFF) => new SolidColorBrush(Color.FromArgb(alpha, _color.R, _color.G, _color.B));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_stage.Overlay);
        if (_tool == Tool.Text)
        {
            e.Handled = true;
            if (_typing is not null)
            {
                CommitText();
                return;
            }

            StartText(p);
            return;
        }

        CommitText();
        _start = p;
        _current = _tool switch
        {
            Tool.Pen => new Polyline
            {
                Stroke = Fill(),
                StrokeThickness = Thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = [p],
            },
            Tool.Rectangle => new Rectangle { Stroke = Fill(), StrokeThickness = Thickness },
            Tool.Highlighter => new Rectangle { Fill = Fill(0x66) },
            _ => new ShapePath { Stroke = Fill(), Fill = Fill(), StrokeThickness = Thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round },
        };
        _current.IsHitTestVisible = false;
        _stage.Overlay.Children.Add(_current);
        _stage.Overlay.CaptureMouse();
        Update(p);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_current is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            Update(e.GetPosition(_stage.Overlay));
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        _stage.Overlay.ReleaseMouseCapture();
        var end = e.GetPosition(_stage.Overlay);
        if ((end - _start).Length < Thickness && _current is not Polyline)
        {
            _stage.Overlay.Children.Remove(_current); // a click, not a drag
        }
        else
        {
            Keep(_current);
        }

        _current = null;
    }

    private void Keep(UIElement element)
    {
        _drawn.Add(element);
        _redo.Clear();
    }

    private void Update(Point p)
    {
        switch (_current)
        {
            case Polyline line:
                line.Points.Add(p);
                break;
            case Rectangle rect:
                Canvas.SetLeft(rect, Math.Min(_start.X, p.X));
                Canvas.SetTop(rect, Math.Min(_start.Y, p.Y));
                rect.Width = Math.Abs(p.X - _start.X);
                rect.Height = Math.Abs(p.Y - _start.Y);
                break;
            case ShapePath arrow:
                arrow.Data = ArrowGeometry(_start, p, Thickness);
                break;
        }
    }

    /// <summary>A line with a filled head at <paramref name="to"/>, sized to the stroke.</summary>
    private static Geometry ArrowGeometry(Point from, Point to, double thickness)
    {
        var direction = to - from;
        var length = direction.Length;
        var group = new GeometryGroup();
        if (length < 0.5)
        {
            return group;
        }

        direction /= length;
        var headLength = Math.Min(length * 0.6, thickness * 5);
        var headWidth = thickness * 3.2;
        var normal = new Vector(-direction.Y, direction.X);
        var baseCenter = to - (direction * headLength);
        group.Children.Add(new LineGeometry(from, baseCenter + (direction * (thickness / 2))));
        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(to, isFilled: true, isClosed: true);
            ctx.LineTo(baseCenter + (normal * (headWidth / 2)), isStroked: true, isSmoothJoin: true);
            ctx.LineTo(baseCenter - (normal * (headWidth / 2)), isStroked: true, isSmoothJoin: true);
        }

        group.Children.Add(head);
        return group;
    }

    private void StartText(Point p)
    {
        _typing = new TextBox
        {
            FontSize = Thickness * 7,
            FontWeight = FontWeights.SemiBold,
            Foreground = Fill(),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1 / _stage.Scale),
            MinWidth = Thickness * 20,
            AcceptsReturn = true,
            Padding = new Thickness(0),
        };
        Canvas.SetLeft(_typing, p.X);
        Canvas.SetTop(_typing, p.Y - (_typing.FontSize * 0.6));
        _typing.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift))
            {
                e.Handled = true;
                CommitText();
                _stage.Overlay.Focus();
            }
        };
        _typing.LostKeyboardFocus += (_, _) => CommitText();
        _stage.Overlay.Children.Add(_typing);
        _typing.Focus();
    }

    /// <summary>Turns the text being typed into a fixed label (or drops it if empty).</summary>
    private void CommitText()
    {
        if (_typing is not { } box)
        {
            return;
        }

        _typing = null;
        _stage.Overlay.Children.Remove(box);
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var label = new TextBlock
        {
            Text = box.Text,
            FontSize = box.FontSize,
            FontWeight = box.FontWeight,
            Foreground = box.Foreground,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, Canvas.GetLeft(box));
        Canvas.SetTop(label, Canvas.GetTop(box));
        _stage.Overlay.Children.Add(label);
        Keep(label);
    }

    protected override void Undo()
    {
        CommitText();
        if (_drawn.Count == 0)
        {
            return;
        }

        var last = _drawn[^1];
        _drawn.RemoveAt(_drawn.Count - 1);
        _stage.Overlay.Children.Remove(last);
        _redo.Push(last);
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var element = _redo.Pop();
        _stage.Overlay.Children.Add(element);
        _drawn.Add(element);
    }

    protected override ToolEdit? BuildEdit()
    {
        CommitText();
        if (_drawn.Count == 0)
        {
            Hint = L.Get("Annotate_NothingYet");
            return null;
        }

        return new AnnotateEdit(RenderOverlay());
    }

    /// <summary>
    /// Draws the annotations alone, at the picture's full size, into a transparent PNG. It lives outside the
    /// editor's temporary folder because the tool reads it after this window has closed.
    /// </summary>
    private string RenderOverlay()
    {
        var w = _stage.PixelWidth;
        var h = _stage.PixelHeight;
        _stage.Overlay.UpdateLayout(); // anything drawn since the last layout pass must have its size first
        var brush = new VisualBrush(_stage.Overlay)
        {
            Viewbox = new Rect(0, 0, w, h),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var folder = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Pomegranate")).FullName;
        var file = System.IO.Path.Combine(folder, $"annotations-{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
        return file;
    }
}
