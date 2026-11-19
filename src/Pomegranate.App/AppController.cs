using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pomegranate.App.Actions;
using Pomegranate.App.Conversion;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Input;
using Pomegranate.App.Wheel;
using Pomegranate.App.Windows;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Input;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Engines;

namespace Pomegranate.App;

/// <summary>
/// Wires everything together: input hook → wheel → conversions and actions, plus the tray,
/// hotkey, settings and the single-instance command pipe.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Application _app;
    private readonly SingleInstance _instance;
    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly EngineSet _engines;
    private readonly JobRunner _runner;
    private readonly TrayIcon _tray;
    private readonly WheelWindow _wheel;
    private readonly ProgressWindow _progress;
    private readonly ConversionCoordinator _conversions;
    private readonly ActionRunner _actions;
    private readonly GlobalInputHook _hook;
    private readonly HotkeyManager _hotkey;
    private readonly DispatcherTimer _releaseGrace = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private FloatWindow? _float;
    private SettingsWindow? _settingsWindow;
    private WelcomeWindow? _welcome;
    private bool _paused;

    public AppController(Application app, SingleInstance instance)
    {
        _app = app;
        _instance = instance;
        _dispatcher = app.Dispatcher;
        _settings = new SettingsService(new SettingsStore(SettingsStore.DefaultPath));

        var locator = EngineLocator.FindDefault();
        _engines = EngineSet.Create(locator);
        _runner = _engines.CreateRunner();
        Log.Info($"Engines folder: {locator.EnginesFolder ?? "(not found)"}; available: {string.Join(", ", _engines.Available)}.");

        _tray = new TrayIcon(ShowSettings, ShowWelcome, TogglePause, Quit);
        _progress = new ProgressWindow();
        _conversions = new ConversionCoordinator(() => _runner, _settings, _tray, _progress, _dispatcher);
        _actions = new ActionRunner(_settings, new ShareHost(), () => _float ??= new FloatWindow(), _tray);

        var builder = new WheelContentBuilder(() => new DropAnalyzer(_engines.Catalog), () => _settings.Current);
        _wheel = new WheelWindow(builder.Build);
        _wheel.Chosen += (_, choice) => Execute(choice);
        _wheel.Dismissed += (_, _) => _hook!.WheelClosed();

        var s = _settings.Current;
        _hook = new GlobalInputHook(s.Keys, s.HoldDelayMs, OnGesture, () => _dispatcher.BeginInvoke(() => _wheel.Close(raise: true)));
        _hotkey = new HotkeyManager(OnHotkey);

        // After the mouse button goes up, the drop normally reaches the wheel within milliseconds.
        // Only if it doesn't (the drag ended somewhere else) is the wheel closed. Closing it straight
        // away could hand the drop to whatever window is underneath.
        _releaseGrace.Tick += (_, _) =>
        {
            _releaseGrace.Stop();
            _wheel.Close(raise: true);
        };

        _settings.Changed += (_, next) => ApplySettings(next);
    }

    public void Start(AppCommand? command)
    {
        _wheel.Prepare();
        _hook.Start();
        RegisterHotkey(_settings.Current.Hotkey, notifyOnFailure: true);
        _ = StartupRegistration.ApplyAsync(_settings.Current.StartWithWindows);
        _instance.Listen(c => _dispatcher.BeginInvoke(() => Handle(c)));

        if (!_settings.Current.FirstRunCompleted)
        {
            ShowWelcome();
        }
        else if (command is not null)
        {
            Handle(command);
        }
    }

    public void Dispose()
    {
        _hook.Dispose();
        _hotkey.Dispose();
        _tray.Dispose();
    }

    // ---- Wheel ----------------------------------------------------------------------------------

    /// <summary>Signals from the input hook thread.</summary>
    private void OnGesture(GestureSignal signal, WheelKind? wheel) => _dispatcher.BeginInvoke(() =>
    {
        switch (signal)
        {
            case GestureSignal.Open when wheel is { } kind:
                if (IsSuppressed())
                {
                    _hook.WheelClosed();
                    return;
                }

                _releaseGrace.Stop();
                _wheel.OpenForDrag(kind);
                break;
            case GestureSignal.Switch when wheel is { } kind:
                _wheel.SwitchTo(kind);
                break;
            case GestureSignal.Close:
                if (_wheel.IsOpen)
                {
                    _releaseGrace.Start();
                }

                break;
        }
    });

    /// <summary>Paused, a full-screen app, or an excluded app (spec §11).</summary>
    private bool IsSuppressed()
    {
        var s = _settings.Current;
        if (_paused)
        {
            return true;
        }

        if (s.PauseInFullScreen && Foreground.IsFullScreenApp())
        {
            return true;
        }

        var process = Foreground.ProcessName();
        return process is not null && s.ExcludedApps.Contains(process, StringComparer.OrdinalIgnoreCase);
    }

    private void Execute(WheelChoice choice)
    {
        switch (choice.Item)
        {
            case ConvertItem convert:
                _conversions.Start(choice.Paths, convert.Target);
                break;
            case FolderItem folder:
                _actions.Run(folder.Action, choice.Paths, folder.Path);
                break;
            case ActionItem action:
                _actions.Run(action.Action, choice.Paths);
                break;
        }
    }

    private void OnHotkey()
    {
        var foreground = Infrastructure.Native.GetForegroundWindow();
        var paths = ExplorerSelection.Get(foreground);
        if (paths.Count == 0)
        {
            _tray.Notify(L.Get("Notify_NoSelectionTitle"), L.Get("Notify_NoSelection"));
            return;
        }

        OpenClickWheel(paths);
    }

    private void OpenClickWheel(IReadOnlyList<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0)
        {
            return;
        }

        _wheel.OpenForClick(WheelKind.Convert, existing);
    }

    // ---- Commands, settings, windows ------------------------------------------------------------

    private void Handle(AppCommand command)
    {
        switch (command.Command)
        {
            case AppCommand.OpenWheel:
                OpenClickWheel(command.Paths);
                break;
            case AppCommand.ShowSettings:
                ShowSettings();
                break;
        }
    }

    private void ApplySettings(PomegranateSettings next)
    {
        _hook.Configure(next.Keys, next.HoldDelayMs);
        RegisterHotkey(next.Hotkey, notifyOnFailure: true);
        _ = StartupRegistration.ApplyAsync(next.StartWithWindows);
    }

    private Hotkey? _registeredHotkey;

    private void RegisterHotkey(Hotkey hotkey, bool notifyOnFailure)
    {
        if (hotkey == _registeredHotkey)
        {
            return;
        }

        _registeredHotkey = hotkey;
        if (!_hotkey.Register(hotkey) && notifyOnFailure)
        {
            _tray.Notify(L.Get("Notify_HotkeyTakenTitle"), L.Format("Notify_HotkeyTaken", HotkeyText.Describe(hotkey)), isError: true, onClick: ShowSettings);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings, _engines), ShowWelcome);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowWelcome()
    {
        if (_welcome is { IsLoaded: true })
        {
            _welcome.Activate();
            return;
        }

        _welcome = new WelcomeWindow(
            _settings.Current,
            ShowSettings,
            () => _settings.Update(s => s with { FirstRunCompleted = true }));
        _welcome.Closed += (_, _) =>
        {
            _welcome = null;
            _settings.Update(s => s with { FirstRunCompleted = true });
        };
        _welcome.Show();
        _welcome.Activate();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _tray.SetPaused(_paused);
        Log.Info(_paused ? "Paused." : "Resumed.");
    }

    private void Quit() => _app.Shutdown();
}
