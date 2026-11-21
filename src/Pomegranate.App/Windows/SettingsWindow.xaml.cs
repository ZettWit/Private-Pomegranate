using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Settings;

namespace Pomegranate.App.Windows;

/// <summary>The Settings window (spec §11). Changes apply immediately; there's no Save button.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly Action _showWelcome;

    internal SettingsWindow(SettingsViewModel model, Action showWelcome)
    {
        InitializeComponent();
        _model = model;
        _showWelcome = showWelcome;
        DataContext = model;
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab or Key.Escape)
        {
            if (key is Key.Tab or Key.Escape)
            {
                e.Handled = false; // keep normal keyboard navigation
            }

            return;
        }

        var modifiers = HotkeyModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        // A hotkey needs Ctrl, Alt or Win, or it would steal ordinary typing.
        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
        {
            return;
        }

        _model.SetHotkey(new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key)));
    }

    private void OnFixedFolderChecked(object sender, RoutedEventArgs e)
    {
        if (_model.OutputNextToOriginal)
        {
            _model.OutputNextToOriginal = false;
        }
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.Get("Settings_OutputFolder") };
        if (dialog.ShowDialog(this) == true)
        {
            _model.OutputFolder = dialog.FolderName;
        }
    }

    private void OnAddPinned(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.Get("Settings_AddFolder") };
        if (dialog.ShowDialog(this) == true &&
            !_model.PinnedFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            _model.PinnedFolders.Add(dialog.FolderName);
        }
    }

    private void OnRemovePinned(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is string folder)
        {
            _model.PinnedFolders.Remove(folder);
        }
    }

    private void OnResetQuality(object sender, RoutedEventArgs e) => _model.ResetQuality();

    private void OnResetTools(object sender, RoutedEventArgs e) => _model.ResetTools();

    private void OnShowWelcome(object sender, RoutedEventArgs e) => _showWelcome();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Folder);
        OpenShell(Log.Folder);
    }

    private void OnOpenLicenses(object sender, RoutedEventArgs e) =>
        OpenShell(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));

    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Couldn't open {target}.", ex);
        }
    }
}
