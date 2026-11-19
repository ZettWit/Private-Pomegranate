using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Input;
using Pomegranate.Core.Settings;

namespace Pomegranate.App.Windows;

/// <summary>First-run welcome (spec §10) with a sample file to practice on.</summary>
public partial class WelcomeWindow : Window
{
    private readonly Action _openSettings;
    private readonly Action _done;
    private Point? _dragStart;

    internal WelcomeWindow(PomegranateSettings settings, Action openSettings, Action done)
    {
        InitializeComponent();
        _openSettings = openSettings;
        _done = done;
        ConvertKeyText.Text = settings.Keys.Convert.ToString();
        ToolsKeyText.Text = settings.Keys.Tools.ToString();
        ActionsKeyText.Text = settings.Keys.Actions.ToString();
        HotkeyRun.Text = HotkeyText.Describe(settings.Hotkey);
    }

    /// <summary>The sample's folder; conversions of the sample land here too.</summary>
    public static string TryFolder => AppPaths.TryItFolder;

    private static string EnsureSample()
    {
        Directory.CreateDirectory(TryFolder);
        var sample = Path.Combine(TryFolder, "sample.png");
        if (!File.Exists(sample))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Pomegranate-256.png"), sample);
        }

        return sample;
    }

    private void OnSampleMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(this);

    private void OnSampleMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { EnsureSample() });
            DragDrop.DoDragDrop(SampleCard, data, DragDropEffects.Copy);
        }
        catch (IOException ex)
        {
            Log.Error("Couldn't create the sample file.", ex);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        EnsureSample();
        Process.Start(new ProcessStartInfo(TryFolder) { UseShellExecute = true });
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _openSettings();

    private void OnDone(object sender, RoutedEventArgs e)
    {
        _done();
        Close();
    }
}
