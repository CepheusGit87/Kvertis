using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Tests.Images;

/// <summary>A per-test scratch folder with helpers that build images in code (no binary test files).</summary>
public sealed class TestImages : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));

    public TestImages()
    {
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string PathFor(string fileName) => Path.Combine(Directory, fileName);

    public string Write(MagickImage image, string fileName, MagickFormat format)
    {
        var path = PathFor(fileName);
        image.Write(path, format);
        return path;
    }

    public string Solid(string fileName, MagickFormat format, uint width = 64, uint height = 48, IMagickColor<ushort>? color = null)
    {
        using var image = new MagickImage(color ?? MagickColors.Red, width, height);
        return Write(image, fileName, format);
    }

    /// <summary>Noisy content that compresses badly, so quality and size actually matter.</summary>
    public string Noisy(string fileName, MagickFormat format, uint width = 400, uint height = 300)
    {
        using var image = new MagickImage("gradient:red-blue", width, height);
        image.AddNoise(NoiseType.Gaussian);
        return Write(image, fileName, format);
    }

    public string AnimatedGif(string fileName)
    {
        using var frames = new MagickImageCollection();
        foreach (var color in new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue })
        {
            frames.Add(new MagickImage(color, 32, 32) { AnimationDelay = 10 });
        }
        var path = PathFor(fileName);
        frames.Write(path, MagickFormat.Gif);
        return path;
    }

    public string WithExifAndIcc(string fileName, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.Orange, 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "TestCamera");
        exif.SetValue(ExifTag.Software, "TestSoftware");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.SetProfile(exif);
        image.SetProfile(ColorProfiles.SRGB);
        image.Comment = "secret comment";
        return Write(image, fileName, format);
    }

    public string Bytes(string fileName, byte[] content)
    {
        var path = PathFor(fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public static InputInfo Info(string path, FormatId format, int? width = null, int? height = null) =>
        new(path, format, new FormatRegistry().KindOf(format), new FileInfo(path).Length, null, width, height, null, []);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}

/// <summary>Synchronous progress sink; System.Progress posts asynchronously and would make assertions racy.</summary>
public sealed class RecordingProgress : IProgress<ConversionProgress>
{
    private readonly List<ConversionProgress> _reports = [];

    public IReadOnlyList<ConversionProgress> Reports
    {
        get
        {
            lock (_reports)
            {
                return _reports.ToList();
            }
        }
    }

    public void Report(ConversionProgress value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }
    }
}
