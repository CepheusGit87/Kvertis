using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// Orbit geometry for one canvas size: pure functions of width and height, no state (ADR-022). All lengths
/// are device independent pixels at scale 1 and are stretched with <see cref="Scale"/>.
/// </summary>
public sealed record GalaxyLayout(float Width, float Height)
{
    public const int OrbitCount = 5;

    /// <summary>Base radius of the innermost orbit in DIP.</summary>
    public const float BaseRadiusInner = 92f;

    /// <summary>Distance between two orbits in DIP.</summary>
    public const float RadiusStep = 56f;

    /// <summary>Vertical squash of an orbit in the overview.</summary>
    public const float FlattenOverview = 0.35f;

    /// <summary>Vertical squash of the single orbit while zoomed in.</summary>
    public const float FlattenZoomed = 0.50f;

    /// <summary>Radius of the hole in the overview, in DIP.</summary>
    public const float HoleRadiusOverview = 18f;

    /// <summary>Radius of the hole while zoomed in, in DIP.</summary>
    public const float HoleRadiusZoomed = 10f;

    /// <summary>Inner to outer: documents, 3D models, video, audio, images (worksheet step 1).</summary>
    public static readonly IReadOnlyList<MediaKind> OrbitOrder =
    [
        MediaKind.Document,
        MediaKind.Model3D,
        MediaKind.Video,
        MediaKind.Audio,
        MediaKind.Image,
    ];

    /// <summary>clamp(min(W / 700, H / 280), 0.6, 1.5).</summary>
    public float Scale { get; } = ScaleOf(Width, Height);

    /// <summary>The hole sits slightly below the middle of the surface.</summary>
    public Vector2 Center { get; } = new(Width / 2f, Height / 2f + 10f * ScaleOf(Width, Height));

    /// <summary>Where new files enter the scene.</summary>
    public Vector2 Entry { get; } = new(Width / 2f, 24f * ScaleOf(Width, Height));

    /// <summary>Horizontal radius of the single orbit while zoomed in.</summary>
    public float ZoomRadius { get; } = ZoomRadiusOf(Width, ScaleOf(Width, Height));

    /// <summary>The orbit index 0..4 (inner to outer) of a kind.</summary>
    public static int OrbitOf(MediaKind kind)
    {
        for (var i = 0; i < OrbitOrder.Count; i++)
        {
            if (OrbitOrder[i] == kind)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only the five known media kinds have an orbit.");
    }

    /// <summary>The kind of an orbit index 0..4.</summary>
    public static MediaKind KindOf(int orbit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orbit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(orbit, OrbitCount);
        return OrbitOrder[orbit];
    }

    /// <summary>(92 + 56 * orbit) * Scale.</summary>
    public float BaseRadius(int orbit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orbit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(orbit, OrbitCount);
        return (BaseRadiusInner + RadiusStep * orbit) * Scale;
    }

    /// <summary>The unit particle sizes and halo radii grow with the orbit: sqrt(clamp(rx / 92, 0.5, 2.6)).</summary>
    public float ParticleScale(int orbit) =>
        MathF.Sqrt(Math.Clamp(BaseRadius(orbit) / (BaseRadiusInner * Scale), 0.5f, 2.6f));

    private static float ScaleOf(float width, float height) =>
        Math.Clamp(Math.Min(width / 700f, height / 280f), 0.6f, 1.5f);

    private static float ZoomRadiusOf(float width, float scale)
    {
        var radius = Math.Min((width - 600f) / 2f, 240f) * scale;
        return Math.Max(radius, 120f * scale);
    }
}
