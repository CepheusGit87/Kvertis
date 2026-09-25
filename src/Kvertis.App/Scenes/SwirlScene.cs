using System.Collections.Concurrent;
using System.Numerics;

namespace Kvertis.App.Scenes;

/// <summary>Where a file of the round is on the swirl surface.</summary>
public enum SwirlFileState
{
    /// <summary>On the inbox stack, not started.</summary>
    Waiting,

    /// <summary>Running without a swirl slot: stays on the stack, lands on the white hole without a pixel stream.</summary>
    RunningOffSwirl,

    /// <summary>Its pixels fly in and circle the black hole.</summary>
    InSwirl,

    /// <summary>Finished; its pixels fly to their target.</summary>
    Leaving,

    /// <summary>Arrived on the white hole or in the bag.</summary>
    Landed,

    /// <summary>Failed or cancelled: lies on the stack again.</summary>
    BackOnStack,
}

/// <summary>One pixel of a file in this frame. <see cref="Additive"/> asks for additive blending (dark theme, barely tinted).</summary>
public struct SwirlPixel
{
    public Vector2 Position { get; set; }

    /// <summary>Position of the previous frame; the renderer draws a trail when 6 &lt; d² &lt; 4000.</summary>
    public Vector2 Previous { get; set; }

    public bool HasPrevious { get; set; }

    public SceneColor Color { get; set; }

    public float Alpha { get; set; }

    public bool Additive { get; set; }
}

/// <summary>A file of the round. Mutable and only touched on the drawing thread.</summary>
public sealed class SwirlFile
{
    internal SwirlFile(SwirlFileSpec spec, int index)
    {
        Spec = spec;
        Index = index;
    }

    public SwirlFileSpec Spec { get; }

    public Guid Id => Spec.Id;

    /// <summary>Position in the plan; fixes the place on the inbox stack.</summary>
    public int Index { get; }

    public SwirlFileState State { get; internal set; }

    /// <summary>The moment the file took a swirl slot (<c>tIn</c>).</summary>
    public TimeSpan? EnteredAt { get; internal set; }

    /// <summary>The moment the file ended (<c>tOut</c>).</summary>
    public TimeSpan? LeftAt { get; internal set; }

    /// <summary>Job progress 0..1.</summary>
    public float Progress { get; internal set; }

    public FileOutcome? Outcome { get; internal set; }

    public PixelTarget? Target { get; internal set; }

    /// <summary>Place on the white hole, -1 without one.</summary>
    public int Slot { get; internal set; } = -1;

    /// <summary>The raster of the drawn sheet (only while the file has pixels).</summary>
    public SheetCell[] Cells { get; internal set; } = [];

    /// <summary>One entry per cell in <see cref="Cells"/>; empty while the file has no pixel stream.</summary>
    public SwirlPixel[] Pixels { get; internal set; } = [];

    /// <summary><c>k</c>: mix from the old to the new sheet colours (0.7·progress while circling, ease(aus) while leaving).</summary>
    public float ColourMix { get; internal set; }

    /// <summary><c>f</c>: how strongly the pixels take on the kind colour.</summary>
    public float TintShare { get; internal set; }

    /// <summary><c>ein</c> = clamp((t - tIn)/0.95).</summary>
    public float EnterShare { get; internal set; }

    /// <summary><c>aus</c> = clamp((t - tOut)/0.95).</summary>
    public float LeaveShare { get; internal set; }

    /// <summary>True while the pixels are drawn.</summary>
    public bool HasPixels => State is SwirlFileState.InSwirl or SwirlFileState.Leaving && Pixels.Length > 0;
}

/// <summary>A drawn sheet lying on the inbox stack.</summary>
public readonly record struct SwirlStackSheet(SwirlFileSpec Spec, SheetPlacement Placement);

/// <summary>
/// What the UI thread reads. Built fresh at the end of every <see cref="SwirlScene.Update"/>; only values,
/// no reference to a mutable object.
/// </summary>
public sealed record SwirlSnapshot(
    TimeSpan Time,
    float MeanProgress,
    int FilesInSwirl,
    int Landed,
    float? FinaleTime,
    FinalePhase? FinalePhase,
    bool ShowReport,
    bool ShakeRequested,
    int ShakeCount,
    float CounterAlpha)
{
    public static readonly SwirlSnapshot Empty = new(TimeSpan.Zero, 0f, 0, 0, null, null, false, false, 0, 1f);
}

/// <summary>
/// The pixel swirl of step 3 with the white hole and the finale (worksheet "Übergänge", ADR-023). Two files at
/// most circle at a time; their pixels come from a computed sheet raster and take on the kind colour; progress
/// is fed from outside. <see cref="Update"/> runs on the drawing thread; the UI thread only calls
/// <see cref="Enqueue"/> and reads <see cref="Snapshot"/>.
/// </summary>
public sealed class SwirlScene
{
    /// <summary>A step longer than this is clamped.</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(0.1);

    /// <summary><c>R</c>: outer radius of the swirl.</summary>
    public const float Radius = 100f;

    /// <summary><c>fl</c>: vertical squash of the swirl.</summary>
    public const float Flatten = 0.42f;

    /// <summary>Length of the fly-in and fly-out, in seconds.</summary>
    public const float FlightSeconds = 0.95f;

    /// <summary><see cref="FlightSeconds"/> exact in ticks; all timing comparisons use it.</summary>
    public static readonly TimeSpan Flight = TimeSpan.FromSeconds(0.95d);

    /// <summary>Radius of the black hole in the middle (same as in step 2 and in the overlay).</summary>
    public const float HoleRadius = 12f;

    /// <summary>Innermost pixel orbit: 14 + R2·R.</summary>
    public const float InnerRadius = 14f;

    public const float MinAngularSpeed = 0.5f;
    public const float MaxAngularSpeed = 2.1f;

    private readonly ConcurrentQueue<SwirlCommand> _commands = new();
    private readonly List<SwirlFile> _files = [];
    private readonly Dictionary<Guid, SwirlFile> _byId = [];
    private readonly List<SwirlStackSheet> _stack = [];
    private readonly Random _random;
    private readonly SwirlBudget _budget;

    private volatile SwirlSnapshot _snapshot = SwirlSnapshot.Empty;
    private ScenePalette _palette;

    public SwirlScene(float width, float height, ScenePalette palette, Random random, SwirlBudget budget)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(budget);

        _palette = palette;
        _random = random;
        _budget = budget;
        WhiteHole = new WhiteHoleScene(width, palette, random, budget);
        ApplyResize(width, height);
        BagAnchor = new Vector2(width - 60f, height - 40f);
        WhiteHole.Update(TimeSpan.Zero, 1f);
        PublishSnapshot();
    }

    public float Width { get; private set; }

    public float Height { get; private set; }

    /// <summary>(W/2, H/2).</summary>
    public Vector2 Centre { get; private set; }

    public ScenePalette Palette
    {
        get => _palette;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _palette = value;
            WhiteHole.Palette = value;
            if (Finale is not null)
            {
                Finale.Palette = value;
            }
        }
    }

    public SwirlBudget Budget => _budget;

    public TimeSpan Time { get; private set; }

    /// <summary>Slots, orbits, planets, core and stars.</summary>
    public WhiteHoleScene WhiteHole { get; }

    /// <summary>Created by <see cref="BeginFinale"/>, null before and after <see cref="SwirlClear"/>.</summary>
    public FinaleScene? Finale { get; private set; }

    /// <summary>All files of the round in plan order: stack, in swirl, leaving, landed.</summary>
    public IReadOnlyList<SwirlFile> Files => _files;

    /// <summary>Sheets drawn on the inbox stack, at most <see cref="SwirlBudget.MaxStackSheets"/>.</summary>
    public IReadOnlyList<SwirlStackSheet> StackSheets => _stack;

    /// <summary>Mean progress of the round (landed files count as 1).</summary>
    public float MeanProgress { get; private set; }

    /// <summary>A file is running (circling, leaving or running without a slot).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Where pixels of files with an own target land.</summary>
    public Vector2 BagAnchor { get; private set; }

    /// <summary>Halo around the swirl: radius R + 60, colour lerp(violet, mint, mean progress).</summary>
    public SceneGlow Halo { get; private set; }

    /// <summary>Rotation of the dashed orbit ellipse (0.05 rad/s).</summary>
    public float OrbitRotation => (float)Time.TotalSeconds * 0.05f;

    /// <summary>Pixels alive plus dust and sparks of the finale; never above <see cref="SwirlBudget.MaxParticles"/>.</summary>
    public int ParticleCount { get; private set; }

    /// <summary>Replaced as a whole at the end of every <see cref="Update"/>.</summary>
    public SwirlSnapshot Snapshot => _snapshot;

    /// <summary>
    /// Place of the <paramref name="index"/>th sheet of a plan of <paramref name="count"/> files on the inbox
    /// stack (<c>quelle5</c>): j' = n - 1 - j, x = 40 + 4j', y = cy - 70 - 5j', scale 0.5, rotation
    /// ((j' mod 3) - 1)·0.04. n is capped at 24 so the pile never leaves the surface; files beyond share the
    /// front place.
    /// </summary>
    public static SheetPlacement StackSlot(int index, int count, float centreY, int maxSheets = TransitionScene.MaxFlyingSheets)
    {
        var n = Math.Clamp(count, 1, Math.Max(1, maxSheets));
        var j = n - 1 - Math.Clamp(index, 0, n - 1);
        return new SheetPlacement(new Vector2(40f + 4f * j, centreY - 70f - 5f * j), 0.5f, (j % 3 - 1) * 0.04f);
    }

    /// <summary>Angular speed of a pixel: 0.5 + 1.6·(1 - R2) rad/s, faster inside.</summary>
    public static float AngularSpeed(float r2) => MinAngularSpeed + 1.6f * (1f - r2);

    /// <summary>Orbit radius of a pixel: 14 + R2·R.</summary>
    public static float OrbitRadiusOf(float r2) => InnerRadius + r2 * Radius;

    /// <summary>Where a pixel circles <paramref name="since"/> seconds after its file took a slot.</summary>
    public Vector2 SwirlPosition(in SheetCell cell, float since)
    {
        var w = cell.R1 * MathF.Tau + since * AngularSpeed(cell.R2);
        var r = OrbitRadiusOf(cell.R2);
        return new Vector2(Centre.X + MathF.Cos(w) * r, Centre.Y + MathF.Sin(w) * r * Flatten + (cell.R3 - 0.5f) * 12f);
    }

    /// <summary>Where a file's sheet lies on the stack.</summary>
    public SheetPlacement StackPlacement(SwirlFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return StackSlot(file.Index, _files.Count, Centre.Y, _budget.MaxStackSheets);
    }

    /// <summary>Where the pixels of a leaving file land (<c>ziel5</c>) at the current time.</summary>
    public SheetPlacement TargetPlacement(SwirlFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var t = (float)Time.TotalSeconds;
        return file.Target switch
        {
            PixelTarget.Bag => new SheetPlacement(BagAnchor, 0.06f, 0f),
            PixelTarget.Inbox => StackPlacement(file),
            _ => new SheetPlacement(
                (file.Slot >= 0 ? WhiteHole.SlotPosition(file.Slot, t) : WhiteHole.Centre) + new Vector2(-2f, -2.5f),
                0.04f,
                0f),
        };
    }

    /// <summary>Thread safe: the UI thread hands over work here and never touches the scene itself.</summary>
    public void Enqueue(SwirlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Enqueue(command);
    }

    /// <summary>Applies the queued commands, advances by <paramref name="elapsed"/> (clamped to 100 ms) and publishes a snapshot.</summary>
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
        Time += elapsed;

        UpdateFiles();
        Finale?.Update(elapsed);
        WhiteHole.Update(Time, Finale?.NearStarsFactor ?? 1f);
        UpdateStack();
        UpdateHalo();

        var pixels = 0;
        foreach (var file in _files)
        {
            if (file.HasPixels)
            {
                pixels += file.Pixels.Length;
            }
        }

        ParticleCount = pixels + (Finale?.ParticleCount ?? 0);
        PublishSnapshot();
    }

    // ----- commands ---------------------------------------------------------------------------------

    private void ApplyCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command)
            {
                case SetPlan plan:
                    ApplyPlan(plan);
                    break;
                case BeginFile begin:
                    ApplyBegin(begin);
                    break;
                case SetProgress progress:
                    if (_byId.TryGetValue(progress.Id, out var running) && running.State is not (SwirlFileState.Landed or SwirlFileState.Leaving))
                    {
                        running.Progress = Math.Clamp(progress.Fraction, 0f, 1f);
                    }

                    break;
                case FinishFile finish:
                    ApplyFinish(finish);
                    break;
                case BeginFinale finale:
                    ApplyFinale(finale);
                    break;
                case SetBagAnchor bag:
                    BagAnchor = bag.Position;
                    break;
                case SwirlResize resize:
                    ApplyResize(resize.Width, resize.Height);
                    Finale?.Complete();
                    break;
                case SwirlClear:
                    _files.Clear();
                    _byId.Clear();
                    _stack.Clear();
                    WhiteHole.Clear();
                    Finale = null;
                    break;
            }
        }
    }

    private void ApplyPlan(SetPlan plan)
    {
        _files.Clear();
        _byId.Clear();
        Finale = null;
        var n = 0;
        foreach (var spec in plan.Files)
        {
            if (_byId.ContainsKey(spec.Id))
            {
                continue;
            }

            var file = new SwirlFile(spec, _files.Count);
            _files.Add(file);
            _byId[spec.Id] = file;
            if (!spec.HasOwnLocation)
            {
                n++;
            }
        }

        WhiteHole.SetPlan(n);
    }

    private void ApplyBegin(BeginFile begin)
    {
        if (!_byId.TryGetValue(begin.Id, out var file) || file.State != SwirlFileState.Waiting)
        {
            return;
        }

        var inSwirl = 0;
        var pixels = 0;
        foreach (var other in _files)
        {
            if (other.State == SwirlFileState.InSwirl)
            {
                inSwirl++;
            }

            if (other.HasPixels)
            {
                pixels += other.Pixels.Length;
            }
        }

        if (inSwirl >= _budget.MaxSwirlFiles)
        {
            file.State = SwirlFileState.RunningOffSwirl;
            return;
        }

        var cells = SheetRaster.Build(file.Spec.Kind, _palette, _random);
        var allowed = Math.Max(0, _budget.MaxParticles - pixels);
        if (cells.Length > allowed)
        {
            cells = cells[..allowed];
        }

        file.Cells = cells;
        file.Pixels = new SwirlPixel[cells.Length];
        file.State = SwirlFileState.InSwirl;
        file.EnteredAt = Time;
    }

    private void ApplyFinish(FinishFile finish)
    {
        if (!_byId.TryGetValue(finish.Id, out var file) || file.Outcome is not null)
        {
            return;
        }

        file.Outcome = finish.Outcome;
        file.Target = finish.Target;
        file.LeftAt = Time;
        if (finish.Outcome == FileOutcome.Completed)
        {
            file.Progress = 1f;
        }

        var streams = file.State == SwirlFileState.InSwirl && file.Pixels.Length > 0;
        if (finish.Outcome != FileOutcome.Cancelled && !file.Spec.HasOwnLocation)
        {
            var arrives = streams ? Time + Flight : Time;
            file.Slot = WhiteHole.Assign(file.Id, file.Spec.Kind, finish.Outcome == FileOutcome.Failed, arrives);
        }

        if (streams)
        {
            file.State = SwirlFileState.Leaving;
        }
        else
        {
            file.State = finish.Outcome == FileOutcome.Completed ? SwirlFileState.Landed : SwirlFileState.BackOnStack;
        }
    }

    private void ApplyFinale(BeginFinale finale)
    {
        foreach (var file in _files)
        {
            if (file.State == SwirlFileState.Leaving)
            {
                Land(file);
            }
        }

        Finale = new FinaleScene(
            Width,
            Height,
            Centre,
            WhiteHole,
            _palette,
            _random,
            _budget,
            (float)Time.TotalSeconds,
            finale.Completed,
            finale.Failed);
    }

    private void ApplyResize(float width, float height)
    {
        Width = Math.Max(1f, width);
        Height = Math.Max(1f, height);
        Centre = new Vector2(Width / 2f, Height / 2f);
        WhiteHole.Resize(Width);
    }

    // ----- frame ------------------------------------------------------------------------------------

    private void UpdateFiles()
    {
        var t = Time;
        var dark = _palette.IsDark;
        foreach (var file in _files)
        {
            if (!file.HasPixels || file.EnteredAt is not { } entered)
            {
                continue;
            }

            // The frame that reached the target still showed the pixels there; now the file has landed.
            if (file.State == SwirlFileState.Leaving && file.LeaveShare >= 1f)
            {
                Land(file);
                continue;
            }

            var since = (float)(t - entered).TotalSeconds;
            var enter = (float)Math.Clamp((t - entered).TotalSeconds / Flight.TotalSeconds, 0d, 1d);
            var leave = file.LeftAt is { } left && file.State == SwirlFileState.Leaving
                ? (float)Math.Clamp((t - left).TotalSeconds / Flight.TotalSeconds, 0d, 1d)
                : 0f;
            file.EnterShare = enter;
            file.LeaveShare = leave;

            var stack = StackPlacement(file);
            var target = leave > 0f ? TargetPlacement(file) : default;
            var tint = _palette.For(file.Spec.Kind);
            float k, f;
            if (leave > 0f)
            {
                k = SceneMotion.Ease(leave);
                f = 0.95f;
            }
            else
            {
                k = 0.7f * file.Progress;
                f = 0.95f * SceneMotion.Ease(enter);
            }

            file.ColourMix = k;
            file.TintShare = f;
            var eased = SceneMotion.Ease(leave > 0f ? leave : enter);
            var cells = file.Cells;
            var pixels = file.Pixels;
            for (var i = 0; i < pixels.Length; i++)
            {
                ref readonly var cell = ref cells[i];
                var swirl = SwirlPosition(cell, since);
                var position = leave > 0f
                    ? Vector2.Lerp(swirl, target.PointOf(cell), eased)
                    : Vector2.Lerp(stack.PointOf(cell), swirl, eased);

                ref var pixel = ref pixels[i];
                pixel.Previous = pixel.HasPrevious ? pixel.Position : position;
                pixel.HasPrevious = true;
                pixel.Position = position;
                pixel.Color = SceneColor.Lerp(SceneColor.Lerp(cell.C0, cell.C1, k), tint, f);
                pixel.Alpha = MathF.Max(0.35f, float.Lerp(cell.A0, cell.A1, k));
                pixel.Additive = dark && f <= 0.3f;
            }
        }

        var sum = 0f;
        var running = false;
        foreach (var file in _files)
        {
            sum += file.Progress;
            running |= file.State is SwirlFileState.InSwirl or SwirlFileState.Leaving or SwirlFileState.RunningOffSwirl;
        }

        MeanProgress = _files.Count == 0 ? 0f : sum / _files.Count;
        IsRunning = running;
    }

    private static void Land(SwirlFile file)
    {
        file.State = file.Outcome == FileOutcome.Completed ? SwirlFileState.Landed : SwirlFileState.BackOnStack;
        file.LeaveShare = 1f;
        file.Pixels = [];
        file.Cells = [];
    }

    private void UpdateStack()
    {
        _stack.Clear();
        foreach (var file in _files)
        {
            if (_stack.Count >= _budget.MaxStackSheets)
            {
                break;
            }

            var onStack = file.State is SwirlFileState.Waiting or SwirlFileState.RunningOffSwirl or SwirlFileState.BackOnStack;
            if (onStack)
            {
                _stack.Add(new SwirlStackSheet(file.Spec, StackPlacement(file)));
            }
        }
    }

    private void UpdateHalo()
    {
        var t = (float)Time.TotalSeconds;
        var alpha = IsRunning ? 0.2f : 0.07f + 0.02f * MathF.Sin(t * 2f);
        Halo = new SceneGlow(Centre, Radius + 60f, SceneColor.Lerp(_palette.Audio, _palette.Mint, MeanProgress), alpha);
    }

    private void PublishSnapshot()
    {
        var inSwirl = 0;
        var landed = 0;
        foreach (var file in _files)
        {
            if (file.State == SwirlFileState.InSwirl)
            {
                inSwirl++;
            }
            else if (file.State == SwirlFileState.Landed)
            {
                landed++;
            }
        }

        var finale = Finale;
        _snapshot = new SwirlSnapshot(
            Time,
            MeanProgress,
            inSwirl,
            landed,
            finale?.F,
            finale?.Phase,
            finale?.ShowReport ?? false,
            finale?.ShakeRequested ?? false,
            finale?.ShakeCount ?? 0,
            finale?.CounterAlpha ?? 1f);
    }
}
