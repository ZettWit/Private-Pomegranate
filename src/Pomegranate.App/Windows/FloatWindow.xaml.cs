using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pomegranate.App.Wheel;

namespace Pomegranate.App.Windows;

/// <summary>
/// The Float tray (spec §6): an always-on-top list of files you can drag out into any app later.
/// It holds references, not copies.
/// </summary>
public partial class FloatWindow : Window
{
    private readonly ObservableCollection<FloatItem> _items = [];
    private Point? _dragStart;

    public FloatWindow()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        Closing += (_, e) =>
        {
            // Closing just hides it; the list is kept until cleared or Pomegranate quits.
            e.Cancel = true;
            Hide();
        };
    }

    internal void Add(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (!_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _items.Add(new FloatItem(path));
            }
        }

        if (!IsVisible)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Top + 80;
            Show();
        }

        Activate();
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(Items);

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start)
        {
            return;
        }

        var delta = e.GetPosition(Items) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        var hovered = (e.OriginalSource as FrameworkElement)?.DataContext as FloatItem;
        var selected = Items.SelectedItems.Cast<FloatItem>().ToList();
        if (hovered is not null && !selected.Contains(hovered))
        {
            selected = [hovered];
        }

        var existing = selected.Select(i => i.Path).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length > 0)
        {
            var data = new DataObject(DataFormats.FileDrop, existing);
            DragDrop.DoDragDrop(Items, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.SelectedItems.Cast<FloatItem>().ToList())
        {
            _items.Remove(item);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        Hide();
    }

    internal sealed record FloatItem(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;

        public string Glyph => Directory.Exists(Path) ? Glyphs.Folder : Glyphs.Document;
    }
}
