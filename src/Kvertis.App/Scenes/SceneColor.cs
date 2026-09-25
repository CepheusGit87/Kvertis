using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// A colour as plain RGB so the scene never references a UI colour type (ADR-022). The view fills it from
/// the <c>Kv*Color</c> resources of <c>Themes/KvertisColors.xaml</c>.
/// </summary>
public readonly record struct SceneColor(byte R, byte G, byte B);

/// <summary>
/// The five kind colours plus mint, error, ink, background and muted. Values only, never brushes; the
/// renderer turns them into Win2D brushes and throws them away on a theme change.
/// </summary>
public sealed record ScenePalette(
    SceneColor Image,
    SceneColor Audio,
    SceneColor Video,
    SceneColor Document,
    SceneColor Model3D,
    SceneColor Mint,
    SceneColor Error,
    SceneColor Ink,
    SceneColor Background,
    SceneColor Muted,
    bool IsDark)
{
    /// <summary>The colour of one media kind; <see cref="MediaKind.Unknown"/> is drawn in the error colour.</summary>
    public SceneColor For(MediaKind kind) => kind switch
    {
        MediaKind.Image => Image,
        MediaKind.Audio => Audio,
        MediaKind.Video => Video,
        MediaKind.Document => Document,
        MediaKind.Model3D => Model3D,
        _ => Error,
    };
}
