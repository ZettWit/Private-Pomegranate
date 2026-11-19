using Pomegranate.Core.Settings;

namespace Pomegranate.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pomegranate-settings-").FullName;

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Missing_file_gives_spec_defaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(ActivationKeys.Default, settings.Keys);
        Assert.Equal(250, settings.HoldDelayMs);
        Assert.Equal(Hotkey.Default, settings.Hotkey);
        Assert.Null(settings.OutputFolder);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.PauseInFullScreen);
        Assert.Equal(85, settings.Quality.JpegQuality);
        Assert.False(settings.FirstRunCompleted);
    }

    [Fact]
    public void Saved_settings_load_back_the_same()
    {
        var store = new SettingsStore(SettingsPath);
        var original = new PomegranateSettings
        {
            Keys = new ActivationKeys(ModifierKey.Alt, ModifierKey.Shift, ModifierKey.Ctrl),
            HoldDelayMs = 400,
            OutputFolder = @"D:\Converted",
            ExcludedApps = ["game.exe"],
            Quality = new QualitySettings { JpegQuality = 70, H264Preset = "fast" },
            RecentFolders = [@"D:\One", @"D:\Two"],
            FirstRunCompleted = true,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.Keys, loaded.Keys);
        Assert.Equal(400, loaded.HoldDelayMs);
        Assert.Equal(@"D:\Converted", loaded.OutputFolder);
        Assert.Equal(["game.exe"], loaded.ExcludedApps);
        Assert.Equal(original.Quality, loaded.Quality);
        Assert.Equal([@"D:\One", @"D:\Two"], loaded.RecentFolders);
        Assert.True(loaded.FirstRunCompleted);
        Assert.Contains("\"Shift\"", File.ReadAllText(SettingsPath)); // enums stored as names
    }

    [Fact]
    public void Broken_file_is_backed_up_and_defaults_are_used()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(250, settings.HoldDelayMs);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(_folder, "settings.json.broken-*"));
    }

    [Fact]
    public void Invalid_values_are_fixed_on_load()
    {
        File.WriteAllText(SettingsPath, """
            {
              "Keys": { "Convert": "Shift", "Tools": "Shift", "Actions": "Alt" },
              "HoldDelayMs": 99999
            }
            """);

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(ActivationKeys.Default, settings.Keys);
        Assert.Equal(PomegranateSettings.MaxHoldDelayMs, settings.HoldDelayMs);
        Assert.NotNull(settings.Quality);
    }

    [Fact]
    public void Recent_folders_move_to_front_without_duplicates_and_are_capped()
    {
        var settings = new PomegranateSettings();
        for (var i = 0; i < 10; i++)
        {
            settings = settings.WithRecentFolder($@"D:\F{i}");
        }

        settings = settings.WithRecentFolder(@"d:\f5");

        Assert.Equal(PomegranateSettings.MaxRecentFolders, settings.RecentFolders.Count);
        Assert.Equal(@"d:\f5", settings.RecentFolders[0]);
        Assert.Single(settings.RecentFolders, f => f.Equals(@"D:\F5", StringComparison.OrdinalIgnoreCase));
    }
}
