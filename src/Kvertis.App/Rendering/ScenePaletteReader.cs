using Kvertis.App.Scenes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kvertis.App.Rendering;

/// <summary>
/// Turns the <c>Kv*Color</c> resources of the current theme into a <see cref="ScenePalette"/> (ADR-022). The
/// scene itself never sees a WinUI colour; the view hands it plain RGB values and replaces them whenever the
/// theme changes.
/// </summary>
/// <remarks>
/// The values are looked up on the element that hosts them, so the result follows
/// <see cref="FrameworkElement.ActualTheme"/> and not the application-wide requested theme. The host declares
/// one <see cref="SolidColorBrush"/> per token with a <c>ThemeResource</c> reference; WinUI re-resolves those
/// on a theme change, which is exactly what a repeated call then reads.
/// </remarks>
public static class ScenePaletteReader
{
    /// <summary>Key prefix of the brushes a host declares for the scene, for example <c>SceneColor_Image</c>.</summary>
    public const string KeyPrefix = "SceneColor_";

    /// <summary>Reads the whole palette from the resources of <paramref name="host"/>.</summary>
    public static ScenePalette Read(FrameworkElement host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var isDark = host.ActualTheme == ElementTheme.Dark;
        var palette = new ScenePalette(
            Read(host, "Image"),
            Read(host, "Audio"),
            Read(host, "Video"),
            Read(host, "Document"),
            Read(host, "Model3D"),
            Read(host, "Mint"),
            Read(host, "Error"),
            Read(host, "Ink"),
            Read(host, "Background"),
            Read(host, "Muted"),
            isDark);

        // The tokens of the swirl and the finale (worksheet "Übergänge", section "Farben"). A host that does
        // not declare one of them keeps the palette's own fallback for its theme.
        return palette with
        {
            LineStrong = TryRead(host, "LineStrong") ?? palette.LineStrong,
            Paper = TryRead(host, "Paper") ?? palette.Paper,
            PaperLine = TryRead(host, "PaperLine") ?? palette.PaperLine,
            OnMint = TryRead(host, "OnMint") ?? palette.OnMint,
            Shadow = TryRead(host, "Shadow") ?? palette.Shadow,
        };
    }

    private static SceneColor Read(FrameworkElement host, string token) =>
        TryRead(host, token) ?? new SceneColor(128, 128, 128);

    private static SceneColor? TryRead(FrameworkElement host, string token)
    {
        var key = KeyPrefix + token;
        if (host.Resources.TryGetValue(key, out var own) && Convert(own) is { } fromHost)
        {
            return fromHost;
        }

        // A control that forgot to declare the brush still draws, only in the application's theme.
        if (Application.Current?.Resources.TryGetValue(key, out var app) == true && Convert(app) is { } fromApp)
        {
            return fromApp;
        }

        return null;
    }

    private static SceneColor? Convert(object? value) => value switch
    {
        SolidColorBrush brush => new SceneColor(brush.Color.R, brush.Color.G, brush.Color.B),
        Color color => new SceneColor(color.R, color.G, color.B),
        _ => null,
    };
}
