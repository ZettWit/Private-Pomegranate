namespace Pomegranate.Core.Formats;

/// <summary>
/// Knows which format a file is in and what it can be converted to.
/// Target lists are kept in the order the wheel shows them (most used first).
/// <para>
/// A pair of formats can have several rules, one per engine, in order of preference (e.g. DOCX → ODT
/// through Office when it's installed, otherwise Pandoc). The first rule whose engine is available wins;
/// <see cref="WithEngines"/> drops the rest.
/// </para>
/// </summary>
public sealed class FormatCatalog
{
    private readonly IReadOnlyList<FileFormat> _formats;
    private readonly IReadOnlyList<ConversionRule> _allRules;
    private readonly (string Suffix, FileFormat Format)[] _suffixes;

    // Only the preferred rule per (from, to) pair.
    private readonly Dictionary<FileFormat, List<ConversionRule>> _rulesBySource = [];

    public FormatCatalog(IEnumerable<FileFormat> formats, IEnumerable<ConversionRule> rules)
    {
        _formats = formats.ToArray();
        _allRules = rules.ToArray();

        // Longest suffix first, so "x.tar.gz" matches TAR.GZ rather than nothing or a plain ".gz".
        _suffixes = _formats
            .SelectMany(f => f.Extensions.Select(e => ("." + e, f)))
            .OrderByDescending(p => p.Item1.Length)
            .ToArray();

        var seen = new HashSet<(FileFormat, FileFormat, EngineKind)>();
        foreach (var rule in _allRules)
        {
            if (rule.From == rule.To)
            {
                throw new ArgumentException($"Rule converts {rule.From} to itself.", nameof(rules));
            }

            if (!seen.Add((rule.From, rule.To, rule.Engine)))
            {
                throw new ArgumentException($"Duplicate rule {rule.From} -> {rule.To} ({rule.Engine}).", nameof(rules));
            }

            if (!_rulesBySource.TryGetValue(rule.From, out var list))
            {
                list = [];
                _rulesBySource[rule.From] = list;
            }

            // Keep the first (preferred) rule for each target.
            if (!list.Any(r => r.To == rule.To))
            {
                list.Add(rule);
            }
        }
    }

    public static FormatCatalog Default { get; } = new(KnownFormats.All, DefaultRules.Create());

    /// <summary>The conversions on offer: one rule (the preferred engine) per pair of formats.</summary>
    public IEnumerable<ConversionRule> AllRules => _rulesBySource.Values.SelectMany(r => r);

    /// <summary>
    /// A catalog without the conversions whose engine isn't installed (e.g. dnglab on ARM64, Office on a
    /// PC without it). Where a pair has a fallback engine, the fallback takes over.
    /// </summary>
    public FormatCatalog WithEngines(IEnumerable<EngineKind> available)
    {
        var set = available.ToHashSet();
        return new(_formats, _allRules.Where(r => set.Contains(r.Engine)));
    }

    /// <summary>Finds a file's format from its name, or null if Pomegranate doesn't know it.</summary>
    public FileFormat? Detect(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var (suffix, format) in _suffixes)
        {
            // Require at least one character before the extension (".png" alone is not a PNG file name).
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return null;
    }

    public IReadOnlyList<FileFormat> TargetsFor(FileFormat source) =>
        _rulesBySource.TryGetValue(source, out var rules) ? rules.Select(r => r.To).ToArray() : [];

    public ConversionRule? FindRule(FileFormat from, FileFormat to) =>
        _rulesBySource.TryGetValue(from, out var rules) ? rules.FirstOrDefault(r => r.To == to) : null;

    /// <summary>
    /// The file name without its format extension: "photo.jpg" → "photo", "backup.tar.gz" → "backup".
    /// </summary>
    public string BaseName(string path)
    {
        var name = Path.GetFileName(path);
        var format = Detect(path);
        if (format is not null)
        {
            foreach (var ext in format.Extensions.OrderByDescending(e => e.Length))
            {
                var suffix = "." + ext;
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name[..^suffix.Length];
                }
            }
        }

        return Path.GetFileNameWithoutExtension(name);
    }
}
