using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pomegranate.App.Editors;

/// <summary>
/// Shows a picture scaled to fit, with an <see cref="Overlay"/> canvas on top whose coordinates are the
/// original's pixels (the preview may be smaller; the scaling is the Viewbox's job). Editors draw on the overlay.
/// </summary>
internal sealed class ImageStage : Grid
{
    private readonly Viewbox _viewbox = new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Grid _surface = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };

    public ImageStage()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80));
        Margin = new Thickness(12, 0, 12, 0);
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _surface.Children.Add(_image);
        _surface.Children.Add(Overlay);
        _viewbox.Child = _surface;
        _viewbox.Margin = new Thickness(16);
        Children.Add(_viewbox);
    }

    /// <summary>Drawing surface in the original's pixel coordinates.</summary>
    public Canvas Overlay { get; } = new() { Background = Brushes.Transparent, ClipToBounds = true, Focusable = true };

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    /// <summary>Screen units per picture pixel (1 when the picture is shown at its real size).</summary>
    public double Scale => PixelWidth > 0 && _viewbox.ActualWidth > 0 ? _viewbox.ActualWidth / PixelWidth : 1;

    public void Show(BitmapSource preview, int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        _image.Source = preview;
        _surface.Width = pixelWidth;
        _surface.Height = pixelHeight;
        Overlay.Width = pixelWidth;
        Overlay.Height = pixelHeight;
    }

    /// <summary>Swaps the picture (another video frame) without changing the coordinates.</summary>
    public void Replace(BitmapSource preview) => _image.Source = preview;

    /// <summary>Loads a PNG fully into memory, so the file can be deleted or overwritten right away.</summary>
    public static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
