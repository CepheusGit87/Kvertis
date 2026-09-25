using System.Numerics;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>The stages of the finale, in order.</summary>
public enum FinalePhase
{
    /// <summary>0 … ANL: planets spin up, orbits contract, dust spirals into the white hole, the holes start circling.</summary>
    RunUp,

    /// <summary>ANL … ANL + M: both holes dance around their common centre and merge.</summary>
    Dance,

    /// <summary>The short silence before the supernova.</summary>
    Silence,

    /// <summary>Supernova, explosion, shock waves and sparks (e = 0 … 1.3).</summary>
    Nova,

    /// <summary>The planets settle on the ring around the report (e = 1.3 … 1.8).</summary>
    Ring,

    /// <summary>Everything has arrived; only the ring keeps turning.</summary>
    Done,
}

/// <summary>A planet of the finale: simulated with inertia while it spins up, later a place on the ring.</summary>
public sealed class FinalePlanet
{
    /// <summary>Trail points kept per planet, relative to the white hole.</summary>
    public const int TrailCapacity = 14;

    private readonly Vector2[] _trail = new Vector2[TrailCapacity];

    internal FinalePlanet(int slot, MediaKind kind, bool failed, SceneColor color, int count = 0)
    {
        Slot = slot;
        Kind = kind;
        Failed = failed;
        Color = color;
        Count = count;
    }

    /// <summary>The place on the white hole this planet came from.</summary>
    public int Slot { get; }

    /// <summary>
    /// Dust mode: how many files this collective planet stands for (the renderer writes the number next to it on
    /// the ring); 0 for a planet of a single file.
    /// </summary>
    public int Count { get; }

    public MediaKind Kind { get; }

    /// <summary>Failed files are drawn as coral rings and have no trail.</summary>
    public bool Failed { get; }

    public SceneColor Color { get; }

    /// <summary>Simulated position (spring towards <see cref="Target"/>).</summary>
    public Vector2 Position { get; internal set; }

    public Vector2 Velocity { get; internal set; }

    /// <summary>The ideal place in this frame (<c>zielAbschluss</c>).</summary>
    public Vector2 Target { get; internal set; }

    /// <summary>|Position - Target|, softly capped at 28 DIP.</summary>
    public float Deviation => Vector2.Distance(Position, Target);

    /// <summary>0..1: fades while the planet is swallowed at the end of the dance.</summary>
    public float Alpha { get; internal set; } = 1f;

    /// <summary>Trail points relative to the white hole, newest first.</summary>
    public ReadOnlySpan<Vector2> Trail => _trail;

    /// <summary>Position on the ring, sorted by kind order and slot.</summary>
    public int RingOrder { get; internal set; }

    public Vector2 RingPosition { get; internal set; }

    public float RingAlpha { get; internal set; }

    internal Vector2[] TrailBuffer => _trail;
}

/// <summary>A dust grain of the pull: a short stroke from <see cref="Tail"/> to <see cref="Head"/> plus a 2 × 2 head.</summary>
public readonly record struct FinaleDust(Vector2 Head, Vector2 Tail, SceneColor Color, float Alpha);

/// <summary>A spark of the explosion: a stroke from <see cref="From"/> to <see cref="To"/>.</summary>
public readonly record struct FinaleSpark(Vector2 From, Vector2 To, SceneColor Color, float Alpha, float Width);

/// <summary>A radial glow; <see cref="ScaleY"/> squashes it vertically.</summary>
public readonly record struct SceneGlow(Vector2 Centre, float Radius, SceneColor Color, float Alpha, float ScaleY = 1f);

/// <summary>A stroke segment (trails of the holes).</summary>
public readonly record struct SceneSegment(Vector2 From, Vector2 To, SceneColor Color, float Alpha, float Width);

/// <summary>
/// The finale after the last file (worksheet "Übergänge", section "Abschluss"): run-up, dance of the two holes,
/// merger, silence, supernova, shake, ring with check mark. Deterministic in its own time <see cref="F"/>;
/// the dance is precomputed as a table with dt = 0.002 s when the finale starts.
/// </summary>
public sealed class FinaleScene
{
    public const float Ph1 = 1.15f;
    public const float Ph2 = 0.75f;
    public const float M = 2.4f;
    public const float Still = 0.28f;
    public const int Rounds = 4;

    /// <summary>ANL = PH1 + PH2: start of the dance.</summary>
    public const float Anl = Ph1 + Ph2;

    /// <summary>DET = ANL + M + STILL: the supernova.</summary>
    public const float Det = Anl + M + Still;

    /// <summary>Time step of the dance table.</summary>
    public const float TableStep = 0.002f;

    /// <summary>Step of the inertia simulation.</summary>
    public const float SimulationStep = 1f / 240f;

    /// <summary>Most simulation steps per frame.</summary>
    public const int MaxStepsPerFrame = 2400;

    /// <summary>Soft cap of the inertia deviation, in DIP.</summary>
    public const float MaxDeviation = 28f;

    /// <summary>Length of the supernova, in seconds after DET.</summary>
    public const float NovaSeconds = 1.3f;

    /// <summary>From here on (e ≥ 1.2) the report card fades in.</summary>
    public const float ReportAt = 1.2f;

    /// <summary>Strength of the explosion (<c>st</c>).</summary>
    public const float Strength = 1.3f;

    /// <summary>Vertical squash of the dance and the shock waves.</summary>
    public const float DanceFlatten = 0.45f;

    /// <summary>16° per frame at 60 fps.</summary>
    public static readonly float MaxAngularSpeed = 16f / 180f * MathF.PI * 60f;

    /// <summary>Check mark polyline relative to its centre.</summary>
    public static readonly Vector2[] CheckPoints = [new(-7f, 0.5f), new(-2f, 5.5f), new(7.5f, -5.5f)];

    private const float DustLife = 0.85f;
    private const float EndTime = Det + 3f;

    private readonly WhiteHoleScene _whiteHole;
    private readonly List<FinalePlanet> _planets = [];
    private readonly DustSeed[] _dustSeeds;
    private readonly SparkSeed[] _sparkSeeds;
    private readonly FullStarSeed[] _fullStarSeeds;
    private readonly List<FinaleDust> _dust = [];
    private readonly List<FinaleSpark> _sparks = [];
    private readonly List<SceneStar> _fullStars = [];
    private readonly List<RingState> _waves = [];
    private readonly List<SceneSegment> _holeTrails = [];

    private readonly float[] _distance;
    private readonly float[] _angle;
    private readonly float[] _boost;
    private readonly float _d0;
    private readonly float _w0;
    private readonly float _phi0;
    private readonly float _fl0;
    private readonly Vector2 _s0;
    private readonly float _startTime;
    private readonly int _orbits;
    private readonly bool _dustMode;

    private long _simSteps;
    private bool _completed;
    private float _previousE = float.NegativeInfinity;

    public FinaleScene(
        float width,
        float height,
        Vector2 blackHole,
        WhiteHoleScene whiteHole,
        ScenePalette palette,
        Random random,
        SwirlBudget budget,
        float startTime,
        int completed,
        int failed)
    {
        ArgumentNullException.ThrowIfNull(whiteHole);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(budget);

        Width = width;
        Height = height;
        Palette = palette;
        Completed = completed;
        Failed = failed;
        _whiteHole = whiteHole;
        _startTime = startTime;
        _dustMode = whiteHole.Mode == WhiteHoleMode.Dust;
        _orbits = _dustMode ? Math.Max(1, whiteHole.RingCount) : whiteHole.Orbits;

        BlackStart = blackHole;
        WhiteStart = whiteHole.Centre;
        EndCentre = new Vector2(width / 2f, 150f);
        _s0 = Vector2.Lerp(BlackStart, WhiteStart, 0.5f);

        // ---- dance table (tabelle) --------------------------------------------------------------
        _fl0 = DanceFlatten * 0.55f;
        var v = new Vector2(BlackStart.X - WhiteStart.X, (BlackStart.Y - WhiteStart.Y) / _fl0);
        _d0 = MathF.Max(1f, v.Length());
        _phi0 = MathF.Atan2(v.Y, v.X);

        var n = (int)MathF.Ceiling(M / TableStep) + 1;
        _distance = new float[n];
        _angle = new float[n];
        var sum = 0d;
        for (var i = 1; i < n; i++)
        {
            sum += Tempo(i * TableStep) * TableStep;
        }

        var k = Rounds * 2d * Math.PI / sum;
        _w0 = (float)(Tempo(0f) * k);
        var w = 0d;
        for (var i = 0; i < n; i++)
        {
            _distance[i] = Distance(i * TableStep);
            if (i > 0)
            {
                w += Math.Min(Tempo(i * TableStep) * k, MaxAngularSpeed) * TableStep;
            }

            _angle[i] = (float)(_phi0 + _w0 * Ph2 / 2d + w);
        }

        var n2 = (int)MathF.Ceiling(EndTime / TableStep);
        _boost = new float[n2];
        var e3 = Math.Exp(3.2) - 1d;
        var x = 0d;
        for (var i = 0; i < n2; i++)
        {
            var f = i * TableStep;
            if (i > 0)
            {
                x += (14d * (Math.Exp(3.2 * SceneMotion.Phase(f, 0f, Ph1)) - 1d) / e3 + 4d * SceneMotion.Phase(f, Anl, Anl + M)) * TableStep;
            }

            _boost[i] = (float)x;
        }

        // ---- planets, dust, sparks, stars ---------------------------------------------------------
        var slots = whiteHole.Slots;
        if (_dustMode)
        {
            // Dust rings: one collective planet per kind (and one coral one per kind with failures), numbered.
            for (var j = 0; j < whiteHole.RingCount; j++)
            {
                var kind = whiteHole.Kinds[j];
                var completedOfKind = 0;
                var failedOfKind = 0;
                foreach (var slot in slots)
                {
                    if (slot.Kind != kind)
                    {
                        continue;
                    }

                    if (slot.Failed)
                    {
                        failedOfKind++;
                    }
                    else
                    {
                        completedOfKind++;
                    }
                }

                if (completedOfKind > 0)
                {
                    _planets.Add(new FinalePlanet(j, kind, false, palette.For(kind), completedOfKind));
                }

                if (failedOfKind > 0)
                {
                    _planets.Add(new FinalePlanet(j + whiteHole.RingCount, kind, true, palette.Error, failedOfKind));
                }
            }
        }
        else
        {
            for (var s = 0; s < slots.Count; s++)
            {
                _planets.Add(new FinalePlanet(s, slots[s].Kind, slots[s].Failed, whiteHole.ColourOf(slots[s])));
            }
        }

        var order = _planets
            .OrderBy(p => KindRank(p.Kind))
            .ThenBy(p => p.Slot)
            .ThenBy(p => p.Failed)
            .ToList();
        for (var o = 0; o < order.Count; o++)
        {
            order[o].RingOrder = o;
        }

        var particles = Math.Max(0, budget.MaxParticles);
        _dustSeeds = new DustSeed[Math.Min(budget.Dust, particles)];
        for (var i = 0; i < _dustSeeds.Length; i++)
        {
            _dustSeeds[i] = new DustSeed(i, random.NextSingle(), (random.NextSingle() - 0.5f) * 0.3f, random.NextSingle() < 0.2f);
        }

        _sparkSeeds = new SparkSeed[Math.Min(budget.Sparks, particles - _dustSeeds.Length)];
        for (var i = 0; i < _sparkSeeds.Length; i++)
        {
            var angle = random.NextSingle() * MathF.Tau;
            var speed = (0.25f + 0.75f * MathF.Pow(random.NextSingle(), 0.7f)) * width * 0.5f;
            var duration = 0.9f + random.NextSingle() * 0.8f;
            SceneColor colour;
            if (_planets.Count == 0)
            {
                colour = palette.Glow;
                random.NextSingle();
            }
            else
            {
                var planet = _planets[(int)(random.NextSingle() * _planets.Count) % _planets.Count];
                colour = random.NextSingle() < 0.08f ? palette.Glow : planet.Color;
            }

            var thick = random.NextSingle() < 0.25f;
            _sparkSeeds[i] = new SparkSeed(angle, speed, duration, colour, thick ? 2.2f : 1.3f);
        }

        _fullStarSeeds = new FullStarSeed[Math.Max(0, budget.StarsFull)];
        for (var i = 0; i < _fullStarSeeds.Length; i++)
        {
            _fullStarSeeds[i] = new FullStarSeed(
                random.NextSingle(),
                random.NextSingle(),
                random.NextSingle() * 9f,
                0.4f + random.NextSingle() * 1.2f,
                random.NextSingle() < 0.12f ? 1.6f : 1f);
        }

        InitialiseSimulation();
        Evaluate();
    }

    public float Width { get; }

    public float Height { get; }

    public ScenePalette Palette { get; set; }

    public int Completed { get; }

    /// <summary>True when the white hole showed dust rings: the planets are one per kind, with a number.</summary>
    public bool IsDust => _dustMode;

    /// <summary>Radius of the planets: the collective planets of dust mode are larger (4.5), else the white hole radius.</summary>
    public float PlanetRadius => _dustMode ? 4.5f : _whiteHole.PlanetRadius;

    public int Failed { get; }

    /// <summary><c>H</c>: the black hole (middle of the swirl) at the start.</summary>
    public Vector2 BlackStart { get; }

    /// <summary><c>T</c>: the white hole at the start.</summary>
    public Vector2 WhiteStart { get; }

    /// <summary><c>C</c> = (W/2, 150): where both holes merge and the ring is centred.</summary>
    public Vector2 EndCentre { get; }

    /// <summary>Time since the finale started, in seconds.</summary>
    public float F { get; private set; }

    /// <summary>e = F - DET.</summary>
    public float E => F - Det;

    public FinalePhase Phase { get; private set; }

    /// <summary>True exactly in the one update in which e crosses 0 (the view runs the XAML shake).</summary>
    public bool ShakeRequested { get; private set; }

    /// <summary>How many shakes were requested (0 or 1), so a polling view cannot miss one.</summary>
    public int ShakeCount { get; private set; }

    /// <summary>e ≥ 1.2: the report card fades in.</summary>
    public bool ShowReport { get; private set; }

    /// <summary>True after <see cref="Complete"/>.</summary>
    public bool IsCompleted => _completed;

    public IReadOnlyList<FinalePlanet> Planets => _planets;

    /// <summary>Initial distance of the holes in the unflattened dance plane.</summary>
    public float InitialDistance => _d0;

    /// <summary>Angular speed at the start of the dance (<c>w0</c>).</summary>
    public float InitialAngularSpeed => _w0;

    // ----- per-frame values for the renderer --------------------------------------------------------

    /// <summary>The black and white hole and how near they are (0..1) in this frame.</summary>
    public (Vector2 Black, Vector2 White, float Near) CurrentHoles { get; private set; }

    /// <summary><c>hoch</c> = phase(f, 0, PH1).</summary>
    public float Rise { get; private set; }

    /// <summary>Alpha of the orbit lines; they are drawn around the white hole with <see cref="OrbitScale"/>.</summary>
    public float OrbitAlpha { get; private set; }

    public float OrbitScale { get; private set; } = 1f;

    /// <summary>Planets are drawn while the dance lasts (fd &lt; M).</summary>
    public bool PlanetsVisible { get; private set; }

    /// <summary>Trail points drawn per planet: round(8 + 6·hoch).</summary>
    public int TrailDrawn { get; private set; }

    /// <summary>Alpha of the XAML counter "n von N".</summary>
    public float CounterAlpha { get; private set; }

    /// <summary>Factor for the stars around the white hole.</summary>
    public float NearStarsFactor { get; private set; }

    /// <summary>Scale of the white core and of the black hole.</summary>
    public float CoreScale { get; private set; } = 1f;

    public float BlackScale { get; private set; } = 1f;

    /// <summary>Mint halo at the white hole during the run-up.</summary>
    public SceneGlow WhiteHalo { get; private set; }

    /// <summary>White halo in C when the holes come near.</summary>
    public SceneGlow MergeGlow { get; private set; }

    public IReadOnlyList<FinaleDust> Dust => _dust;

    public IReadOnlyList<SceneSegment> HoleTrails => _holeTrails;

    /// <summary>The glowing point of the silence, or null.</summary>
    public SceneGlow? SilenceGlow { get; private set; }

    /// <summary>The supernova gradient (white → mint 0.55 → violet 0.85 → 0), alpha factor (1 - u), or null.</summary>
    public SceneGlow? Nova { get; private set; }

    /// <summary>Alpha of the horizontal light streak (full width, 2.4 DIP high) and of its flat mint halo.</summary>
    public float StreakAlpha { get; private set; }

    public SceneGlow? ExplosionWhite { get; private set; }

    public SceneGlow? ExplosionMint { get; private set; }

    /// <summary>Alpha of the white flash over the whole surface.</summary>
    public float FlashAlpha { get; private set; }

    public IReadOnlyList<RingState> Waves => _waves;

    public IReadOnlyList<FinaleSpark> Sparks => _sparks;

    public IReadOnlyList<SceneStar> FullStars => _fullStars;

    /// <summary>Offset of the whole drawing while the canvas shakes.</summary>
    public Vector2 ShakeOffset { get; private set; }

    /// <summary>Ellipse of the ring: <c>Rx</c> = min(0.44·W, 440), <c>Ry</c> = 116.</summary>
    public Vector2 RingRadii => new(MathF.Min(Width * 0.44f, 440f), 116f);

    public float RingLineAlpha { get; private set; }

    /// <summary>Soft backdrop behind the report card.</summary>
    public float BackdropAlpha { get; private set; }

    public Vector2 CheckCentre { get; private set; }

    public float CheckDiscRadius { get; private set; }

    public float CheckHaloAlpha { get; private set; }

    /// <summary>0..1: how much of the check stroke is drawn (first segment up to 0.4).</summary>
    public float CheckStroke { get; private set; }

    /// <summary>Number of particles (dust and sparks) alive in this frame.</summary>
    public int ParticleCount => _dust.Count + _sparks.Count;

    // ----- geometry ---------------------------------------------------------------------------------

    /// <summary>Common centre m(f) = lerp(s0, C, ease(phase(f, PH1, ANL + 1.2))).</summary>
    public Vector2 CentreAt(float f) => Vector2.Lerp(_s0, EndCentre, SceneMotion.Ease(SceneMotion.Phase(f, Ph1, Anl + 1.2f)));

    /// <summary>Distance of the holes in the unflattened dance plane at fd = f - ANL.</summary>
    public float DistanceAt(float fd) => Sample(_distance, fd);

    /// <summary>The angle of the black hole around the common centre (unflattened plane).</summary>
    public float HoleAngle(float f)
    {
        if (f >= Anl)
        {
            return Sample(_angle, f - Anl);
        }

        var u = SceneMotion.Phase(f, Ph1, Anl);
        return _phi0 + _w0 * Ph2 * u * u / 2f;
    }

    /// <summary>Both holes at time <paramref name="f"/> (<c>loecher</c>); they are point-symmetric to <see cref="CentreAt"/>.</summary>
    public (Vector2 Black, Vector2 White, float Near) Holes(float f)
    {
        var m = CentreAt(f);
        if (f >= Anl)
        {
            var d = DistanceAt(f - Anl);
            var phi = Sample(_angle, f - Anl);
            var fl = DanceFlatten * (0.55f + 0.45f * Math.Clamp(1f - d / _d0, 0f, 1f));
            var offset = new Vector2(MathF.Cos(phi) * d * 0.5f, MathF.Sin(phi) * d * 0.5f * fl);
            return (m + offset, m - offset, Math.Clamp(1f - d / (_d0 * 0.36f), 0f, 1f));
        }

        var u = SceneMotion.Phase(f, Ph1, Anl);
        var a = _w0 * Ph2 * u * u / 2f;
        return (Place(BlackStart, a, m), Place(WhiteStart, a, m), 0f);
    }

    /// <summary>Scale of the orbits (<c>skalVon</c>).</summary>
    public static float OrbitScaleAt(float f) =>
        float.Lerp(1f, 0.58f, SceneMotion.Ease(SceneMotion.Phase(f, 0.15f, Ph1 + 0.45f)))
        * float.Lerp(1f, 0.75f, SceneMotion.Ease(SceneMotion.Phase(f - Anl, 0f, M)));

    /// <summary>The phase for a finale time.</summary>
    public static FinalePhase PhaseAt(float f)
    {
        if (f < Anl)
        {
            return FinalePhase.RunUp;
        }

        if (f < Anl + M)
        {
            return FinalePhase.Dance;
        }

        if (f < Det)
        {
            return FinalePhase.Silence;
        }

        var e = f - Det;
        return e < NovaSeconds ? FinalePhase.Nova : e < 1.8f ? FinalePhase.Ring : FinalePhase.Done;
    }

    /// <summary>The ideal place of planet <paramref name="slot"/> (<c>zielAbschluss</c>).</summary>
    public Vector2 PlanetTarget(int slot, float f)
    {
        var white = Holes(f).White;
        var r = OrbitRadiusOf(slot) * OrbitScaleAt(f);
        var w = OrbitAngle(slot, f);
        var onOrbit = white + new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * WhiteHoleScene.Flatten);
        return Vector2.Lerp(onOrbit, white, SceneMotion.Ease(SceneMotion.Phase(f - Anl, M - 0.5f, M)));
    }

    /// <summary>Jump to the end state (reduced motion, skip, resize): no shake, report shown.</summary>
    public void Complete()
    {
        _completed = true;
        F = EndTime;
        ShakeRequested = false;
        Evaluate();
    }

    /// <summary>Advances the finale; called by <see cref="SwirlScene.Update"/> with the clamped step.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        else if (elapsed > SwirlScene.MaxStep)
        {
            elapsed = SwirlScene.MaxStep;
        }

        ShakeRequested = false;
        if (!_completed)
        {
            F += (float)elapsed.TotalSeconds;
            Simulate(MathF.Min(F, Anl + M));
            var e = E;
            if (_previousE < 0f && e >= 0f)
            {
                ShakeRequested = true;
                ShakeCount++;
            }

            _previousE = e;
        }

        Evaluate();
    }

    // ----- simulation -------------------------------------------------------------------------------

    private void InitialiseSimulation()
    {
        var h = SimulationStep;
        foreach (var p in _planets)
        {
            var a = PlanetTarget(p.Slot, 0f);
            var b = PlanetTarget(p.Slot, -h);
            p.Position = a;
            p.Target = a;
            p.Velocity = (a - b) / h;
            var trail = p.TrailBuffer;
            for (var i = 0; i < trail.Length; i++)
            {
                trail[i] = PlanetTarget(p.Slot, -(i + 1) * 4 * h) - WhiteStart;
            }
        }
    }

    private void Simulate(float until)
    {
        var h = SimulationStep;
        var steps = 0;
        while ((_simSteps + 1) * (double)h <= until + 1e-6 && steps < MaxStepsPerFrame)
        {
            steps++;
            _simSteps++;
            var f = (float)(_simSteps * (double)h);
            var holes = Holes(f);
            var near = f > Anl ? holes.Near : 0f;
            foreach (var p in _planets)
            {
                var a = PlanetTarget(p.Slot, f);
                var b = PlanetTarget(p.Slot, f - h);
                var j = OrbitIndexOf(p.Slot);
                var kf = float.Lerp(150f, 45f, (float)j / Math.Max(1, _orbits - 1)) * (1f + 12f * near * near);
                var c = MathF.Sqrt(kf);
                var velocity = p.Velocity + (kf * (a - p.Position) + c * ((a - b) / h - p.Velocity)) * h;
                var position = p.Position + velocity * h;
                var delta = position - a;
                var l = delta.Length();
                if (l > 1f)
                {
                    position = a + delta * (MaxDeviation * MathF.Tanh(l / MaxDeviation) / l);
                }

                p.Velocity = velocity;
                p.Position = position;
                p.Target = a;
                if (_simSteps % 4 == 0)
                {
                    var trail = p.TrailBuffer;
                    Array.Copy(trail, 0, trail, 1, trail.Length - 1);
                    trail[0] = position - holes.White;
                }
            }
        }
    }

    // ----- evaluation -------------------------------------------------------------------------------

    private void Evaluate()
    {
        var f = F;
        var fd = f - Anl;
        var e = E;
        var t = _startTime + f;
        Phase = _completed ? FinalePhase.Done : PhaseAt(f);
        ShowReport = _completed || e >= ReportAt;

        var holes = Holes(MathF.Min(f, Anl + M));
        CurrentHoles = holes;
        Rise = SceneMotion.Phase(f, 0f, Ph1);
        PlanetsVisible = fd < M;
        OrbitAlpha = PlanetsVisible ? 1f - SceneMotion.Phase(f, 0.3f, Ph1 + 0.3f) : 0f;
        OrbitScale = OrbitScaleAt(f);
        TrailDrawn = (int)MathF.Round(8f + 6f * Rise);
        CounterAlpha = 1f - SceneMotion.Phase(f, 0f, 0.4f);
        NearStarsFactor = 1f - SceneMotion.Phase(f, Ph1, Anl + 1f);
        CoreScale = 1f + 0.2f * holes.Near + 0.1f * Rise;
        BlackScale = 1f - 0.25f * holes.Near;
        WhiteHalo = new SceneGlow(holes.White, 50f + 40f * Rise, Palette.Mint, 0.22f * Rise * (1f - SceneMotion.Phase(fd, 0f, 1f)));
        MergeGlow = new SceneGlow(EndCentre, 40f + 80f * holes.Near, Palette.Glow, PlanetsVisible ? 0.35f * holes.Near * holes.Near : 0f);

        // Dust mode: the collective planets did not exist during the round, they fade in over 0.35 s.
        var fade = (1f - SceneMotion.Phase(fd, M - 0.3f, M)) * (_dustMode ? SceneMotion.Phase(f, 0f, 0.35f) : 1f);
        foreach (var p in _planets)
        {
            p.Alpha = PlanetsVisible ? fade : 0f;
        }

        EvaluateDust(f, fd, holes.White);
        EvaluateHoleTrails(f, fd);
        EvaluateNova(e);
        EvaluateRing(e, t);

        ShakeOffset = e >= 0f && e < 0.45f && !_completed
            ? new Vector2(MathF.Sin(e * 97f), MathF.Cos(e * 83f) * 0.6f) * (5f * (1f - e / 0.45f))
            : Vector2.Zero;

        _fullStars.Clear();
        var fullFactor = SceneMotion.Phase(e, 0.4f, 1.8f);
        if (fullFactor > 0f)
        {
            var colour = Palette.IsDark ? SceneColor.FromHex(0xDFE9EF) : Palette.LineStrong;
            var theme = Palette.IsDark ? 0.8f : 0.55f;
            foreach (var s in _fullStarSeeds)
            {
                var alpha = (0.25f + 0.5f * (0.5f + 0.5f * MathF.Sin(t * s.Frequency + s.Phase))) * theme * fullFactor;
                _fullStars.Add(new SceneStar(new Vector2(s.X * Width, s.Y * Height), s.Size, alpha, colour));
            }
        }

        BackdropAlpha = SceneMotion.Phase(e, 0.9f, 1.7f);
        var k = SceneMotion.Phase(e, 0.5f, 1.4f);
        CheckCentre = new Vector2(EndCentre.X, float.Lerp(EndCentre.Y, 96f, SceneMotion.Ease(SceneMotion.Phase(e, 0.6f, 1.4f))));
        CheckDiscRadius = k <= 0f ? 0f : 16f * SceneMotion.EaseOut(Math.Clamp(k / 0.45f, 0f, 1f));
        CheckHaloAlpha = k <= 0f ? 0f : (Palette.IsDark ? 0.22f : 0.16f) * (1f + 0.1f * MathF.Sin(t * 1.6f));
        CheckStroke = Math.Clamp((k - 0.4f) / 0.6f, 0f, 1f);
    }

    private void EvaluateDust(float f, float fd, Vector2 white)
    {
        _dust.Clear();
        if (_planets.Count == 0 || fd >= M)
        {
            return;
        }

        var pull = SceneMotion.Phase(f, 0.1f, Ph1 * 0.7f) * (1f - SceneMotion.Phase(fd, M - 0.5f, M - 0.1f));
        if (pull <= 0f)
        {
            return;
        }

        foreach (var q in _dustSeeds)
        {
            var planet = _planets[q.Index % _planets.Count];
            if (planet.Failed)
            {
                continue;
            }

            var u = (f / DustLife + q.Phase) % 1f;
            var fr = f - u * DustLife;
            if (fr < 0f)
            {
                continue;
            }

            var r0 = OrbitRadiusOf(planet.Slot) * OrbitScaleAt(fr);
            var w0 = OrbitAngle(planet.Slot, fr);
            var head = DustAt(f, fr, r0, w0, planet.Slot, q.Scatter, white);
            var tail = DustAt(MathF.Max(fr, f - 0.045f), fr, r0, w0, planet.Slot, q.Scatter, white);
            var alpha = pull * MathF.Pow(MathF.Sin(MathF.PI * u), 0.7f) * (0.35f + 0.65f * u);
            _dust.Add(new FinaleDust(head, tail, q.White ? Palette.Glow : planet.Color, alpha));
        }
    }

    private Vector2 DustAt(float x, float fr, float r0, float w0, int slot, float scatter, Vector2 white)
    {
        var v = Math.Clamp((x - fr) / DustLife, 0f, 1f);
        var r = 4f + r0 * MathF.Pow(1f - v, 1.4f) * (1f + scatter * v);
        var w = w0 + (OrbitAngle(slot, x) - w0) * (1f + 1.3f * v);
        return white + new Vector2(MathF.Cos(w) * r, MathF.Sin(w) * r * WhiteHoleScene.Flatten);
    }

    private void EvaluateHoleTrails(float f, float fd)
    {
        _holeTrails.Clear();
        if (f <= Ph1 || fd >= M)
        {
            return;
        }

        var a = 0.6f * SceneMotion.Phase(f, Ph1, Anl);
        for (var i = 1; i <= 10; i++)
        {
            var p = Holes(MathF.Max(0f, f - (i - 1) * 0.018f));
            var q = Holes(MathF.Max(0f, f - i * 0.018f));
            var alpha = a * (1f - i / 11f);
            var width = 3f * (1f - i / 12f);
            _holeTrails.Add(new SceneSegment(p.Black, q.Black, Palette.Audio, alpha, width));
            _holeTrails.Add(new SceneSegment(p.White, q.White, Palette.Mint, alpha, width));
        }
    }

    private void EvaluateNova(float e)
    {
        var c = EndCentre;
        var silence = e + Still;
        SilenceGlow = silence >= 0f && silence < Still
            ? new SceneGlow(c, 70f * (1f - silence / Still) + 6f, Palette.Glow, 0.9f)
            : null;

        _waves.Clear();
        _sparks.Clear();
        if (e < 0f || _completed)
        {
            Nova = null;
            ExplosionWhite = null;
            ExplosionMint = null;
            StreakAlpha = 0f;
            FlashAlpha = 0f;
            return;
        }

        var u = Math.Clamp(e / NovaSeconds, 0f, 1f);
        Nova = u < 1f ? new SceneGlow(c, 20f + SceneMotion.EaseOut(u) * Width * 0.32f, Palette.Glow, 1f - u, 0.62f) : null;
        StreakAlpha = 1f - Math.Clamp(e / 0.9f, 0f, 1f);

        var whiteAlpha = 0.7f * (1f - Math.Clamp(e / 0.9f, 0f, 1f));
        ExplosionWhite = whiteAlpha > 0f
            ? new SceneGlow(c, 60f + 280f * SceneMotion.EaseOut(Math.Clamp(e / 0.35f, 0f, 1f)) * Strength, Palette.Glow, whiteAlpha)
            : null;
        var mintAlpha = 0.6f * (1f - Math.Clamp(e / 0.6f, 0f, 1f));
        ExplosionMint = mintAlpha > 0f
            ? new SceneGlow(c, 30f + 140f * SceneMotion.EaseOut(Math.Clamp(e / 0.2f, 0f, 1f)), Palette.Mint, mintAlpha)
            : null;
        FlashAlpha = e < 0.45f ? 0.1f * Strength * (1f - e / 0.45f) : 0f;

        for (var i = 0; i < 3; i++)
        {
            var w = Math.Clamp((e - i * 0.12f) / 1.4f, 0f, 1f);
            if (w <= 0f || w >= 1f)
            {
                continue;
            }

            _waves.Add(new RingState(
                c,
                10f + SceneMotion.EaseOut(w) * Width * 0.62f * Strength,
                DanceFlatten,
                i == 0 ? Palette.Glow : Palette.Mint,
                (1f - w) * 0.8f,
                3.4f * (1f - w) + 0.6f));
        }

        foreach (var s in _sparkSeeds)
        {
            var w = Math.Clamp(e / s.Duration, 0f, 1f);
            if (w >= 1f)
            {
                continue;
            }

            var direction = new Vector2(MathF.Cos(s.Angle), MathF.Sin(s.Angle) * 0.5f);
            var to = s.Speed * Strength * SceneMotion.EaseOut(w);
            var from = s.Speed * Strength * SceneMotion.EaseOut(MathF.Max(0f, w - 0.07f));
            _sparks.Add(new FinaleSpark(c + direction * from, c + direction * to, s.Color, (1f - w) * 0.85f, s.Width));
        }
    }

    private void EvaluateRing(float e, float t)
    {
        RingLineAlpha = 0.22f * SceneMotion.Phase(e, 0.9f, 1.6f);
        var n = _planets.Count;
        var radii = RingRadii;
        foreach (var p in _planets)
        {
            if (e <= 0f || n == 0)
            {
                p.RingAlpha = 0f;
                p.RingPosition = EndCentre;
                continue;
            }

            var o = p.RingOrder;
            var u = SceneMotion.EaseOut(SceneMotion.Phase(e, 0.1f + 0.35f * o / n, 1.2f + 0.35f * o / n));
            var w = -MathF.PI / 2f + (float)o / n * MathF.Tau + t * 0.08f;
            var onRing = EndCentre + new Vector2(MathF.Cos(w) * radii.X, MathF.Sin(w) * radii.Y);
            p.RingPosition = Vector2.Lerp(EndCentre, onRing, u);
            p.RingAlpha = SceneMotion.Phase(e, 0.1f, 0.3f);
        }
    }

    // ----- helpers ----------------------------------------------------------------------------------

    private float OrbitAngle(int slot, float f) =>
        BaseAngle(slot, _startTime + f) + 0.9f * MathF.Pow(24f / OrbitRadiusOf(slot), 0.7f) * Boost(f);

    /// <summary>Orbit of a slot: the ring of the kind in dust mode, else <c>k mod B</c>.</summary>
    private int OrbitIndexOf(int slot) => slot % _orbits;

    private float OrbitRadiusOf(int slot) => _dustMode ? _whiteHole.RingRadius(OrbitIndexOf(slot)) : _whiteHole.OrbitRadius(OrbitIndexOf(slot));

    // Dust mode: the collective planet rides its ring at the ring rotation, offset per ring so the planets
    // never line up, the coral planet of the failures (slot + rings) opposite; ring mode: the place angle.
    private float BaseAngle(int slot, float t) => _dustMode
        ? _whiteHole.RingRotation(OrbitIndexOf(slot), t) + OrbitIndexOf(slot) * 2.39f + (slot / _orbits) * MathF.PI
        : _whiteHole.Angle(slot, t);

    private float Boost(float f) => f <= 0f ? 0f : Sample(_boost, f);

    private Vector2 Place(Vector2 p, float angle, Vector2 m)
    {
        var v = new Vector2(p.X - _s0.X, (p.Y - _s0.Y) / _fl0);
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        var r = new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        return new Vector2(m.X + r.X, m.Y + r.Y * _fl0);
    }

    private float Distance(float fd) =>
        _d0 * float.Lerp(1f, 0.36f, SceneMotion.Ease(SceneMotion.Phase(fd, 0f, 1.1f))) * MathF.Pow(MathF.Max(0f, M - fd) / M, 0.25f);

    private double Tempo(float fd) => Math.Pow(Math.Max(Distance(fd), _d0 * 0.02f) / _d0, -1.5);

    // Linear interpolation in a table with step dt; the design rounds to the nearest entry, interpolation
    // keeps the angular speed between two entries exactly at the table's (capped) rate.
    private static float Sample(float[] table, float x)
    {
        var position = Math.Max(0f, x) / TableStep;
        var i = (int)position;
        if (i >= table.Length - 1)
        {
            return table[^1];
        }

        return float.Lerp(table[i], table[i + 1], position - i);
    }

    private static int KindRank(MediaKind kind)
    {
        for (var i = 0; i < TargetPlanner.KindOrder.Count; i++)
        {
            if (TargetPlanner.KindOrder[i] == kind)
            {
                return i;
            }
        }

        return TargetPlanner.KindOrder.Count;
    }

    private readonly record struct DustSeed(int Index, float Phase, float Scatter, bool White);

    private readonly record struct SparkSeed(float Angle, float Speed, float Duration, SceneColor Color, float Width);

    private readonly record struct FullStarSeed(float X, float Y, float Phase, float Frequency, float Size);
}
