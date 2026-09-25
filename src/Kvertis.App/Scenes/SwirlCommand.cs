using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>How a file of the round ended.</summary>
public enum FileOutcome
{
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Where the pixels of a finished file fly.</summary>
public enum PixelTarget
{
    /// <summary>Its place on the white hole (files that follow the common target).</summary>
    WhiteHole,

    /// <summary>The bag: files with their own target folder.</summary>
    Bag,

    /// <summary>Back onto the inbox stack (failed or cancelled).</summary>
    Inbox,
}

/// <summary>
/// A file of the round as the swirl needs it; texts come ready from the view model. <see cref="HasOwnLocation"/>
/// files fly into the bag and take no place on the white hole.
/// </summary>
public sealed record SwirlFileSpec(Guid Id, MediaKind Kind, string FormatFrom, string FormatTo, string FileName, bool HasOwnLocation);

/// <summary>
/// A message from the UI thread to the swirl. Commands are applied at the start of the next
/// <see cref="SwirlScene.Update"/>, never in between (ADR-022).
/// </summary>
public abstract record SwirlCommand;

/// <summary>The whole round: inbox stack and the capacity N of the white hole. Replaces an earlier plan.</summary>
public sealed record SetPlan(IReadOnlyList<SwirlFileSpec> Files) : SwirlCommand;

/// <summary>The file took one of the swirl slots (at most two).</summary>
public sealed record BeginFile(Guid Id) : SwirlCommand;

/// <summary>Progress of a running file, 0..1, from <c>ConversionProgress.Fraction</c>.</summary>
public sealed record SetProgress(Guid Id, float Fraction) : SwirlCommand;

/// <summary>The file ended. Files that never had a swirl slot only arrive on the white hole (no pixel stream).</summary>
public sealed record FinishFile(Guid Id, FileOutcome Outcome, PixelTarget Target) : SwirlCommand;

/// <summary>The round is over: the finale starts.</summary>
public sealed record BeginFinale(int Completed, int Failed) : SwirlCommand;

/// <summary>Where the bag sits on the surface; pixels of files with their own target land there.</summary>
public sealed record SetBagAnchor(Vector2 Position) : SwirlCommand;

/// <summary>The surface changed its size. During the finale this completes the finale.</summary>
public sealed record SwirlResize(float Width, float Height) : SwirlCommand;

/// <summary>"Neue Runde": files, white hole and finale are dropped.</summary>
public sealed record SwirlClear : SwirlCommand;
