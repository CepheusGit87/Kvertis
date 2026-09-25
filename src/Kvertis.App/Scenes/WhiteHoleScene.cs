using System.Numerics;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>How the white hole shows the arrived files (design/weisses-loch-mengen.html).</summary>
public enum WhiteHoleMode
{
    /// <summary>Draft 1 "Gebündelte Bahnen": at most ten orbits with places, one planet per file.</summary>
    Rings,

    /// <summary>Draft 2 "Staubringe" from <see cref="WhiteHoleScene.DustThreshold"/> files on: one ring per kind, dust grains per file.</summary>
    Dust,
}

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

/// <summary>
/// One dust ring (mode <see cref="WhiteHoleMode.Dust"/>): the ring of one kind. <see cref="Fill"/> is the share of
/// its files that arrived; <see cref="GlowWidth"/> and <see cref="GlowAlpha"/> describe the soft band that grows
/// with the fill (0 while nothing arrived).
/// </summary>
public readonly record struct WhiteHoleRing(
    int Index,
    MediaKind Kind,
    float RadiusX,
    float RadiusY,
    SceneColor Color,
    int Count,
    int Arrived,
    float Fill,
    float GlowWidth,
    float GlowAlpha);

/// <summary>A dust grain of a ring: a 1.7 DIP square at <see cref="Offset"/> from the white hole.</summary>
public readonly record struct WhiteHoleGrain(Vector2 Offset, SceneColor Color, float Alpha);

/// <summary>The 0.8 s arrival pulse of a file in dust mode: an ellipse around the white hole growing to the ring.</summary>
public readonly record struct WhiteHolePulse(float RadiusX, float RadiusY, SceneColor Color, float Alpha);

/// <summary>A twinkling star: square of <see cref="Size"/> DIP.</summary>
public readonly record struct SceneStar(Vector2 Position, float Size, float Alpha, SceneColor Color);

/// <summary>
/// The white hole in the upper right of the swirl surface (worksheet "Übergänge", section "Weißes Loch"):
/// at most ten orbits, a capacity per orbit, planets in the order the files finish, empty places as faint
/// dots, failed files as coral rings. From <see cref="DustThreshold"/> files on (draft 2 "Staubringe") the
/// orbits give way to one ring per kind into which every arrived file scatters dust grains. Owned and
/// updated by <see cref="SwirlScene"/> on the drawing thread; deterministic for a given plan and time.
/// </summary>
public sealed class WhiteHoleScene
{
    /// <summary>Vertical squash of every orbit.</summary>
    public const float Flatten = 0.42f;

    /// <summary>Most orbits; beyond ten files the places are bundled.</summary>
    public const int MaxOrbits = 10;

    /// <summary>From this many files on the white hole shows dust rings instead of orbits with places.</summary>
    public const int DustThreshold = 150;

    /// <summary>Grains per file: clamp(round(700 / N), 10, 64).</summary>
    public const int MinGrainsPerFile = 10;

    public const int MaxGrainsPerFile = 64;

    public const float GrainSpread = 700f;

    /// <summary>Length of the arrival ring, in seconds.</summary>
    public const float ArrivalSeconds = 0.8f;

    /// <summary>Tail circles behind a planet and their angle step.</summary>
    public const int TailLength = 8;

    public const float TailStep = 0.07f;

    private readonly List<WhiteHoleSlot> _slots = [];
    private readonly List<WhiteHoleOrbit> _orbits = [];
    private readonly List<WhiteHolePlanet> _planets = [];
    private readonly List<Vector2> _emptyPlaces = [];
    private readonly List<MediaKind> _kinds = [];
    private readonly List<int> _kindCounts = [];
    private readonly List<int> _kindSeeds = [];
    private readonly List<WhiteHoleRing> _rings = [];
    private readonly List<WhiteHoleGrain> _grains = [];
    private readonly List<WhiteHolePulse> _pulses = [];
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

    /// <summary>Orbits with places below <see cref="DustThreshold"/> files, dust rings from there on.</summary>
    public WhiteHoleMode Mode => N >= DustThreshold ? WhiteHoleMode.Dust : WhiteHoleMode.Rings;

    /// <summary><c>B</c> = clamp(N, 1, 10).</summary>
    public int Orbits => Math.Clamp(N, 1, MaxOrbits);

    /// <summary>Places in the order of finishing (Completed and Failed, never Cancelled).</summary>
    public IReadOnlyList<WhiteHoleSlot> Slots => _slots;

    /// <summary>Arrived files that did not fail.</summary>
    public int Arrived { get; private set; }

    /// <summary>Arrived files that did not fail, divided by N (the <c>Anteil</c> of the core).</summary>
    public float Share { get; private set; }

    /// <summary>1 + 0.04·sin(2.2t).</summary>
    public float Pulse { get; private set; } = 1f;

    /// <summary>The core scale g = 1 + 0.35·Share.</summary>
    public float CoreScale => 1f + 0.35f * Share;

    /// <summary>Planet radius gP: 2.8 up to ten files, else 2.0 (also the coral rings of failed files in dust mode).</summary>
    public float PlanetRadius => N <= MaxOrbits ? 2.8f : 2f;

    /// <summary>Y of the counter "n von N": below the outermost orbit, or below the arc radius in dust mode.</summary>
    public float CounterY => Mode == WhiteHoleMode.Dust
        ? Centre.Y + ArcRadius * Flatten + 20f
        : Centre.Y + OrbitRadius(Orbits - 1) * Flatten + 22f;

    /// <summary>Orbit lines; empty in dust mode.</summary>
    public IReadOnlyList<WhiteHoleOrbit> OrbitLines => _orbits;

    /// <summary>Planets per file; in dust mode only the coral rings of failed files (at most <see cref="SwirlBudget.MaxEmptyPlaces"/>).</summary>
    public IReadOnlyList<WhiteHolePlanet> Planets => _planets;

    /// <summary>Places not yet taken (only for N &gt; 10 in ring mode), at most <see cref="SwirlBudget.MaxEmptyPlaces"/>.</summary>
    public IReadOnlyList<Vector2> EmptyPlaces => _emptyPlaces;

    public IReadOnlyList<SceneStar> Stars => _stars;

    // ----- dust mode -----------------------------------------------------------------------------------

    /// <summary>The kinds of the plan in display order (<see cref="TargetPlanner.KindOrder"/>); one dust ring each.</summary>
    public IReadOnlyList<MediaKind> Kinds => _kinds;

    /// <summary>Number of dust rings (kinds present in the plan).</summary>
    public int RingCount => _kinds.Count;

    /// <summary>The dust rings of this frame; empty in ring mode.</summary>
    public IReadOnlyList<WhiteHoleRing> Rings => _rings;

    /// <summary>Lit dust grains of this frame; empty in ring mode.</summary>
    public IReadOnlyList<WhiteHoleGrain> Grains => _grains;

    /// <summary>Arrival pulses of this frame (dust mode).</summary>
    public IReadOnlyList<WhiteHolePulse> Pulses => _pulses;

    /// <summary>Grains drawn in this frame; counted against <see cref="SwirlBudget.MaxParticles"/>.</summary>
    public int GrainCount => _grains.Count;

    /// <summary><c>G</c> = clamp(round(700 / N), 10, 64): the nominal grains one file scatters into its ring.</summary>
    public int GrainsPerFile => N == 0 ? 0 : Math.Clamp((int)MathF.Round(GrainSpread / N), MinGrainsPerFile, MaxGrainsPerFile);

    /// <summary>Radius of the counter arc, <c>R</c> = min(tw/2 - 8, 132).</summary>
    public float ArcRadius => MathF.Min(TotalWidth / 2f - 8f, 132f);

    /// <summary>Radial scatter of the grains around their ring: 26 with a single ring, 9 otherwise.</summary>
    public float RingScatter => RingCount == 1 ? 26f : 9f;

    /// <summary><c>rx</c> = R0 + j·dr with R0 = 62 (one ring) or 36, dr = min(20, (tw/2 - 70)/max(1, rings - 1)).</summary>
    public float RingRadius(int ring)
    {
        if (RingCount <= 1)
        {
            return 62f;
        }

        var dr = MathF.Min(20f, (TotalWidth / 2f - 70f) / Math.Max(1, RingCount - 1));
        return 36f + ring * dr;
    }

    /// <summary>Rotation of a ring: <c>t·0.35·(36/rx)^1.2</c>, inner rings faster.</summary>
    public float RingRotation(int ring, float t) => t * 0.35f * MathF.Pow(36f / RingRadius(ring), 1.2f);

    /// <summary>The ring of a kind, or 0 if the kind is not in the plan.</summary>
    public int RingOf(MediaKind kind)
    {
        var index = _kinds.IndexOf(kind);
        return index < 0 ? 0 : index;
    }

    /// <summary>A point on ring <paramref name="ring"/>: angle share <paramref name="r1"/>, radial scatter <paramref name="r2"/> (both 0..1).</summary>
    public Vector2 RingPoint(int ring, float r1, float r2, float t, Vector2? centre = null, float scale = 1f)
    {
        var m = centre ?? Centre;
        var r = (RingRadius(ring) + (r2 - 0.5f) * RingScatter) * scale;
        var w = r1 * MathF.Tau + RingRotation(ring, t);
        return m + new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * Flatten);
    }

    /// <summary>Grain seeds one ring may light at most: min(G·count, pool·count/N) with the pool from the budget.</summary>
    public int RingSeeds(int ring) => ring >= 0 && ring < _kindSeeds.Count ? _kindSeeds[ring] : 0;

    // ----- ring mode -----------------------------------------------------------------------------------

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

    /// <summary>
    /// The place <paramref name="slot"/> at time <paramref name="t"/>, around <paramref name="centre"/> (default: the
    /// white hole). In dust mode a fixed point on the ring of the slot's kind, so a pixel stream flies into the ring.
    /// </summary>
    public Vector2 SlotPosition(int slot, float t, Vector2? centre = null, float scale = 1f)
    {
        if (Mode == WhiteHoleMode.Dust)
        {
            var kind = slot >= 0 && slot < _slots.Count ? _slots[slot].Kind : (_kinds.Count > 0 ? _kinds[0] : MediaKind.Image);
            return RingPoint(RingOf(kind), Hash(slot, 0, 7), Hash(slot, 1, 7), t, centre, scale);
        }

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

    /// <summary>A plan of <paramref name="n"/> files of unknown kind (one dust ring).</summary>
    internal void SetPlan(int n)
    {
        var kinds = new MediaKind[Math.Max(0, n)];
        Array.Fill(kinds, MediaKind.Image);
        SetPlan(kinds);
    }

    /// <summary>The kinds of the files without an own target, in plan order.</summary>
    internal void SetPlan(IReadOnlyList<MediaKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        N = kinds.Count;
        _slots.Clear();
        _kinds.Clear();
        _kindCounts.Clear();
        _kindSeeds.Clear();
        foreach (var kind in TargetPlanner.KindOrder)
        {
            var count = 0;
            foreach (var k in kinds)
            {
                if (k == kind)
                {
                    count++;
                }
            }

            if (count > 0)
            {
                _kinds.Add(kind);
                _kindCounts.Add(count);
            }
        }

        // Kinds outside the display order still get a ring at the end.
        foreach (var k in kinds)
        {
            if (!_kinds.Contains(k))
            {
                _kinds.Add(k);
                _kindCounts.Add(kinds.Count(x => x == k));
            }
        }

        var pool = Math.Max(0, Math.Min(_budget.RingGrains, _budget.MaxParticles));
        var g = GrainsPerFile;
        for (var j = 0; j < _kinds.Count; j++)
        {
            var count = _kindCounts[j];
            var byFile = (long)g * count;
            var byPool = N == 0 ? 0 : (long)pool * count / N;
            _kindSeeds.Add((int)Math.Min(byFile, byPool));
        }
    }

    internal void Clear()
    {
        N = 0;
        _slots.Clear();
        _orbits.Clear();
        _planets.Clear();
        _emptyPlaces.Clear();
        _kinds.Clear();
        _kindCounts.Clear();
        _kindSeeds.Clear();
        _rings.Clear();
        _grains.Clear();
        _pulses.Clear();
        Arrived = 0;
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

    /// <summary>
    /// Recomputes orbits, planets, empty places, rings, grains and stars for time <paramref name="time"/>.
    /// <paramref name="allowedGrains"/> is what the particle budget leaves for the dust rings in this frame.
    /// </summary>
    internal void Update(TimeSpan time, float starFactor, int allowedGrains = int.MaxValue)
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

        Arrived = arrived;
        Share = N == 0 ? 0f : (float)arrived / N;
        Pulse = 1f + 0.04f * MathF.Sin(t * 2.2f);

        if (Mode == WhiteHoleMode.Dust)
        {
            _orbits.Clear();
            _emptyPlaces.Clear();
            UpdateRings(time, t, allowedGrains);
        }
        else
        {
            _rings.Clear();
            _grains.Clear();
            _pulses.Clear();
            UpdateOrbits(time);
            UpdatePlanets(time, t);
        }

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

    // Dust rings: one ring per kind, its glow grows with the fill, the arrived files light their grains in
    // order, failed files stay visible as coral rings on their ring, every arrival sends a pulse to its ring.
    private void UpdateRings(TimeSpan time, float t, int allowedGrains)
    {
        _rings.Clear();
        _grains.Clear();
        _pulses.Clear();
        _planets.Clear();
        if (N == 0 || _kinds.Count == 0)
        {
            return;
        }

        Span<int> arrivedByRing = stackalloc int[_kinds.Count];
        arrivedByRing.Clear();
        for (var k = 0; k < _slots.Count; k++)
        {
            var slot = _slots[k];
            if (slot.ArrivesAt > time)
            {
                continue;
            }

            var ring = RingOf(slot.Kind);
            if (!slot.Failed)
            {
                arrivedByRing[ring]++;
            }
            else if (_planets.Count < _budget.MaxEmptyPlaces)
            {
                var position = RingPoint(ring, Hash(k, 0, 7), Hash(k, 1, 7), t);
                _planets.Add(new WhiteHolePlanet(k, position, 0f, RingRadius(ring), PlanetRadius, Palette.Error, true, 0f, 0f));
            }

            var e = (float)(time - slot.ArrivesAt).TotalSeconds;
            if (e < ArrivalSeconds)
            {
                var p = SceneMotion.EaseOut(e / ArrivalSeconds);
                var r = 5f + p * RingRadius(ring);
                _pulses.Add(new WhiteHolePulse(r, r * Flatten, ColourOf(slot), 1f - p));
            }
        }

        var single = _kinds.Count == 1;
        var wanted = 0;
        Span<int> lit = stackalloc int[_kinds.Count];
        for (var j = 0; j < _kinds.Count; j++)
        {
            var count = _kindCounts[j];
            var fill = count == 0 ? 0f : (float)arrivedByRing[j] / count;
            lit[j] = (int)MathF.Round(_kindSeeds[j] * fill);
            wanted += lit[j];
            var rx = RingRadius(j);
            _rings.Add(new WhiteHoleRing(
                j,
                _kinds[j],
                rx,
                rx * Flatten,
                Palette.For(_kinds[j]),
                count,
                arrivedByRing[j],
                fill,
                fill <= 0f ? 0f : single ? 18f * fill + 4f : 6f * fill + 2f,
                fill <= 0f ? 0f : 0.1f + 0.12f * fill));
        }

        var allowed = Math.Max(0, allowedGrains);
        var factor = wanted > allowed ? (float)allowed / wanted : 1f;
        for (var j = 0; j < _kinds.Count; j++)
        {
            var n = factor < 1f ? (int)(lit[j] * factor) : lit[j];
            var colour = Palette.For(_kinds[j]);
            var rx = RingRadius(j);
            var rotation = RingRotation(j, t);
            for (var i = 0; i < n && _grains.Count < allowed; i++)
            {
                var r = rx + (Hash(j, i, 1) - 0.5f) * RingScatter;
                var w = Hash(j, i, 0) * MathF.Tau + rotation;
                _grains.Add(new WhiteHoleGrain(new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * Flatten), colour, 0.85f));
            }
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

    /// <summary>A deterministic value in [0, 1) for a grain or place; no random state, so two runs match.</summary>
    internal static float Hash(int a, int b, int c)
    {
        var h = unchecked((uint)a * 0x9E3779B1u) ^ unchecked((uint)b * 0x85EBCA77u) ^ unchecked((uint)c * 0xC2B2AE3Du);
        h ^= h >> 15;
        h = unchecked(h * 0x2C1B3C6Du);
        h ^= h >> 12;
        h = unchecked(h * 0x297A2D39u);
        h ^= h >> 15;
        return (h & 0xFFFFFF) / 16777216f;
    }

    private readonly record struct StarSeed(float Angle, float Radius, float Phase, float Frequency, float Size);
}
