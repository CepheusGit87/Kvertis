using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.App.ViewModels.Drop;

/// <summary>
/// Shared part of the two zoom lists: which format a row stands for and how it reads.
/// </summary>
/// <remarks>
/// The two sides have their own type although they carry almost the same data. Reason: the XAML compiler of
/// the Windows App SDK aborts when one file holds two <c>DataTemplate</c>s with the same <c>x:DataType</c>,
/// and the overlay needs both lists side by side.
/// </remarks>
public abstract partial class PathRowBase : ObservableObject
{
    protected PathRowBase(FormatId id, string label, string hint)
    {
        Id = id;
        Label = label;
        Hint = hint;
    }

    public FormatId Id { get; }

    /// <summary>The badge text, for example "HEIC".</summary>
    public string Label { get; }

    /// <summary>The short explanation next to it, for example "Handyfoto".</summary>
    public string Hint { get; }
}

/// <summary>One row on the left, "Kvertis öffnet": a format the user can pick as the starting point.</summary>
public sealed partial class PathFormatViewModel : PathRowBase
{
    public PathFormatViewModel(FormatId id, string label, string hint, bool isStaged)
        : base(id, label, hint)
    {
        IsStaged = isStaged;
    }

    /// <summary>True when a file of exactly this format was dropped in; the row then wears a colour dot.</summary>
    public bool IsStaged { get; }

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>One row on the right, "kann werden zu": a format the chosen input may or may not reach.</summary>
public sealed partial class PathTargetViewModel : PathRowBase
{
    public PathTargetViewModel(FormatId id, string label, string hint)
        : base(id, label, hint)
    {
    }

    /// <summary>False when the chosen input cannot become this format; the row is then dimmed.</summary>
    [ObservableProperty]
    private bool isReachable = true;

    /// <summary>The one suggestion, drawn in mint.</summary>
    [ObservableProperty]
    private bool isRecommended;

    /// <summary>"empfohlen" or "nicht von HEIC" for the screen reader.</summary>
    [ObservableProperty]
    private string helpText = string.Empty;
}

/// <summary>
/// The zoom "Wege durchs Loch" of one kind (worksheet step 1, part D): which formats Kvertis can read, what
/// they can become, and which target it suggests. Free of WinUI so the rules can be tested; the overlay only
/// shows what this class computed.
/// </summary>
public sealed partial class PathsViewModel : ObservableObject
{
    /// <summary>Without a choice of the user the left selection moves on after this long.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromSeconds(2.6);

    /// <summary>A choice with the keyboard blocks the automatic change for this long.</summary>
    public static readonly TimeSpan LockAfterChoice = TimeSpan.FromSeconds(6);

    /// <summary>A choice with the mouse blocks it a little longer, because the pointer is still on the list.</summary>
    public static readonly TimeSpan LockAfterClick = TimeSpan.FromSeconds(8);

    private readonly FormatRegistry _registry;
    private readonly ISystemCodecCapabilities? _codecs;
    private readonly ILocalizer _loc;
    private readonly TimeProvider _time;

    private DateTimeOffset _lockedUntil;
    private DateTimeOffset _lastAdvance;
    private bool _applying;
    private bool _userChose;

    public PathsViewModel(FormatRegistry registry, ISystemCodecCapabilities? codecs, ILocalizer loc, TimeProvider time)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _codecs = codecs;
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The kind the camera is on; <see cref="MediaKind.Unknown"/> while the overlay is closed.</summary>
    public MediaKind Kind { get; private set; } = MediaKind.Unknown;

    public ObservableCollection<PathFormatViewModel> Inputs { get; } = [];

    public ObservableCollection<PathTargetViewModel> Outputs { get; } = [];

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string subtitle = string.Empty;

    [ObservableProperty]
    private PathFormatViewModel? selectedInput;

    /// <summary>The ids the chosen input can become; the scene draws a way to each of them.</summary>
    public IReadOnlySet<string> Reachable { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The suggested target, or null when the input reaches nothing.</summary>
    public string? RecommendedId { get; private set; }

    /// <summary>
    /// The target step 2 should preselect (ADR-022): the recommendation for an input that was really dropped
    /// in or that the user picked. The automatic change alone expresses no wish, so it gives null.
    /// </summary>
    public FormatId? PreferredOutput =>
        RecommendedId is { } id && SelectedInput is { } input && (input.IsStaged || _userChose)
            ? Outputs.FirstOrDefault(o => string.Equals(o.Id.Id, id, StringComparison.OrdinalIgnoreCase))?.Id
            : null;

    /// <summary>
    /// Fills both lists for one kind. <paramref name="stagedFormats"/> are the formats actually dropped in;
    /// their rows get a colour dot and the first of them is preselected.
    /// </summary>
    public void Open(MediaKind kind, IReadOnlyList<FormatId> stagedFormats)
    {
        ArgumentNullException.ThrowIfNull(stagedFormats);
        Kind = kind;
        Title = _loc.Get("Kind_" + kind + "_Name");
        Subtitle = _loc.Get("Kind_" + kind + "_Sub");

        Inputs.Clear();
        Outputs.Clear();
        foreach (var descriptor in _registry.All.Where(d => d.Kind == kind && d.CanRead).OrderBy(d => d.DisplayName, StringComparer.CurrentCulture))
        {
            Inputs.Add(new PathFormatViewModel(
                descriptor.Id,
                descriptor.DisplayName,
                _loc.Get("Format_" + descriptor.Id.Id + "_Hint"),
                stagedFormats.Contains(descriptor.Id)));
        }

        foreach (var descriptor in _registry.All
            .Where(d => d.Kind == kind && d.CanWrite && _registry.IsProducible(d.Id, _codecs))
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCulture))
        {
            Outputs.Add(new PathTargetViewModel(
                descriptor.Id,
                descriptor.DisplayName,
                _loc.Get("Format_" + descriptor.Id.Id + "_Hint")));
        }

        _lockedUntil = DateTimeOffset.MinValue;
        _userChose = false;
        _lastAdvance = _time.GetUtcNow();
        var first = Inputs.FirstOrDefault(i => i.IsStaged) ?? Inputs.FirstOrDefault();
        Apply(first);
    }

    /// <summary>Clears both lists; the overlay is closed.</summary>
    public void Close()
    {
        Kind = MediaKind.Unknown;
        Inputs.Clear();
        Outputs.Clear();
        Apply(null);
    }

    /// <summary>A choice of the user. It blocks the automatic change for a few seconds.</summary>
    public void Choose(PathFormatViewModel? input, bool byClick)
    {
        _lockedUntil = _time.GetUtcNow() + (byClick ? LockAfterClick : LockAfterChoice);
        _userChose = true;
        Apply(input);
    }

    /// <summary>
    /// Moves the left selection on when nothing was chosen for a while. Returns true when the selection
    /// changed, so the caller knows it has to report new anchors.
    /// </summary>
    public bool Tick()
    {
        var now = _time.GetUtcNow();
        if (Inputs.Count < 2 || now < _lockedUntil || now - _lastAdvance < AutoInterval)
        {
            return false;
        }

        _lastAdvance = now;
        _userChose = false;
        var index = SelectedInput is null ? -1 : Inputs.IndexOf(SelectedInput);
        Apply(Inputs[(index + 1) % Inputs.Count]);
        return true;
    }

    partial void OnSelectedInputChanged(PathFormatViewModel? value)
    {
        if (!_applying)
        {
            Choose(value, byClick: true);
        }
    }

    private void Apply(PathFormatViewModel? input)
    {
        _applying = true;
        try
        {
            SelectedInput = input;
        }
        finally
        {
            _applying = false;
        }

        foreach (var row in Inputs)
        {
            row.IsSelected = ReferenceEquals(row, input);
        }

        var outputs = input is null ? null : _registry.OutputsFor(input.Id, _codecs);
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (outputs is not null)
        {
            foreach (var id in outputs)
            {
                if (Outputs.Any(o => o.Id == id))
                {
                    reachable.Add(id.Id);
                }
            }
        }

        Reachable = reachable;

        // Same rule as OutputSuggestion.Default: the first target that is not the input itself, except for
        // video, where "MP4 → MP4, but smaller" is the common wish.
        RecommendedId = null;
        if (outputs is not null && input is not null)
        {
            var recommended = Kind == MediaKind.Video && outputs.Count > 0 && outputs[0] == input.Id
                ? outputs[0]
                : outputs.FirstOrDefault(o => o != input.Id);
            if (recommended != default && reachable.Contains(recommended.Id))
            {
                RecommendedId = recommended.Id;
            }
        }

        foreach (var row in Outputs)
        {
            row.IsReachable = reachable.Contains(row.Id.Id);
            row.IsRecommended = RecommendedId is { } id && string.Equals(row.Id.Id, id, StringComparison.OrdinalIgnoreCase);
            row.HelpText = row.IsRecommended
                ? _loc.Get("Galaxy_Recommended_Text")
                : row.IsReachable ? string.Empty : _loc.Format("Galaxy_NotReachable_Text", input?.Label ?? string.Empty);
        }

        OnPropertyChanged(nameof(Reachable));
        OnPropertyChanged(nameof(RecommendedId));
    }
}
