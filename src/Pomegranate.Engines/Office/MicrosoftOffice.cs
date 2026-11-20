using System.Diagnostics;
using System.Runtime.InteropServices;
using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;

namespace Pomegranate.Engines.Office;

/// <summary>
/// Word, Excel and PowerPoint through COM automation, hidden, on a dedicated STA thread.
/// <list type="bullet">
/// <item>Macros are forced off and alerts suppressed.</item>
/// <item>Password-protected files fail cleanly: a dummy password is passed, which a protected file rejects.</item>
/// <item>A watchdog stops the conversion after <see cref="Timeout"/>; it only ever kills an Office process
/// Pomegranate started itself. An already-open PowerPoint (which is single-instance) is used but never closed.</item>
/// </list>
/// </summary>
internal static class MicrosoftOffice
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private const string NoPassword = "\u0001pomegranate-no-password";
    private const int MsoAutomationSecurityForceDisable = 3;

    public static Task<ConversionOutput> ConvertAsync(
        EngineKind kind, ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var (progId, processName) = kind switch
        {
            EngineKind.OfficeText => ("Word.Application", "WINWORD"),
            EngineKind.OfficeSheet => ("Excel.Application", "EXCEL"),
            _ => ("PowerPoint.Application", "POWERPNT"),
        };

        var session = new Session(progId, processName);
        var result = new TaskCompletionSource<ConversionOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ConversionOutput? output = null;
            Exception? error = null;
            try
            {
                output = kind switch
                {
                    EngineKind.OfficeText => Word(session, request),
                    EngineKind.OfficeSheet => Excel(session, request),
                    _ => PowerPoint(session, request, progress, cancellationToken),
                };
            }
            catch (COMException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                error = new ConversionException("This file is password-protected.", ex);
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
            {
                error = new ConversionException($"{AppName(kind)} couldn't convert this file: {ex.Message.Split('\n')[0].Trim()}", ex);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                // Report back only once the app is really gone, so the next conversion never attaches to a
                // half-closed instance (PowerPoint is single-instance).
                session.Close();
            }

            if (error is not null)
            {
                result.TrySetException(error);
            }
            else
            {
                result.TrySetResult(output!);
            }
        })
        {
            IsBackground = true,
            Name = $"Pomegranate {processName} automation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return WatchAsync(result.Task, session, kind, cancellationToken);
    }

    private static async Task<ConversionOutput> WatchAsync(
        Task<ConversionOutput> work, Session session, EngineKind kind, CancellationToken cancellationToken)
    {
        try
        {
            return await work.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            session.KillOwnProcess();
            throw new ConversionException($"{AppName(kind)} stopped responding (it may be waiting for an answer it can't show).");
        }
        catch (OperationCanceledException)
        {
            session.KillOwnProcess();
            throw;
        }
    }

    private static ConversionOutput Word(Session session, ConversionRequest request)
    {
        dynamic word = session.Start();
        word.Visible = false;
        word.DisplayAlerts = 0; // wdAlertsNone
        TrySet(() => word.AutomationSecurity = MsoAutomationSecurityForceDisable);

        dynamic document = word.Documents.Open(
            FileName: request.SourcePath,
            ConfirmConversions: false,
            ReadOnly: true,
            AddToRecentFiles: false,
            PasswordDocument: NoPassword,
            WritePasswordDocument: NoPassword,
            Visible: false,
            NoEncodingDialog: true);
        try
        {
            switch (request.Rule.To.Id)
            {
                case "pdf":
                    document.ExportAsFixedFormat(OutputFileName: request.OutputPath, ExportFormat: 17, OpenAfterExport: false);
                    break;
                case "txt":
                    document.SaveAs2(FileName: request.OutputPath, FileFormat: 7, Encoding: 65001, AddToRecentFiles: false); // Unicode text, UTF-8
                    break;
                default:
                    document.SaveAs2(FileName: request.OutputPath, FileFormat: WordFormat(request.Rule.To), AddToRecentFiles: false);
                    break;
            }
        }
        finally
        {
            document.Close(SaveChanges: 0);
            Release(document);
        }

        return ConversionOutput.Single;
    }

    private static ConversionOutput Excel(Session session, ConversionRequest request)
    {
        dynamic excel = session.Start();
        excel.Visible = false;
        excel.DisplayAlerts = false;
        excel.AskToUpdateLinks = false;
        excel.EnableEvents = false;
        TrySet(() => excel.AutomationSecurity = MsoAutomationSecurityForceDisable);

        dynamic book;
        var from = request.Rule.From;
        if (from == KnownFormats.Csv || from == KnownFormats.Tsv)
        {
            // Excel ignores import settings for files named .csv and splits on the PC's list separator
            // (";" in much of Europe), so import a .txt copy with the separator spelled out.
            var copy = Path.Combine(request.WorkFolder, "input.txt");
            File.Copy(request.SourcePath, copy, overwrite: true);
            var tab = from == KnownFormats.Tsv;
            excel.Workbooks.OpenText(
                Filename: copy, Origin: 65001, StartRow: 1, DataType: 1, TextQualifier: 1,
                ConsecutiveDelimiter: false, Tab: tab, Semicolon: false, Comma: !tab, Space: false, Other: false, Local: false);
            book = excel.ActiveWorkbook;
        }
        else
        {
            // A dummy open password makes a locked workbook fail instead of asking. (No dummy write password:
            // Excel rejects one on workbooks that have none, and read-only opening never asks for it anyway.)
            book = excel.Workbooks.Open(
                Filename: request.SourcePath, UpdateLinks: 0, ReadOnly: true, Password: NoPassword,
                IgnoreReadOnlyRecommended: true, AddToMru: false, Notify: false);
        }

        try
        {
            switch (request.Rule.To.Id)
            {
                case "pdf":
                    book.ExportAsFixedFormat(Type: 0, Filename: request.OutputPath, OpenAfterPublish: false);
                    break;
                case "xlsx":
                    book.SaveAs(Filename: request.OutputPath, FileFormat: 51);
                    break;
                case "ods":
                    book.SaveAs(Filename: request.OutputPath, FileFormat: 60);
                    break;
                default:
                    throw new ConversionException($"Excel can't write {request.Rule.To.Label} here.");
            }
        }
        finally
        {
            book.Close(SaveChanges: false);
            Release(book);
        }

        return ConversionOutput.Single;
    }

    private static ConversionOutput PowerPoint(Session session, ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
    {
        dynamic powerPoint = session.Start();
        TrySet(() => powerPoint.DisplayAlerts = 1); // ppAlertsNone
        TrySet(() => powerPoint.AutomationSecurity = MsoAutomationSecurityForceDisable);

        dynamic presentation = powerPoint.Presentations.Open(request.SourcePath, -1, 0, 0); // ReadOnly, not Untitled, no window
        try
        {
            switch (request.Rule.To.Id)
            {
                case "pdf":
                    presentation.SaveAs(request.OutputPath, 32);
                    return ConversionOutput.Single;
                case "pptx":
                    presentation.SaveAs(request.OutputPath, 24);
                    return ConversionOutput.Single;
                case "odp":
                    presentation.SaveAs(request.OutputPath, 35);
                    return ConversionOutput.Single;
                case "png":
                case "jpg":
                    var width = request.Quality.SlideImageWidth;
                    var height = (int)Math.Round(width * (double)presentation.PageSetup.SlideHeight / (double)presentation.PageSetup.SlideWidth);
                    var filter = request.Rule.To.Id == "png" ? "PNG" : "JPG";
                    int count = presentation.Slides.Count;
                    var files = new List<string>();
                    for (var i = 1; i <= count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = Path.Combine(request.WorkFolder, $"slide-{i}.{request.Rule.To.PrimaryExtension}");
                        presentation.Slides[i].Export(file, filter, width, height);
                        files.Add(file);
                        progress.Report((double)i / count);
                    }

                    if (files.Count == 0)
                    {
                        throw new ConversionException("This presentation has no slides.");
                    }

                    return ConversionOutput.Pages(files);
                default:
                    throw new ConversionException($"PowerPoint can't write {request.Rule.To.Label}.");
            }
        }
        finally
        {
            presentation.Close();
            Release(presentation);
        }
    }

    private static int WordFormat(FileFormat format) => format.Id switch
    {
        "docx" => 16, // wdFormatXMLDocument
        "odt" => 23,  // wdFormatOpenDocumentText
        "rtf" => 6,   // wdFormatRTF
        _ => throw new ConversionException($"Word can't write {format.Label}."),
    };

    private static string AppName(EngineKind kind) => kind switch
    {
        EngineKind.OfficeText => "Word",
        EngineKind.OfficeSheet => "Excel",
        _ => "PowerPoint",
    };

    private static void TrySet(Action set)
    {
        try
        {
            set();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Older Office versions lack some properties; the conversion still works without them.
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    /// <summary>One Office app started for one conversion, and the process it lives in.</summary>
    private sealed class Session(string progId, string processName)
    {
        private dynamic? _app;
        private int? _ownProcessId;
        private bool _wasRunning;

        public dynamic Start()
        {
            var before = Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();
            _wasRunning = before.Count > 0;
            var type = Type.GetTypeFromProgID(progId) ?? throw new ConversionException($"{progId} isn't installed.");
            _app = Activator.CreateInstance(type) ?? throw new ConversionException($"Couldn't start {progId}.");
            var started = Process.GetProcessesByName(processName).Select(p => p.Id).Where(id => !before.Contains(id)).ToArray();
            _ownProcessId = started.Length == 1 ? started[0] : null;
            return _app;
        }

        /// <summary>
        /// Quits the app unless it's an instance the user already had open (PowerPoint), then makes sure the
        /// process Pomegranate started really exits.
        /// </summary>
        public void Close()
        {
            if (_app is null)
            {
                return;
            }

            var leaveRunning = processName == "POWERPNT" && _wasRunning && _ownProcessId is null;
            if (!leaveRunning)
            {
                try
                {
                    _app.Quit();
                }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                }
            }

            Release(_app);
            _app = null;

            // Calls like app.Workbooks or presentation.Slides[i] leave COM references behind that keep the
            // Office process alive until they're finalized. Finalize them now, on this (the owning) thread.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            WaitForOwnProcessToExit(TimeSpan.FromSeconds(10));
        }

        private void WaitForOwnProcessToExit(TimeSpan grace)
        {
            if (_ownProcessId is not { } id)
            {
                return;
            }

            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.WaitForExit(grace))
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }

        /// <summary>For the watchdog: kills the Office process this session started, never one the user had open.</summary>
        public void KillOwnProcess()
        {
            if (_ownProcessId is not { } id)
            {
                return;
            }

            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}
