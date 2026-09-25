using System.Numerics;
using System.Runtime.InteropServices;

namespace Kvertis.App.Scenes;

/// <summary>
/// The two gimmicks of step 1 (design/ENTSCHEIDUNGEN.md, draft <c>design/faecher-galaxie.html</c>:
/// <c>planetTick</c>, <c>wirbelPos</c>, <c>urknallTick</c>, <c>urknallPos</c>, <c>uiSog</c>, <c>planetenSog</c>).
/// A pointer resting about four seconds on an orbit gathers a whirl there, a flash, and a new planet is born
/// (six kinds in turn). A pointer resting ten seconds on the hole charges it: the pull grows ever faster, the
/// self-formed planets crash into it, in the last two seconds the whole XAML surface is swallowed, then the
/// big bang and a new universe. Only in the overview, only when the host enabled it, never with reduced
/// motion. Files are never lost: no body is touched, only the particles fly and come back.
/// </summary>
public sealed partial class GalaxyScene
{
    /// <summary>How long the pointer has to rest on an orbit until a planet is born, in seconds.</summary>
    public const float WhirlSeconds = 4f;

    /// <summary>The dwell timer has to reach this before the whirl charges, so a drifting pointer never charges.</summary>
    public static readonly TimeSpan WhirlSettle = TimeSpan.FromSeconds(0.15);

    /// <summary>How long the pointer has to rest on the hole until it explodes, in seconds.</summary>
    public const float ChargeSeconds = 10f;

    /// <summary>The surface is swallowed once the charge passes this (the last two seconds).</summary>
    public const float SwallowFrom = 0.8f;

    /// <summary>Duration of the explosion, in seconds.</summary>
    public const float ExplosionSeconds = 1.4f;

    /// <summary>Duration of the new universe until everything is home again, in seconds.</summary>
    public const float RebirthSeconds = 3.4f;

    /// <summary>How long a burst (planet birth, impact) is drawn, in seconds.</summary>
    public const float BurstSeconds = 0.8f;

    /// <summary>How long the flash in the hole after an impact lasts, in seconds.</summary>
    public const float ImpactFlashSeconds = 0.6f;

    /// <summary>How long a new planet takes to spring to its full size, in seconds.</summary>
    public const float PlanetSettleSeconds = 0.7f;

    /// <summary>Vertical squash of the whirl disc.</summary>
    private const float WhirlFlatten = 0.4f;

    /// <summary>Vertical squash used for the suction spiral of the particles.</summary>
    private const float SuctionFlatten = 0.5f;

    private readonly List<GalaxyPlanet> _planets = [];
    private readonly List<GalaxyBurst> _bursts = [];

    private bool _gimmicksEnabled;
    private bool _dwellReset;

    // whirl
    private bool _whirlActive;
    private bool _whirlLocked;
    private bool _whirlCollapsing;
    private int _whirlOrbit;
    private Vector2 _whirlPosition;
    private float _whirlCharge;
    private float _whirlSpin;
    private int _planetCounter = -1;
    private TimeSpan _lastBirth;

    // hole
    private BangPhase _bang = BangPhase.Idle;
    private TimeSpan _bangStart;
    private float _holeCharge;
    private float _holeSpin;
    private bool _holeLocked;
    private Vector2 _shake;
    private TimeSpan? _impactAt;
    private TimeSpan? _flashAt;
    private SceneColor _flashColor;

    private enum BangPhase
    {
        Idle,
        Exploding,
        Rebirth,
    }

    /// <summary>Where the gimmick timeline is.</summary>
    public GimmickState Gimmick { get; private set; } = GimmickState.Idle;

    /// <summary>True once the host allowed the gimmicks (<see cref="SetGimmicksEnabled"/>).</summary>
    public bool GimmicksEnabled => _gimmicksEnabled;

    /// <summary>0..1: how far the whirl on an orbit has gathered.</summary>
    public float WhirlCharge => _whirlActive && !_whirlLocked ? _whirlCharge : 0f;

    /// <summary>The centre of the whirl, valid while <see cref="WhirlCharge"/> is above 0.</summary>
    public Vector2 WhirlPosition => _whirlPosition;

    /// <summary>The orbit the whirl sits on, valid while <see cref="WhirlCharge"/> is above 0.</summary>
    public int WhirlOrbit => _whirlOrbit;

    /// <summary>The accumulated rotation of the whirl, in radians.</summary>
    public float WhirlSpin => _whirlSpin;

    /// <summary>0..1: how far the hole has charged.</summary>
    public float HoleCharge => _holeCharge;

    /// <summary>The accumulated rotation of the suction, in radians.</summary>
    public float HoleSpin => _holeSpin;

    /// <summary>The random offset of the hole this frame while it charges or right after an impact, in DIP.</summary>
    public Vector2 Shake => _shake;

    /// <summary>Seconds since the explosion or the rebirth began; only meaningful in those states.</summary>
    public float BangElapsed => _bang == BangPhase.Idle ? 0f : (float)(_time - _bangStart).TotalSeconds;

    /// <summary>The planets the user formed; gone after the big bang.</summary>
    public IReadOnlyList<GalaxyPlanet> Planets => _planets;

    /// <summary>Bursts of the last 0.8 s: births on the orbits and impacts in the hole.</summary>
    public IReadOnlyList<GalaxyBurst> Bursts => _bursts;

    /// <summary>When the last self-formed planet hit the hole (null: never) and which colour it had; the hole flashes 0.6 s.</summary>
    public (TimeSpan? At, SceneColor Color) HoleFlash => (_flashAt, _flashColor);

    /// <summary>Factor on the orbit lines, arrival chips and planets: they fade with the charge and are gone in the explosion.</summary>
    public float GimmickOrbitAlpha => _bang switch
    {
        BangPhase.Exploding => 0f,
        BangPhase.Rebirth => Math.Clamp((BangElapsed - 1f) / 1.6f, 0f, 1f),
        _ => 1f - 0.6f * _holeCharge,
    };

    /// <summary>0..1 (slightly below 0 while springing back): how far the XAML surface is pulled into the hole.</summary>
    public float SurfaceSuction { get; private set; }

    /// <summary>0..1: opacity of the XAML surface during the suction.</summary>
    public float SurfaceOpacity { get; private set; } = 1f;

    /// <summary>Amplitude in DIP of the random shake of the XAML surface.</summary>
    public float SurfaceJitter { get; private set; }

    // ----- per frame --------------------------------------------------------------------------------

    private void UpdateGimmicks(float dt)
    {
        var scale = Layout.Scale;

        // Both timelines read the same conditions; the hole only differs in the spot the pointer rests on.
        var quiet = _gimmicksEnabled
            && Zoom == ZoomState.Overview
            && !_cameraRunning
            && !_dragOverTarget
            && AllBodiesSettled();

        if (_bang == BangPhase.Exploding)
        {
            if (_time - _bangStart >= TimeSpan.FromSeconds(ExplosionSeconds))
            {
                _bang = BangPhase.Rebirth;
                _bangStart = _time;
            }
        }
        else if (_bang == BangPhase.Rebirth)
        {
            if (_time - _bangStart >= TimeSpan.FromSeconds(RebirthSeconds))
            {
                _bang = BangPhase.Idle;
            }
        }
        else
        {
            UpdateHoleCharge(dt, quiet, scale);
        }

        if (_bang == BangPhase.Idle)
        {
            UpdateWhirl(dt, quiet, scale);
            UpdatePlanets(dt, quiet, scale);
        }

        _bursts.RemoveAll(b => (_time - b.At).TotalSeconds >= BurstSeconds);
        UpdateSurface();
        Gimmick = StateOf();
    }

    private void UpdateHoleCharge(float dt, bool quiet, float scale)
    {
        var near = _pointer.HasValue && Dwell.OnHole;
        if (!near)
        {
            // The lock after an explosion opens only once the pointer left the hole (draft "gesperrt").
            _holeLocked = false;
        }

        var allowed = quiet && near && !_holeLocked;
        _holeCharge = allowed
            ? Math.Min(1f, _holeCharge + dt / ChargeSeconds)
            : Math.Max(0f, _holeCharge - dt * 0.6f);
        _holeSpin = _holeCharge > 0f ? _holeSpin + dt * (0.4f + 9f * MathF.Pow(_holeCharge, 2.2f)) : 0f;

        // The shake grows steadily: hardly felt at first, violent at the end; an impact adds a shove.
        var shove = _impactAt is { } impact
            ? Math.Max(0f, 1f - (float)(_time - impact).TotalSeconds / 0.45f) * 14f
            : 0f;
        var strength = (MathF.Pow(_holeCharge, 1.6f) * 16f + shove) * scale;
        _shake = strength > 0.001f
            ? new Vector2((_random.NextSingle() - 0.5f) * strength, (_random.NextSingle() - 0.5f) * strength)
            : Vector2.Zero;

        if (_holeCharge >= 1f)
        {
            Explode();
        }
    }

    private void Explode()
    {
        _bang = BangPhase.Exploding;
        _bangStart = _time;
        _holeCharge = 0f;
        _holeSpin = 0f;
        _holeLocked = true;
        _shake = Vector2.Zero;
        _impactAt = null;
        _whirlActive = false;
        _whirlLocked = false;
        _whirlCharge = 0f;
        _planets.Clear();
        _particles.RemoveAll(p => p.PlanetIndex >= 0);
        // Files stay exactly where they are in the list: only their halos fly out and spiral home again.
    }

    private void UpdateWhirl(float dt, bool quiet, float scale)
    {
        if (_whirlActive)
        {
            _whirlSpin += dt * (0.5f + 16f * MathF.Pow(_whirlCharge, 2.2f));
        }

        var allowed = quiet
            && _holeCharge <= 0f
            && _pointer.HasValue
            && Dwell.Orbit.HasValue
            && _planets.Count < _budget.MaxPlanets;

        if (!allowed)
        {
            // Leaving the orbit or the surface lets the whirl fall softly in on itself.
            if (_whirlActive)
            {
                _whirlCharge = Math.Max(0f, _whirlCharge - dt * 1.1f);
                if (_whirlCharge <= 0f)
                {
                    _whirlActive = false;
                    _whirlLocked = false;
                }
            }

            return;
        }

        var pointer = _pointer!.Value;
        var orbit = Dwell.Orbit!.Value;

        if (!_whirlActive)
        {
            StartWhirl(pointer, orbit);
            return;
        }

        if (_whirlLocked)
        {
            // After a birth nothing happens until the pointer moves away (dwell reset: more than 6 px).
            if (_dwellReset)
            {
                StartWhirl(pointer, orbit);
            }

            return;
        }

        // A move of more than 6 px (dwell reset) or a change of orbit resets the whirl: it follows the pointer a
        // little, collapses softly (draft rate 1.4/s, follow 4/s) and then gathers again from nothing.
        if (_dwellReset || orbit != _whirlOrbit)
        {
            _whirlCollapsing = true;
        }

        if (_whirlCollapsing)
        {
            _whirlCharge = Math.Max(0f, _whirlCharge - dt * 1.4f);
            _whirlPosition = GalaxyMotion.Approach(_whirlPosition, pointer, 4f, dt);
            if (_whirlCharge <= 0.02f)
            {
                StartWhirl(pointer, orbit);
            }

            return;
        }

        if (Dwell.Duration >= WhirlSettle)
        {
            _whirlCharge = Math.Min(1f, _whirlCharge + dt / WhirlSeconds);
        }

        if (_whirlCharge >= 1f)
        {
            BirthPlanet(scale);
        }
    }

    private void StartWhirl(Vector2 pointer, int orbit)
    {
        _whirlActive = true;
        _whirlLocked = false;
        _whirlCollapsing = false;
        _whirlOrbit = orbit;
        _whirlPosition = pointer;
        _whirlCharge = 0f;
    }

    private void BirthPlanet(float scale)
    {
        if (_planetCounter < 0)
        {
            _planetCounter = _random.Next(6);
        }

        var kind = (PlanetKind)(_planetCounter % 6);
        _planetCounter++;

        var orbit = _whirlOrbit;
        var art = Palette.For(GalaxyLayout.KindOf(orbit));
        var (primary, secondary) = PlanetColors(kind, art);
        var large = kind == PlanetKind.GasGiant ? 1.3f : 1f;
        var planet = new GalaxyPlanet(
            orbit,
            AngleOf(_whirlPosition, orbit) - _orbitPhase[orbit],
            _time,
            kind,
            primary,
            secondary,
            (9f + _random.NextSingle() * 3f) * large,
            (_random.NextSingle() - 0.5f) * 0.7f,
            _random.NextSingle() * TwoPi,
            _random.NextSingle() * 1000f)
        {
            FallStart = 0.12f + _random.NextSingle() * 0.4f,
            Position = _whirlPosition,
        };

        _planets.Add(planet);
        AddPlanetHalo(_planets.Count - 1);
        _bursts.Add(new GalaxyBurst(_whirlPosition + new Vector2(0f, 8f * scale), _time, art, 1f));
        _lastBirth = _time;

        // The whirl stays where it is, empty and locked, until the pointer moves away.
        _whirlLocked = true;
        _whirlCharge = 0f;
    }

    /// <summary>Draft PTYPEN: fixed hues for four kinds, the orbit colour lightened and darkened for the other two.</summary>
    private static (SceneColor Primary, SceneColor Secondary) PlanetColors(PlanetKind kind, SceneColor art)
    {
        var white = new SceneColor(255, 255, 255);
        var black = new SceneColor(0, 0, 0);
        return kind switch
        {
            PlanetKind.Glowing => (Mix(new SceneColor(240, 128, 84), art, 0.25f), Mix(new SceneColor(120, 34, 24), art, 0.2f)),
            PlanetKind.Icy => (Mix(new SceneColor(214, 238, 246), art, 0.25f), Mix(new SceneColor(80, 128, 158), art, 0.2f)),
            PlanetKind.GasGiant => (Mix(new SceneColor(232, 196, 138), art, 0.25f), Mix(new SceneColor(150, 96, 56), art, 0.2f)),
            PlanetKind.Rocky => (Mix(new SceneColor(178, 168, 158), art, 0.25f), Mix(new SceneColor(74, 64, 58), art, 0.2f)),
            PlanetKind.Ocean => (Mix(new SceneColor(86, 156, 232), art, 0.25f), Mix(new SceneColor(22, 52, 112), art, 0.2f)),
            _ => (Mix(art, white, 0.15f), Mix(art, black, 0.55f)),
        };
    }

    private void UpdatePlanets(float dt, bool quiet, float scale)
    {
        var allowed = quiet && _pointer.HasValue && Dwell.OnHole && !_holeLocked;
        var flatten = GalaxyLayout.FlattenOverview;
        var centre = Layout.Center;

        for (var i = _planets.Count - 1; i >= 0; i--)
        {
            var planet = _planets[i];
            var pulls = allowed && _holeCharge > planet.FallStart;
            planet.Fall = Math.Clamp(
                planet.Fall + (pulls ? dt / (2.2f + planet.Radius * 0.05f) : -dt / 1.3f),
                0f,
                1f);

            if (planet.Fall >= 1f)
            {
                // Impact: a small flash in the hole, a shove, and the planet is gone.
                _bursts.Add(new GalaxyBurst(centre, _time, planet.Primary, 1.3f));
                _flashAt = _time;
                _flashColor = planet.Primary;
                _impactAt = _time;
                RemovePlanet(i);
                continue;
            }

            var basePoint = OrbitPoint(planet.Orbit, planet.Angle + _orbitPhase[planet.Orbit]);
            if (planet.Fall <= 0f)
            {
                planet.Position = basePoint;
                planet.Shrink = 1f;
                planet.ClearTrail();
                continue;
            }

            // A spiral that speeds up towards the middle (draft planetPos).
            var dx = basePoint.X - centre.X;
            var dy = (basePoint.Y - centre.Y) / flatten;
            var radius = MathF.Sqrt(dx * dx + dy * dy);
            var angle = MathF.Atan2(dy, dx);
            var k = MathF.Pow(planet.Fall, 1.7f);
            var r = radius * (1f - k);
            var w = angle + k * 5.5f;
            planet.Position = centre + new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * flatten);
            planet.Shrink = 1f - 0.65f * k;
            if (planet.Fall > 0.02f)
            {
                planet.PushTrail(planet.Position);
            }
            else
            {
                planet.ClearTrail();
            }
        }
    }

    private void RemovePlanet(int index)
    {
        _planets.RemoveAt(index);
        _particles.RemoveAll(p => p.PlanetIndex == index);
        var span = CollectionsMarshal.AsSpan(_particles);
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i].PlanetIndex > index)
            {
                span[i].PlanetIndex--;
            }
        }
    }

    private void AddPlanetHalo(int planetIndex)
    {
        var planet = _planets[planetIndex];
        var count = _budget.HaloPerPlanet;
        if (count <= 0 || _particles.Count + count > _budget.MaxParticles)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            _particles.Add(new GalaxyParticle
            {
                Orbit = planet.Orbit,
                BodyIndex = -1,
                PlanetIndex = planetIndex,
                R1 = _random.NextSingle(),
                R2 = _random.NextSingle(),
                R3 = _random.NextSingle(),
                Color = planet.Primary,
                Alpha = 0.95f,
            });
        }
    }

    /// <summary>Draft uiSog: the surface is swallowed in the last fifth of the charge and springs back after the rebirth.</summary>
    private void UpdateSurface()
    {
        float suction, opacity, jitter = 0f;
        switch (_bang)
        {
            case BangPhase.Exploding:
                suction = 1f;
                opacity = 0f;
                break;
            case BangPhase.Rebirth:
                var t = Math.Clamp((BangElapsed - 1.3f) / 1.1f, 0f, 1f);
                suction = 1f - GalaxyMotion.EaseBack(t);
                opacity = t;
                break;
            default:
                var u = Math.Clamp((_holeCharge - SwallowFrom) / (1f - SwallowFrom), 0f, 1f);
                suction = 0.92f * u * u * u;
                jitter = u * u * 9f * Layout.Scale;
                opacity = 1f - 0.55f * u * u;
                break;
        }

        SurfaceSuction = suction;
        SurfaceOpacity = opacity;
        SurfaceJitter = jitter;
    }

    private GimmickState StateOf()
    {
        if (_bang == BangPhase.Exploding)
        {
            return GimmickState.BigBang;
        }

        if (_bang == BangPhase.Rebirth)
        {
            return GimmickState.NewUniverse;
        }

        if (_holeCharge > SwallowFrom)
        {
            return GimmickState.Swallowing;
        }

        if (_holeCharge > 0f)
        {
            foreach (var planet in _planets)
            {
                if (planet.Fall > 0f)
                {
                    return GimmickState.Impacts;
                }
            }

            return GimmickState.Suction;
        }

        if (_whirlActive && _whirlLocked)
        {
            return (_time - _lastBirth).TotalSeconds < BurstSeconds ? GimmickState.Flash : GimmickState.NewPlanet;
        }

        return _whirlActive && _whirlCharge > 0f ? GimmickState.Whirl : GimmickState.Idle;
    }

    private bool AllBodiesSettled()
    {
        foreach (var body in _bodies)
        {
            if (body.Phase is BodyPhase.Waiting or BodyPhase.Approaching)
            {
                return false;
            }
        }

        return true;
    }

    // ----- particle displacement ----------------------------------------------------------------------

    /// <summary>
    /// Draft wirbelPos: a flat disc in the plane of the orbits that turns ever faster and sinks in the middle
    /// like a funnel. Returns the depth factor for the particle size (front particles look larger).
    /// </summary>
    private float WhirlDisplace(ref Vector2 position, float r2)
    {
        if (!_whirlActive || _whirlCharge <= 0.005f)
        {
            return 1f;
        }

        var scale = Layout.Scale;
        var charge = _whirlCharge;
        var reach = (75f + 70f * charge) * scale;
        var dx = position.X - _whirlPosition.X;
        var dy = (position.Y - _whirlPosition.Y) / WhirlFlatten;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance >= reach)
        {
            return 1f;
        }

        var f = 1f - distance / reach;
        var weight = GalaxyMotion.Smooth(Math.Min(1f, charge * 2.5f));
        var s = MathF.Pow(charge, 1.2f) * MathF.Pow(f, 0.55f);
        var a = _whirlSpin * (0.35f + f) * (1.1f - r2 * 0.4f);
        var c = MathF.Cos(a);
        var si = MathF.Sin(a);
        var k = 1f - 0.9f * s;
        var rx = (dx * c - dy * si) * k;
        var ry = (dx * si + dy * c) * k;
        var sink = s * 16f * charge * scale;
        var front = Math.Clamp(ry / (reach * 0.6f), -1f, 1f);
        position = new Vector2(
            float.Lerp(position.X, _whirlPosition.X + rx, weight),
            float.Lerp(position.Y, _whirlPosition.Y + ry * WhirlFlatten + sink, weight));
        return 1f + 0.55f * front * s * weight;
    }

    /// <summary>
    /// Draft urknallPos: the place of a particle while the hole charges (a tightening spiral), during the
    /// explosion (flung outwards) and while the universe forms again (a spiral from outside onto its own
    /// orbit). Returns the alpha factor.
    /// </summary>
    private float BangDisplace(ref Vector2 position, float r1, float r2, float r3)
    {
        var centre = Layout.Center;
        var dx = position.X - centre.X;
        var dy = (position.Y - centre.Y) / SuctionFlatten;
        var radius = MathF.Sqrt(dx * dx + dy * dy);
        var angle = MathF.Atan2(dy, dx);
        var far = (0.35f + r2 * 1.15f) * Math.Max(Layout.Width, Layout.Height) * 0.7f;
        var direction = r1 * TwoPi * 5f + r3 * 2f;

        if (_bang == BangPhase.Idle)
        {
            var l = _holeCharge * _holeCharge;
            var weight = GalaxyMotion.Smooth(Math.Min(1f, _holeCharge * 4f));
            var w = angle + _holeSpin * (1.25f - r2);
            var rr = radius * (1f - 0.72f * l) + (_random.NextSingle() - 0.5f) * 7f * l * Layout.Scale;
            // Blend softly between the orbit and the suction, so nothing jumps when the pointer leaves.
            position = new Vector2(
                float.Lerp(position.X, centre.X + MathF.Cos(w) * rr + _shake.X * 0.6f, weight),
                float.Lerp(position.Y, centre.Y + MathF.Sin(w) * rr * SuctionFlatten + _shake.Y * 0.6f, weight));
            return 1f;
        }

        if (_bang == BangPhase.Exploding)
        {
            var u = GalaxyMotion.EaseOut(Math.Clamp(BangElapsed / (ExplosionSeconds * (0.6f + 0.4f * r3)), 0f, 1f));
            var rr = float.Lerp((4f + r2 * 10f) * Layout.Scale, far, u);
            position = centre + new Vector2(MathF.Cos(direction) * rr, MathF.Sin(direction) * rr * 0.62f);
            return 1f - 0.75f * u;
        }

        // Rebirth: from far outside in a spiral onto the own orbit; k = 1 puts the particle exactly back.
        var t = Math.Clamp((BangElapsed - 0.5f - r2 * 1.1f) / 1.6f, 0f, 1f);
        if (t <= 0f)
        {
            position = centre + new Vector2(MathF.Cos(direction) * far, MathF.Sin(direction) * far * 0.62f);
            return 0.12f;
        }

        var k = GalaxyMotion.Ease(t);
        var sx = MathF.Cos(direction) * far;
        var sy = MathF.Sin(direction) * far * 0.62f / SuctionFlatten;
        var r0 = MathF.Sqrt(sx * sx + sy * sy);
        var a0 = MathF.Atan2(sy, sx);
        var d = GalaxyMotion.WrapAngle(a0 - angle) + 2.2f;
        var wr = angle + d * (1f - k);
        var rk = float.Lerp(r0, radius, k);
        position = centre + new Vector2(MathF.Cos(wr) * rk, MathF.Sin(wr) * rk * SuctionFlatten);
        return float.Lerp(0.12f, 1f, k);
    }

    private static SceneColor Mix(SceneColor from, SceneColor to, float t) => new(
        (byte)Math.Clamp(float.Lerp(from.R, to.R, t), 0f, 255f),
        (byte)Math.Clamp(float.Lerp(from.G, to.G, t), 0f, 255f),
        (byte)Math.Clamp(float.Lerp(from.B, to.B, t), 0f, 255f));
}
