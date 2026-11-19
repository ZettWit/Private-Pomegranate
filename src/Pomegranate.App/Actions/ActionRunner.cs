using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Pomegranate.App.Conversion;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Wheel;
using Pomegranate.App.Windows;

namespace Pomegranate.App.Actions;

/// <summary>The six actions (spec §6). They work on files, folders and mixed selections alike.</summary>
internal sealed class ActionRunner(SettingsService settings, ShareHost share, Func<FloatWindow> floatWindow, TrayIcon tray)
{
    public void Run(WheelAction action, IReadOnlyList<string> paths, string? folder = null)
    {
        if (paths.Count == 0)
        {
            return;
        }

        Log.Info($"Action {action} on {paths.Count} item(s).");
        try
        {
            switch (action)
            {
                case WheelAction.Share:
                    share.Share(paths);
                    break;
                case WheelAction.Float:
                    floatWindow().Add(paths);
                    break;
                case WheelAction.CopyTo:
                case WheelAction.MoveTo:
                    Transfer(paths, folder, move: action == WheelAction.MoveTo);
                    break;
                case WheelAction.CopyPath:
                    Clipboard.SetText(string.Join(Environment.NewLine, paths));
                    break;
                case WheelAction.Terminal:
                    OpenTerminal(paths[0]);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            Log.Error($"Action {action} failed.", ex);
            tray.Notify(L.Get("Notify_ActionFailedTitle"), ex.Message, isError: true, onClick: ConversionCoordinator.OpenLog);
        }
    }

    /// <summary>
    /// Copy to… / Move to…: Windows' own copy engine does the work, so large copies get the usual
    /// progress dialog and name clashes get the usual Replace / Skip choice.
    /// </summary>
    private void Transfer(IReadOnlyList<string> paths, string? folder, bool move)
    {
        folder ??= PickFolder(move);
        if (folder is null)
        {
            return;
        }

        settings.Update(s => s.WithRecentFolder(folder));
        var items = paths.ToArray();

        // The shell copy UI is modal; run it on its own STA thread so Pomegranate stays responsive.
        var thread = new Thread(() =>
        {
            foreach (var path in items)
            {
                try
                {
                    var name = Path.GetFileName(path.TrimEnd('\\'));
                    var destination = Path.Combine(folder, name);
                    if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // already there
                    }

                    if (Directory.Exists(path))
                    {
                        if (move)
                        {
                            FileSystem.MoveDirectory(path, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                        }
                        else
                        {
                            FileSystem.CopyDirectory(path, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                        }
                    }
                    else if (move)
                    {
                        FileSystem.MoveFile(path, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                    }
                    else
                    {
                        FileSystem.CopyFile(path, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"{(move ? "Moving" : "Copying")} {path} to {folder} failed.", ex);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Pomegranate file transfer",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static string? PickFolder(bool move)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L.Get(move ? "Action_MoveToPick" : "Action_CopyToPick"),
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>Opens Windows Terminal (or PowerShell) in the item's folder, or in the folder itself.</summary>
    private static void OpenTerminal(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var wt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wt))
        {
            Process.Start(new ProcessStartInfo(wt) { ArgumentList = { "-d", folder }, UseShellExecute = false });
            return;
        }

        Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder, UseShellExecute = true });
    }
}
