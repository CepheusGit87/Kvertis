using System.Collections.Concurrent;
using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// One planet on the orbit of step 2: a file of the chosen kind. Position is rebuilt every frame.
/// </summary>
public sealed class TargetPlanetState
{
    internal TargetPlanetState(string fileId, long sizeBytes, int index)
    {
        FileId = fileId;
        SizeBytes = sizeBytes;
        Index = index;
    }

    public string FileId { get; }

    public long SizeBytes { get; }

    /// <summary>Order on the orbit; the planets are spread 1.35 rad apart (draft).</summary>
    public int Index { get; }

    public Vector2 Position { get; internal set; }

    /// <summary>Draft: 5 + sqrt(MB) * 0.6, scaled with the layout.</summary>
    public float Radius { get; internal set; }
}

/// <summary>
/// One way of step 2: from a file card on the left through the hole to the chosen target on the right. Two
/// cubic Bézier halves like the draft's <c>weg()</c>; both ends follow their anchors smoothly, so a change
/// of the target bends the way instead of snapping it.
/// </summary>
public sealed class TargetPath
{
    /// <summary>Distance of the control points from the ends (draft: 60).</summary>
    public const float ControlOffset = 60f;

    internal TargetPath(string fileId, Vector2 from, Vector2 to)
    {
        FileId = fileId;
        From = from;
        To = to;
        TargetFrom = from;
        TargetTo = to;
    }

    public string FileId { get; }

    /// <summary>Where the way starts now (the right edge of the file card).</summary>
    public Vector2 From { get; internal set; }

    /// <summary>Where the way ends now; moves towards <see cref="TargetTo"/> after a change.</summary>
    public Vector2 To { get; internal set; }

    internal Vector2 TargetFrom { get; set; }

    internal Vector2 TargetTo { get; set; }

    public string? TargetId { get; internal set; }

    /// <summary>True when the way ends on the recommended format: it is drawn in mint.</summary>
    public bool IsRecommended { get; internal set; }

    /// <summary>True when the file picked its own target ("Jede einzeln"): amber and dashed like the draft.</summary>
    public bool IsOwnChoice { get; internal set; }

    /// <summary>Whether the target list currently has a row for this way; without one only the first half is drawn.</summary>
    public bool HasTarget { get; internal set; }

    /// <summary>The point on the way at 0..1; the first half runs into the hole, the second out of it.</summary>
    public Vector2 PointAt(Vector2 hole, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f
            ? Cubic(From, new Vector2(From.X + ControlOffset, From.Y), new Vector2(hole.X - ControlOffset, hole.Y), hole, t * 2f)
            : Cubic(hole, new Vector2(hole.X + ControlOffset, hole.Y), new Vector2(To.X - ControlOffset, To.Y), To, (t - 0.5f) * 2f);
    }

    /// <summary>Cubic Bézier, the draft's <c>bez()</c>.</summary>
    public static Vector2 Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = 1f - t;
        return (u * u * u * a) + (3f * u * u * t * b) + (3f * u * t * t * c) + (t * t * t * d);
    }
}

/// <summary>
/// The middle of step 2 "Ziel" (ADR-018, ADR-022): one orbit in the colour of the chosen kind with a planet
/// per file, the hole in the middle and one way per file through the hole to its target. Free of WinUI and
/// Win2D; the time is only the sum of the <see cref="Update"/> steps. The UI thread only calls
/// <see cref="Enqueue"/> and sets <see cref="Palette"/>; everything else is read on the drawing thread.
/// </summary>
/// <remarks>
/// Nothing runs forever except the slow turn of the planets and the dust (0.12 rad/s, draft). The travelling
/// dots on the ways only show for <see cref="ActivitySeconds"/> after a change (a new target, a new kind),
/// then the ways stand still.
/// </remarks>
public sealed class TargetPathsScene
{
    /// <summary>Largest step one frame may advance; a pause must not make the planets jump.</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(0.1);

    /// <summary>The orbit opens over 0.9 s (draft: ease(clamp((T - tZoom) / .9))).</summary>
    public const float RevealSeconds = 0.9f;

    /// <summary>How long the dots run along the ways after a change.</summary>
    public const float ActivitySeconds = 2.5f;

    /// <summary>Angular speed of planets and dust in rad/s (draft: T * .12).</summary>
    public const float OrbitSpeed = 0.12f;

    /// <summary>Rate (1/s) with which the ends of a way follow their anchors.</summary>
    public const float BendRate = 9f;

    /// <summary>Number of dust squares on the orbit (draft: 110).</summary>
    public const int DustCount = 110;

    private static readonly ScenePalette DefaultPalette = new(
        Image: new SceneColor(0x6B, 0xA7, 0xFF),
        Audio: new SceneColor(0xB2, 0x8C, 0xFF),
        Video: new SceneColor(0xF2, 0xB4, 0x5A),
        Document: new SceneColor(0x4F, 0xC3, 0xE8),
        Model3D: new SceneColor(0xF0, 0x8F, 0xD0),
        Mint: new SceneColor(0x6F, 0xE0, 0xBF),
        Error: new SceneColor(0xF0, 0x7A, 0x6A),
        Ink: new SceneColor(0xE7, 0xEC, 0xEE),
        Background: new SceneColor(0x0C, 0x0F, 0x11),
        Muted: new SceneColor(0x8B, 0x95, 0x9C),
        IsDark: true);

    private readonly ConcurrentQueue<TargetPathsCommand> _commands = new();
    private readonly List<TargetPlanetState> _planets = [];
    private readonly List<TargetPath> _paths = [];
    private readonly List<Vector2> _idleTargets = [];
    private TimeSpan _time;
    private TimeSpan _revealStart;
    private bool _revealed;
    private float _activity;
    private float _revealRadius;

    public TargetPathsScene(TargetPathsLayout layout, ScenePalette? palette = null)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Palette = palette ?? DefaultPalette;
        _revealRadius = 0f;
    }

    public TargetPathsLayout Layout { get; private set; }

    /// <summary>The colours of the current theme. The view replaces the whole record on a theme change.</summary>
    public ScenePalette Palette { get; set; }

    /// <summary>The kind whose orbit is shown; null draws only the hole.</summary>
    public MediaKind? Kind { get; private set; }

    /// <summary>The colour of the orbit, the dust and the first half of every way.</summary>
    public SceneColor KindColor => Kind is { } kind ? Palette.For(kind) : Palette.LineStrong;

    public TimeSpan Time => _time;

    /// <summary>0..1 eased: how far the orbit has opened since the last <see cref="ShowKind"/>.</summary>
    public float Reveal { get; private set; }

    /// <summary>1 right after a change, 0 after <see cref="ActivitySeconds"/>: drives the dots on the ways.</summary>
    public float Activity => _activity;

    /// <summary>Current phase of the slow turn, in radians.</summary>
    public float OrbitPhase => (float)_time.TotalSeconds * OrbitSpeed;

    /// <summary>Horizontal radius of the orbit now (0.25 to 1 of the layout radius while opening).</summary>
    public float OrbitRadiusX => _revealRadius;

    public float OrbitRadiusY => _revealRadius * TargetPathsLayout.Flatten;

    public Vector2 Hole => Layout.Hole;

    public float HoleRadius => TargetPathsLayout.HoleRadius;

    public IReadOnlyList<TargetPlanetState> Planets => _planets;

    /// <summary>One way per file with an anchor, in the order of the file list.</summary>
    public IReadOnlyList<TargetPath> Paths => _paths;

    /// <summary>The rows of the target list nobody goes to: a faint line from the hole to each (draft).</summary>
    public IReadOnlyList<Vector2> IdleTargets => _idleTargets;

    public string? RecommendedId { get; private set; }

    /// <summary>Thread-safe; the command is applied at the start of the next <see cref="Update"/>.</summary>
    public void Enqueue(TargetPathsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);
    }

    /// <summary>The point on the orbit at <paramref name="angle"/> with a radial <paramref name="offset"/>.</summary>
    public Vector2 OrbitPoint(float angle, float offset = 0f)
    {
        var rx = _revealRadius + offset;
        var ry = _revealRadius * TargetPathsLayout.Flatten + offset * 0.5f;
        return Hole + new Vector2(MathF.Cos(angle) * rx, MathF.Sin(angle) * ry);
    }

    /// <summary>Dust square <paramref name="index"/> of <see cref="DustCount"/>: the draft's fixed scatter ((i * 37) % 11 - 5) * 1.4.</summary>
    public Vector2 DustPoint(int index)
    {
        var angle = index / (float)DustCount * MathF.Tau + OrbitPhase;
        var offset = ((index * 37) % 11 - 5) * 1.4f;
        return OrbitPoint(angle, offset);
    }

    /// <summary>The angle of planet <paramref name="index"/> now (draft: π * .72 + j * 1.35 + T * .12).</summary>
    public float PlanetAngle(int index) => MathF.PI * 0.72f + index * 1.35f + OrbitPhase;

    /// <summary>Advances the scene by <paramref name="elapsed"/> (clamped to <see cref="MaxStep"/>) after applying the queued commands.</summary>
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

        ApplyCommands();
        _time += elapsed;
        var dt = (float)elapsed.TotalSeconds;

        Reveal = _revealed ? GalaxyMotion.Ease((float)(_time - _revealStart).TotalSeconds / RevealSeconds) : 0f;
        _revealRadius = Layout.OrbitRadiusX * float.Lerp(0.25f, 1f, Reveal);
        _activity = Math.Max(0f, _activity - dt / ActivitySeconds);

        foreach (var planet in _planets)
        {
            planet.Position = OrbitPoint(PlanetAngle(planet.Index));
            planet.Radius = (5f + MathF.Sqrt(planet.SizeBytes / 1_048_576f) * 0.6f) * Layout.Scale;
        }

        foreach (var path in _paths)
        {
            path.From = GalaxyMotion.Approach(path.From, path.TargetFrom, BendRate, dt);
            path.To = GalaxyMotion.Approach(path.To, path.TargetTo, BendRate, dt);
        }
    }

    private void ApplyCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command)
            {
                case ResizeTargetPaths resize:
                    ApplyResize(resize);
                    break;
                case ShowKind show:
                    ApplyShow(show);
                    break;
                case SetTargetAnchors anchors:
                    ApplyAnchors(anchors);
                    break;
            }
        }
    }

    private void ApplyResize(ResizeTargetPaths resize)
    {
        if (resize.Width <= 0f || resize.Height <= 0f)
        {
            return;
        }

        var old = Layout;
        Layout = new TargetPathsLayout(resize.Width, resize.Height);
        // The ways hang on anchors the page reports again after its layout; only the hole moves at once.
        var shift = Layout.Hole - old.Hole;
        foreach (var path in _paths)
        {
            path.From += shift;
            path.To += shift;
            path.TargetFrom += shift;
            path.TargetTo += shift;
        }
    }

    private void ApplyShow(ShowKind show)
    {
        var sameKind = show.Kind == Kind && _revealed;
        Kind = show.Kind;
        _planets.Clear();
        for (var i = 0; i < show.Planets.Count; i++)
        {
            _planets.Add(new TargetPlanetState(show.Planets[i].FileId, show.Planets[i].SizeBytes, i));
        }

        if (!sameKind)
        {
            _revealStart = _time;
            _revealed = show.Kind is not null;
            _activity = 1f;
            // Another kind: its files are others; the old ways would end on cards that no longer exist.
            _paths.Clear();
        }
    }

    private void ApplyAnchors(SetTargetAnchors anchors)
    {
        RecommendedId = anchors.RecommendedId;
        var formats = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        foreach (var format in anchors.Formats)
        {
            formats[format.FormatId] = format.Position;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var kept = new List<TargetPath>(anchors.Files.Count);
        foreach (var file in anchors.Files)
        {
            var path = _paths.Find(p => string.Equals(p.FileId, file.FileId, StringComparison.Ordinal));
            var to = Hole;
            var hasTarget = file.TargetId is { } id && formats.TryGetValue(id, out to);
            if (!hasTarget)
            {
                to = Hole;
            }

            if (path is null)
            {
                path = new TargetPath(file.FileId, file.Position, to);
                changed = true;
            }
            else
            {
                if (!string.Equals(path.TargetId, file.TargetId, StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                }

                path.TargetFrom = file.Position;
                path.TargetTo = to;
            }

            path.TargetId = file.TargetId;
            path.HasTarget = hasTarget;
            path.IsOwnChoice = file.IsOwnChoice;
            path.IsRecommended = file.TargetId is { } t && string.Equals(t, anchors.RecommendedId, StringComparison.OrdinalIgnoreCase);
            if (file.TargetId is { } usedId)
            {
                used.Add(usedId);
            }

            kept.Add(path);
        }

        if (kept.Count != _paths.Count)
        {
            changed = true;
        }

        _paths.Clear();
        _paths.AddRange(kept);

        _idleTargets.Clear();
        foreach (var format in anchors.Formats)
        {
            if (!used.Contains(format.FormatId))
            {
                _idleTargets.Add(format.Position);
            }
        }

        if (changed)
        {
            _activity = 1f;
        }
    }
}
