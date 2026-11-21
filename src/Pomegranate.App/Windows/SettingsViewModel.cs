using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pomegranate.App.Infrastructure;
using Pomegranate.App.Input;
using Pomegranate.Core.Settings;
using Pomegranate.Engines;

namespace Pomegranate.App.Windows;

/// <summary>Settings window state. Every change is applied and saved immediately (spec §11).</summary>
internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _service;

    public SettingsViewModel(SettingsService service, EngineSet engines)
    {
        _service = service;
        PinnedFolders = new ObservableCollection<string>(service.Current.PinnedFolders);
        PinnedFolders.CollectionChanged += (_, _) => Set(s => s with { PinnedFolders = PinnedFolders.ToArray() });

        var available = engines.Available.ToHashSet();
        var office = engines.Locator.Office;
        var microsoft = new[] { (office.Word, "Word"), (office.Excel, "Excel"), (office.PowerPoint, "PowerPoint") }
            .Where(a => a.Item1).Select(a => a.Item2).ToList();
        if (office.LibreOffice is not null)
        {
            microsoft.Add("LibreOffice");
        }

        EngineStatus = string.Join(Environment.NewLine,
        [
            L.Get("Settings_EngineImages"),
            available.Contains(Core.Formats.EngineKind.Media) ? L.Get("Settings_EngineMedia") : L.Get("Settings_EngineMediaMissing"),
            available.Contains(Core.Formats.EngineKind.Dng) ? L.Get("Settings_EngineDng") : L.Get("Settings_EngineDngMissing"),
            available.Contains(Core.Formats.EngineKind.Pandoc) ? L.Get("Settings_EngineDocuments") : L.Get("Settings_EngineDocumentsMissing"),
            available.Contains(Core.Formats.EngineKind.WebPdf) ? L.Get("Settings_EngineEdge") : L.Get("Settings_EngineEdgeMissing"),
            microsoft.Count > 0 ? L.Format("Settings_EngineOffice", string.Join(", ", microsoft)) : L.Get("Settings_EngineOfficeMissing"),
        ]);
        _service.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private PomegranateSettings S => _service.Current;

    public IReadOnlyList<ModifierKey> Keys { get; } = Enum.GetValues<ModifierKey>();

    public IReadOnlyList<string> Presets { get; } = ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower"];

    public string Version => AppInfo.Version;

    public string EngineStatus { get; }

    // ---- Activation ----

    public ModifierKey ConvertKey
    {
        get => S.Keys.Convert;
        set => SetKey(value, S.Keys.Convert, k => S.Keys with { Convert = k });
    }

    public ModifierKey ToolsKey
    {
        get => S.Keys.Tools;
        set => SetKey(value, S.Keys.Tools, k => S.Keys with { Tools = k });
    }

    public ModifierKey ActionsKey
    {
        get => S.Keys.Actions;
        set => SetKey(value, S.Keys.Actions, k => S.Keys with { Actions = k });
    }

    public int HoldDelayMs
    {
        get => S.HoldDelayMs;
        set => Set(s => s with { HoldDelayMs = value });
    }

    public string HotkeyText => Input.HotkeyText.Describe(S.Hotkey);

    public void SetHotkey(Hotkey hotkey) => Set(s => s with { Hotkey = hotkey });

    // ---- Output and general ----

    public bool OutputNextToOriginal
    {
        get => S.OutputFolder is null;
        set => Set(s => s with { OutputFolder = value ? null : s.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
    }

    public string? OutputFolder
    {
        get => S.OutputFolder;
        set => Set(s => s with { OutputFolder = string.IsNullOrWhiteSpace(value) ? null : value });
    }

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set => Set(s => s with { StartWithWindows = value });
    }

    public bool PauseInFullScreen
    {
        get => S.PauseInFullScreen;
        set => Set(s => s with { PauseInFullScreen = value });
    }

    public string ExcludedAppsText
    {
        get => string.Join(Environment.NewLine, S.ExcludedApps);
        set => Set(s => s with
        {
            ExcludedApps = value.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        });
    }

    public ObservableCollection<string> PinnedFolders { get; }

    // ---- Quality ----

    public int JpegQuality { get => S.Quality.JpegQuality; set => Q(q => q with { JpegQuality = value }); }

    public int WebPQuality { get => S.Quality.WebPQuality; set => Q(q => q with { WebPQuality = value }); }

    public int AvifQuality { get => S.Quality.AvifQuality; set => Q(q => q with { AvifQuality = value }); }

    public int H264Crf { get => S.Quality.H264Crf; set => Q(q => q with { H264Crf = value }); }

    public string H264Preset { get => S.Quality.H264Preset; set => Q(q => q with { H264Preset = value }); }

    public int Vp9Crf { get => S.Quality.Vp9Crf; set => Q(q => q with { Vp9Crf = value }); }

    public int AacKbps { get => S.Quality.AacKbps; set => Q(q => q with { AacKbps = value }); }

    public int OpusKbps { get => S.Quality.OpusKbps; set => Q(q => q with { OpusKbps = value }); }

    public int WmaKbps { get => S.Quality.WmaKbps; set => Q(q => q with { WmaKbps = value }); }

    public int Mp3VbrQuality { get => S.Quality.Mp3VbrQuality; set => Q(q => q with { Mp3VbrQuality = value }); }

    public int VorbisQuality { get => S.Quality.VorbisQuality; set => Q(q => q with { VorbisQuality = value }); }

    public int GifFps { get => S.Quality.GifFps; set => Q(q => q with { GifFps = value }); }

    public int GifMaxWidth { get => S.Quality.GifMaxWidth; set => Q(q => q with { GifMaxWidth = value }); }

    public int PdfImageDpi { get => S.Quality.PdfImageDpi; set => Q(q => q with { PdfImageDpi = value }); }

    public int SlideImageWidth { get => S.Quality.SlideImageWidth; set => Q(q => q with { SlideImageWidth = value }); }

    public int ImageQualityLight { get => S.Tools.ImageQuality.Light; set => T(t => t with { ImageQuality = t.ImageQuality with { Light = value } }); }

    public int ImageQualityMedium { get => S.Tools.ImageQuality.Medium; set => T(t => t with { ImageQuality = t.ImageQuality with { Medium = value } }); }

    public int ImageQualityStrong { get => S.Tools.ImageQuality.Strong; set => T(t => t with { ImageQuality = t.ImageQuality with { Strong = value } }); }

    public int VideoCrfLight { get => S.Tools.VideoCrf.Light; set => T(t => t with { VideoCrf = t.VideoCrf with { Light = value } }); }

    public int VideoCrfMedium { get => S.Tools.VideoCrf.Medium; set => T(t => t with { VideoCrf = t.VideoCrf with { Medium = value } }); }

    public int VideoCrfStrong { get => S.Tools.VideoCrf.Strong; set => T(t => t with { VideoCrf = t.VideoCrf with { Strong = value } }); }

    public int AudioKbpsLight { get => S.Tools.AudioKbps.Light; set => T(t => t with { AudioKbps = t.AudioKbps with { Light = value } }); }

    public int AudioKbpsMedium { get => S.Tools.AudioKbps.Medium; set => T(t => t with { AudioKbps = t.AudioKbps with { Medium = value } }); }

    public int AudioKbpsStrong { get => S.Tools.AudioKbps.Strong; set => T(t => t with { AudioKbps = t.AudioKbps with { Strong = value } }); }

    public int PdfDpiLight { get => S.Tools.PdfDpi.Light; set => T(t => t with { PdfDpi = t.PdfDpi with { Light = value } }); }

    public int PdfDpiMedium { get => S.Tools.PdfDpi.Medium; set => T(t => t with { PdfDpi = t.PdfDpi with { Medium = value } }); }

    public int PdfDpiStrong { get => S.Tools.PdfDpi.Strong; set => T(t => t with { PdfDpi = t.PdfDpi with { Strong = value } }); }

    public void ResetTools() => Set(s => s with { Tools = new ToolSettings() });

    private void T(Func<ToolSettings, ToolSettings> change, [CallerMemberName] string? name = null) =>
        Set(s => s with { Tools = change(s.Tools) }, name);

    public void ResetQuality() => Set(s => s with { Quality = new QualitySettings() });

    private void Q(Func<QualitySettings, QualitySettings> change) => Set(s => s with { Quality = change(s.Quality) });

    /// <summary>Picking a key another wheel uses swaps the two, so the three always stay different.</summary>
    private void SetKey(ModifierKey value, ModifierKey old, Func<ModifierKey, ActivationKeys> with)
    {
        // `with` changes only the edited wheel. Any other wheel that already used `value` gets the old key.
        var edited = with(value);
        var before = S.Keys;
        ModifierKey Fix(ModifierKey after, ModifierKey previous) => after == previous && after == value ? old : after;
        var keys = new ActivationKeys(
            Fix(edited.Convert, before.Convert),
            Fix(edited.Tools, before.Tools),
            Fix(edited.Actions, before.Actions));
        Set(s => s with { Keys = keys });
    }

    private void Set(Func<PomegranateSettings, PomegranateSettings> change, [CallerMemberName] string? name = null)
    {
        _service.Update(change);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
