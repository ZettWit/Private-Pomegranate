namespace Pomegranate.Core.Formats;

/// <summary>Every format Pomegranate knows about in v1.</summary>
public static class KnownFormats
{
    // Images
    public static readonly FileFormat Jpg = new("jpg", "JPG", FormatCategory.Image, "jpg", "jpeg", "jpe", "jfif");
    public static readonly FileFormat Png = new("png", "PNG", FormatCategory.Image, "png");
    public static readonly FileFormat WebP = new("webp", "WebP", FormatCategory.Image, "webp");
    public static readonly FileFormat Heic = new("heic", "HEIC", FormatCategory.Image, "heic", "heif", "hif");
    public static readonly FileFormat Avif = new("avif", "AVIF", FormatCategory.Image, "avif");
    public static readonly FileFormat Tiff = new("tiff", "TIFF", FormatCategory.Image, "tiff", "tif");
    public static readonly FileFormat Bmp = new("bmp", "BMP", FormatCategory.Image, "bmp", "dib");
    public static readonly FileFormat Svg = new("svg", "SVG", FormatCategory.Image, "svg");
    public static readonly FileFormat Dng = new("dng", "DNG", FormatCategory.Image, "dng");

    /// <summary>Camera RAW files other than DNG. Input only.</summary>
    public static readonly FileFormat Raw = new(
        "raw", "RAW", FormatCategory.Image,
        "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "raf", "orf", "rw2", "pef",
        "srw", "3fr", "iiq", "erf", "kdc", "mrw", "x3f", "rwl", "raw");

    // Audio
    public static readonly FileFormat Mp3 = new("mp3", "MP3", FormatCategory.Audio, "mp3");
    public static readonly FileFormat M4a = new("m4a", "M4A", FormatCategory.Audio, "m4a");
    public static readonly FileFormat Wav = new("wav", "WAV", FormatCategory.Audio, "wav");
    public static readonly FileFormat Flac = new("flac", "FLAC", FormatCategory.Audio, "flac");
    public static readonly FileFormat Ogg = new("ogg", "OGG", FormatCategory.Audio, "ogg", "oga");
    public static readonly FileFormat Opus = new("opus", "Opus", FormatCategory.Audio, "opus");
    public static readonly FileFormat Aiff = new("aiff", "AIFF", FormatCategory.Audio, "aiff", "aif", "aifc");
    public static readonly FileFormat Wma = new("wma", "WMA", FormatCategory.Audio, "wma");

    // Video
    public static readonly FileFormat Mp4 = new("mp4", "MP4", FormatCategory.Video, "mp4", "m4v");
    public static readonly FileFormat Mov = new("mov", "MOV", FormatCategory.Video, "mov", "qt");
    public static readonly FileFormat Mkv = new("mkv", "MKV", FormatCategory.Video, "mkv");
    public static readonly FileFormat WebM = new("webm", "WebM", FormatCategory.Video, "webm");
    public static readonly FileFormat Avi = new("avi", "AVI", FormatCategory.Video, "avi");
    public static readonly FileFormat Wmv = new("wmv", "WMV", FormatCategory.Video, "wmv");
    public static readonly FileFormat Gif = new("gif", "GIF", FormatCategory.Video, "gif");

    // Archives
    public static readonly FileFormat Zip = new("zip", "ZIP", FormatCategory.Archive, "zip");
    public static readonly FileFormat Tar = new("tar", "TAR", FormatCategory.Archive, "tar");
    public static readonly FileFormat TarGz = new("targz", "TAR.GZ", FormatCategory.Archive, "tar.gz", "tgz");
    public static readonly FileFormat Rar = new("rar", "RAR", FormatCategory.Archive, "rar");

    // Subtitles
    public static readonly FileFormat Srt = new("srt", "SRT", FormatCategory.Subtitle, "srt");
    public static readonly FileFormat Vtt = new("vtt", "VTT", FormatCategory.Subtitle, "vtt");

    // Documents (v1 only writes these)
    public static readonly FileFormat Pdf = new("pdf", "PDF", FormatCategory.Document, "pdf");
    public static readonly FileFormat Txt = new("txt", "TXT", FormatCategory.Document, "txt");

    public static IReadOnlyList<FileFormat> All { get; } =
    [
        Jpg, Png, WebP, Heic, Avif, Tiff, Bmp, Svg, Dng, Raw,
        Mp3, M4a, Wav, Flac, Ogg, Opus, Aiff, Wma,
        Mp4, Mov, Mkv, WebM, Avi, Wmv, Gif,
        Zip, Tar, TarGz, Rar,
        Srt, Vtt,
        Pdf, Txt,
    ];
}
