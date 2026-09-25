using System.Collections.Concurrent;
using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>The three flights of the overlay (ADR-023).</summary>
public enum TransitionKind
{
    /// <summary>Step 1 → 2: trays are sucked into the hole and jump out as kind symbols.</summary>
    WormholeForward,

    /// <summary>Step 2 → 1: kind symbols fly back to the trays on an arc.</summary>
    ArcBack,

    /// <summary>Step 2 → 3: the hole hands the files as sheets to the inbox stack and moves into the swirl.</summary>
    SheetsForward,
}

/// <summary>The drawn stand-ins of the overlay.</summary>
public enum GhostShape
{
    TrayHeader,
    KindSymbol,
    Sheet,
}

/// <summary>What the overlay flies: a stand-in, never a copy of a XAML element (ADR-023). Texts come ready from the view model.</summary>
public sealed record GhostSpec(GhostShape Shape, MediaKind Kind, string Label, string? Count, string? FormatLabel, string? FileName);

/// <summary>Anchor data the service measured on the UI thread; plain vectors, no UI types.</summary>
public sealed record TransitionEndpoint(Vector2 Centre, Vector2 Size);

/// <summary>One ghost of a plan: per kind (trays, symbols) or per file (sheets).</summary>
public sealed record TransitionGhost(GhostSpec Ghost, TransitionEndpoint From, TransitionEndpoint To);

/// <summary>
/// Everything a transition needs, measured before it starts. For <see cref="TransitionKind.SheetsForward"/>
/// the ghosts are the kind symbols (they pulse at <c>From</c>) followed by every sheet of the plan in plan
/// order (they fly from the hole to <c>To</c>, their place on the inbox stack).
/// </summary>
public sealed record TransitionPlan(
    TransitionKind Kind,
    IReadOnlyList<TransitionGhost> Ghosts,
    TransitionEndpoint HoleFrom,
    float HoleRadiusFrom,
    TransitionEndpoint HoleTo,
    float HoleRadiusTo);

/// <summary>
/// A ghost in one frame. <see cref="GhostSpec.Shape"/> is drawn with <see cref="AlphaA"/>, <see cref="ShapeB"/>
/// (if any) with <see cref="AlphaB"/>, both around <see cref="Position"/> with the same transform. Each shape
/// is scaled by <see cref="Width"/> divided by its own natural width (<see cref="NaturalSizeA"/>,
/// <see cref="NaturalSizeB"/>), as the design scales each copy relative to its own box.
/// </summary>
public readonly record struct GhostState(
    GhostSpec Spec,
    Vector2 Position,
    float Width,
    float ScaleX,
    float ScaleY,
    float Rotation,
    float AlphaA,
    float AlphaB,
    GhostShape? ShapeB = null,
    Vector2 NaturalSizeA = default,
    Vector2 NaturalSizeB = default);

/// <summary>An expanding ellipse (flattened by <see cref="Flatten"/>).</summary>
public readonly record struct RingState(Vector2 Centre, float Radius, float Flatten, SceneColor Color, float Alpha, float Width);

/// <summary>The travelling black hole of the overlay.</summary>
public readonly record struct HoleState(Vector2 Position, float Radius, float Alpha);

/// <summary>What the UI thread reads: whether the flight is over and how far the new page has faded in.</summary>
public sealed record TransitionSnapshot(bool IsFinished, float PageFadeIn, TimeSpan Time)
{
    public static readonly TransitionSnapshot Start = new(false, 0f, TimeSpan.Zero);
}

/// <summary>
/// One transition, deterministic in time (worksheet "Übergänge"). Built from a plan; finished when every
/// track, the rings and the hole are done. <see cref="Update"/> runs on the drawing thread; the UI thread
/// only calls <see cref="Finish"/> and reads <see cref="Snapshot"/>.
/// </summary>
public sealed class TransitionScene
{
    /// <summary>A step longer than this is clamped.</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(0.1);

    /// <summary>At most this many sheets fly; the rest already lie on the stack when the page fades in.</summary>
    public const int MaxFlyingSheets = 24;

    /// <summary>Upper bound of the sheet flight: 0.15 + (n - 1)·Δ + 0.95 never exceeds it.</summary>
    public const float MaxSheetsSeconds = 2.0f;

    /// <summary>Clean-up after the last track (<c>aufraeumen</c>).</summary>
    public const float CleanUpDelay = 0.08f;

    /// <summary>Fade of the overlay hole after the clean-up (<c>L.aus</c>).</summary>
    public const float HoleFade = 0.28f;

    /// <summary>The new page starts fading in this long before the flights end ...</summary>
    public const float PageFadeLead = 0.26f;

    /// <summary>... and needs this long.</summary>
    public const float PageFadeLength = 0.32f;

    /// <summary>Sparks of the hole dissolving into the swirl (own addition, worksheet 2→3).</summary>
    public const int SparkCount = 40;

    private const float RingSeconds = 0.65f;
    private const float RingFlatten = 0.36f;

    private readonly List<Track> _tracks = [];
    private readonly List<RingPlan> _ringPlans = [];
    private readonly List<GhostState> _ghosts = [];
    private readonly List<RingState> _rings = [];
    private readonly GalaxyParticle[] _sparkSeeds;
    private readonly List<GalaxyParticle> _sparks = [];
    private readonly ConcurrentQueue<bool> _finishRequests = new();
    private readonly HolePlan _hole;

    private volatile TransitionSnapshot _snapshot = TransitionSnapshot.Start;

    public TransitionScene(TransitionPlan plan, ScenePalette palette, Random random)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(random);

        Plan = plan;
        Palette = palette;

        switch (plan.Kind)
        {
            case TransitionKind.WormholeForward:
                BuildWormhole();
                break;
            case TransitionKind.ArcBack:
                BuildArc();
                break;
            default:
                BuildSheets();
                break;
        }

        FlightsEnd = _tracks.Where(t => t.CountsForEnd).Select(t => t.Delay + t.Length).DefaultIfEmpty(0f).Max();
        _hole = BuildHolePlan();

        _sparkSeeds = new GalaxyParticle[plan.Kind == TransitionKind.SheetsForward ? SparkCount : 0];
        for (var i = 0; i < _sparkSeeds.Length; i++)
        {
            _sparkSeeds[i] = new GalaxyParticle
            {
                Orbit = -1,
                BodyIndex = -1,
                R1 = random.NextSingle(),
                R2 = random.NextSingle(),
                R3 = random.NextSingle(),
                Size = 2f,
                Color = random.NextSingle() < 0.5f ? palette.Mint : palette.Audio,
            };
        }

        Duration = TimeSpan.FromSeconds(FlightsEnd + CleanUpDelay + HoleFade);
        Evaluate();
    }

    public TransitionPlan Plan { get; }

    public ScenePalette Palette { get; set; }

    /// <summary><c>fertigZeit</c>: the end of the latest flight, in seconds.</summary>
    public float FlightsEnd { get; }

    /// <summary>fertigZeit + 0.08 s + 0.28 s hole fade.</summary>
    public TimeSpan Duration { get; }

    public TimeSpan Time { get; private set; }

    public bool IsFinished => Time >= Duration;

    /// <summary>0..1, starts at fertigZeit - 0.26 s over 0.32 s; the service drives the page opacity from the snapshot.</summary>
    public float PageFadeIn { get; private set; }

    public IReadOnlyList<GhostState> Ghosts => _ghosts;

    public IReadOnlyList<RingState> Rings => _rings;

    /// <summary>Only <see cref="TransitionKind.SheetsForward"/>, in the last 0.28 s.</summary>
    public IReadOnlyList<GalaxyParticle> Sparks => _sparks;

    /// <summary>Null once the transition is finished.</summary>
    public HoleState? Hole { get; private set; }

    /// <summary>Delay and length of the hole's travel, in seconds (<c>lochPlan</c>).</summary>
    public (float Start, float Length) HoleTravel => (_hole.Start, _hole.Length);

    /// <summary>Replaced as a whole at the end of every <see cref="Update"/>.</summary>
    public TransitionSnapshot Snapshot => _snapshot;

    /// <summary>The number of tracks built from the plan (flying sheets are capped at <see cref="MaxFlyingSheets"/>).</summary>
    public int TrackCount => _tracks.Count;

    /// <summary>Delay and length of one track, in seconds, in the order of the plan.</summary>
    public (float Delay, float Length) TrackTiming(int index) => (_tracks[index].Delay, _tracks[index].Length);

    /// <summary>The stations of one track, for tests and for a later inspection view.</summary>
    public IReadOnlyList<GhostKeyframe> TrackKeys(int index) => _tracks[index].Keys;

    /// <summary>Start times of the rings in seconds, in the order they were planned.</summary>
    public IReadOnlyList<float> RingStarts => _ringPlans.Select(r => r.Start).ToList();

    /// <summary>
    /// The stagger of the sheets: min(0.09 s, 0.90 s / max(1, n - 1)), so the flight never exceeds 2.0 s.
    /// </summary>
    public static float SheetStagger(int sheetCount) => MathF.Min(0.09f, 0.90f / Math.Max(1, sheetCount - 1));

    /// <summary>Thread safe: jump to the end state at the next <see cref="Update"/>.</summary>
    public void Finish() => _finishRequests.Enqueue(true);

    /// <summary>Advances by <paramref name="elapsed"/> (clamped to 100 ms) and publishes a snapshot.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        else if (elapsed > MaxStep)
        {
            elapsed = MaxStep;
        }

        var jump = false;
        while (_finishRequests.TryDequeue(out _))
        {
            jump = true;
        }

        Time = jump ? Duration : Time + elapsed;
        if (Time > Duration)
        {
            Time = Duration;
        }

        Evaluate();
    }

    // ----- building ---------------------------------------------------------------------------------

    private void BuildWormhole()
    {
        var c = Plan.HoleFrom.Centre;
        var i = 0;
        foreach (var g in Plan.Ghosts)
        {
            var s = g.From.Centre;
            var t = g.To.Centre;
            var sw = g.From.Size.X;
            var tw = g.To.Size.X;
            var keys = new[]
            {
                new GhostKeyframe(0f, s, sw, 1f, 1f, 0f, 1f, 0f, SceneEasing.Suck),
                new GhostKeyframe(0.30f, Vector2.Lerp(s, c, 0.62f), 0.4f * sw, 0.55f, 1.5f, -0.5f, 1f, 0f, SceneEasing.Suck),
                new GhostKeyframe(0.42f, c, 6f, 0.3f, 2f, -1.2f, 0f, 0f, SceneEasing.Linear),
                new GhostKeyframe(0.56f, t, 6f, 1f, 1f, 0f, 0f, 0f, SceneEasing.Overshoot),
                new GhostKeyframe(0.82f, t, 1.06f * tw, 1f, 1f, 0f, 0f, 1f, SceneEasing.EaseOutCss),
                new GhostKeyframe(1f, t, tw, 1f, 1f, 0f, 0f, 1f, SceneEasing.Linear),
            };
            var track = new Track(g, GhostShape.KindSymbol, 0.12f * i, 1.40f, keys, countsForEnd: true);
            _tracks.Add(track);
            _ringPlans.Add(new RingPlan(c, 14f, 44f, Palette.For(g.Ghost.Kind), track.Delay + 0.42f * track.Length));
            _ringPlans.Add(new RingPlan(new Vector2(t.X, t.Y - 8f), 2f, 40f, Palette.Mint, track.Delay + 0.56f * track.Length));
            i++;
        }
    }

    private void BuildArc()
    {
        var i = 0;
        foreach (var g in Plan.Ghosts)
        {
            var s = g.From.Centre;
            var t = g.To.Centre;
            var sw = g.From.Size.X;
            var tw = g.To.Size.X;
            var apex = new Vector2(
                float.Lerp(s.X, t.X, 0.5f) - 30f,
                MathF.Min(s.Y, t.Y) + 0.35f * MathF.Abs(s.Y - t.Y) - 40f - 14f * i);
            var keys = new[]
            {
                new GhostKeyframe(0f, s, sw, 1f, 1f, 0f, 1f, 0f, SceneEasing.ArcRise),
                new GhostKeyframe(0.5f, apex, float.Lerp(sw, tw, 0.6f), 1f, 1f, 0f, 0.45f, 0.55f, SceneEasing.ArcLand),
                new GhostKeyframe(1f, t, tw, 1f, 1f, 0f, 0f, 1f, SceneEasing.Linear),
            };
            _tracks.Add(new Track(g, GhostShape.TrayHeader, 0.07f * i, 0.82f, keys, countsForEnd: true));
            i++;
        }
    }

    private void BuildSheets()
    {
        var symbols = Plan.Ghosts.Where(g => g.Ghost.Shape != GhostShape.Sheet).ToList();
        var sheets = Plan.Ghosts.Where(g => g.Ghost.Shape == GhostShape.Sheet).ToList();

        for (var i = 0; i < symbols.Count; i++)
        {
            var g = symbols[i];
            var s = g.From.Centre;
            var w = g.From.Size.X;
            var keys = new[]
            {
                new GhostKeyframe(0f, s, w, 1f, 1f, 0f, 1f, 0f, SceneEasing.Linear),
                new GhostKeyframe(0.35f, s, 1.04f * w, 1f, 1f, 0f, 1f, 0f, SceneEasing.Linear),
                new GhostKeyframe(1f, s, 0.9f * w, 1f, 1f, 0f, 0f, 0f, SceneEasing.Linear),
            };
            _tracks.Add(new Track(g, null, 0.06f * i, 1.1f, keys, countsForEnd: sheets.Count == 0));
        }

        var n = sheets.Count;
        var flying = Math.Min(n, MaxFlyingSheets);
        var stagger = SheetStagger(n);
        var a = Plan.HoleFrom.Centre;
        for (var j = 0; j < flying; j++)
        {
            var g = sheets[j];
            var b = g.To.Centre;
            var tw = g.To.Size.X;
            var apex = new Vector2(float.Lerp(a.X, b.X, 0.5f) + 20f, MathF.Min(a.Y, b.Y) - 50f - 6f * j);
            var rotation = SwirlScene.StackSlot(j, n, 0f).Rotation;
            var keys = new[]
            {
                new GhostKeyframe(0f, a, 10f, 1f, 1f, 0f, 0f, 0f, SceneEasing.EaseOutCss),
                new GhostKeyframe(0.18f, Vector2.Lerp(a, apex, 0.25f), 26f, 1f, 1f, 0f, 1f, 0f, SceneEasing.SheetRise),
                new GhostKeyframe(0.55f, apex, float.Lerp(26f, tw, 0.7f), 1f, 1f, 0.15f, 1f, 0f, SceneEasing.ArcLand),
                new GhostKeyframe(1f, b, tw, 1f, 1f, rotation, 1f, 0f, SceneEasing.Linear),
            };
            _tracks.Add(new Track(g, null, 0.15f + j * stagger, 0.95f, keys, countsForEnd: true, naturalA: new Vector2(SheetRaster.Width, SheetRaster.Height)));
        }
    }

    private HolePlan BuildHolePlan()
    {
        var from = Plan.HoleFrom.Centre;
        var to = Plan.HoleTo.Centre;
        return Plan.Kind switch
        {
            TransitionKind.WormholeForward => new HolePlan(from, to, Plan.HoleRadiusFrom, Plan.HoleRadiusTo, 0.55f * FlightsEnd, 0.40f * FlightsEnd, 0f),
            TransitionKind.ArcBack => new HolePlan(from, to, Plan.HoleRadiusFrom, Plan.HoleRadiusTo, 0f, 0.9f * FlightsEnd, 0f),
            _ => new HolePlan(from, to, Plan.HoleRadiusFrom, Plan.HoleRadiusTo, 0f, 0.85f * FlightsEnd, 30f),
        };
    }

    // ----- evaluation -------------------------------------------------------------------------------

    private void Evaluate()
    {
        var t = (float)Time.TotalSeconds;
        var cleanUp = FlightsEnd + CleanUpDelay;

        _ghosts.Clear();
        if (t < cleanUp)
        {
            foreach (var track in _tracks)
            {
                var u = track.Length <= 0f ? 1f : Math.Clamp((t - track.Delay) / track.Length, 0f, 1f);
                var state = SceneMotion.Evaluate(track.Ghost.Ghost, track.Keys, u);
                _ghosts.Add(state with { ShapeB = track.ShapeB, NaturalSizeA = track.NaturalA, NaturalSizeB = track.Ghost.To.Size });
            }
        }

        _rings.Clear();
        foreach (var ring in _ringPlans)
        {
            var e = (t - ring.Start) / RingSeconds;
            if (e < 0f || e >= 1f)
            {
                continue;
            }

            _rings.Add(new RingState(
                ring.Centre,
                float.Lerp(ring.From, ring.To, SceneMotion.EaseOut(e)),
                RingFlatten,
                ring.Color,
                0.9f * (1f - e),
                2f * (1f - 0.6f * e)));
        }

        _sparks.Clear();
        var fadeStart = cleanUp;
        if (_sparkSeeds.Length > 0 && t >= fadeStart && t < fadeStart + HoleFade)
        {
            var e = (t - fadeStart) / HoleFade;
            var radius = float.Lerp(12f, 40f, SceneMotion.EaseOut(e));
            var centre = _hole.To;
            foreach (var seed in _sparkSeeds)
            {
                var angle = seed.R1 * MathF.Tau;
                var spark = seed;
                spark.Position = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                spark.Alpha = 0.85f * (1f - e);
                _sparks.Add(spark);
            }
        }

        if (IsFinished)
        {
            Hole = null;
        }
        else
        {
            var k = SceneMotion.Ease(SceneMotion.Phase(t, _hole.Start, _hole.Start + _hole.Length));
            var position = Vector2.Lerp(_hole.From, _hole.To, k);
            position.Y -= MathF.Sin(MathF.PI * k) * _hole.Arc;
            var alpha = t > cleanUp ? Math.Clamp(1f - (t - cleanUp) / HoleFade, 0f, 1f) : 1f;
            Hole = new HoleState(position, float.Lerp(_hole.FromRadius, _hole.ToRadius, k), alpha);
        }

        PageFadeIn = SceneMotion.Phase(t, FlightsEnd - PageFadeLead, FlightsEnd - PageFadeLead + PageFadeLength);
        _snapshot = new TransitionSnapshot(IsFinished, PageFadeIn, Time);
    }

    private sealed class Track(TransitionGhost ghost, GhostShape? shapeB, float delay, float length, GhostKeyframe[] keys, bool countsForEnd, Vector2? naturalA = null)
    {
        public TransitionGhost Ghost { get; } = ghost;

        public GhostShape? ShapeB { get; } = shapeB;

        public float Delay { get; } = delay;

        public float Length { get; } = length;

        public GhostKeyframe[] Keys { get; } = keys;

        public bool CountsForEnd { get; } = countsForEnd;

        public Vector2 NaturalA { get; } = naturalA ?? ghost.From.Size;
    }

    private sealed record RingPlan(Vector2 Centre, float From, float To, SceneColor Color, float Start);

    private sealed record HolePlan(Vector2 From, Vector2 To, float FromRadius, float ToRadius, float Start, float Length, float Arc);
}
