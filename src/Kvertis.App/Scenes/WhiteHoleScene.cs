using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>A place on the white hole, given in the order the files finished. <see cref="Failed"/> is drawn as a coral ring.</summary>
public readonly record struct WhiteHoleSlot(Guid Id, MediaKind Kind, bool Failed, TimeSpan ArrivesAt);

/// <summary>One orbit line of the white hole in this frame.</summary>
public readonly record struct WhiteHoleOrbit(float RadiusX, float RadiusY, bool Dashed, SceneColor Color, float Alpha, float Width);

/// <summary>An arrived file as a planet; <see cref="ArrivalRadius"/> and <see cref="ArrivalAlpha"/> describe its 0.8 s arrival ring.</summary>
public readonly record struct WhiteHolePlanet(
    int Slot,
    Vector2 Position,
    float Angle,
    float OrbitRadius,
    float Radius,
    SceneColor Color,
    bool Failed,
    float ArrivalRadius,
    float ArrivalAlpha);

/// <summary>A twinkling star: square of <see cref="Size"/> DIP.</summary>
public readonly record struct SceneStar(Vector2 Position, float Size, float Alpha, SceneColor Color);

/// <summary>
/// The white hole in the upper right of the swirl surface (worksheet "Übergänge", section "Weißes Loch"):
/// at most ten orbits, a capacity per orbit, planets in the order the files finish, empty places as faint
/// dots, failed files as coral rings. Owned and updated by <see cref="SwirlScene"/> on the drawing thread.
/// </summary>
public sealed class WhiteHoleScene
{
    /// <summary>Vertical squash of every orbit.</summary>
    public const float Flatten = 0.42f;

    /// <summary>Most orbits; beyond ten files the places are bundled.</summary>
    public const int MaxOrbits = 10;

    /// <summary>Length of the arrival ring, in seconds.</summary>
    public const float ArrivalSeconds = 0.8f;

    /// <summary>Tail circles behind a planet and their angle step.</summary>
    public const int TailLength = 8;

    public const float TailStep = 0.07f;

    private readonly List<WhiteHoleSlot> _slots = [];
    private readonly List<WhiteHoleOrbit> _orbits = [];
    private readonly List<WhiteHolePlanet> _planets = [];
    private readonly List<Vector2> _emptyPlaces = [];
    private readonly StarSeed[] _starSeeds;
    private readonly SceneStar[] _stars;
    private readonly SwirlBudget _budget;

    internal WhiteHoleScene(float width, ScenePalette palette, Random random, SwirlBudget budget)
    {
        Palette = palette;
        _budget = budget;
        _starSeeds = new StarSeed[Math.Max(0, budget.StarsNear)];
        for (var i = 0; i < _starSeeds.Length; i++)
        {
            _starSeeds[i] = new StarSeed(
                random.NextSingle() * MathF.Tau,
                MathF.Sqrt(random.NextSingle()),
                random.NextSingle(),
                0.5f + random.NextSingle() * 1.5f,
                random.NextSingle() < 0.15f ? 1.6f : 1f);
        }

        _stars = new SceneStar[_starSeeds.Length];
        Resize(width);
    }

    public ScenePalette Palette { get; set; }

    /// <summary><c>tw</c> = min(300, 0.3·W).</summary>
    public float TotalWidth { get; private set; }

    /// <summary><c>T</c> = (W - 22 - tw/2, 100).</summary>
    public Vector2 Centre { get; private set; }

    /// <summary>Files of the plan without an own target; they all get a place.</summary>
    public int N { get; private set; }

    /// <summary><c>B</c> = clamp(N, 1, 10).</summary>
    public int Orbits => Math.Clamp(N, 1, MaxOrbits);

    /// <summary>Places in the order of finishing (Completed and Failed, never Cancelled).</summary>
    public IReadOnlyList<WhiteHoleSlot> Slots => _slots;

    /// <summary>Arrived files that did not fail, divided by N (the <c>Anteil</c> of the core).</summary>
    public float Share { get; private set; }

    /// <summary>1 + 0.04·sin(2.2t).</summary>
    public float Pulse { get; private set; } = 1f;

    /// <summary>The core scale g = 1 + 0.35·Share.</summary>
    public float CoreScale => 1f + 0.35f * Share;

    /// <summary>Planet radius gP: 2.8 up to ten files, else 2.0.</summary>
    public float PlanetRadius => N <= MaxOrbits ? 2.8f : 2f;

    public IReadOnlyList<WhiteHoleOrbit> OrbitLines => _orbits;

    public IReadOnlyList<WhiteHolePlanet> Planets => _planets;

    /// <summary>Places not yet taken (only for N &gt; 10), at most <see cref="SwirlBudget.MaxEmptyPlaces"/>.</summary>
    public IReadOnlyList<Vector2> EmptyPlaces => _emptyPlaces;

    public IReadOnlyList<SceneStar> Stars => _stars;

    /// <summary><c>kap(j)</c> = ⌊N/B⌋ + (j &lt; N mod B ? 1 : 0).</summary>
    public int Capacity(int orbit)
    {
        var b = Orbits;
        return N / b + (orbit < N % b ? 1 : 0);
    }

    /// <summary><c>rx(j)</c> = 24 + j·min(12, (tw/2 - 30)/max(1, B - 1)).</summary>
    public float OrbitRadius(int orbit) =>
        24f + orbit * MathF.Min(12f, (TotalWidth / 2f - 30f) / Math.Max(1, Orbits - 1));

    /// <summary><c>winkel(k, t)</c> = 2.39·j + p/kap(j)·2π + t·0.9·(24/rx(j))^1.5 with j = k mod B, p = ⌊k/B⌋.</summary>
    public float Angle(int slot, float t)
    {
        var b = Orbits;
        var j = slot % b;
        var p = slot / b;
        return j * 2.39f + (float)p / Math.Max(1, Capacity(j)) * MathF.Tau + t * 0.9f * MathF.Pow(24f / OrbitRadius(j), 1.5f);
    }

    /// <summary>The place <paramref name="slot"/> at time <paramref name="t"/>, around <paramref name="centre"/> (default: the white hole).</summary>
    public Vector2 SlotPosition(int slot, float t, Vector2? centre = null, float scale = 1f)
    {
        var m = centre ?? Centre;
        var w = Angle(slot, t);
        var r = OrbitRadius(slot % Orbits) * scale;
        return m + new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * Flatten);
    }

    /// <summary>The <paramref name="index"/>th tail circle (1..8) behind a planet: centre, radius factor and alpha factor.</summary>
    public (Vector2 Position, float RadiusFactor, float Alpha) TailPoint(in WhiteHolePlanet planet, int index)
    {
        var w = planet.Angle - index * TailStep;
        var position = Centre + new Vector2(MathF.Cos(w) * planet.OrbitRadius, MathF.Sin(w) * planet.OrbitRadius * Flatten);
        return (position, 1f - index / 12f, 0.38f * (1f - index / 9f));
    }

    internal void Resize(float width)
    {
        TotalWidth = MathF.Min(300f, width * 0.3f);
        Centre = new Vector2(width - 22f - TotalWidth / 2f, 100f);
    }

    internal void SetPlan(int n)
    {
        N = Math.Max(0, n);
        _slots.Clear();
    }

    internal void Clear()
    {
        N = 0;
        _slots.Clear();
        _orbits.Clear();
        _planets.Clear();
        _emptyPlaces.Clear();
        Share = 0f;
    }

    /// <summary>Gives the next free place to a finished file; -1 if it already has one or the plan is full.</summary>
    internal int Assign(Guid id, MediaKind kind, bool failed, TimeSpan arrivesAt)
    {
        if (_slots.Count >= N || _slots.Exists(s => s.Id == id))
        {
            return -1;
        }

        _slots.Add(new WhiteHoleSlot(id, kind, failed, arrivesAt));
        return _slots.Count - 1;
    }

    internal int SlotOf(Guid id) => _slots.FindIndex(s => s.Id == id);

    /// <summary>The colour a slot is drawn in: kind colour, or the error colour for a failed file.</summary>
    public SceneColor ColourOf(in WhiteHoleSlot slot) => slot.Failed ? Palette.Error : Palette.For(slot.Kind);

    /// <summary>Recomputes orbits, planets, empty places and stars for time <paramref name="time"/>.</summary>
    internal void Update(TimeSpan time, float starFactor)
    {
        var t = (float)time.TotalSeconds;
        var arrived = 0;
        foreach (var slot in _slots)
        {
            if (slot.ArrivesAt <= time && !slot.Failed)
            {
                arrived++;
            }
        }

        Share = N == 0 ? 0f : (float)arrived / N;
        Pulse = 1f + 0.04f * MathF.Sin(t * 2.2f);

        UpdateOrbits(time);
        UpdatePlanets(time, t);
        UpdateStars(t, (0.4f + 0.6f * Share) * starFactor);
    }

    private void UpdateOrbits(TimeSpan time)
    {
        _orbits.Clear();
        if (N == 0)
        {
            return;
        }

        var b = Orbits;
        for (var j = 0; j < b; j++)
        {
            var occupied = 0;
            WhiteHoleSlot first = default;
            for (var k = j; k < _slots.Count; k += b)
            {
                if (_slots[k].ArrivesAt <= time)
                {
                    if (occupied == 0)
                    {
                        first = _slots[k];
                    }

                    occupied++;
                }
            }

            var rx = OrbitRadius(j);
            if (occupied == 0)
            {
                _orbits.Add(new WhiteHoleOrbit(rx, rx * Flatten, true, Palette.LineStrong, 0.75f, 1f));
            }
            else if (N <= MaxOrbits)
            {
                _orbits.Add(new WhiteHoleOrbit(rx, rx * Flatten, false, ColourOf(first), 0.38f, 1.2f));
            }
            else
            {
                _orbits.Add(new WhiteHoleOrbit(rx, rx * Flatten, false, Palette.Mint, 0.12f + 0.25f * occupied / Math.Max(1, Capacity(j)), 1.2f));
            }
        }
    }

    private void UpdatePlanets(TimeSpan time, float t)
    {
        _planets.Clear();
        _emptyPlaces.Clear();
        var showEmpty = N > MaxOrbits;
        for (var k = 0; k < N; k++)
        {
            var taken = k < _slots.Count;
            if (!taken || _slots[k].ArrivesAt > time)
            {
                if (showEmpty && _emptyPlaces.Count < _budget.MaxEmptyPlaces)
                {
                    _emptyPlaces.Add(SlotPosition(k, t));
                }

                continue;
            }

            var slot = _slots[k];
            var e = (float)(time - slot.ArrivesAt).TotalSeconds;
            var ring = e < ArrivalSeconds ? SceneMotion.EaseOut(e / ArrivalSeconds) : 1f;
            _planets.Add(new WhiteHolePlanet(
                k,
                SlotPosition(k, t),
                Angle(k, t),
                OrbitRadius(k % Orbits),
                PlanetRadius,
                ColourOf(slot),
                slot.Failed,
                5f + 14f * ring,
                e < ArrivalSeconds ? 1f - ring : 0f));
        }
    }

    private void UpdateStars(float t, float factor)
    {
        var colour = Palette.IsDark ? SceneColor.FromHex(0xDFE9EF) : Palette.LineStrong;
        var theme = Palette.IsDark ? 1f : 0.7f;
        for (var i = 0; i < _starSeeds.Length; i++)
        {
            var s = _starSeeds[i];
            var position = Centre + new Vector2(
                MathF.Cos(s.Angle) * s.Radius * (TotalWidth / 2f + 6f),
                MathF.Sin(s.Angle) * s.Radius * 90f);
            var alpha = (0.25f + 0.5f * (0.5f + 0.5f * MathF.Sin(t * s.Frequency + s.Phase * 9f))) * theme * factor;
            _stars[i] = new SceneStar(position, s.Size, Math.Max(0f, alpha), colour);
        }
    }

    private readonly record struct StarSeed(float Angle, float Radius, float Phase, float Frequency, float Size);
}
