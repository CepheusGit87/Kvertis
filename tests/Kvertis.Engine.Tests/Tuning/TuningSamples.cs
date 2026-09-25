using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Tests.Tuning;

/// <summary>Inputs shared by the tuning tests. Pure data, no files on disk.</summary>
internal static class TuningSamples
{
    public static readonly FormatRegistry Registry = new();

    public static readonly InputInfo Photo =
        new("/in/photo.jpg", FormatRegistry.Jpg, MediaKind.Image, 8_000_000, null, 4032, 3024, null, []);

    public static readonly InputInfo TinyImage =
        new("/in/icon.png", FormatRegistry.Png, MediaKind.Image, 40_000, null, 320, 240, null, []);

    public static readonly InputInfo Song =
        new("/in/song.flac", FormatRegistry.Flac, MediaKind.Audio, 30_000_000, TimeSpan.FromSeconds(200), null, null, null, []);

    public static readonly InputInfo Clip =
        new("/in/clip.mp4", FormatRegistry.Mp4, MediaKind.Video, 60_000_000, TimeSpan.FromSeconds(60), 1920, 1080, null, []);

    public static readonly InputInfo Report =
        new("/in/report.pdf", FormatRegistry.Pdf, MediaKind.Document, 400_000, null, null, null, 12, []);

    public static readonly InputInfo Part =
        new("/in/part.obj", FormatRegistry.Obj, MediaKind.Model3D, 900_000, null, null, null, null, []);

    /// <summary>One representative (input, output) pair per media kind, used by the "every kind" tests.</summary>
    public static IEnumerable<object[]> GradedPairs()
    {
        yield return [Photo, FormatRegistry.Jpg];
        yield return [Photo, FormatRegistry.WebP];
        yield return [Photo, FormatRegistry.Png];
        yield return [TinyImage, FormatRegistry.Jpg];
        yield return [Song, FormatRegistry.Mp3];
        yield return [Song, FormatRegistry.Opus];
        yield return [Song, FormatRegistry.M4a];
        yield return [Clip, FormatRegistry.Mp4];
        yield return [Clip, FormatRegistry.WebM];
        yield return [Clip, FormatRegistry.Mp3];
    }
}
