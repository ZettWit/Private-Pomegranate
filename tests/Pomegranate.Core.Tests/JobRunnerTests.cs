using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using static Pomegranate.Core.Formats.KnownFormats;

namespace Pomegranate.Core.Tests;

public sealed class JobRunnerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pomegranate-jobs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string MakeFile(string name, string content = "source")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static JobRunner Runner(IConverter converter, int limit = 4) =>
        new(FormatCatalog.Default, [converter], new Dictionary<EngineKind, int> { [converter.Engine] = limit });

    private static async Task<ConversionBatch> RunAsync(JobRunner runner, FileFormat target, params string[] sources)
    {
        var batch = runner.Start(sources, target, new QualitySettings());
        await batch.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return batch;
    }

    [Fact]
    public async Task Successful_conversion_lands_next_to_the_original()
    {
        var source = MakeFile("photo.png");
        var batch = await RunAsync(Runner(new FakeConverter()), WebP, source);

        var job = Assert.Single(batch.Jobs);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Path.Combine(_folder, "photo.webp"), job.OutputPath);
        Assert.Equal("converted:source", File.ReadAllText(job.OutputPath!));
        Assert.Equal("source", File.ReadAllText(source)); // original untouched
        Assert.Equal(1, job.Progress);
        Assert.Empty(Directory.GetFiles(_folder, ".pomegranate-*"));
    }

    [Fact]
    public async Task Existing_output_name_gets_a_number()
    {
        var source = MakeFile("photo.png");
        MakeFile("photo.webp", "already here");

        var batch = await RunAsync(Runner(new FakeConverter()), WebP, source);

        Assert.Equal(Path.Combine(_folder, "photo (1).webp"), batch.Jobs[0].OutputPath);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(_folder, "photo.webp")));
    }

    [Fact]
    public async Task Files_with_the_same_base_name_get_different_outputs()
    {
        var a = MakeFile("photo.png");
        var b = MakeFile("photo.bmp");

        var batch = await RunAsync(Runner(new FakeConverter()), WebP, a, b);

        var outputs = batch.Jobs.Select(j => Path.GetFileName(j.OutputPath)).Order().ToArray();
        Assert.Equal(["photo (1).webp", "photo.webp"], outputs);
    }

    [Fact]
    public async Task Failure_leaves_no_output_and_reports_the_error()
    {
        var source = MakeFile("photo.png");

        var batch = await RunAsync(Runner(new FakeConverter { FailWith = "codec exploded" }), WebP, source);

        var job = batch.Jobs[0];
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("codec exploded", job.Error);
        Assert.Null(job.OutputPath);
        Assert.Equal(["photo.png"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Engine_that_writes_nothing_is_a_failure()
    {
        var source = MakeFile("photo.png");

        var batch = await RunAsync(Runner(new FakeConverter { WriteOutput = false }), WebP, source);

        Assert.Equal(JobStatus.Failed, batch.Jobs[0].Status);
    }

    [Fact]
    public async Task Cancelling_stops_the_job_and_cleans_up()
    {
        var source = MakeFile("clip.mov");
        var converter = new FakeConverter(EngineKind.Media) { WaitForCancel = true };
        var batch = Runner(converter).Start([source], Mp4, new QualitySettings());

        await converter.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        batch.CancelAll();
        await batch.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.Canceled, batch.Jobs[0].Status);
        Assert.Equal(["clip.mov"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Files_already_in_the_target_format_are_skipped()
    {
        var png = MakeFile("one.png");
        var jpg = MakeFile("two.jpg");

        var batch = await RunAsync(Runner(new FakeConverter()), Jpg, png, jpg);

        Assert.Equal(JobStatus.Succeeded, batch.Jobs[0].Status);
        Assert.Equal(JobStatus.Skipped, batch.Jobs[1].Status);
        Assert.False(File.Exists(Path.Combine(_folder, "two (1).jpg")));
    }

    [Fact]
    public async Task Missing_engine_fails_cleanly()
    {
        var source = MakeFile("song.wav");

        var batch = await RunAsync(Runner(new FakeConverter(EngineKind.Image)), Mp3, source);

        Assert.Equal(JobStatus.Failed, batch.Jobs[0].Status);
        Assert.Contains("Media", batch.Jobs[0].Error);
    }

    [Fact]
    public async Task Engine_limit_caps_how_many_run_at_once()
    {
        var converter = new FakeConverter { DelayMs = 50 };
        var sources = Enumerable.Range(0, 8).Select(i => MakeFile($"p{i}.png")).ToArray();

        var batch = await RunAsync(Runner(converter, limit: 2), WebP, sources);

        Assert.All(batch.Jobs, j => Assert.Equal(JobStatus.Succeeded, j.Status));
        Assert.Equal(2, converter.MaxConcurrent);
    }

    [Fact]
    public async Task Output_folder_setting_redirects_output()
    {
        var source = MakeFile("photo.png");
        var outFolder = Directory.CreateDirectory(Path.Combine(_folder, "out")).FullName;

        var batch = Runner(new FakeConverter()).Start([source], WebP, new QualitySettings(), outFolder);
        await batch.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(outFolder, "photo.webp"), batch.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task Several_pages_go_into_a_pages_folder()
    {
        var source = MakeFile("report.pdf");
        var batch = await RunAsync(Runner(new FakeConverter(EngineKind.PdfImage) { Pages = 3 }), Png, source);

        var job = batch.Jobs[0];
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Path.Combine(_folder, "report (pages)"), job.OutputPath);
        Assert.Equal(3, job.OutputCount);
        Assert.Equal(
            ["report - 01.png", "report - 02.png", "report - 03.png"],
            Directory.GetFiles(job.OutputPath!).Select(Path.GetFileName).Order());
        Assert.Empty(Directory.GetDirectories(_folder, ".pomegranate-*"));
    }

    [Fact]
    public async Task One_page_is_just_a_file()
    {
        var source = MakeFile("report.pdf");
        var batch = await RunAsync(Runner(new FakeConverter(EngineKind.PdfImage) { Pages = 1 }), Png, source);

        Assert.Equal(Path.Combine(_folder, "report.png"), batch.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task Named_parts_go_next_to_the_original_with_safe_names()
    {
        var source = MakeFile("budget.xlsx");
        var converter = new FakeConverter(EngineKind.Sheet) { Sheets = ["Sheet1", "Q1/Q2"] };

        var batch = await RunAsync(Runner(converter), Csv, source);

        Assert.Equal(2, batch.Jobs[0].OutputCount);
        Assert.True(File.Exists(Path.Combine(_folder, "budget - Sheet1.csv")));
        Assert.True(File.Exists(Path.Combine(_folder, "budget - Q1_Q2.csv")));
    }

    [Fact]
    public void Page_numbers_get_more_digits_past_99()
    {
        var output = ConversionOutput.Pages(Enumerable.Range(0, 120).Select(i => $"p{i}").ToArray());

        Assert.Equal("001", output.Parts[0].Suffix);
        Assert.Equal("120", output.Parts[^1].Suffix);
    }

    private sealed class FakeConverter(EngineKind engine = EngineKind.Image) : IConverter
    {
        private int _running;
        private int _maxConcurrent;

        public EngineKind Engine { get; } = engine;

        public string? FailWith { get; init; }

        public bool WriteOutput { get; init; } = true;

        public bool WaitForCancel { get; init; }

        public int DelayMs { get; init; }

        public int MaxConcurrent => _maxConcurrent;

        /// <summary>When set, writes this many page files into the work folder instead of one output.</summary>
        public int Pages { get; init; }

        /// <summary>When set, writes one named file per sheet next to the original.</summary>
        public string[] Sheets { get; init; } = [];

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ConversionOutput> ConvertAsync(ConversionRequest request, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (Pages > 0)
            {
                var files = new List<string>();
                for (var i = 0; i < Pages; i++)
                {
                    var page = Path.Combine(request.WorkFolder, $"page{i}.png");
                    await File.WriteAllTextAsync(page, $"page {i}", cancellationToken);
                    files.Add(page);
                }

                return ConversionOutput.Pages(files);
            }

            if (Sheets.Length > 0)
            {
                var parts = new List<OutputPart>();
                foreach (var sheet in Sheets)
                {
                    var file = Path.Combine(request.WorkFolder, Guid.NewGuid() + ".csv");
                    await File.WriteAllTextAsync(file, sheet, cancellationToken);
                    parts.Add(new OutputPart(file, sheet));
                }

                return ConversionOutput.Named(parts);
            }

            var now = Interlocked.Increment(ref _running);
            InterlockedMax(ref _maxConcurrent, now);
            try
            {
                Started.TrySetResult();
                progress.Report(0.5);
                await File.WriteAllTextAsync(request.OutputPath, "partial", cancellationToken);

                if (WaitForCancel)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                if (DelayMs > 0)
                {
                    await Task.Delay(DelayMs, cancellationToken);
                }

                if (FailWith is not null)
                {
                    throw new ConversionException(FailWith);
                }

                if (WriteOutput)
                {
                    var text = await File.ReadAllTextAsync(request.SourcePath, cancellationToken);
                    await File.WriteAllTextAsync(request.OutputPath, "converted:" + text, cancellationToken);
                }
                else
                {
                    File.Delete(request.OutputPath);
                }

                return ConversionOutput.Single;
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
