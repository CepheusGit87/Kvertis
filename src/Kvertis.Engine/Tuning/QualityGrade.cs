namespace Kvertis.Engine.Tuning;

/// <summary>
/// One quality grade 0..100 as shown in the ring of step 2. The grade is not stored in
/// <see cref="Abstractions.ConversionSettings"/>; it is a view on the settings that
/// <see cref="GradeMapper"/> computes in both directions (ADR-019).
/// </summary>
public readonly record struct QualityGrade(int Value)
{
    /// <summary>The value the ring starts at when nothing else is known.</summary>
    public static readonly QualityGrade Default = new(80);

    public static readonly QualityGrade Lowest = new(0);

    public static readonly QualityGrade Highest = new(100);

    public int Clamped => Math.Clamp(Value, 0, 100);

    /// <summary>Word band for the ring label; the app translates the band, never the number.</summary>
    public GradeBand Band => Clamped switch
    {
        >= 85 => GradeBand.Excellent,
        >= 65 => GradeBand.Good,
        >= 45 => GradeBand.Usable,
        _ => GradeBand.HeavilyReduced,
    };

    public override string ToString() => Clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum GradeBand
{
    HeavilyReduced = 0,
    Usable,
    Good,
    Excellent,
}

/// <summary>
/// A perceivable property of the output that one zone bar controls. Which ones apply depends on
/// <see cref="Abstractions.MediaKind"/> and the output format.
/// </summary>
public enum TuningAspect
{
    /// <summary>Images and video: pixel dimensions (maxDimension). Zone "Schärfe".</summary>
    Sharpness = 0,
    /// <summary>Images: encoder quality. Video: bitrate budget per pixel. Zone "Details".</summary>
    Detail,
    /// <summary>Video only: frame rate (frameRate). Zone "Bewegung".</summary>
    Motion,
    /// <summary>Audio (also the audio track of video): bitrate (audioBitrateKbps). Zone "Klang".</summary>
    Sound,
}

/// <summary>
/// Level of one aspect for the current settings, expressed in the same 0..100 scale as the grade.
/// <paramref name="Adjustable"/> is false when the output format fixes it (lossless, audio-only output,
/// GIF palette) or when the source is already smaller than every rung of the ladder.
/// </summary>
public sealed record AspectLevel(TuningAspect Aspect, int Value, bool Adjustable, AspectDetail Detail);

/// <summary>
/// Numbers the UI puts next to the bar ("1920 statt 4032 px", "128 kbit/s"). Texts come from resources.
/// <paramref name="Pixels"/> and <paramref name="SourcePixels"/> are the longest edge of an image and the
/// height of a video, in pixels, not a pixel count.
/// </summary>
public sealed record AspectDetail(
    int? Pixels = null,
    int? SourcePixels = null,
    int? Kbps = null,
    int? FrameRate = null,
    int? Quality = null,
    bool Lossless = false)
{
    public static readonly AspectDetail None = new();
}
