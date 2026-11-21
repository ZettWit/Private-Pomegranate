using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// The frame every editor shares (spec §7.3, "Editors"): a toolbar, the editing area, a hint and Save/Cancel.
/// Enter saves, Esc cancels, Ctrl+Z undoes. Previews load in the background while "Loading…" shows.
/// </summary>
internal abstract class EditorWindow : Window
{
    private readonly CancellationTokenSource _closing = new();
    private readonly ContentControl _body = new();
    private readonly Grid _loading;
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
    private bool _saved;

    protected EditorWindow(string toolTitle, string path)
    {
        SourcePath = path;
        Title = $"{toolTitle} — {Path.GetFileName(path)}";
        Temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Pomegranate", $"editor-{Guid.NewGuid():N}")).FullName;

        var area = SystemParameters.WorkArea;
        Width = Math.Min(1100, area.Width * 0.85);
        Height = Math.Min(800, area.Height * 0.85);
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;

        Toolbar = new WrapPanel { Margin = new Thickness(12, 10, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        Footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        SaveButton = new Button { Content = L.Get("Editor_Save"), MinWidth = 96, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        SaveButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        SaveButton.Click += (_, _) => TrySave();
        var cancel = new Button { Content = L.Get("Editor_Cancel"), MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        Footer.Children.Add(cancel);
        Footer.Children.Add(SaveButton);

        var bottom = new DockPanel { Margin = new Thickness(12, 8, 12, 12), LastChildFill = true };
        DockPanel.SetDock(Footer, Dock.Right);
        bottom.Children.Add(Footer);
        bottom.Children.Add(_hint);

        _loading = new Grid();
        _loading.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = L.Get("Editor_Loading"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) },
                new ProgressBar { IsIndeterminate = true, Width = 220, Height = 4 },
            },
        });

        var root = new DockPanel();
        DockPanel.SetDock(Toolbar, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(Toolbar);
        root.Children.Add(bottom);
        var center = new Grid();
        center.Children.Add(_body);
        center.Children.Add(_loading);
        root.Children.Add(center);
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += async (_, _) => await LoadSafelyAsync();
        Closed += (_, _) => Cleanup();
    }

    /// <summary>Raised once, with the edit, when the user saves.</summary>
    public event Action<ToolEdit>? Saved;

    public string SourcePath { get; }

    /// <summary>A private temporary folder, deleted when the window closes.</summary>
    protected string Temp { get; }

    protected WrapPanel Toolbar { get; }

    protected StackPanel Footer { get; }

    protected Button SaveButton { get; }

    protected CancellationToken ClosingToken => _closing.Token;

    protected UIElement? Body
    {
        get => _body.Content as UIElement;
        set => _body.Content = value;
    }

    protected string Hint
    {
        get => _hint.Text;
        set => _hint.Text = value;
    }

    /// <summary>Shows the window in front of whatever the file was dropped from.</summary>
    public void ShowInFront()
    {
        Topmost = true;
        Show();
        BringToFront(this);
        ContentRendered += (_, _) => Topmost = false;
    }

    /// <summary>Loads previews. Runs on the UI thread; await background work inside.</summary>
    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>The edit to hand to the tool, or null (with <see cref="Hint"/> saying why) when there's nothing to save.</summary>
    protected abstract ToolEdit? BuildEdit();

    protected virtual void Undo()
    {
    }

    protected void TrySave()
    {
        if (!SaveButton.IsEnabled)
        {
            return;
        }

        var edit = BuildEdit();
        if (edit is null)
        {
            return;
        }

        _saved = true;
        Saved?.Invoke(edit);
        Close();
    }

    private async Task LoadSafelyAsync()
    {
        try
        {
            await LoadAsync(ClosingToken);
            _loading.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Editor couldn't open {SourcePath}.", ex);
            _loading.Children.Clear();
            _loading.Children.Add(new TextBlock
            {
                Text = L.Format("Editor_LoadFailed", ex.Message),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Escape && !typing)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && !typing && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            TrySave();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !typing)
        {
            e.Handled = true;
            Undo();
        }
    }

    private void Cleanup()
    {
        _closing.Cancel();
        if (!_saved)
        {
            Log.Info($"Editor closed without saving: {SourcePath}");
        }

        // Media players and decoders may still hold files for a moment; try, and leave the rest to Windows' temp cleanup.
        try
        {
            Directory.Delete(Temp, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- Small building blocks shared by the editors --------------------------------------------

    /// <summary>A toolbar button with an icon and a label.</summary>
    protected static Button IconButton(string glyph, string label, RoutedEventHandler click, string? tooltip = null)
    {
        var button = new Button { Content = IconLabel(glyph, label), Margin = new Thickness(0, 0, 6, 6), ToolTip = tooltip };
        button.Click += click;
        return button;
    }

    protected static StackPanel IconLabel(string glyph, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, label.Length > 0 ? 6 : 0, 0),
            });
        }

        if (label.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        }

        return panel;
    }

    /// <summary>A row of toggle buttons where exactly one is on. Calls <paramref name="changed"/> with the chosen value.</summary>
    protected static Panel Choice<T>(IReadOnlyList<(string Glyph, string Label, T Value)> options, T selected, Action<T> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
        var buttons = new List<ToggleButton>();
        foreach (var (glyph, label, value) in options)
        {
            var button = new ToggleButton
            {
                Content = IconLabel(glyph, label),
                IsChecked = EqualityComparer<T>.Default.Equals(value, selected),
                Margin = new Thickness(0, 0, 2, 0),
                Tag = value,
                ToolTip = label.Length == 0 ? null : label,
            };
            AutomationName(button, label);
            button.Click += (_, _) =>
            {
                foreach (var other in buttons)
                {
                    other.IsChecked = ReferenceEquals(other, button);
                }

                changed(value);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        return panel;
    }

    protected static void AutomationName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    protected static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 6) };

    // ---- Coming to the front ---------------------------------------------------------------------

    /// <summary>
    /// After a drop, Windows normally won't let a background app take the focus. A tap of Alt (sent by us, so it
    /// reaches no app) lifts that lock for this one window, the usual workaround.
    /// </summary>
    private static void BringToFront(Window window)
    {
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (Native.GetForegroundWindow() == handle)
        {
            return;
        }

        // Alt goes down before and up after, so the release reaches our window, not the one the file came from.
        Send(new Native.KEYBDINPUT { wVk = Native.VK_MENU });
        Native.SetForegroundWindow(handle);
        Send(new Native.KEYBDINPUT { wVk = Native.VK_MENU, dwFlags = Native.KEYEVENTF_KEYUP });
        window.Activate();

        static void Send(Native.KEYBDINPUT key) =>
            Native.SendInput(1, [new Native.INPUT { type = Native.INPUT_KEYBOARD, ki = key }], Marshal.SizeOf<Native.INPUT>());
    }
}
