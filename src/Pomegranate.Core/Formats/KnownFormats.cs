namespace Pomegranate.Core.Formats;

/// <summary>Every format Pomegranate knows about.</summary>
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

    // Documents
    public static readonly FileFormat Pdf = new("pdf", "PDF", FormatCategory.Document, "pdf");
    public static readonly FileFormat Txt = new("txt", "TXT", FormatCategory.Document, "txt");
    public static readonly FileFormat Docx = new("docx", "DOCX", FormatCategory.Document, "docx");
    public static readonly FileFormat Doc = new("doc", "DOC", FormatCategory.Document, "doc");
    public static readonly FileFormat Odt = new("odt", "ODT", FormatCategory.Document, "odt");
    public static readonly FileFormat Rtf = new("rtf", "RTF", FormatCategory.Document, "rtf");
    public static readonly FileFormat Markdown = new("md", "Markdown", FormatCategory.Document, "md", "markdown");
    public static readonly FileFormat Html = new("html", "HTML", FormatCategory.Document, "html", "htm");
    public static readonly FileFormat Epub = new("epub", "EPUB", FormatCategory.Document, "epub");

    // Slides
    public static readonly FileFormat Pptx = new("pptx", "PPTX", FormatCategory.Presentation, "pptx");
    public static readonly FileFormat Ppt = new("ppt", "PPT", FormatCategory.Presentation, "ppt");
    public static readonly FileFormat Odp = new("odp", "ODP", FormatCategory.Presentation, "odp");

    // Spreadsheets
    public static readonly FileFormat Xlsx = new("xlsx", "XLSX", FormatCategory.Spreadsheet, "xlsx");
    public static readonly FileFormat Xls = new("xls", "XLS", FormatCategory.Spreadsheet, "xls");
    public static readonly FileFormat Ods = new("ods", "ODS", FormatCategory.Spreadsheet, "ods");
    public static readonly FileFormat Csv = new("csv", "CSV", FormatCategory.Spreadsheet, "csv");
    public static readonly FileFormat Tsv = new("tsv", "TSV", FormatCategory.Spreadsheet, "tsv", "tab");

    public static IReadOnlyList<FileFormat> All { get; } =
    [
        Jpg, Png, WebP, Heic, Avif, Tiff, Bmp, Svg, Dng, Raw,
        Mp3, M4a, Wav, Flac, Ogg, Opus, Aiff, Wma,
        Mp4, Mov, Mkv, WebM, Avi, Wmv, Gif,
        Zip, Tar, TarGz, Rar,
        Srt, Vtt,
        Pdf, Txt, Docx, Doc, Odt, Rtf, Markdown, Html, Epub,
        Pptx, Ppt, Odp,
        Xlsx, Xls, Ods, Csv, Tsv,
    ];
}
