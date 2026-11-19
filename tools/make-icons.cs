#:package Magick.NET-Q16-AnyCPU@15.1.0
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false

// Renders assets/pomegranate.svg into the app icon (.ico) and the MSIX logo PNGs.
// Run from the repository root:  dotnet run tools/make-icons.cs
using ImageMagick;

var root = Directory.GetCurrentDirectory();
var svg = Path.Combine(root, "assets", "pomegranate.svg");
var outDir = Path.Combine(root, "src", "Pomegranate.App", "Assets");
Directory.CreateDirectory(outDir);

MagickImage Render(uint size)
{
    var settings = new MagickReadSettings { BackgroundColor = MagickColors.Transparent, Width = size, Height = size };
    var image = new MagickImage(svg, settings);
    image.Resize(new MagickGeometry(size, size) { IgnoreAspectRatio = true });
    return image;
}

using (var ico = new MagickImageCollection())
{
    foreach (var size in new uint[] { 16, 20, 24, 32, 40, 48, 64, 256 })
    {
        ico.Add(Render(size));
    }

    ico.Write(Path.Combine(outDir, "Pomegranate.ico"), MagickFormat.Ico);
}

void Png(string name, uint width, uint height, uint iconSize)
{
    using var canvas = new MagickImage(MagickColors.Transparent, width, height);
    using var icon = Render(iconSize);
    canvas.Composite(icon, Gravity.Center, CompositeOperator.Over);
    canvas.Write(Path.Combine(outDir, name), MagickFormat.Png);
}

Png("Square44x44Logo.png", 44, 44, 40);
foreach (var size in new uint[] { 16, 24, 32, 48, 256 })
{
    Png($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size, size, size);
}

Png("Square150x150Logo.png", 150, 150, 110);
Png("Wide310x150Logo.png", 310, 150, 110);
Png("StoreLogo.png", 50, 50, 46);
Png("Pomegranate-256.png", 256, 256, 256);

Console.WriteLine($"Wrote icons to {outDir}");
