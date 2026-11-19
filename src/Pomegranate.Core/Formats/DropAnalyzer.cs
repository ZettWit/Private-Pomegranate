namespace Pomegranate.Core.Formats;

/// <summary>Why the Convert wheel can or can't offer anything for a drop.</summary>
public enum ConvertAvailability
{
    Available,

    /// <summary>Nothing was dropped.</summary>
    Empty,

    /// <summary>At least one folder was dropped. Folders get Actions only.</summary>
    ContainsFolders,

    /// <summary>The single file (or every file) is a type Pomegranate can't convert.</summary>
    UnsupportedType,

    /// <summary>Several files were dropped and no format works for all of them.</summary>
    NoSharedFormats,
}

public sealed record DropAnalysis(
    IReadOnlyList<string> Paths,
    ConvertAvailability Availability,
    IReadOnlyList<FileFormat> ConvertTargets)
{
    public bool CanConvert => Availability == ConvertAvailability.Available;
}

/// <summary>Works out what the Convert wheel shows for a set of dropped paths (spec §4.3).</summary>
public sealed class DropAnalyzer(FormatCatalog catalog, Func<string, bool>? isDirectory = null)
{
    private readonly Func<string, bool> _isDirectory = isDirectory ?? Directory.Exists;

    /// <summary>
    /// A target is offered when every file can be converted to it or is already in it,
    /// and at least one file actually needs converting. Files already in the target are skipped.
    /// </summary>
    public DropAnalysis Analyze(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return new(paths, ConvertAvailability.Empty, []);
        }

        if (paths.Any(_isDirectory))
        {
            return new(paths, ConvertAvailability.ContainsFolders, []);
        }

        var sources = paths.Select(catalog.Detect).ToArray();
        if (sources.All(s => s is null || catalog.TargetsFor(s).Count == 0))
        {
            return new(paths, ConvertAvailability.UnsupportedType, []);
        }

        if (sources.Any(s => s is null))
        {
            return new(paths, ConvertAvailability.NoSharedFormats, []);
        }

        var distinct = sources.Distinct().Cast<FileFormat>().ToArray();

        // Candidate order: targets of the first file's format first, then any target the other formats add.
        var candidates = distinct
            .SelectMany(s => catalog.TargetsFor(s))
            .Distinct()
            .ToArray();

        var shared = candidates
            .Where(target => distinct.All(s => s == target || catalog.FindRule(s, target) is not null))
            .Where(target => distinct.Any(s => s != target))
            .ToArray();

        return shared.Length == 0
            ? new(paths, ConvertAvailability.NoSharedFormats, [])
            : new(paths, ConvertAvailability.Available, shared);
    }
}
