using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pomegranate.Engines;

/// <summary>Which office suite apps are installed (spec §7.2: Microsoft Office first for its own formats, LibreOffice for OpenDocument).</summary>
public sealed record OfficeApps(bool Word, bool Excel, bool PowerPoint, string? LibreOffice)
{
    public static OfficeApps None { get; } = new(false, false, false, null);

    public bool CanDoText => Word || LibreOffice is not null;

    public bool CanDoSheets => Excel || LibreOffice is not null;

    public bool CanDoSlides => PowerPoint || LibreOffice is not null;

    /// <summary>Looks in the registry; doesn't start anything.</summary>
    public static OfficeApps Detect() => new(
        HasProgId("Word.Application"),
        HasProgId("Excel.Application"),
        HasProgId("PowerPoint.Application"),
        FindLibreOffice());

    private static bool HasProgId(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID");
        return key?.GetValue(null) is string { Length: > 0 };
    }

    private static string? FindLibreOffice()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var appPath = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\soffice.exe");
            if (appPath?.GetValue(null) is string path && File.Exists(path))
            {
                return path;
            }

            using var uno = hive.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath");
            if (uno?.GetValue(null) is string folder && File.Exists(Path.Combine(folder, "soffice.exe")))
            {
                return Path.Combine(folder, "soffice.exe");
            }
        }

        var programFiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe");
        return File.Exists(programFiles) ? programFiles : null;
    }
}

/// <summary>
/// Finds the engines. Bundled ones (FFmpeg, dnglab, Pandoc) sit in <c>engines\</c> next to the app, or in
/// <c>engines\&lt;arch&gt;\</c> at the repository root during development (downloaded by
/// <c>tools/fetch-engines.ps1</c>). Microsoft Edge and the office suites are the PC's own.
/// </summary>
public sealed class EngineLocator
{
    public EngineLocator(string? enginesFolder, OfficeApps? office = null, string? edgePath = null)
    {
        EnginesFolder = enginesFolder;
        FfmpegPath = Existing("ffmpeg", "ffmpeg.exe");
        FfprobePath = Existing("ffmpeg", "ffprobe.exe");
        DnglabPath = Existing("dnglab", "dnglab.exe");
        PandocPath = Existing("pandoc", "pandoc.exe");
        Office = office ?? OfficeApps.None;
        EdgePath = edgePath;
    }

    public string? EnginesFolder { get; }

    public string? FfmpegPath { get; }

    public string? FfprobePath { get; }

    public string? DnglabPath { get; }

    public string? PandocPath { get; }

    public OfficeApps Office { get; }

    /// <summary>msedge.exe, used headless and offline to print simple documents to PDF.</summary>
    public string? EdgePath { get; }

    public static string ArchitectureFolder => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    /// <summary>Looks next to the app first, then walks up to a repository checkout. Also detects Office and Edge.</summary>
    public static EngineLocator FindDefault()
    {
        var office = OfficeApps.Detect();
        var edge = FindEdge();

        var installed = Path.Combine(AppContext.BaseDirectory, "engines");
        if (Directory.Exists(installed))
        {
            return new EngineLocator(installed, office, edge);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "engines", ArchitectureFolder);
            if (Directory.Exists(candidate))
            {
                return new EngineLocator(candidate, office, edge);
            }
        }

        return new EngineLocator(null, office, edge);
    }

    public static string? FindEdge()
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
        {
            if (key?.GetValue(null) is string path && File.Exists(path))
            {
                return path;
            }
        }

        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
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
