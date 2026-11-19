using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Pomegranate.App.Infrastructure;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Pomegranate.App.Actions;

/// <summary>
/// Opens the Windows share panel for files (spec §6, "Share"). The panel needs a window to belong to,
/// so a tiny invisible one is kept at the cursor.
/// </summary>
internal sealed class ShareHost
{
    private Window? _anchor;
    private IntPtr _handle;
    private DataTransferManager? _manager;
    private IReadOnlyList<string> _pending = [];

    public void Share(IReadOnlyList<string> paths)
    {
        EnsureAnchor();
        _pending = paths;

        Native.GetCursorPos(out var cursor);
        Native.SetWindowPos(_handle, Native.HWND_TOPMOST, cursor.X, cursor.Y, 1, 1, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        DataTransferManagerInterop.ShowShareUIForWindow(_handle);
    }

    private void EnsureAnchor()
    {
        if (_anchor is not null)
        {
            return;
        }

        _anchor = new Window
        {
            Style = new Style(typeof(Window)),
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Width = 1,
            Height = 1,
            Title = "Pomegranate",
        };
        _handle = new WindowInteropHelper(_anchor).EnsureHandle();
        _anchor.Show();

        _manager = DataTransferManagerInterop.GetForWindow(_handle);
        _manager.DataRequested += OnDataRequested;
    }

    private async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var paths = _pending;
            var items = new List<IStorageItem>();
            foreach (var path in paths)
            {
                items.Add(Directory.Exists(path)
                    ? await StorageFolder.GetFolderFromPathAsync(path)
                    : await StorageFile.GetFileFromPathAsync(path));
            }

            var data = args.Request.Data;
            data.Properties.Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : L.Format("Notify_FileCount", paths.Count);
            data.SetStorageItems(items);
        }
        catch (Exception ex)
        {
            Log.Error("Preparing files to share failed.", ex);
            args.Request.FailWithDisplayText(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
