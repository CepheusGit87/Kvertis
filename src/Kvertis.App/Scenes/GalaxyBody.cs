using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>Where a body is in its life: waiting for its staggered start, flying in, circling, gone.</summary>
public enum BodyPhase
{
    Waiting,
    Approaching,
    Orbiting,
    Rejected,
    Removed,
}

/// <summary>
/// One staged file in the scene. Mutable and only touched on the drawing thread; the UI thread reads the
/// positions from <see cref="GalaxySnapshot"/> instead.
/// </summary>
public sealed class GalaxyBody
{
    /// <summary>How long a new file needs to fly from the entry point to its orbit.</summary>
    public static readonly TimeSpan ApproachDuration = TimeSpan.FromSeconds(0.75);

    /// <summary>Delay of the first file of a batch.</summary>
    public static readonly TimeSpan ApproachDelay = TimeSpan.FromSeconds(0.1);

    /// <summary>Extra delay per file already waiting.</summary>
    public static readonly TimeSpan ApproachStagger = TimeSpan.FromSeconds(0.16);

    private const int TrailLength = 10;

    private readonly List<Vector2> _trail = new(TrailLength);

    internal GalaxyBody(Guid id, MediaKind kind, string formatLabel, string name, float sizeFactor, int ordinal, Vector2 position)
    {
        Id = id;
        Kind = kind;
        FormatLabel = formatLabel;
        Name = name;
        SizeFactor = sizeFactor;
        Ordinal = ordinal;
        Position = position;
        Origin = position;
    }

    /// <summary>Same value as <c>JobItemViewModel.Id</c>, so the view can match rows and planets.</summary>
    public Guid Id { get; }

    /// <summary><see cref="MediaKind.Unknown"/> for a rejected file: it has no orbit.</summary>
    public MediaKind Kind { get; }

    /// <summary>Chip text, for example "HEIC". Comes ready from the view model; the scene never builds text.</summary>
    public string FormatLabel { get; }

    /// <summary>Chip subtitle after arrival: the file name.</summary>
    public string Name { get; }

    /// <summary>gr = clamp(0.6 + 0.45 * sqrt(MB / 20), 0.6, 1.8).</summary>
    public float SizeFactor { get; }

    /// <summary>n, the running number since the round started; it fixes the angle on the orbit.</summary>
    public int Ordinal { get; }

    public BodyPhase Phase { get; internal set; } = BodyPhase.Waiting;

    /// <summary>Current position on the surface.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Where the approach starts (entry point, a little below the drop line).</summary>
    public Vector2 Origin { get; internal set; }

    /// <summary>Current angle on the orbit, in radians.</summary>
    public float Angle { get; internal set; }

    /// <summary>The moment the approach starts; until then the body waits at the entry point.</summary>
    public TimeSpan PhaseStart { get; internal set; }

    /// <summary>The moment the body reached its orbit, for the arrival chip and the settling halo.</summary>
    public TimeSpan ArrivedAt { get; internal set; }

    /// <summary>True once the file turned out to be unconvertible; it then flies to the rejected card.</summary>
    public bool IsRejected { get; internal set; }

    /// <summary>The orbit index 0..4, or -1 for a rejected body.</summary>
    public int Orbit => IsRejected || Kind == MediaKind.Unknown ? -1 : GalaxyLayout.OrbitOf(Kind);

    /// <summary>True while the body owns halo particles; beyond the budget it is only a dot.</summary>
    public bool HasHalo { get; internal set; }

    /// <summary>Up to ten past positions while approaching, newest last.</summary>
    public IReadOnlyList<Vector2> Trail => _trail;

    internal void PushTrail(Vector2 point)
    {
        _trail.Add(point);
        if (_trail.Count > TrailLength)
        {
            _trail.RemoveAt(0);
        }
    }

    internal void ClearTrail() => _trail.Clear();
}

/// <summary>
/// A dust or halo particle. A struct in one array for cache friendliness; the renderer draws it as a square.
/// <see cref="BodyIndex"/> is -1 for the dust of an orbit.
/// </summary>
public struct GalaxyParticle
{
    /// <summary>The orbit this particle belongs to, 0..4.</summary>
    public int Orbit { get; set; }

    /// <summary>Index into <see cref="GalaxyScene.Bodies"/>, or -1 for the dust of an orbit.</summary>
    public int BodyIndex { get; set; }

    /// <summary>Index into <see cref="GalaxyScene.Planets"/> for the halo of a self-formed planet, else -1.</summary>
    public int PlanetIndex { get; set; }

    /// <summary>Fixed random angle share, 0..1.</summary>
    public float R1 { get; set; }

    /// <summary>Fixed random depth z, 0..1: radial offset, halo radius and size.</summary>
    public float R2 { get; set; }

    /// <summary>Fixed random spare, 0..1: the scatter direction on arrival.</summary>
    public float R3 { get; set; }

    /// <summary>Recomputed every frame; the particle keeps no velocity of its own.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Edge length of the square in DIP.</summary>
    public float Size { get; set; }

    public float Alpha { get; set; }

    public SceneColor Color { get; set; }
}

/// <summary>
/// Particle limits (worksheet "Leistung"). The maximum is hard: beyond it a body gets no halo and no planet is
/// formed. <see cref="HaloPerPlanet"/> and <see cref="MaxPlanets"/> belong to the whirl gimmick (draft: 44).
/// </summary>
public sealed record GalaxyBudget(
    int DustPerOrbit = 160,
    int HaloPerBody = 34,
    int HaloPerBodyManyFiles = 12,
    int ManyFilesFrom = 30,
    int MaxParticles = 4000,
    int HaloPerPlanet = 44,
    int MaxPlanets = 12);

/// <summary>How long the pointer has rested on the same orbit or on the hole; the later gimmicks read it.</summary>
public readonly record struct PointerDwell(int? Orbit, bool OnHole, TimeSpan Duration);

/// <summary>The smoothed pointer position and how strongly it currently pulls (0..1).</summary>
public readonly record struct PointerInfluence(Vector2 Position, float Strength, bool HasPointer);
