using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// A message from the UI thread to the target paths scene. Applied at the start of the next
/// <see cref="TargetPathsScene.Update"/>, never in between (ADR-022).
/// </summary>
public abstract record TargetPathsCommand;

/// <summary>One file of the chosen kind as a planet on the orbit; the size sets its radius.</summary>
public sealed record TargetPlanet(string FileId, long SizeBytes);

/// <summary>The chosen kind changed (or the page was entered): the orbit opens again in that kind's colour.</summary>
public sealed record ShowKind(MediaKind? Kind, IReadOnlyList<TargetPlanet> Planets) : TargetPathsCommand;

/// <summary>
/// Where one file card ends on the left, in canvas coordinates, and which format it becomes.
/// <see cref="IsOwnChoice"/> is true when the file picked its own target in "Jede einzeln".
/// </summary>
public sealed record TargetFileAnchor(string FileId, Vector2 Position, string? TargetId, bool IsOwnChoice);

/// <summary>Where one row of the target list starts on the right, in canvas coordinates.</summary>
public sealed record TargetFormatAnchor(string FormatId, Vector2 Position);

/// <summary>The page reports the edges of its lists, so the ways start and end exactly where the rows are.</summary>
public sealed record SetTargetAnchors(
    IReadOnlyList<TargetFileAnchor> Files,
    IReadOnlyList<TargetFormatAnchor> Formats,
    string? RecommendedId) : TargetPathsCommand;

/// <summary>The drawing surface changed its size; the geometry is rebuilt.</summary>
public sealed record ResizeTargetPaths(float Width, float Height) : TargetPathsCommand;
