using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// The galaxy of step 1 without any drawing: orbits, bodies, particles, pointer influence, zoom camera and
/// the timing of the approaches (ADR-022). <see cref="Update"/> runs on the drawing thread; the UI thread
/// only calls <see cref="Enqueue"/>, reads <see cref="Snapshot"/> and asks the pure <see cref="OrbitAt"/>.
/// </summary>
/// <remarks>
/// All numbers come from the worksheet <c>docs/entwuerfe/schritt-1-galaxie.md</c>. Time is only ever the sum
/// of the <see cref="TimeSpan"/>s handed to <see cref="Update"/>; randomness only ever comes from the
/// injected <see cref="Random"/>, so two scenes with the same seed and the same commands stay identical.
/// </remarks>
public sealed partial class GalaxyScene
{
    /// <summary>A step longer than this is clamped, so nothing jumps after a pause.</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(0.1);

    /// <summary>How long the camera takes to reach a new target.</summary>
    public const float CameraSeconds = 1.15f;

    /// <summary>Angle between two consecutive files on their orbit, in radians.</summary>
    public const float AngleStep = 1.9f;

    /// <summary>Radius of the pointer hit test for an orbit, in DIP.</summary>
    public const float OrbitHitTolerance = 40f;

    /// <summary>Maximum outward bulge of an orbit under the pointer, in DIP.</summary>
    public const float BulgeHeight = 19f;

    /// <summary>The pointer has to stay inside this radius for the dwell timer to keep running, in DIP.</summary>
    public const float DwellTolerance = 6f;

    private const float TwoPi = MathF.PI * 2f;

    private readonly ConcurrentQueue<GalaxyCommand> _commands = new();
    private readonly List<GalaxyBody> _bodies = [];
    private readonly Dictionary<Guid, GalaxyBody> _byId = [];
    private readonly List<GalaxyParticle> _particles = [];
    private readonly Random _random;
    private readonly GalaxyBudget _budget;

    private readonly float[] _orbitPhase = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _orbitHover = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _orbitRadiusX = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _orbitFlatten = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _orbitAlpha = new float[GalaxyLayout.OrbitCount];

    private readonly float[] _cameraFromRadius = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _cameraFromFlatten = new float[GalaxyLayout.OrbitCount];
    private readonly float[] _cameraFromAlpha = new float[GalaxyLayout.OrbitCount];

    private volatile GalaxySnapshot _snapshot = GalaxySnapshot.Empty;

    private TimeSpan _time;
    private int _ordinal;
    private int _haloPerBody;

    private float _holeBase;
    private float _cameraFromHole;
    private float _cameraFromProgress;
    private float _cameraElapsed;
    private bool _cameraRunning;

    private Vector2? _pointer;
    private Vector2 _pointerSmooth;
    private float _pointerStrength;
    private bool _pointerSeen;

    private bool _dragOverTarget;
    private float _dragOver;
    private MediaKind? _trayHover;
    private int? _hoveredOrbit;
    private Vector2 _rejectedAnchor;
    private Vector2 _dwellAnchor;
    private TimeSpan _dwellDuration;

    private IReadOnlyList<PathAnchor> _leftAnchors = [];
    private IReadOnlyList<PathAnchor> _rightAnchors = [];
    private IReadOnlySet<string> _reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string? _selectedInput;
    private string? _recommended;
    private IReadOnlyList<GalaxyPath> _paths = [];

    public GalaxyScene(GalaxyLayout layout, ScenePalette palette, Random random, GalaxyBudget budget)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(budget);

        Layout = layout;
        Palette = palette;
        _random = random;
        _budget = budget;
        _haloPerBody = budget.HaloPerBody;

        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            _orbitPhase[k] = _random.NextSingle() * TwoPi;
            _orbitRadiusX[k] = layout.BaseRadius(k);
            _orbitFlatten[k] = GalaxyLayout.FlattenOverview;
            _orbitAlpha[k] = 1f;
        }

        _holeBase = GalaxyLayout.HoleRadiusOverview * layout.Scale;
        _pointerSmooth = layout.Center;
        _rejectedAnchor = new Vector2(layout.Width - 60f * layout.Scale, 60f * layout.Scale);
        BuildDust();
        PublishSnapshot();
    }

    /// <summary>The geometry of the current surface size; <see cref="Resize"/> replaces it.</summary>
    public GalaxyLayout Layout { get; private set; }

    /// <summary>The colours of the current theme. The view replaces the whole record on a theme change.</summary>
    public ScenePalette Palette { get; set; }

    /// <summary>The sum of all steps handed to <see cref="Update"/>.</summary>
    public TimeSpan Time => _time;

    public IReadOnlyList<GalaxyBody> Bodies => _bodies;

    /// <summary>Dust first (five orbits), then the halos of the bodies.</summary>
    public ReadOnlySpan<GalaxyParticle> Particles => CollectionsMarshal.AsSpan(_particles);

    public IReadOnlyList<float> OrbitPhase => _orbitPhase;

    public IReadOnlyList<float> OrbitHover => _orbitHover;

    public IReadOnlyList<float> OrbitRadiusX => _orbitRadiusX;

    public IReadOnlyList<float> OrbitFlatten => _orbitFlatten;

    public IReadOnlyList<float> OrbitAlpha => _orbitAlpha;

    /// <summary>The radius of the hole, already grown by a drag over the window.</summary>
    public float HoleRadius => _holeBase * (1f + 0.7f * _dragOver);

    /// <summary>0..1, eased: files are being dragged over the window.</summary>
    public float DragOver => _dragOver;

    /// <summary>The smoothed pointer and how strongly it pulls. Named PointerState because CA1720 rejects "Pointer".</summary>
    public PointerInfluence PointerState => new(_pointerSmooth, _pointerStrength, _pointer.HasValue);

    public ZoomState Zoom { get; private set; } = ZoomState.Overview;

    /// <summary>The kind the camera is driving to or resting on.</summary>
    public MediaKind? ZoomKind { get; private set; }

    /// <summary>0 in the overview, 1 when the zoom is reached; eased in between.</summary>
    public float ZoomProgress { get; private set; }

    /// <summary>The ways from the chosen input to every reachable output; only from <see cref="ZoomProgress"/> 0.6 on.</summary>
    public IReadOnlyList<GalaxyPath> Paths => _paths;

    /// <summary>Replaced as a whole at the end of every <see cref="Update"/>.</summary>
    public GalaxySnapshot Snapshot => _snapshot;

    /// <summary>The measuring point for the later gimmicks (whirl, big bang).</summary>
    public PointerDwell Dwell { get; private set; }

    /// <summary>Thread safe: the UI thread hands over work here and never touches the scene itself.</summary>
    public void Enqueue(GalaxyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);
    }

    /// <summary>Applies the queued commands, advances everything by <paramref name="elapsed"/> and publishes a snapshot.</summary>
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

        var dt = (float)elapsed.TotalSeconds;
        _time += elapsed;

        UpdatePointer(dt);
        UpdateHover(dt);
        UpdateDragOver(dt);
        UpdateCamera(dt);
        UpdateOrbits(dt);
        UpdateDwell(elapsed);
        UpdateGimmicks(dt);
        UpdateBodies();
        UpdateParticles();
        UpdatePaths();
        PublishSnapshot();
    }

    /// <summary>
    /// A point on an orbit, including the bulge the pointer pushes into it. Pure: it only reads the current
    /// state, so the UI thread may call it as well.
    /// </summary>
    public Vector2 OrbitPoint(int orbit, float angle, float radialOffset = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orbit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(orbit, GalaxyLayout.OrbitCount);

        var radius = _orbitRadiusX[orbit] + radialOffset + Bulge(orbit, angle);
        return Layout.Center + new Vector2(radius * MathF.Cos(angle), radius * _orbitFlatten[orbit] * MathF.Sin(angle));
    }

    /// <summary>The orbit closest to a point, or null if none is nearer than <paramref name="tolerance"/> DIP.</summary>
    public int? OrbitAt(Vector2 point, float tolerance = OrbitHitTolerance)
    {
        int? best = null;
        var bestDistance = tolerance * Layout.Scale;
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            if (_orbitAlpha[k] < 0.05f)
            {
                continue;
            }

            var angle = AngleOf(point, k);
            var radius = _orbitRadiusX[k];
            var onOrbit = Layout.Center + new Vector2(radius * MathF.Cos(angle), radius * _orbitFlatten[k] * MathF.Sin(angle));
            var distance = Vector2.Distance(point, onOrbit);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k;
            }
        }

        return best;
    }

    /// <summary>The size factor of a planet: clamp(0.6 + 0.45 * sqrt(MB / 20), 0.6, 1.8).</summary>
    public static float SizeFactorOf(long sizeBytes)
    {
        var megabytes = Math.Max(sizeBytes, 0L) / (1024f * 1024f);
        return Math.Clamp(0.6f + 0.45f * MathF.Sqrt(megabytes / 20f), 0.6f, 1.8f);
    }

    // ----- commands ---------------------------------------------------------------------------------

    private void ApplyCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command)
            {
                case AddBody add:
                    ApplyAdd(add);
                    break;
                case RemoveBody remove:
                    ApplyRemove(remove.Id);
                    break;
                case RejectBody reject:
                    ApplyReject(reject.Id);
                    break;
                case SetPointer pointer:
                    ApplyPointer(pointer.Position);
                    break;
                case SetDragOver drag:
                    _dragOverTarget = drag.Active;
                    break;
                case SetTrayHover tray:
                    _trayHover = tray.Kind;
                    break;
                case ZoomTo zoom:
                    ApplyZoom(zoom.Kind);
                    break;
                case SetPathAnchors anchors:
                    _leftAnchors = anchors.Left;
                    _rightAnchors = anchors.Right;
                    _selectedInput = anchors.SelectedInput;
                    _reachable = anchors.Reachable;
                    _recommended = anchors.Recommended;
                    break;
                case SetRejectedAnchor rejected:
                    _rejectedAnchor = rejected.Position;
                    break;
                case Resize resize:
                    ApplyResize(resize);
                    break;
                case Clear:
                    ApplyClear();
                    break;
                case SetGimmicksEnabled gimmicks:
                    _gimmicksEnabled = gimmicks.Enabled;
                    break;
            }
        }
    }

    private void ApplyAdd(AddBody add)
    {
        if (_byId.ContainsKey(add.Id))
        {
            return;
        }

        var waiting = 0;
        foreach (var other in _bodies)
        {
            if (other.Phase == BodyPhase.Waiting)
            {
                waiting++;
            }
        }

        var origin = Layout.Entry + new Vector2(0f, 18f * Layout.Scale);
        var body = new GalaxyBody(add.Id, add.Kind, add.FormatLabel, add.Name, SizeFactorOf(add.SizeBytes), _ordinal, origin)
        {
            PhaseStart = _time + GalaxyBody.ApproachDelay + GalaxyBody.ApproachStagger * waiting,
            IsRejected = add.Kind == MediaKind.Unknown,
        };

        _ordinal++;
        _bodies.Add(body);
        _byId[body.Id] = body;

        var desired = DesiredHaloPerBody();
        if (desired != _haloPerBody)
        {
            _haloPerBody = desired;
            RebuildHalos();
        }
        else
        {
            AddHalo(_bodies.Count - 1, desired);
        }
    }

    private void ApplyRemove(Guid id)
    {
        if (!_byId.Remove(id, out var body))
        {
            return;
        }

        body.Phase = BodyPhase.Removed;
        _bodies.Remove(body);
        _haloPerBody = DesiredHaloPerBody();
        RebuildHalos();
    }

    private void ApplyReject(Guid id)
    {
        if (!_byId.TryGetValue(id, out var body) || body.IsRejected)
        {
            return;
        }

        body.IsRejected = true;
        body.Origin = body.Position;
        body.PhaseStart = _time;
        body.ArrivedAt = TimeSpan.Zero;
        body.ClearTrail();
    }

    private void ApplyPointer(Vector2? position)
    {
        if (position is { } p)
        {
            if (!_pointer.HasValue)
            {
                _pointerSmooth = p;
                _dwellAnchor = p;
                _dwellDuration = TimeSpan.Zero;
            }

            _pointer = p;
            _pointerSeen = true;
        }
        else
        {
            _pointer = null;
        }
    }

    private void ApplyZoom(MediaKind? kind)
    {
        if (kind == ZoomKind)
        {
            return;
        }

        // The ride always starts from the values of this moment, so a change of mind stays continuous.
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            _cameraFromRadius[k] = _orbitRadiusX[k];
            _cameraFromFlatten[k] = _orbitFlatten[k];
            _cameraFromAlpha[k] = _orbitAlpha[k];
        }

        _cameraFromHole = _holeBase;
        _cameraFromProgress = ZoomProgress;
        _cameraElapsed = 0f;
        _cameraRunning = true;
        ZoomKind = kind;
        Zoom = kind is null ? ZoomState.ZoomingOut : ZoomState.ZoomingIn;
    }

    private void ApplyResize(Resize resize)
    {
        var old = Layout;
        var next = new GalaxyLayout(Math.Max(resize.Width, 1f), Math.Max(resize.Height, 1f));
        if (Math.Abs(next.Width - old.Width) < 0.5f && Math.Abs(next.Height - old.Height) < 0.5f)
        {
            return;
        }

        var ratio = next.Scale / old.Scale;
        Layout = next;

        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            _cameraFromRadius[k] *= ratio;
            if (!_cameraRunning)
            {
                _orbitRadiusX[k] = TargetRadius(k);
                _orbitFlatten[k] = TargetFlatten(k);
                _orbitAlpha[k] = TargetAlpha(k);
            }
        }

        _cameraFromHole *= ratio;
        if (!_cameraRunning)
        {
            _holeBase = TargetHole();
        }
    }

    private void ApplyClear()
    {
        foreach (var body in _bodies)
        {
            body.Phase = BodyPhase.Removed;
        }

        _bodies.Clear();
        _byId.Clear();
        _ordinal = 0;
        _haloPerBody = _budget.HaloPerBody;
        RebuildHalos();
    }

    // ----- per frame --------------------------------------------------------------------------------

    private void UpdatePointer(float dt)
    {
        if (_pointer is { } p)
        {
            _pointerSmooth = GalaxyMotion.Approach(_pointerSmooth, p, 14f, dt);
            _pointerStrength = GalaxyMotion.Approach(_pointerStrength, 1f, 5f, dt);
        }
        else
        {
            _pointerStrength = GalaxyMotion.Approach(_pointerStrength, 0f, 3.5f, dt);
        }
    }

    private void UpdateHover(float dt)
    {
        int? hovered = null;
        if (_trayHover is { } kind && kind != MediaKind.Unknown)
        {
            hovered = GalaxyLayout.OrbitOf(kind);
        }
        else if (_pointer is { } p)
        {
            hovered = OrbitAt(p);
        }

        _hoveredOrbit = hovered;
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            var target = hovered == k ? 1f : 0f;
            _orbitHover[k] = GalaxyMotion.Approach(_orbitHover[k], target, 6f, dt);
        }
    }

    private void UpdateDragOver(float dt) =>
        _dragOver = GalaxyMotion.Approach(_dragOver, _dragOverTarget ? 1f : 0f, 8f, dt);

    private void UpdateCamera(float dt)
    {
        if (!_cameraRunning)
        {
            return;
        }

        _cameraElapsed += dt;
        var t = Math.Min(_cameraElapsed / CameraSeconds, 1f);
        var e = GalaxyMotion.Ease(t);

        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            _orbitRadiusX[k] = float.Lerp(_cameraFromRadius[k], TargetRadius(k), e);
            _orbitFlatten[k] = float.Lerp(_cameraFromFlatten[k], TargetFlatten(k), e);
            _orbitAlpha[k] = float.Lerp(_cameraFromAlpha[k], TargetAlpha(k), e);
        }

        _holeBase = float.Lerp(_cameraFromHole, TargetHole(), e);
        ZoomProgress = float.Lerp(_cameraFromProgress, ZoomKind is null ? 0f : 1f, e);

        if (t >= 1f)
        {
            _cameraRunning = false;
            Zoom = ZoomKind is null ? ZoomState.Overview : ZoomState.Zoomed;
        }
    }

    private void UpdateOrbits(float dt)
    {
        var zoomOrbit = ZoomKind is { } kind ? GalaxyLayout.OrbitOf(kind) : -1;
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            var omega = 0.2f - 0.026f * k;
            var zoomed = k == zoomOrbit ? ZoomProgress : 0f;
            _orbitPhase[k] += omega * (1f + 0.5f * zoomed + 0.6f * _orbitHover[k]) * dt;
            if (_orbitPhase[k] >= TwoPi)
            {
                _orbitPhase[k] -= TwoPi * MathF.Floor(_orbitPhase[k] / TwoPi);
            }
        }
    }

    private void UpdateBodies()
    {
        foreach (var body in _bodies)
        {
            var orbit = body.Orbit;
            Vector2 target;
            if (orbit < 0)
            {
                body.Angle = 0f;
                target = _rejectedAnchor;
            }
            else
            {
                body.Angle = body.Ordinal * AngleStep + _orbitPhase[orbit];
                target = OrbitPoint(orbit, body.Angle);
            }

            if (_time <= body.PhaseStart)
            {
                body.Phase = BodyPhase.Waiting;
                body.Position = body.Origin;
                continue;
            }

            var t = (float)((_time - body.PhaseStart).TotalSeconds / GalaxyBody.ApproachDuration.TotalSeconds);
            if (t < 1f)
            {
                body.Phase = BodyPhase.Approaching;
                var control = GalaxyMotion.ApproachControl(body.Origin, target, Layout.Center.X, Layout.Scale);
                body.Position = GalaxyMotion.Approach(body.Origin, control, target, GalaxyMotion.Ease(t));
                body.PushTrail(body.Position);
                continue;
            }

            if (body.Phase is BodyPhase.Waiting or BodyPhase.Approaching)
            {
                body.ArrivedAt = body.PhaseStart + GalaxyBody.ApproachDuration;
                body.ClearTrail();
            }

            body.Phase = orbit < 0 ? BodyPhase.Rejected : BodyPhase.Orbiting;
            body.Position = target;
        }
    }

    private void UpdateParticles()
    {
        var span = CollectionsMarshal.AsSpan(_particles);
        var seconds = (float)_time.TotalSeconds;
        var scale = Layout.Scale;

        // Only when the hole charges or the universe is reborn are the dust and halos displaced at all.
        var bang = _bang != BangPhase.Idle;
        var suction = bang || _holeCharge > 0f;

        for (var i = 0; i < span.Length; i++)
        {
            ref var particle = ref span[i];
            var orbit = particle.Orbit;
            var particleScale = ParticleScale(orbit);

            if (particle.PlanetIndex >= 0)
            {
                UpdatePlanetParticle(ref particle, particleScale, seconds, scale, bang);
                continue;
            }

            if (particle.BodyIndex < 0)
            {
                var angle = _orbitPhase[orbit] + particle.R1 * TwoPi;
                var radial = (particle.R2 - 0.5f) * 14f * scale;
                var dust = WithForces(OrbitPoint(orbit, angle, radial), orbit, particle.R2);
                var dustDepth = WhirlDisplace(ref dust, particle.R2);
                var dustAlpha = suction ? BangDisplace(ref dust, particle.R1, particle.R2, particle.R3) : 1f;
                particle.Position = dust;
                particle.Size = 1.6f * particleScale * dustDepth;
                particle.Alpha = 0.55f * _orbitAlpha[orbit] * dustAlpha;
                particle.Color = Palette.For(GalaxyLayout.KindOf(orbit));
                continue;
            }

            var body = _bodies[particle.BodyIndex];
            var haloAngle = particle.R1 * TwoPi + 2f * seconds;
            var haloRadius = (3f + 7f * particle.R2) * particleScale * body.SizeFactor;
            var halo = body.Position + new Vector2(
                haloRadius * MathF.Cos(haloAngle),
                haloRadius * 0.6f * MathF.Sin(haloAngle));

            if ((body.Phase is BodyPhase.Orbiting or BodyPhase.Rejected) && body.ArrivedAt > TimeSpan.Zero)
            {
                var since = (float)(_time - body.ArrivedAt).TotalSeconds;
                if (since < 0.8f)
                {
                    var scatterAngle = particle.R3 * TwoPi;
                    var scatter = body.Position + new Vector2(
                        MathF.Cos(scatterAngle), MathF.Sin(scatterAngle)) * (30f * scale * particle.R2);
                    halo = Vector2.Lerp(scatter, halo, GalaxyMotion.Ease(since / 0.8f));
                }
            }

            var hoverOrbit = body.Orbit < 0 ? 0 : body.Orbit;
            halo = WithForces(halo, hoverOrbit, particle.R2);
            var haloDepth = WhirlDisplace(ref halo, particle.R2);
            var haloAlpha = suction ? BangDisplace(ref halo, particle.R1, particle.R2, particle.R3) : 1f;
            particle.Position = halo;
            particle.Size = 2.4f * particleScale * haloDepth;
            particle.Alpha = 0.95f * (body.Orbit < 0 ? 1f : _orbitAlpha[body.Orbit]) * haloAlpha;
            particle.Color = body.IsRejected ? Palette.Error : Palette.For(body.Kind);
        }
    }

    /// <summary>The halo of a self-formed planet: a flat ring of 44 particles that scatters in at the birth.</summary>
    private void UpdatePlanetParticle(ref GalaxyParticle particle, float particleScale, float seconds, float scale, bool bang)
    {
        var planet = _planets[particle.PlanetIndex];
        var angle = particle.R1 * TwoPi + seconds * (1.2f + particle.R3);
        var radius = planet.Radius * particleScale * (1.4f + particle.R2 * 1.4f) * planet.Shrink;
        var position = planet.Position + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.35f);

        var since = seconds - (float)planet.BornAt.TotalSeconds;
        var t = Math.Clamp(since / 0.8f, 0f, 1f);
        if (t < 1f)
        {
            var w = particle.R1 * TwoPi * 3f;
            var far = (1f - GalaxyMotion.EaseOut(t)) * (30f + particle.R2 * 60f) * scale;
            position += new Vector2(MathF.Cos(w) * far, MathF.Sin(w) * far * 0.45f);
        }

        var settle = float.Lerp(1f, 0.3f, Math.Clamp((since - 0.8f) / 1.5f, 0f, 1f));
        var alpha = bang ? BangDisplace(ref position, particle.R1, particle.R2, particle.R3) : 1f;
        particle.Position = position;
        particle.Size = 1.3f * particleScale;
        particle.Alpha = 0.95f * _orbitAlpha[planet.Orbit] * settle * alpha;
        particle.Color = planet.Primary;
    }

    private void UpdateDwell(TimeSpan elapsed)
    {
        if (_pointer is not { } p)
        {
            _dwellReset = _dwellDuration > TimeSpan.Zero;
            _dwellDuration = TimeSpan.Zero;
            Dwell = new PointerDwell(null, false, TimeSpan.Zero);
            return;
        }

        var orbit = OrbitAt(p);
        var onHole = Vector2.Distance(p, Layout.Center) < HoleRadius + 12f * Layout.Scale;
        var moved = Vector2.Distance(p, _dwellAnchor) >= DwellTolerance * Layout.Scale;

        _dwellReset = moved || orbit != Dwell.Orbit || onHole != Dwell.OnHole;
        if (_dwellReset)
        {
            _dwellAnchor = p;
            _dwellDuration = TimeSpan.Zero;
        }
        else
        {
            _dwellDuration += elapsed;
        }

        Dwell = new PointerDwell(orbit, onHole, _dwellDuration);
    }

    private void UpdatePaths()
    {
        if (ZoomProgress <= 0.6f || _selectedInput is null || _rightAnchors.Count == 0)
        {
            _paths = [];
            return;
        }

        PathAnchor? from = null;
        foreach (var anchor in _leftAnchors)
        {
            if (string.Equals(anchor.FormatId, _selectedInput, StringComparison.OrdinalIgnoreCase))
            {
                from = anchor;
                break;
            }
        }

        if (from is null)
        {
            _paths = [];
            return;
        }

        var hole = Layout.Center;
        var offset = 70f * Layout.Scale;
        var paths = new List<GalaxyPath>(_rightAnchors.Count);
        foreach (var anchor in _rightAnchors)
        {
            if (!_reachable.Contains(anchor.FormatId))
            {
                continue;
            }

            paths.Add(new GalaxyPath(
                anchor.FormatId,
                from.Position,
                new Vector2(hole.X - offset, from.Position.Y),
                hole,
                new Vector2(hole.X + offset, anchor.Position.Y),
                anchor.Position,
                string.Equals(anchor.FormatId, _recommended, StringComparison.OrdinalIgnoreCase)));
        }

        _paths = paths;
    }

    private void PublishSnapshot()
    {
        var positions = new Dictionary<Guid, Vector2>(_bodies.Count);
        foreach (var body in _bodies)
        {
            positions[body.Id] = body.Position;
        }

        _snapshot = new GalaxySnapshot(
            Zoom,
            ZoomKind,
            ZoomProgress,
            _hoveredOrbit,
            new ReadOnlyDictionary<Guid, Vector2>(positions),
            _time,
            Gimmick,
            SurfaceSuction,
            SurfaceOpacity,
            SurfaceJitter,
            Layout.Center);
    }

    // ----- helpers ----------------------------------------------------------------------------------

    private Vector2 WithForces(Vector2 position, int orbit, float z)
    {
        if (_dragOver > 0.001f)
        {
            var pull = 0.10f * (1f - z / 2f) * _dragOver;
            position = Vector2.Lerp(position, Layout.Center, pull);
        }

        return position + GalaxyMotion.PointerPull(position, _pointerSmooth, _orbitHover[orbit], _pointerStrength, Layout.Scale);
    }

    private float ParticleScale(int orbit) =>
        MathF.Sqrt(Math.Clamp(_orbitRadiusX[orbit] / (GalaxyLayout.BaseRadiusInner * Layout.Scale), 0.5f, 2.6f));

    private float Bulge(int orbit, float angle)
    {
        var hover = _orbitHover[orbit];
        if (!_pointerSeen || hover <= 0.001f)
        {
            return 0f;
        }

        var delta = GalaxyMotion.WrapAngle(angle - AngleOf(_pointerSmooth, orbit));
        return BulgeHeight * Layout.Scale * hover * MathF.Exp(-(delta * delta) / 0.34f);
    }

    private float AngleOf(Vector2 point, int orbit)
    {
        var flatten = Math.Max(_orbitFlatten[orbit], 0.01f);
        return MathF.Atan2((point.Y - Layout.Center.Y) / flatten, point.X - Layout.Center.X);
    }

    private float TargetRadius(int orbit)
    {
        if (ZoomKind is not { } kind)
        {
            return Layout.BaseRadius(orbit);
        }

        var zoomOrbit = GalaxyLayout.OrbitOf(kind);
        if (orbit == zoomOrbit)
        {
            return Layout.ZoomRadius;
        }

        return Layout.BaseRadius(orbit) * (orbit < zoomOrbit ? 0.1f : 3.1f);
    }

    private float TargetFlatten(int orbit) =>
        ZoomKind is { } kind && GalaxyLayout.OrbitOf(kind) == orbit
            ? GalaxyLayout.FlattenZoomed
            : GalaxyLayout.FlattenOverview;

    private float TargetAlpha(int orbit) =>
        ZoomKind is not { } kind || GalaxyLayout.OrbitOf(kind) == orbit ? 1f : 0f;

    private float TargetHole() =>
        (ZoomKind is null ? GalaxyLayout.HoleRadiusOverview : GalaxyLayout.HoleRadiusZoomed) * Layout.Scale;

    private int DesiredHaloPerBody() =>
        _bodies.Count > _budget.ManyFilesFrom ? _budget.HaloPerBodyManyFiles : _budget.HaloPerBody;

    private void BuildDust()
    {
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            for (var i = 0; i < _budget.DustPerOrbit; i++)
            {
                if (_particles.Count >= _budget.MaxParticles)
                {
                    return;
                }

                _particles.Add(new GalaxyParticle
                {
                    Orbit = k,
                    BodyIndex = -1,
                    PlanetIndex = -1,
                    R1 = _random.NextSingle(),
                    R2 = _random.NextSingle(),
                    R3 = _random.NextSingle(),
                    Color = Palette.For(GalaxyLayout.KindOf(k)),
                    Alpha = 0.55f,
                });
            }
        }
    }

    private void RebuildHalos()
    {
        _particles.RemoveAll(p => p.BodyIndex >= 0);
        for (var i = 0; i < _bodies.Count; i++)
        {
            AddHalo(i, _haloPerBody);
        }
    }

    private void AddHalo(int bodyIndex, int count)
    {
        var body = _bodies[bodyIndex];
        if (count <= 0 || _particles.Count + count > _budget.MaxParticles)
        {
            body.HasHalo = false;
            return;
        }

        body.HasHalo = true;
        var orbit = Math.Max(body.Orbit, 0);
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new GalaxyParticle
            {
                Orbit = orbit,
                BodyIndex = bodyIndex,
                PlanetIndex = -1,
                R1 = _random.NextSingle(),
                R2 = _random.NextSingle(),
                R3 = _random.NextSingle(),
                Color = Palette.For(body.Kind),
                Alpha = 0.95f,
            });
        }
    }
}
