using System.Runtime.InteropServices;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Engines.Documents;
using Pomegranate.Engines.Sheets;

namespace Pomegranate.Engines.Tests;

/// <summary>v2 samples: documents, slides and spreadsheets.</summary>
public sealed partial class Samples
{
    /// <summary>Text every document, slide and PDF sample contains, so outputs can be checked for it.</summary>
    public const string Marker = "Pomegranate";

    public const string Markdown = """
        # Quarterly report

        Pomegranate turns **Markdown** into Word, PDF and slides.

        - First point
        - Second point

        | Fruit | Count |
        |-------|------:|
        | Pomegranate | 12 |
        | Fig | 7 |

        # Second slide

        More text on the second page.
        """;

    /// <summary>The two sheets of the workbook samples.</summary>
    public static IReadOnlyList<(string Name, object?[][] Rows)> Sheets { get; } =
    [
        ("Fruit", [["Fruit", "Count"], ["Pomegranate", 12.0], ["Fig", 7.0]]),
        ("Notes", [["Note"], ["Pomegranate season is autumn"]]),
    ];

    private async Task MakeDocumentsAsync()
    {
        var md = Add(KnownFormats.Markdown, "sample.md");
        await File.WriteAllTextAsync(md, Markdown);
        await File.WriteAllTextAsync(Add(KnownFormats.Txt, "sample.txt"), "Quarterly report\r\n\r\nPomegranate turns plain text into documents.\r\nSecond line.\r\n");

        if (Engines.PandocPath is not { } pandoc)
        {
            return;
        }

        foreach (var (format, name, writer, standalone) in new[]
        {
            (KnownFormats.Docx, "sample.docx", "docx", false),
            (KnownFormats.Odt, "sample.odt", "odt", false),
            (KnownFormats.Rtf, "sample.rtf", "rtf", true),
            (KnownFormats.Html, "sample.html", "html5", true),
            (KnownFormats.Epub, "sample.epub", "epub3", false),
            (KnownFormats.Pptx, "sample.pptx", "pptx", false),
        })
        {
            var args = new List<string> { "--from", "markdown", "--to", writer, "--metadata", "title=Quarterly report", "--output", Add(format, name) };
            if (standalone)
            {
                args.Add("--standalone");
            }

            args.Add(md);
            await PandocConverter.RunAsync(pandoc, args, CancellationToken.None);
        }

        // A real text PDF, printed by Edge from the Markdown sample.
        if (Engines.EdgePath is { } edge)
        {
            var work = Directory.CreateDirectory(Path.Combine(Folder, "pdf-work")).FullName;
            var pdf = Add(KnownFormats.Pdf, "sample.pdf");
            var rule = new ConversionRule(KnownFormats.Markdown, KnownFormats.Pdf, EngineKind.WebPdf);
            await new WebPdfConverter(edge, pandoc).ConvertAsync(
                new ConversionRequest(md, pdf, rule, new QualitySettings(), work), new Progress<double>(), CancellationToken.None);
        }
    }

    private void MakeSheets()
    {
        var tables = Sheets.Select(s => new SheetTable(s.Name, s.Rows)).ToArray();
        SheetData.WriteWorkbook(tables, Add(KnownFormats.Xlsx, "sample.xlsx"));
        File.WriteAllText(Add(KnownFormats.Csv, "sample.csv"), SheetData.FormatDelimited(tables[0].Rows, ','));
        File.WriteAllText(Add(KnownFormats.Tsv, "sample.tsv"), SheetData.FormatDelimited(tables[0].Rows, '\t'));
    }

    /// <summary>The old Office formats and OpenDocument, saved by Office itself when it's installed.</summary>
    private Task MakeOfficeSamplesAsync()
    {
        var office = Engines.Office;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (office.Word && PathFor(KnownFormats.Docx) is { } docx)
                {
                    SaveWith("Word.Application", docx, (app, path) => app.Documents.Open(path, false, true),
                        (doc, path, format) => doc.SaveAs2(path, format), d => d.Close(0),
                        (KnownFormats.Doc, "sample.doc", 0));
                }

                if (office.Excel && PathFor(KnownFormats.Xlsx) is { } xlsx)
                {
                    SaveWith("Excel.Application", xlsx, (app, path) => app.Workbooks.Open(path),
                        (book, path, format) => book.SaveAs(path, format), b => b.Close(false),
                        (KnownFormats.Xls, "sample.xls", 56), (KnownFormats.Ods, "sample.ods", 60));
                }

                if (office.PowerPoint && PathFor(KnownFormats.Pptx) is { } pptx)
                {
                    SaveWith("PowerPoint.Application", pptx, (app, path) => app.Presentations.Open(path, -1, 0, 0),
                        (pres, path, format) => pres.SaveAs(path, format), p => p.Close(),
                        (KnownFormats.Ppt, "sample.ppt", 1), (KnownFormats.Odp, "sample.odp", 35));
                }

                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private void SaveWith(
        string progId,
        string source,
        Func<dynamic, string, dynamic> open,
        Action<dynamic, string, int> save,
        Action<dynamic> close,
        params (FileFormat Format, string Name, int Code)[] targets)
    {
        // PowerPoint is single-instance: never quit one the user already had open.
        var processName = progId.Split('.')[0] switch { "Word" => "WINWORD", "Excel" => "EXCEL", _ => "POWERPNT" };
        var before = System.Diagnostics.Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();
        var wasRunning = before.Count > 0;
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;
        var started = System.Diagnostics.Process.GetProcessesByName(processName).Select(p => p.Id).Where(id => !before.Contains(id)).ToArray();
        try
        {
            try
            {
                app.DisplayAlerts = progId.StartsWith("PowerPoint", StringComparison.Ordinal) ? 1 : 0;
            }
            catch (COMException)
            {
            }

            dynamic document = open(app, source);
            try
            {
                foreach (var (format, name, code) in targets)
                {
                    save(document, Add(format, name), code);
                }
            }
            finally
            {
                close(document);
                Marshal.FinalReleaseComObject(document);
            }
        }
        finally
        {
            if (!(processName == "POWERPNT" && wasRunning))
            {
                app.Quit();
            }

            Marshal.FinalReleaseComObject(app);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Make sure the app this fixture started is gone before the tests use Office themselves.
            foreach (var id in started)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(id);
                    if (!process.WaitForExit(10_000))
                    {
                        process.Kill();
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }
            }
        }
    }
}
