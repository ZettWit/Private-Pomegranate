using System.IO.Compression;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Tools;
using SharpCompress.Readers;

namespace Pomegranate.Engines.Tools;

/// <summary>Zip (any files and folders into one ZIP) and Extract (an archive into a folder), spec §7.3.</summary>
public sealed class ArchiveTools : ITool
{
    public EngineKind Engine => EngineKind.Archive;

    public Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(
            () => request.Kind switch
            {
                ToolKind.Zip => Zip(request, progress, cancellationToken),
                ToolKind.Extract => Extract(request, progress, cancellationToken),
                _ => throw new ConversionException($"{request.Kind} isn't an archive tool."),
            },
            cancellationToken);

    private static ToolOutput Zip(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var work = Path.GetFullPath(request.WorkFolder);
        var entries = new List<(string File, string Name)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in request.Sources)
        {
            var full = Path.GetFullPath(source).TrimEnd('\\', '/');
            if (Directory.Exists(full))
            {
                var root = Unique(Path.GetFileName(full), used);
                var files = Directory.EnumerateFiles(full, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                });
                foreach (var file in files)
                {
                    // Never zip our own temporary files (when the output folder is inside a zipped folder).
                    if (!file.StartsWith(work, StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add((file, root + "/" + Path.GetRelativePath(full, file).Replace('\\', '/')));
                    }
                }

                if (!files.Any())
                {
                    entries.Add(("", root + "/"));
                }
            }
            else if (File.Exists(full))
            {
                entries.Add((full, Unique(Path.GetFileName(full), used)));
            }
        }

        if (entries.Count == 0)
        {
            throw new ConversionException("There's nothing to zip.");
        }

        var total = Math.Max(1, entries.Where(e => e.File.Length > 0).Sum(e => new FileInfo(e.File).Length));
        long written = 0;
        var output = request.WorkFile("zip");
        using (var stream = File.Create(output))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (file, name) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Length == 0)
                {
                    zip.CreateEntry(name);
                    continue;
                }

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                var modified = File.GetLastWriteTime(file);
                entry.LastWriteTime = modified.Year < 1980 ? new DateTime(1980, 1, 1) : modified;
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var target = entry.Open();
                input.CopyTo(target);
                written += input.Length;
                progress.Report(Math.Min(0.99, written / (double)total));
            }
        }

        return ToolOutput.File(output);
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var i = 1; !used.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        }

        return candidate;
    }

    /// <summary>
    /// Unpacks into a new folder. Entries that would land outside it ("..", absolute paths) are skipped. When
    /// everything sits in one top folder, that folder's contents are returned so there's no folder-in-a-folder.
    /// </summary>
    private static ToolOutput Extract(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var target = Path.Combine(request.WorkFolder, "extracted");
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        var length = Math.Max(1, new FileInfo(request.Source).Length);

        try
        {
            using var source = File.OpenRead(request.Source);
            using var reader = ReaderFactory.OpenReader(source);
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                if (entry.IsEncrypted)
                {
                    throw new ConversionException("This archive is password-protected.");
                }

                var name = SafeRelativePath(entry.Key);
                if (name.Length == 0)
                {
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(target, name));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var data = reader.OpenEntryStream())
                {
                    data.CopyTo(output);
                }

                if (entry.LastModifiedTime is { } modified)
                {
                    File.SetLastWriteTime(path, modified);
                }

                progress.Report(Math.Min(0.99, (double)source.Position / length));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or SharpCompress.Common.ArchiveException
                                       or IOException and not FileNotFoundException)
        {
            throw new ConversionException($"Couldn't read this archive: {ex.Message}", ex);
        }

        var files = Directory.GetFiles(target);
        var folders = Directory.GetDirectories(target);
        if (files.Length == 0 && folders.Length == 0)
        {
            throw new NothingToDoException("This archive is empty.");
        }

        return ToolOutput.Folder(files.Length == 0 && folders.Length == 1 ? folders[0] : target);
    }

    /// <summary>
    /// An entry name as a relative path with only ordinary parts: no "..", ".", drive letters or leading
    /// slashes, and characters Windows doesn't allow replaced.
    /// </summary>
    internal static string SafeRelativePath(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var parts = key.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not "." and not ".." && !p.EndsWith(':'))
            .Select(p => new string(p.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' '))
            .Where(p => p.Length > 0);
        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
