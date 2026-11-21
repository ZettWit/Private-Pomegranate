namespace Pomegranate.Core.Jobs;

/// <summary>Moving finished output to its final name, and cleaning up temporary files. Shared by both runners.</summary>
internal static class OutputFiles
{
    /// <summary>Sheet names and the like can contain characters Windows doesn't allow in file names.</summary>
    public static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "part" : cleaned;
    }

    /// <summary>Renames the temporary file to its final name, choosing a new name if another file took it meanwhile.</summary>
    /// <param name="extension">Without the dot.</param>
    public static string Commit(string temp, string planned, string folder, string baseName, string extension, OutputNamer namer)
    {
        var target = planned;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                target = namer.Reserve(folder, baseName, extension);
            }
        }

        throw new ConversionException("Couldn't find a free file name for the output.");
    }

    /// <summary>Moves a finished folder (Extract) to a free name in <paramref name="folder"/>.</summary>
    public static string CommitFolder(string temp, string folder, string baseName, OutputNamer namer)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var target = namer.Reserve(folder, baseName, extension: null);
            try
            {
                Directory.Move(temp, target);
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
            }
        }

        throw new ConversionException("Couldn't find a free folder name for the output.");
    }

    /// <summary>
    /// Puts numbered pages into a new "&lt;name&gt; (pages)" folder as "&lt;name&gt; - 01.ext", … (spec §7.2).
    /// Returns the folder.
    /// </summary>
    public static string CommitPages(IReadOnlyList<OutputPart> parts, string folder, string baseName, string extension, OutputNamer namer)
    {
        var pages = namer.Reserve(folder, $"{baseName} (pages)", extension: null);
        Directory.CreateDirectory(pages);
        foreach (var part in parts)
        {
            File.Move(part.TempPath, Path.Combine(pages, $"{baseName} - {SafeName(part.Suffix)}.{extension}"));
        }

        return pages;
    }

    public static void DeleteFolderQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
