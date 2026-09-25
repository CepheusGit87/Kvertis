using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.App.Services;

/// <summary>What the free tier forbids on the target page. Built from <see cref="FreemiumPolicy"/>.</summary>
public sealed record TargetLimits(bool VideoLocked, int? BatchLimit)
{
    /// <summary>Pro, or any state without limits.</summary>
    public static readonly TargetLimits None = new(false, null);
}

/// <summary>One file as step 2 wants to convert it, before the free-tier limits are applied.</summary>
public sealed record PlanDraft(InputInfo Input, ConversionSettings Settings, string NamePattern);

/// <summary>
/// The logic of step 2 without any WinUI type: kind order, the shared format list of a group, the size bar
/// scale and the plan that goes to step 3. Kept separate so it can be tested without the app host.
/// </summary>
public static class TargetPlanner
{
    /// <summary>Reason keys of skipped files; <see cref="FreemiumPolicy"/> uses the same constants.</summary>
    public const string ReasonVideo = "video";

    public const string ReasonBatchSize = "batch-size";

    /// <summary>Display order of the kinds on the target page (design/ENTSCHEIDUNGEN.md, "Schritt 2").</summary>
    public static readonly IReadOnlyList<MediaKind> KindOrder =
    [
        MediaKind.Image,
        MediaKind.Audio,
        MediaKind.Video,
        MediaKind.Model3D,
        MediaKind.Document,
    ];

    /// <summary>Steps of the size bar slider. The bar itself is logarithmic (see <see cref="PositionOf"/>).</summary>
    public const int BarSteps = 1000;

    /// <summary>Theme resource prefix of the colour that stands for this kind (ADR-017).</summary>
    public static string ColorKey(MediaKind kind) => kind switch
    {
        MediaKind.Image => "KvImage",
        MediaKind.Audio => "KvAudio",
        MediaKind.Video => "KvVideo",
        MediaKind.Document => "KvDocument",
        MediaKind.Model3D => "KvModel",
        _ => "KvMuted",
    };

    /// <summary>The kinds present in <paramref name="files"/>, in display order.</summary>
    public static IReadOnlyList<MediaKind> OrderKinds(IEnumerable<MediaKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var present = new HashSet<MediaKind>(kinds);
        return KindOrder.Where(present.Contains).ToList();
    }

    /// <summary>
    /// The outputs every file of the group can produce: the intersection of what
    /// <see cref="IConverterResolver.CanConvert"/> allows. The suggestion of the first file wins when it
    /// survives the intersection, otherwise the first entry does.
    /// </summary>
    public static IReadOnlyList<FormatOption> SharedFormats(
        IReadOnlyList<InputInfo> files,
        IConverterResolver resolver,
        FormatRegistry registry,
        ISystemCodecCapabilities? codecs)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(registry);
        if (files.Count == 0)
        {
            return [];
        }

        List<FormatId>? shared = null;
        FormatId? preferred = null;
        foreach (var file in files)
        {
            var suggestion = registry.Suggest(file, codecs);
            var options = suggestion is null
                ? []
                : suggestion.Options.Where(id => resolver.CanConvert(file, id)).ToList();
            preferred ??= suggestion?.Default;
            if (shared is null)
            {
                shared = options;
            }
            else
            {
                shared = shared.Where(options.Contains).ToList();
            }
            if (shared.Count == 0)
            {
                return [];
            }
        }

        var list = shared ?? [];
        var defaultId = preferred is { } p && list.Contains(p) ? p : list[0];
        return list
            .Select(id => new FormatOption(id, Label(registry, id), id == defaultId))
            .ToList();
    }

    /// <summary>Format name as the user sees it ("JPG"). Not translated; a format name is not a word.</summary>
    public static string Label(FormatRegistry registry, FormatId id)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Get(id)?.DisplayName ?? id.Id.ToUpperInvariant();
    }

    /// <summary>
    /// Applies the free-tier limits to the drafts: locked kinds and everything above the batch limit land in
    /// <see cref="TargetPlan.Skipped"/>, the rest keeps its order.
    /// </summary>
    public static TargetPlan BuildPlan(IReadOnlyList<PlanDraft> drafts, TargetLimits limits)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(limits);

        var items = new List<PlannedConversion>(drafts.Count);
        var skipped = new List<SkippedFile>();
        foreach (var draft in drafts)
        {
            if (limits.VideoLocked && draft.Input.Kind == MediaKind.Video)
            {
                skipped.Add(new SkippedFile(draft.Input, ReasonVideo));
                continue;
            }
            if (limits.BatchLimit is { } limit && items.Count >= limit)
            {
                skipped.Add(new SkippedFile(draft.Input, ReasonBatchSize));
                continue;
            }
            items.Add(new PlannedConversion(draft.Input, draft.Settings, draft.NamePattern));
        }
        return new TargetPlan(items, skipped);
    }

    /// <summary>
    /// Whether the output of a history entry can be produced again here. False shows "damals: WebP, hier
    /// nicht möglich" instead of the takeover button (docs/03-architektur.md, "Anpassen aus dem Verlauf").
    /// </summary>
    public static bool PreviousFits(IReadOnlyList<FormatOption> shared, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(shared);
        return shared.Any(f => f.Id == output);
    }

    /// <summary>Position 0..<see cref="BarSteps"/> of a size on the logarithmic bar.</summary>
    public static double PositionOf(long bytes, long minBytes, long maxBytes)
    {
        var (lo, hi) = Range(minBytes, maxBytes);
        var value = Math.Clamp(bytes, lo, hi);
        var fraction = (Math.Log(value) - Math.Log(lo)) / (Math.Log(hi) - Math.Log(lo));
        return Math.Clamp(fraction, 0, 1) * BarSteps;
    }

    /// <summary>Size at a position 0..<see cref="BarSteps"/> on the logarithmic bar.</summary>
    public static long BytesAt(double position, long minBytes, long maxBytes)
    {
        var (lo, hi) = Range(minBytes, maxBytes);
        var fraction = Math.Clamp(position / BarSteps, 0, 1);
        var value = Math.Exp(Math.Log(lo) + (fraction * (Math.Log(hi) - Math.Log(lo))));
        return (long)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), lo, hi);
    }

    /// <summary>A usable range even when the table is degenerate (one byte, equal ends).</summary>
    private static (long Lo, long Hi) Range(long minBytes, long maxBytes)
    {
        var lo = Math.Max(1, minBytes);
        var hi = Math.Max(lo * 2, maxBytes);
        return (lo, hi);
    }
}
