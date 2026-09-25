using System.Numerics;

namespace Kvertis.App.Scenes;

/// <summary>
/// Where the gimmicks of step 1 are (design/ENTSCHEIDUNGEN.md "Spielereien in Schritt 1"). One deterministic
/// timeline: the whirl states follow a pointer resting on an orbit, the suction states a pointer resting on
/// the hole. Both start from <see cref="Idle"/> and only ever run in the overview.
/// </summary>
public enum GimmickState
{
    Idle,

    /// <summary>The pointer rests on an orbit and a whirl gathers there (about 4 s).</summary>
    Whirl,

    /// <summary>The whirl collapsed into a small flash (0.8 s); a planet was just born.</summary>
    Flash,

    /// <summary>The planet circles; nothing more happens until the pointer moves away.</summary>
    NewPlanet,

    /// <summary>The pointer rests on the hole and it charges (10 s in all).</summary>
    Suction,

    /// <summary>Same as <see cref="Suction"/> while a self-formed planet spirals in and crashes.</summary>
    Impacts,

    /// <summary>The last 2 s of the charge: the whole surface is being swallowed.</summary>
    Swallowing,

    /// <summary>The explosion (1.4 s): everything flies out.</summary>
    BigBang,

    /// <summary>The universe forms again (3.4 s): a bright point, the hole, and every particle spirals home.</summary>
    NewUniverse,
}

/// <summary>The six kinds a self-formed planet cycles through (draft PTYPEN, in that order).</summary>
public enum PlanetKind
{
    /// <summary>A ring and three faint bands in the orbit colour.</summary>
    Ringed,

    /// <summary>Glowing red with three craters.</summary>
    Glowing,

    /// <summary>Icy with clouds and one moon.</summary>
    Icy,

    /// <summary>A gas giant with six bands, 1.3 times as large.</summary>
    GasGiant,

    /// <summary>A rocky planet with six craters and two moons.</summary>
    Rocky,

    /// <summary>An ocean planet with clouds and a ring.</summary>
    Ocean,
}

/// <summary>
/// A planet the user formed with the whirl. Decorative only: it belongs to no file, the big bang sweeps it
/// away, and the renderer paints it from these values alone.
/// </summary>
public sealed class GalaxyPlanet
{
    private const int TrailLength = 14;

    private readonly List<Vector2> _trail = new(TrailLength);

    internal GalaxyPlanet(
        int orbit,
        float angle,
        TimeSpan bornAt,
        PlanetKind kind,
        SceneColor primary,
        SceneColor secondary,
        float radius,
        float tilt,
        float moonPhase,
        float seed)
    {
        Orbit = orbit;
        Angle = angle;
        BornAt = bornAt;
        Kind = kind;
        Primary = primary;
        Secondary = secondary;
        Radius = radius;
        Tilt = tilt;
        MoonPhase = moonPhase;
        Seed = seed;
    }

    /// <summary>The orbit it circles on, 0..4.</summary>
    public int Orbit { get; }

    /// <summary>Its angle relative to the orbit phase, so it moves with the orbit.</summary>
    public float Angle { get; }

    public TimeSpan BornAt { get; }

    public PlanetKind Kind { get; }

    /// <summary>The surface colour.</summary>
    public SceneColor Primary { get; }

    /// <summary>The colour of bands and craters.</summary>
    public SceneColor Secondary { get; }

    /// <summary>The radius at scale 1 and without the orbit's particle scale, in DIP (9..12, ×1.3 for a gas giant).</summary>
    public float Radius { get; }

    /// <summary>The tilt of ring and bands, in radians.</summary>
    public float Tilt { get; }

    /// <summary>Where the moons start.</summary>
    public float MoonPhase { get; }

    /// <summary>A fixed random value that places craters and shifts bands and clouds.</summary>
    public float Seed { get; }

    /// <summary>Number of rings (0 or 1).</summary>
    public bool HasRing => Kind is PlanetKind.Ringed or PlanetKind.Ocean;

    public bool IsGlowing => Kind == PlanetKind.Glowing;

    public bool HasClouds => Kind is PlanetKind.Icy or PlanetKind.Ocean;

    public int Bands => Kind switch
    {
        PlanetKind.Ringed => 3,
        PlanetKind.GasGiant => 6,
        _ => 0,
    };

    public int Craters => Kind switch
    {
        PlanetKind.Glowing => 3,
        PlanetKind.Rocky => 6,
        _ => 0,
    };

    public int Moons => Kind switch
    {
        PlanetKind.Icy => 1,
        PlanetKind.Rocky => 2,
        _ => 0,
    };

    /// <summary>0 while circling, 1 when it hits the hole; grows while the hole charges.</summary>
    public float Fall { get; internal set; }

    /// <summary>The charge of the hole at which this planet lets go (0.12..0.52).</summary>
    public float FallStart { get; internal set; }

    /// <summary>Current position on the surface, already spiralling in while falling.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>1 while circling, down to 0.35 at the hole: the planet shrinks as it falls.</summary>
    public float Shrink { get; internal set; } = 1f;

    /// <summary>The trail of light while falling, oldest first.</summary>
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
/// A small explosion: the birth of a planet on its orbit, or an impact in the hole. Lives 0.8 s; the renderer
/// paints two glows and two expanding rings from it.
/// </summary>
public readonly record struct GalaxyBurst(Vector2 Position, TimeSpan At, SceneColor Color, float Size);
