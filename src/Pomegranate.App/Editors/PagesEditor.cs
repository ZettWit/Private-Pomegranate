using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// Pages (spec §7.3): thumbnails of every page. Drag or Ctrl+arrows to reorder, rotate, delete. Save writes the
/// whole document; Save selection writes only the selected pages.
/// </summary>
internal sealed class PagesEditor : EditorWindow
{
    private const string DragFormat = "Pomegranate.Pages";
    private const string TileTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <StackPanel Width="164" Margin="4">
            <Border Height="164" Background="#1A808080" CornerRadius="4">
              <Image Source="{Binding Thumb}" Stretch="Uniform" Margin="8">
                <Image.LayoutTransform><RotateTransform Angle="{Binding Rotation}" /></Image.LayoutTransform>
              </Image>
            </Border>
            <TextBlock Text="{Binding Label}" HorizontalAlignment="Center" Margin="0,4,0,0" />
          </StackPanel>
        </DataTemplate>
        """;

    private readonly ObservableCollection<PageTile> _tiles = [];
    private readonly Stack<(PageTile Tile, int Rotation)[]> _undo = new();
    private readonly ListBox _list = new()
    {
        SelectionMode = SelectionMode.Extended,
        AllowDrop = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Margin = new Thickness(8, 0, 8, 0),
    };

    private (PageTile Tile, int Rotation)[] _original = [];
    private Point _pressAt;
    private PageTile? _pressed;
    private bool _deferSelect;
    private bool _selectionOnly;
    private Button? _saveSelection;

    public PagesEditor(string path)
        : base(L.Get("Tool_Pages"), path)
    {
        _list.ItemsSource = _tiles;
        _list.ItemTemplate = (DataTemplate)XamlReader.Parse(TileTemplate);
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.PreviewMouseLeftButtonDown += OnPress;
        _list.PreviewMouseMove += OnDragMove;
        _list.PreviewMouseLeftButtonUp += OnRelease;
        _list.Drop += OnDrop;
        _list.PreviewKeyDown += OnKey;
        _list.SelectionChanged += (_, _) =>
        {
            if (_saveSelection is not null)
            {
                _saveSelection.IsEnabled = _list.SelectedItems.Count > 0;
            }
        };
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var count = await PdfPreview.PageCountAsync(SourcePath);
        for (var i = 0; i < count; i++)
        {
            _tiles.Add(new PageTile(i));
        }

        _original = Snapshot();
        Toolbar.Children.Add(IconButton("", L.Get("Pages_RotateLeft"), (_, _) => Rotate(-90), L.Get("Pages_RotateLeftTip")));
        Toolbar.Children.Add(IconButton("", L.Get("Pages_RotateRight"), (_, _) => Rotate(90), L.Get("Pages_RotateRightTip")));
        Toolbar.Children.Add(IconButton("", L.Get("Pages_Delete"), (_, _) => DeleteSelected(), L.Get("Pages_DeleteTip")));
        Toolbar.Children.Add(IconButton("", L.Get("Editor_Undo"), (_, _) => Undo(), "Ctrl+Z"));
        ((StackPanel)((Button)Toolbar.Children[0]).Content).Children[0].RenderTransform = new ScaleTransform(-1, 1, 7, 7);

        _saveSelection = new Button { Content = L.Get("Pages_SaveSelection"), MinWidth = 96, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        _saveSelection.Click += (_, _) =>
        {
            _selectionOnly = true;
            TrySave();
            _selectionOnly = false;
        };
        Footer.Children.Insert(0, _saveSelection);

        Body = _list;
        Hint = L.Get("Pages_Hint");

        // Thumbnails arrive one by one while the editor is already usable.
        var folder = Path.Combine(Temp, "thumbs");
        _ = Task.Run(async () =>
        {
            try
            {
                await PdfPreview.ThumbnailsAsync(SourcePath, folder, 240, (index, file) =>
                {
                    var bitmap = ImageStage.LoadBitmap(file);
                    Dispatcher.BeginInvoke(() =>
                    {
                        foreach (var tile in _tiles.Concat(_undo.SelectMany(s => s.Select(x => x.Tile))).Where(t => t.SourceIndex == index))
                        {
                            tile.Thumb = bitmap;
                        }
                    });
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Page thumbnails for {SourcePath} failed.", ex);
            }
        }, cancellationToken);

        if (_tiles.Count > 0)
        {
            _list.SelectedIndex = 0;
            await Dispatcher.InvokeAsync(() => (_list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private PageTile[] Selected() => _tiles.Where(t => _list.SelectedItems.Contains(t)).ToArray();

    private (PageTile Tile, int Rotation)[] Snapshot() => _tiles.Select(t => (t, t.Rotation)).ToArray();

    private void Remember() => _undo.Push(Snapshot());

    protected override void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var state = _undo.Pop();
        _tiles.Clear();
        foreach (var (tile, rotation) in state)
        {
            tile.Rotation = rotation;
            _tiles.Add(tile);
        }
    }

    private void Rotate(int degrees)
    {
        var selected = Selected();
        if (selected.Length == 0)
        {
            return;
        }

        Remember();
        foreach (var tile in selected)
        {
            tile.Rotation = ((tile.Rotation + degrees) % 360 + 360) % 360;
        }
    }

    private void DeleteSelected()
    {
        var selected = Selected();
        if (selected.Length == 0)
        {
            return;
        }

        Remember();
        var next = _tiles.IndexOf(selected[0]);
        foreach (var tile in selected)
        {
            _tiles.Remove(tile);
        }

        if (_tiles.Count > 0)
        {
            _list.SelectedIndex = Math.Min(next, _tiles.Count - 1);
        }
    }

    /// <summary>Moves the selected pages, keeping their order, so they start at <paramref name="index"/>.</summary>
    private void MoveSelected(PageTile[] moving, int index)
    {
        if (moving.Length == 0)
        {
            return;
        }

        Remember();
        var before = _tiles.Take(index).Count(t => !moving.Contains(t));
        foreach (var tile in moving)
        {
            _tiles.Remove(tile);
        }

        for (var i = 0; i < moving.Length; i++)
        {
            _tiles.Insert(Math.Min(before + i, _tiles.Count), moving[i]);
        }

        _list.SelectedItems.Clear();
        foreach (var tile in moving)
        {
            _list.SelectedItems.Add(tile);
        }

        _list.ScrollIntoView(moving[0]);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var selected = Selected();
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right && selected.Length > 0)
        {
            var first = _tiles.IndexOf(selected[0]);
            var last = _tiles.IndexOf(selected[^1]);
            var target = e.Key == Key.Left ? Math.Max(0, first - 1) : Math.Min(_tiles.Count, last + 2);
            MoveSelected(selected, target);
            e.Handled = true;
        }
        else if (e.Key == Key.OemOpenBrackets)
        {
            Rotate(-90);
            e.Handled = true;
        }
        else if (e.Key == Key.OemCloseBrackets)
        {
            Rotate(90);
            e.Handled = true;
        }
    }

    // ---- Dragging pages -------------------------------------------------------------------------

    private static PageTile? TileAt(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as PageTile;
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(_list);
        _pressed = TileAt(e.OriginalSource as DependencyObject);

        // Pressing an already selected page keeps the selection, so several pages can be dragged together.
        _deferSelect = _pressed is not null && _list.SelectedItems.Contains(_pressed) && Keyboard.Modifiers == ModifierKeys.None;
        if (_deferSelect)
        {
            e.Handled = true;
            _list.Focus();
        }
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_deferSelect && _pressed is not null)
        {
            _list.SelectedItems.Clear();
            _list.SelectedItem = _pressed;
        }

        _deferSelect = false;
        _pressed = null;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is null)
        {
            return;
        }

        var delta = e.GetPosition(_list) - _pressAt;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var moving = Selected();
        if (!moving.Contains(_pressed))
        {
            moving = [_pressed];
        }

        _deferSelect = false;
        _pressed = null;
        DragDrop.DoDragDrop(_list, new DataObject(DragFormat, moving), DragDropEffects.Move);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not PageTile[] moving)
        {
            return;
        }

        var target = TileAt(e.OriginalSource as DependencyObject);
        int index;
        if (target is null)
        {
            index = _tiles.Count;
        }
        else
        {
            index = _tiles.IndexOf(target);
            var item = (ListBoxItem)_list.ItemContainerGenerator.ContainerFromItem(target);
            if (item is not null && e.GetPosition(item).X > item.ActualWidth / 2)
            {
                index++;
            }
        }

        MoveSelected(moving, index);
        e.Handled = true;
    }

    protected override ToolEdit? BuildEdit()
    {
        var pages = _selectionOnly ? Selected() : _tiles.ToArray();
        if (pages.Length == 0)
        {
            Hint = L.Get("Pages_KeepOne");
            return null;
        }

        if (!_selectionOnly && Snapshot().SequenceEqual(_original))
        {
            Hint = L.Get("Pages_NothingYet");
            return null;
        }

        return new PagesEdit(pages.Select(t => new PageEdit(t.SourceIndex, t.Rotation)).ToArray(), _selectionOnly);
    }

    /// <summary>One page in the grid.</summary>
    private sealed class PageTile(int sourceIndex) : INotifyPropertyChanged
    {
        private BitmapSource? _thumb;
        private int _rotation;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int SourceIndex { get; } = sourceIndex;

        public BitmapSource? Thumb
        {
            get => _thumb;
            set
            {
                _thumb = value;
                Raise();
            }
        }

        /// <summary>Clockwise degrees added to the page.</summary>
        public int Rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                Raise();
                Raise(nameof(Label));
            }
        }

        public string Label => Rotation == 0 ? $"{SourceIndex + 1}" : $"{SourceIndex + 1}  ↻{Rotation}°";

        /// <summary>What Narrator reads for the page.</summary>
        public override string ToString() =>
            Rotation == 0 ? L.Format("Pages_ItemName", SourceIndex + 1) : L.Format("Pages_ItemNameRotated", SourceIndex + 1, Rotation);

        private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
