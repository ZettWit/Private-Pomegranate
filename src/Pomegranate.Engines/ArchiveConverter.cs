using System.Formats.Tar;
using System.IO.Compression;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using SharpCompress.Readers;

namespace Pomegranate.Engines;

/// <summary>
/// Archive conversions. SharpCompress reads every input (ZIP, TAR, TAR.GZ, RAR); .NET's own
/// ZipArchive and TarWriter write the output, which handles large files (ZIP64) and long names (PAX).
/// </summary>
public sealed class ArchiveConverter : IConverter
{
    public EngineKind Engine => EngineKind.Archive;

    public Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                Convert(request, progress, cancellationToken);
                return ConversionOutput.Single;
            },
            cancellationToken);

    private static void Convert(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var sourceLength = Math.Max(1, new FileInfo(request.SourcePath).Length);
        using var source = File.OpenRead(request.SourcePath);
        using var output = File.Create(request.OutputPath);
        using var writer = ArchiveWriter.Create(request.Rule.To, output);

        try
        {
            using var reader = ReaderFactory.OpenReader(source);
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                if (entry.IsEncrypted)
                {
                    throw new ConversionException("This archive is password-protected.");
                }

                var name = NormalizeName(entry.Key);
                if (name.Length == 0)
                {
                    continue;
                }

                var modified = entry.LastModifiedTime ?? DateTime.Now;
                if (entry.IsDirectory)
                {
                    writer.AddDirectory(name, modified);
                }
                else
                {
                    using var data = reader.OpenEntryStream();
                    writer.AddFile(name, data, modified, cancellationToken);
                }

                progress.Report(Math.Min(0.99, (double)source.Position / sourceLength));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or SharpCompress.Common.ArchiveException
                                       or IOException and not FileNotFoundException)
        {
            throw new ConversionException($"Couldn't read this archive: {ex.Message}", ex);
        }

        progress.Report(1);
    }

    /// <summary>Forward slashes, no leading slash, no ".." (so nothing can escape the archive root).</summary>
    internal static string NormalizeName(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "";
        }

        var parts = key.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not "." and not "..");
        return string.Join('/', parts);
    }

    /// <summary>Writes entries to one output format.</summary>
    private abstract class ArchiveWriter : IDisposable
    {
        public static ArchiveWriter Create(FileFormat target, Stream output) => target.Id switch
        {
            "zip" => new Zip(output),
            "tar" => new Tar(output, gzip: false),
            "targz" => new Tar(output, gzip: true),
            _ => throw new ConversionException($"{target.Label} isn't an archive format Pomegranate can write."),
        };

        public abstract void AddDirectory(string name, DateTime modified);

        public abstract void AddFile(string name, Stream data, DateTime modified, CancellationToken cancellationToken);

        public abstract void Dispose();

        private sealed class Zip(Stream output) : ArchiveWriter
        {
            private readonly ZipArchive _zip = new(output, ZipArchiveMode.Create, leaveOpen: true);

            public override void AddDirectory(string name, DateTime modified)
            {
                var entry = _zip.CreateEntry(name + "/");
                entry.LastWriteTime = ClampZipTime(modified);
            }

            public override void AddFile(string name, Stream data, DateTime modified, CancellationToken cancellationToken)
            {
                var entry = _zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = ClampZipTime(modified);
                using var target = entry.Open();
                data.CopyTo(target);
            }

            public override void Dispose() => _zip.Dispose();

            // ZIP timestamps can't go before 1980.
            private static DateTimeOffset ClampZipTime(DateTime time) =>
                time.Year < 1980 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local) : time;
        }

        private sealed class Tar : ArchiveWriter
        {
            private readonly Stream? _gzip;
            private readonly TarWriter _tar;

            public Tar(Stream output, bool gzip)
            {
                _gzip = gzip ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : null;
                _tar = new TarWriter(_gzip ?? output, TarEntryFormat.Pax, leaveOpen: true);
            }

            public override void AddDirectory(string name, DateTime modified) =>
                _tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name + "/") { ModificationTime = modified });

            public override void AddFile(string name, Stream data, DateTime modified, CancellationToken cancellationToken)
            {
                // TAR headers need the size up front; buffer entries whose stream can't tell us.
                Stream? buffer = null;
                try
                {
                    var seekable = data;
                    if (!data.CanSeek)
                    {
                        buffer = new FileStream(
                            Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                            81920, FileOptions.DeleteOnClose);
                        data.CopyTo(buffer);
                        buffer.Position = 0;
                        seekable = buffer;
                    }

                    _tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        ModificationTime = modified,
                        DataStream = seekable,
                    });
                }
                finally
                {
                    buffer?.Dispose();
                }
            }

            public override void Dispose()
            {
                _tar.Dispose();
                _gzip?.Dispose();
            }
        }
    }
}
