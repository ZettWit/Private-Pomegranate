using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pomegranate.Core.Settings;

/// <summary>Reads and writes settings.json. Never throws on a bad file; it backs it up and uses defaults.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pomegranate",
        "settings.json");

    public PomegranateSettings Load()
    {
        if (!File.Exists(Path))
        {
            return new PomegranateSettings();
        }

        try
        {
            var json = File.ReadAllText(Path);
            var settings = JsonSerializer.Deserialize<PomegranateSettings>(json, JsonOptions);
            return (settings ?? new PomegranateSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            TryBackUpBrokenFile();
            return new PomegranateSettings();
        }
    }

    /// <summary>Writes atomically: a temporary file first, then a rename over the old one.</summary>
    public void Save(PomegranateSettings settings)
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }

    private void TryBackUpBrokenFile()
    {
        try
        {
            var backup = $"{Path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(Path, backup, overwrite: true);
        }
        catch (IOException)
        {
            // Can't move it; the next Save will overwrite it anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
