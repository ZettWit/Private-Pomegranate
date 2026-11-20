namespace Pomegranate.Core.Jobs;

/// <summary>
/// Picks output file names that don't clash with existing files or with other outputs of the same batch:
/// "photo.png", then "photo (1).png", "photo (2).png", … (spec §8). Thread-safe.
/// </summary>
public sealed class OutputNamer(Func<string, bool>? exists = null)
{
    private readonly Func<string, bool> _exists = exists ?? (p => File.Exists(p) || Directory.Exists(p));
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>Returns a free path in <paramref name="folder"/> and reserves it.</summary>
    /// <param name="extension">Null for a folder name.</param>
    public string Reserve(string folder, string baseName, string? extension)
    {
        var dot = extension is null ? "" : "." + extension;
        lock (_lock)
        {
            for (var i = 0; ; i++)
            {
                var name = i == 0 ? $"{baseName}{dot}" : $"{baseName} ({i}){dot}";
                var candidate = Path.Combine(folder, name);
                if (!_reserved.Contains(candidate) && !_exists(candidate))
                {
                    _reserved.Add(candidate);
                    return candidate;
                }
            }
        }
    }

    /// <summary>Frees a name reserved earlier (e.g. when its job failed).</summary>
    public void Release(string path)
    {
        lock (_lock)
        {
            _reserved.Remove(path);
        }
    }

    /// <summary>A temporary scratch folder inside <paramref name="folder"/>, for engines that write several files.</summary>
    public static string TemporaryFolderIn(string folder) =>
        Path.Combine(folder, $".pomegranate-{Guid.NewGuid().ToString("N")[..8]}-work");

    /// <summary>
    /// A temporary path in the same folder that keeps the real extension, so engines that pick the
    /// output format from the extension (FFmpeg) still work.
    /// </summary>
    public static string TemporaryPathFor(string finalPath)
    {
        var folder = Path.GetDirectoryName(finalPath) ?? ".";
        var ext = Path.GetExtension(finalPath);
        var token = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(folder, $".pomegranate-{token}{ext}");
    }
}
