using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// A colour as plain RGB so the scene never references a UI colour type (ADR-022). The view fills it from
/// the <c>Kv*Color</c> resources of <c>Themes/KvertisColors.xaml</c>.
/// </summary>
public readonly record struct SceneColor(byte R, byte G, byte B)
{
    /// <summary>Pure white, the "white" of the white hole and the supernova in the dark theme.</summary>
    public static readonly SceneColor White = new(0xFF, 0xFF, 0xFF);

    /// <summary>Parses "#RRGGBB"; only used for the fixed content colours of the drawn sheet.</summary>
    public static SceneColor FromHex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Channel-wise linear mix, rounded towards zero like the design (<c>R | 0</c>).</summary>
    public static SceneColor Lerp(SceneColor a, SceneColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new SceneColor(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}

/// <summary>
/// The five kind colours plus mint, error, ink, background and muted. Values only, never brushes; the
/// renderer turns them into Win2D brushes and throws them away on a theme change.
/// </summary>
/// <remarks>
/// <see cref="LineStrong"/>, <see cref="Paper"/>, <see cref="PaperLine"/>, <see cref="OnMint"/> and
/// <see cref="Shadow"/> (worksheet "Übergänge", section "Farben") are init properties so the existing
/// positional construction keeps compiling. Until the palette reader sets them from the <c>Kv*Color</c>
/// resources they fall back to the same values as <c>Themes/KvertisColors.xaml</c> for the theme in
/// <see cref="IsDark"/>. <see cref="Shadow"/> carries no alpha; the renderer applies 0.35.
/// </remarks>
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
    /// <summary>KvLineStrong: empty orbits, empty places, stars in the light theme, the dashed swirl orbit.</summary>
    public SceneColor LineStrong { get; init; } = IsDark ? SceneColor.FromHex(0x3A454B) : SceneColor.FromHex(0xB6BEC4);

    /// <summary>KvPaper: the paper of the drawn sheet.</summary>
    public SceneColor Paper { get; init; } = IsDark ? SceneColor.FromHex(0xF2F0EA) : SceneColor.FromHex(0xFFFFFF);

    /// <summary>KvPaperLine: lines on the drawn sheet.</summary>
    public SceneColor PaperLine { get; init; } = IsDark ? SceneColor.FromHex(0xCFCBC1) : SceneColor.FromHex(0xD8D5CD);

    /// <summary>KvOnMint: the stroke of the check mark on the mint disc.</summary>
    public SceneColor OnMint { get; init; } = IsDark ? SceneColor.FromHex(0x08110E) : SceneColor.FromHex(0xFFFFFF);

    /// <summary>KvShadow without its alpha: the offset rectangle under a sheet.</summary>
    public SceneColor Shadow { get; init; } = IsDark ? SceneColor.FromHex(0x000000) : SceneColor.FromHex(0x1A232A);

    /// <summary>The "white" of the white hole, the supernova and the flash: white in the dark, mint in the light theme.</summary>
    public SceneColor Glow => IsDark ? SceneColor.White : Mint;

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
