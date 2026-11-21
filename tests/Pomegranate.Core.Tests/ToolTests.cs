using Pomegranate.Core.Formats;
using Pomegranate.Core.Jobs;
using Pomegranate.Core.Settings;
using Pomegranate.Core.Tools;

namespace Pomegranate.Core.Tests;

public sealed class ToolCatalogTests
{
    private static readonly ToolCatalog Catalog = ToolCatalog.Default;

    private static ToolKind[] Offer(params string[] paths) =>
        Catalog.Offer(paths, FormatCatalog.Default, p => p.EndsWith('\\')).Select(d => d.Kind).ToArray();

    [Fact]
    public void Definitions_are_indexed_by_kind()
    {
        foreach (var kind in Enum.GetValues<ToolKind>())
        {
            Assert.Equal(kind, ToolCatalog.Definition(kind).Kind);
        }
    }

    [Fact]
    public void A_photo_gets_the_image_tools_in_wheel_order()
    {
        Assert.Equal(
            [ToolKind.Compress, ToolKind.Resize, ToolKind.Crop, ToolKind.Rotate, ToolKind.Annotate, ToolKind.Redact,
             ToolKind.StripMetadata, ToolKind.ReadCode, ToolKind.CopyText, ToolKind.Zip],
            Offer("photo.jpg"));
    }

    [Fact]
    public void A_video_gets_the_video_tools()
    {
        Assert.Equal(
            [ToolKind.Compress, ToolKind.Trim, ToolKind.Crop, ToolKind.Rotate, ToolKind.Speed, ToolKind.Redact, ToolKind.Mute,
             ToolKind.ExtractAudio, ToolKind.StripMetadata, ToolKind.Zip],
            Offer("clip.mp4"));
    }

    [Fact]
    public void Lossless_audio_has_no_compress()
    {
        Assert.Equal([ToolKind.Trim, ToolKind.Speed, ToolKind.Bleep, ToolKind.Normalize, ToolKind.StripMetadata, ToolKind.Zip], Offer("song.flac"));
        Assert.Contains(ToolKind.Compress, Offer("song.mp3"));
    }

    [Fact]
    public void Merge_needs_two_PDFs_and_editors_need_one_file()
    {
        Assert.DoesNotContain(ToolKind.Merge, Offer("a.pdf"));
        Assert.Contains(ToolKind.Pages, Offer("a.pdf"));

        var two = Offer("a.pdf", "b.pdf");
        Assert.Contains(ToolKind.Merge, two);
        Assert.DoesNotContain(ToolKind.Pages, two);
        Assert.Contains(ToolKind.Split, two);
    }

    [Fact]
    public void Several_files_get_the_tools_that_work_for_all_of_them()
    {
        // Compress, Rotate and Strip metadata exist for both, through different engines; editors need one file.
        Assert.Equal([ToolKind.Compress, ToolKind.Rotate, ToolKind.StripMetadata, ToolKind.Zip], Offer("photo.jpg", "clip.mp4"));
    }

    [Fact]
    public void Read_only_images_get_reading_tools_only()
    {
        Assert.Equal([ToolKind.ReadCode, ToolKind.CopyText, ToolKind.Zip], Offer("phone.heic"));
    }

    [Fact]
    public void Folders_and_unknown_files_get_Zip_only()
    {
        Assert.Equal([ToolKind.Zip], Offer("D:\\Photos\\"));
        Assert.Equal([ToolKind.Zip], Offer("photo.jpg", "D:\\Photos\\"));
        Assert.Equal([ToolKind.Zip], Offer("notes.xyz"));
        Assert.Empty(Offer());
    }

    [Fact]
    public void Archives_can_be_extracted()
    {
        Assert.Equal([ToolKind.Extract, ToolKind.Zip], Offer("backup.tar.gz"));
    }

    [Fact]
    public void Without_FFmpeg_media_tools_disappear()
    {
        var noMedia = Catalog.WithEngines([EngineKind.Image, EngineKind.Archive, EngineKind.PdfTools, EngineKind.Recognition]);
        Assert.Equal([ToolKind.Zip], noMedia.Offer(["clip.mp4"], FormatCatalog.Default, _ => false).Select(d => d.Kind));
        Assert.Contains(noMedia.Offer(["photo.png"], FormatCatalog.Default, _ => false), d => d.Kind == ToolKind.Compress);
    }

    [Fact]
    public void Options_have_defaults_on_their_ring()
    {
        foreach (var definition in ToolCatalog.Definitions.Where(d => d.Options.Count > 0))
        {
            Assert.Contains(definition.DefaultOption, definition.Options);
        }

        Assert.Equal(ToolOption.Medium, ToolCatalog.Definition(ToolKind.Compress).DefaultOption);
        Assert.Equal(ToolOption.Speed2, ToolCatalog.Definition(ToolKind.Speed).DefaultOption);
    }

    [Theory]
    [InlineData(ToolOption.Speed05, 0.5, "0.5x")]
    [InlineData(ToolOption.Speed15, 1.5, "1.5x")]
    [InlineData(ToolOption.Speed2, 2, "2x")]
    [InlineData(ToolOption.Speed4, 4, "4x")]
    public void Speed_options(ToolOption option, double factor, string text)
    {
        Assert.Equal(factor, ToolOptions.SpeedFactor(option));
        Assert.Equal(text, ToolOptions.SpeedText(option));
    }
}

public sealed class NaturalComparerTests
{
    [Fact]
    public void Numbers_sort_as_numbers()
    {
        string[] names = ["Part 10.pdf", "part 2.pdf", "Part 1.pdf", "Part 01b.pdf", "Appendix.pdf"];
        Assert.Equal(
            ["Appendix.pdf", "Part 1.pdf", "Part 01b.pdf", "part 2.pdf", "Part 10.pdf"],
            names.OrderBy(n => n, NaturalComparer.Instance));
    }
}

public sealed class ToolRunnerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pomegranate-tools-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string MakeFile(string name, string content = "source")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ToolRunner Runner(ITool tool) => new(ToolCatalog.Default, FormatCatalog.Default, [tool]);

    private static async Task<ConversionBatch> RunAsync(ToolRunner runner, ToolInvocation invocation, params string[] paths)
    {
        var batch = runner.Start(invocation, invocation.Kind.ToString(), paths, new QualitySettings(), new ToolSettings());
        await batch.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return batch;
    }

    [Fact]
    public async Task Result_gets_the_tool_suffix_next_to_the_original()
    {
        var source = MakeFile("photo.jpeg");
        var tool = new FakeTool(EngineKind.Image);
        var batch = await RunAsync(Runner(tool), new(ToolKind.Compress), source);

        var job = Assert.Single(batch.Jobs);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Path.Combine(_folder, "photo (compressed).jpeg"), job.OutputPath);
        Assert.Equal(ToolOption.Medium, tool.Requests.Single().Option); // the default
        Assert.Equal("source", File.ReadAllText(source));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(_folder), p => Path.GetFileName(p).StartsWith(".pomegranate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clashes_get_numbers_and_speed_names_the_factor()
    {
        var source = MakeFile("clip.mp4");
        MakeFile("clip (2x).mp4", "older");
        var batch = await RunAsync(Runner(new FakeTool(EngineKind.Media)), new(ToolKind.Speed), source);
        Assert.Equal(Path.Combine(_folder, "clip (2x) (1).mp4"), batch.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task Each_file_is_its_own_job_but_merge_is_one_in_name_order()
    {
        var a = MakeFile("Part 10.pdf");
        var b = MakeFile("Part 2.pdf");
        var tool = new FakeTool(EngineKind.PdfTools);
        var batch = await RunAsync(Runner(tool), new(ToolKind.Merge), a, b);

        var job = Assert.Single(batch.Jobs);
        Assert.Equal([b, a], tool.Requests.Single().Sources);
        Assert.Equal(Path.Combine(_folder, "Part 2 (merged).pdf"), job.OutputPath);

        var each = await RunAsync(Runner(new FakeTool(EngineKind.PdfTools)), new(ToolKind.StripMetadata), a, b);
        Assert.Equal(2, each.Jobs.Count);
        Assert.Contains(each.Jobs, j => j.OutputPath == Path.Combine(_folder, "Part 2 (no metadata).pdf"));
    }

    [Fact]
    public async Task Zip_is_named_after_the_item_or_the_folder()
    {
        var one = MakeFile("notes.txt");
        var single = await RunAsync(Runner(new FakeTool(EngineKind.Archive, "zip")), new(ToolKind.Zip), one);
        Assert.Equal(Path.Combine(_folder, "notes.zip"), single.Jobs[0].OutputPath);

        var two = MakeFile("more.txt");
        var both = await RunAsync(Runner(new FakeTool(EngineKind.Archive, "zip")), new(ToolKind.Zip), one, two);
        Assert.Equal(Path.Combine(_folder, Path.GetFileName(_folder) + ".zip"), both.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task Extracted_folders_and_pages_get_their_own_folders()
    {
        var archive = MakeFile("backup.tar.gz");
        var extract = await RunAsync(Runner(new FakeTool(EngineKind.Archive, output: ToolOutputKind.Folder)), new(ToolKind.Extract), archive);
        Assert.Equal(Path.Combine(_folder, "backup"), extract.Jobs[0].OutputPath);
        Assert.True(File.Exists(Path.Combine(_folder, "backup", "inside.txt")));

        var pdf = MakeFile("scan.pdf");
        var split = await RunAsync(Runner(new FakeTool(EngineKind.PdfTools, output: ToolOutputKind.Pages)), new(ToolKind.Split), pdf);
        var job = split.Jobs[0];
        Assert.Equal(Path.Combine(_folder, "scan (pages)"), job.OutputPath);
        Assert.Equal(3, job.OutputCount);
        Assert.True(File.Exists(Path.Combine(_folder, "scan (pages)", "scan - 03.pdf")));
    }

    [Fact]
    public async Task Pages_editor_names_a_selection_differently()
    {
        var pdf = MakeFile("scan.pdf");
        var edit = new PagesEdit([new PageEdit(0, 90)], Selection: true);
        var batch = await RunAsync(Runner(new FakeTool(EngineKind.PdfTools)), new(ToolKind.Pages, Edit: edit), pdf);
        Assert.Equal(Path.Combine(_folder, "scan (selection).pdf"), batch.Jobs[0].OutputPath);
    }

    [Fact]
    public async Task Text_results_write_no_file()
    {
        var photo = MakeFile("code.png");
        var batch = await RunAsync(Runner(new FakeTool(EngineKind.Recognition, output: ToolOutputKind.Text)), new(ToolKind.ReadCode), photo);
        var job = batch.Jobs[0];
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("text from code.png", job.ResultText);
        Assert.Null(job.OutputPath);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Nothing_to_do_is_a_skip_with_a_reason()
    {
        var photo = MakeFile("tiny.png");
        var batch = await RunAsync(Runner(new FakeTool(EngineKind.Image) { NothingToDo = true }), new(ToolKind.Compress), photo);
        var job = batch.Jobs[0];
        Assert.Equal(JobStatus.Skipped, job.Status);
        Assert.Equal("Already as small as it gets.", job.Note);
        Assert.Single(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task Failures_and_missing_engines_leave_nothing_behind()
    {
        var photo = MakeFile("broken.png");
        var failed = await RunAsync(Runner(new FakeTool(EngineKind.Image) { Fail = true }), new(ToolKind.Resize), photo);
        Assert.Equal(JobStatus.Failed, failed.Jobs[0].Status);
        Assert.Equal("boom", failed.Jobs[0].Error);

        var video = MakeFile("clip.mp4");
        var missing = await RunAsync(Runner(new FakeTool(EngineKind.Image)), new(ToolKind.Mute), video);
        Assert.Equal(JobStatus.Failed, missing.Jobs[0].Status);

        Assert.Equal(2, Directory.GetFileSystemEntries(_folder).Length);
    }

    [Fact]
    public async Task Cancelling_removes_the_work_folder()
    {
        var source = MakeFile("long.mp4");
        var tool = new FakeTool(EngineKind.Media) { Delay = TimeSpan.FromSeconds(30) };
        var batch = Runner(tool).Start(new(ToolKind.Compress), "Compress", [source], new QualitySettings(), new ToolSettings());
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        batch.CancelAll();
        await batch.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(JobStatus.Canceled, batch.Jobs[0].Status);
        Assert.Single(Directory.GetFileSystemEntries(_folder));
    }

    private sealed class FakeTool(EngineKind engine, string? extension = null, ToolOutputKind output = ToolOutputKind.File) : ITool
    {
        public EngineKind Engine => engine;

        public List<ToolRequest> Requests { get; } = [];

        public bool NothingToDo { get; init; }

        public bool Fail { get; init; }

        public TimeSpan Delay { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ToolOutput> RunAsync(ToolRequest request, IProgress<double> progress, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            Started.TrySetResult();
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Fail)
            {
                throw new InvalidOperationException("boom");
            }

            if (NothingToDo)
            {
                throw new NothingToDoException("Already as small as it gets.");
            }

            switch (output)
            {
                case ToolOutputKind.Text:
                    return ToolOutput.FromText($"text from {Path.GetFileName(request.Source)}");
                case ToolOutputKind.Folder:
                    var folder = Directory.CreateDirectory(Path.Combine(request.WorkFolder, "out")).FullName;
                    await File.WriteAllTextAsync(Path.Combine(folder, "inside.txt"), "x", cancellationToken);
                    return ToolOutput.Folder(folder);
                case ToolOutputKind.Pages:
                    var pages = new List<string>();
                    for (var i = 1; i <= 3; i++)
                    {
                        var page = request.WorkFile("pdf", $"p{i}");
                        await File.WriteAllTextAsync(page, $"page {i}", cancellationToken);
                        pages.Add(page);
                    }

                    return ToolOutput.Pages(pages);
                default:
                    var path = request.WorkFile(extension ?? request.SourceExtension);
                    await File.WriteAllTextAsync(path, "tool output", cancellationToken);
                    return ToolOutput.File(path);
            }
        }
    }
}
