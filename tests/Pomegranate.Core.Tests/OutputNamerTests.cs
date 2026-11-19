using Pomegranate.Core.Jobs;

namespace Pomegranate.Core.Tests;

public class OutputNamerTests
{
    [Fact]
    public void Uses_plain_name_when_free()
    {
        var namer = new OutputNamer(_ => false);

        Assert.Equal(@"C:\out\photo.png", namer.Reserve(@"C:\out", "photo", "png"));
    }

    [Fact]
    public void Adds_a_number_when_the_name_is_taken()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\out\photo.png", @"C:\out\photo (1).png" };
        var namer = new OutputNamer(existing.Contains);

        Assert.Equal(@"C:\out\photo (2).png", namer.Reserve(@"C:\out", "photo", "png"));
    }

    [Fact]
    public void Two_outputs_in_one_batch_get_different_names()
    {
        var namer = new OutputNamer(_ => false);

        var first = namer.Reserve(@"C:\out", "photo", "webp");
        var second = namer.Reserve(@"C:\out", "PHOTO", "webp");

        Assert.Equal(@"C:\out\photo.webp", first);
        Assert.Equal(@"C:\out\PHOTO (1).webp", second);
    }

    [Fact]
    public void Released_names_can_be_reused()
    {
        var namer = new OutputNamer(_ => false);
        var first = namer.Reserve(@"C:\out", "photo", "png");

        namer.Release(first);

        Assert.Equal(first, namer.Reserve(@"C:\out", "photo", "png"));
    }

    [Fact]
    public void Temporary_path_keeps_folder_and_extension()
    {
        var temp = OutputNamer.TemporaryPathFor(@"C:\out\clip.mp4");

        Assert.Equal(@"C:\out", Path.GetDirectoryName(temp));
        Assert.EndsWith(".mp4", temp);
        Assert.StartsWith(".pomegranate-", Path.GetFileName(temp));
    }
}
