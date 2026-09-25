using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.App.ViewModels.Drop;

/// <summary>
/// What a tray needs to know about one staged file. Deliberately small: the row itself is drawn from
/// <see cref="JobItemViewModel"/>, this interface only carries what sorting into trays depends on, so the
/// tray logic stays free of WinUI and can be tested (ADR-022).
/// </summary>
public interface ITrayFile
{
    Guid Id { get; }

    MediaKind Kind { get; }

    bool IsReady { get; }

    bool IsRejected { get; }
}

/// <summary>
/// One of the five trays of step 1: its kind, its files and the two counts of the foot line
/// ("opens 11 → makes 8 formats"). Always visible, also while empty.
/// </summary>
public sealed partial class TrayViewModel : ObservableObject
{
    /// <summary>How many example formats an empty tray shows.</summary>
    public const int ExampleLimit = 5;

    private readonly ILocalizer _loc;

    public TrayViewModel(MediaKind kind, FormatRegistry registry, ISystemCodecCapabilities? codecs, ILocalizer loc)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        Kind = kind;
        BrushKey = BrushKeyOf(kind);
        Name = loc.Get("Kind_" + kind + "_Name");
        Subtitle = loc.Get("Kind_" + kind + "_Sub");

        var ofKind = registry.All.Where(d => d.Kind == kind).ToList();
        var readable = ofKind.Where(d => d.CanRead).ToList();
        ReadableCount = readable.Count;
        WritableCount = ofKind.Count(d => d.CanWrite && registry.IsProducible(d.Id, codecs));
        ExampleFormats = readable.Take(ExampleLimit).Select(d => d.DisplayName).ToList();
        ExampleText = string.Join(" · ", ExampleFormats);
        FootText = loc.Format("Tray_Foot_Text", ReadableCount, WritableCount);
        FootOpens = loc.Get("Tray_Foot_Opens");
        FootArrow = loc.Get("Tray_Foot_Arrow");
        FootMakes = loc.Get("Tray_Foot_Makes");
        FootFormats = loc.Get("Tray_Foot_Formats");
        ReadableCountText = ReadableCount.ToString(CultureInfo.CurrentCulture);
        WritableCountText = WritableCount.ToString(CultureInfo.CurrentCulture);
    }

    public MediaKind Kind { get; }

    /// <summary>Resource key of the kind colour, for example <c>KvImageBrush</c> (ADR-017).</summary>
    public string BrushKey { get; }

    public string Name { get; }

    /// <summary>One short line under the name, shown in the zoom title.</summary>
    public string Subtitle { get; }

    /// <summary>The files of this kind, in the order they were added. Runtime type is <see cref="JobItemViewModel"/>.</summary>
    public ObservableCollection<ITrayFile> Files { get; } = [];

    public int ReadableCount { get; }

    public int WritableCount { get; }

    public IReadOnlyList<string> ExampleFormats { get; }

    /// <summary>The example formats of an empty tray in one line, for example "JPG · PNG · WebP".</summary>
    public string ExampleText { get; }

    /// <summary>"öffnet 11 → macht 8 Formate".</summary>
    public string FootText { get; }

    // The same foot line in pieces, so the view can set the numbers bold and the arrow in mint. The screen
    // reader reads FootText as a whole.
    public string FootOpens { get; }

    public string FootArrow { get; }

    public string FootMakes { get; }

    public string FootFormats { get; }

    public string ReadableCountText { get; }

    public string WritableCountText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsEmpty), nameof(AutomationName))]
    private int count;

    /// <summary>True while the galaxy is zoomed to exactly this kind; the head then wears its colour.</summary>
    [ObservableProperty]
    private bool isZoomed;

    /// <summary>True while any kind is zoomed: all five trays shrink to a bar of heads.</summary>
    [ObservableProperty]
    private bool isBar;

    public bool IsEmpty => Count == 0;

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>"Bilder, 3 Dateien" — read out again by the narrator after every change.</summary>
    public string AutomationName => _loc.Format("Tray_AutomationName", Name, Count);

    /// <summary>
    /// Brings <see cref="Files"/> in line with the whole list of staged files. Rejected files belong to the
    /// "not convertible" card and never to a tray; files still being detected have no kind yet and appear as
    /// soon as detection named one.
    /// </summary>
    public void Sync(IReadOnlyList<ITrayFile> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        var mine = all.Where(f => !f.IsRejected && f.Kind == Kind).ToList();

        for (var i = Files.Count - 1; i >= 0; i--)
        {
            if (!mine.Contains(Files[i]))
            {
                Files.RemoveAt(i);
            }
        }

        for (var i = 0; i < mine.Count; i++)
        {
            if (i >= Files.Count)
            {
                Files.Add(mine[i]);
            }
            else if (!ReferenceEquals(Files[i], mine[i]))
            {
                Files.Insert(i, mine[i]);
            }
        }

        Count = Files.Count;
    }

    /// <summary>The colour token of a kind (ADR-017). <see cref="MediaKind.Unknown"/> is the error colour.</summary>
    public static string BrushKeyOf(MediaKind kind) => kind switch
    {
        MediaKind.Image => "KvImageBrush",
        MediaKind.Audio => "KvAudioBrush",
        MediaKind.Video => "KvVideoBrush",
        MediaKind.Document => "KvDocumentBrush",
        MediaKind.Model3D => "KvModelBrush",
        _ => "KvErrorBrush",
    };
}
