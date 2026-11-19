using System.IO;
using Forms = System.Windows.Forms;

namespace Pomegranate.App.Infrastructure;

/// <summary>
/// The tray icon (spec §10). Left-click opens Settings; right-click shows Pause/Resume, Settings, Help, Quit.
/// Notifications are tray balloons, which Windows 11 shows as regular notifications; clicking one runs its action.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private Action? _balloonAction;

    public TrayIcon(Action openSettings, Action openHelp, Action togglePause, Action quit)
    {
        _pauseItem = new Forms.ToolStripMenuItem(L.Get("Tray_Pause"), null, (_, _) => togglePause());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripMenuItem(L.Get("Tray_Settings"), null, (_, _) => openSettings()));
        menu.Items.Add(new Forms.ToolStripMenuItem(L.Get("Tray_Help"), null, (_, _) => openHelp()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(L.Get("Tray_Quit"), null, (_, _) => quit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Pomegranate",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                openSettings();
            }
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = L.Get(paused ? "Tray_Resume" : "Tray_Pause");
        _icon.Text = paused ? $"Pomegranate ({L.Get("Tray_PausedSuffix")})" : "Pomegranate";
    }

    public void Notify(string title, string text, bool isError = false, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(5000, title, text, isError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Pomegranate.ico");
        return File.Exists(path)
            ? new System.Drawing.Icon(path, Forms.SystemInformation.SmallIconSize)
            : System.Drawing.SystemIcons.Application;
    }
}
