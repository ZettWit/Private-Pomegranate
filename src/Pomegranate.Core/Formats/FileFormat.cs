namespace Pomegranate.Core.Formats;

/// <summary>The kind of file a format belongs to when it is the input of a conversion.</summary>
public enum FormatCategory
{
    Image,
    Audio,
    Video,
    Archive,
    Subtitle,
    Document,
}

/// <summary>Which engine performs a conversion.</summary>
public enum EngineKind
{
    /// <summary>Magick.NET, in-process.</summary>
    Image,

    /// <summary>dnglab, camera RAW to DNG.</summary>
    Dng,

    /// <summary>FFmpeg, audio and video.</summary>
    Media,

    /// <summary>SharpCompress, in-process.</summary>
    Archive,

    /// <summary>Pomegranate's own subtitle converter.</summary>
    Subtitle,
}

/// <summary>
/// A file format. Instances are singletons defined in <see cref="KnownFormats"/>, so reference equality is used.
/// </summary>
public sealed class FileFormat
{
    internal FileFormat(string id, string label, FormatCategory category, params string[] extensions)
    {
        if (extensions.Length == 0)
        {
            throw new ArgumentException("A format needs at least one extension.", nameof(extensions));
        }

        Id = id;
        Label = label;
        Category = category;
        Extensions = extensions.Select(e => e.TrimStart('.').ToLowerInvariant()).ToArray();
    }

    /// <summary>Stable identifier, e.g. "jpg" or "targz".</summary>
    public string Id { get; }

    /// <summary>What the wheel shows, e.g. "JPG" or "TAR.GZ".</summary>
    public string Label { get; }

    public FormatCategory Category { get; }

    /// <summary>Lower-case extensions without the leading dot. May contain dots, e.g. "tar.gz".</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The extension used for files Pomegranate writes in this format.</summary>
    public string PrimaryExtension => Extensions[0];

    public override string ToString() => Label;
}

/// <summary>One possible conversion: a source format, a target format and the engine that does it.</summary>
public sealed record ConversionRule(FileFormat From, FileFormat To, EngineKind Engine);
