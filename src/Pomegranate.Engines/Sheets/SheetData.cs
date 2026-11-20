using System.Globalization;
using System.Text;
using ExcelDataReader;
using MiniExcelLibs;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Sheets;

/// <summary>A sheet's values, row by row.</summary>
internal sealed record SheetTable(string Name, IReadOnlyList<object?[]> Rows);

/// <summary>Reading and writing spreadsheet data: CSV, TSV, XLSX, XLS. Values only: no formatting or formulas.</summary>
internal static class SheetData
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

    static SheetData()
    {
        // XLS files and non-UTF-8 CSVs use Windows code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<SheetTable> Read(string path, FileFormat format)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return format.Id switch
        {
            "csv" => [new SheetTable(name, ParseDelimited(ReadText(path), ','))],
            "tsv" => [new SheetTable(name, ParseDelimited(ReadText(path), '\t'))],
            "xlsx" or "xls" => ReadWorkbook(path),
            _ => throw new ConversionException($"Pomegranate can't read {format.Label} spreadsheets by itself."),
        };
    }

    /// <summary>
    /// Writes <paramref name="tables"/> as CSV or TSV: one file at <paramref name="outputPath"/> for one sheet,
    /// otherwise one file per sheet in <paramref name="workFolder"/> ("Budget - Sheet1.csv").
    /// </summary>
    public static ConversionOutput WriteDelimited(IReadOnlyList<SheetTable> tables, char separator, string outputPath, string workFolder)
    {
        if (tables.Count == 1)
        {
            File.WriteAllText(outputPath, FormatDelimited(tables[0].Rows, separator), new UTF8Encoding(true));
            return ConversionOutput.Single;
        }

        var extension = separator == '\t' ? ".tsv" : ".csv";
        var parts = tables.Select((table, i) =>
        {
            var file = Path.Combine(workFolder, $"sheet-{i + 1}{extension}");
            File.WriteAllText(file, FormatDelimited(table.Rows, separator), new UTF8Encoding(true));
            return new OutputPart(file, table.Name);
        });
        return ConversionOutput.Named(parts.ToArray());
    }

    public static void WriteWorkbook(IReadOnlyList<SheetTable> tables, string outputPath)
    {
        var sheets = new Dictionary<string, object>();
        foreach (var table in tables)
        {
            var name = UniqueSheetName(table.Name, sheets.Keys);
            sheets[name] = table.Rows
                .Select(row => (IDictionary<string, object?>)row
                    .Select((value, i) => (Key: ColumnName(i), Value: value is string s ? CellValue(s) : value))
                    .ToDictionary(c => c.Key, c => c.Value))
                .ToList();
        }

        MiniExcel.SaveAs(outputPath, sheets, printHeader: false, excelType: ExcelType.XLSX);
    }

    /// <summary>RFC 4180: quotes around fields that need them, doubled quotes inside.</summary>
    public static string FormatDelimited(IReadOnlyList<object?[]> rows, char separator)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(separator);
                }

                var text = Format(row[i]);
                if (text.IndexOfAny([separator, '"', '\r', '\n']) >= 0)
                {
                    sb.Append('"').Append(text.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                }
                else
                {
                    sb.Append(text);
                }
            }

            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    public static List<object?[]> ParseDelimited(string text, char separator)
    {
        var rows = new List<object?[]>();
        var row = new List<object?>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }

    /// <summary>A number if the text is plainly one (keeping leading zeros like postcodes as text).</summary>
    public static object CellValue(string text)
    {
        var trimmed = text.Trim();
        var leadingZero = trimmed.Length > 1 && trimmed[0] == '0' && trimmed[1] != '.';
        return !leadingZero && trimmed.Length > 0 &&
               double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : text;
    }

    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static string Format(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>UTF-8 if the bytes are valid UTF-8 (with or without a BOM), otherwise the PC's ANSI code page.</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return Utf8Strict.GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    private static IReadOnlyList<SheetTable> ReadWorkbook(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var tables = new List<SheetTable>();
            do
            {
                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = reader.GetValue(i);
                    }

                    rows.Add(row);
                }

                tables.Add(new SheetTable(reader.Name, Trim(rows)));
            }
            while (reader.NextResult());

            return tables;
        }
        catch (ExcelDataReader.Exceptions.InvalidPasswordException ex)
        {
            throw new ConversionException("This file is password-protected.", ex);
        }
        catch (ExcelDataReader.Exceptions.ExcelReaderException ex)
        {
            throw new ConversionException($"Couldn't read this spreadsheet: {ex.Message}", ex);
        }
    }

    /// <summary>Drops empty rows at the end and empty columns on the right.</summary>
    private static List<object?[]> Trim(List<object?[]> rows)
    {
        static bool Empty(object? v) => v is null or DBNull || (v is string s && s.Length == 0);

        while (rows.Count > 0 && rows[^1].All(Empty))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var width = rows.Count == 0 ? 0 : rows.Max(r => Array.FindLastIndex(r, v => !Empty(v)) + 1);
        return rows.Select(r => r.Take(width).Concat(Enumerable.Repeat<object?>(null, Math.Max(0, width - r.Length))).ToArray()).ToList();
    }

    private static string UniqueSheetName(string name, IEnumerable<string> taken)
    {
        // Excel: at most 31 characters, none of : \ / ? * [ ].
        var clean = new string(name.Select(c => ":\\/?*[]".Contains(c) ? '_' : c).ToArray()).Trim();
        clean = clean.Length == 0 ? "Sheet" : clean[..Math.Min(31, clean.Length)];
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = clean;
        for (var i = 2; used.Contains(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
        }

        return candidate;
    }
}

/// <summary>CSV, TSV, XLSX and XLS data conversions, built in so they work on every PC (spec §7.2).</summary>
public sealed class SheetConverter : IConverter
{
    public EngineKind Engine => EngineKind.Sheet;

    public Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var tables = SheetData.Read(request.SourcePath, request.Rule.From);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(0.5);
                ConversionOutput output;
                switch (request.Rule.To.Id)
                {
                    case "csv":
                        output = SheetData.WriteDelimited(tables, ',', request.OutputPath, request.WorkFolder);
                        break;
                    case "tsv":
                        output = SheetData.WriteDelimited(tables, '\t', request.OutputPath, request.WorkFolder);
                        break;
                    case "xlsx":
                        SheetData.WriteWorkbook(tables, request.OutputPath);
                        output = ConversionOutput.Single;
                        break;
                    default:
                        throw new ConversionException($"Pomegranate can't write {request.Rule.To.Label} spreadsheets by itself.");
                }

                progress.Report(1);
                return output;
            },
            cancellationToken);
}
