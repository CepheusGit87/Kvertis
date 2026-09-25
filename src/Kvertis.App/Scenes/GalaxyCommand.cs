using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// A message from the UI thread to the scene. Commands are applied at the start of the next
/// <see cref="GalaxyScene.Update"/>, never in between (ADR-022).
/// </summary>
public abstract record GalaxyCommand;

/// <summary>A newly staged file enters the scene and flies to the orbit of its kind.</summary>
public sealed record AddBody(Guid Id, MediaKind Kind, string FormatLabel, string Name, long SizeBytes) : GalaxyCommand;

/// <summary>The file was removed from the list; its planet and halo disappear.</summary>
public sealed record RemoveBody(Guid Id) : GalaxyCommand;

/// <summary>The file cannot be converted: it flies to the rejected card instead of to an orbit.</summary>
public sealed record RejectBody(Guid Id) : GalaxyCommand;

/// <summary>The pointer moved over the surface; null means it left.</summary>
public sealed record SetPointer(Vector2? Position) : GalaxyCommand;

/// <summary>Files are being dragged over the window.</summary>
public sealed record SetDragOver(bool Active) : GalaxyCommand;

/// <summary>A tray is hovered, so its orbit lights up; null means none.</summary>
public sealed record SetTrayHover(MediaKind? Kind) : GalaxyCommand;

/// <summary>Drive the camera to one kind, or back to the overview with null.</summary>
public sealed record ZoomTo(MediaKind? Kind) : GalaxyCommand;

/// <summary>The canvas coordinates of one row of the zoom overlay.</summary>
public sealed record PathAnchor(string FormatId, Vector2 Position);

/// <summary>The zoom overlay reports where its rows sit, so the paths can start and end there.</summary>
public sealed record SetPathAnchors(
    IReadOnlyList<PathAnchor> Left,
    IReadOnlyList<PathAnchor> Right,
    string? SelectedInput,
    IReadOnlySet<string> Reachable,
    string? Recommended) : GalaxyCommand;

/// <summary>Where rejected files fly to: the position of the "not convertible" card.</summary>
public sealed record SetRejectedAnchor(Vector2 Position) : GalaxyCommand;

/// <summary>The drawing surface changed its size; the geometry is rebuilt.</summary>
public sealed record Resize(float Width, float Height) : GalaxyCommand;

/// <summary>"Neue Runde": all bodies leave, the running number starts at zero again.</summary>
public sealed record Clear : GalaxyCommand;
