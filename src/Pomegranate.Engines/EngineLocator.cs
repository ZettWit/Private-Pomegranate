using System.Runtime.InteropServices;

namespace Pomegranate.Engines;

/// <summary>
/// Finds the bundled engine executables. In the installed app they sit in <c>engines\</c> next to the app.
/// During development and tests they're in <c>engines\&lt;arch&gt;\</c> at the repository root
/// (downloaded by <c>tools/fetch-engines.ps1</c>).
/// </summary>
public sealed class EngineLocator
{
    public EngineLocator(string? enginesFolder)
    {
        EnginesFolder = enginesFolder;
        FfmpegPath = Existing("ffmpeg", "ffmpeg.exe");
        FfprobePath = Existing("ffmpeg", "ffprobe.exe");
        DnglabPath = Existing("dnglab", "dnglab.exe");
    }

    public string? EnginesFolder { get; }

    public string? FfmpegPath { get; }

    public string? FfprobePath { get; }

    public string? DnglabPath { get; }

    public static string ArchitectureFolder => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    /// <summary>Looks next to the app first, then walks up to a repository checkout.</summary>
    public static EngineLocator FindDefault()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "engines");
        if (Directory.Exists(installed))
        {
            return new EngineLocator(installed);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "engines", ArchitectureFolder);
            if (Directory.Exists(candidate))
            {
                return new EngineLocator(candidate);
            }
        }

        return new EngineLocator(null);
    }

    private string? Existing(string folder, string file)
    {
        if (EnginesFolder is null)
        {
            return null;
        }

        var path = Path.Combine(EnginesFolder, folder, file);
        return File.Exists(path) ? path : null;
    }
}
