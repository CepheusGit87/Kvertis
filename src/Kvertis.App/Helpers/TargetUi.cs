using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Tuning;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kvertis.App.Helpers;

/// <summary>x:Bind functions of step 2 "Ziel" (Views/TargetPage.xaml): kind colours, symbols and previews.</summary>
public static class TargetUi
{
    /// <summary>A kind colour brush by media kind: <paramref name="suffix"/> "Brush", "Tint12Brush", "Frame40Brush" (ADR-017).</summary>
    public static Brush KindBrushOf(MediaKind kind, string suffix) => Ui.Brush(TargetPlanner.ColorKey(kind) + suffix);

    /// <summary>Segoe Fluent Icons symbol of a media kind, the same as the file rows of step 1.</summary>
    public static string KindGlyph(MediaKind kind) => kind switch
    {
        MediaKind.Image => "",
        MediaKind.Audio => "",
        MediaKind.Video => "",
        MediaKind.Document => "",
        MediaKind.Model3D => "",
        _ => "",
    };

    /// <summary>
    /// A small preview of an image file for a card. The framework decodes it off the UI thread; other kinds get
    /// null and keep their symbol, and a file the decoder cannot read simply stays empty over the symbol.
    /// </summary>
    public static ImageSource? Thumbnail(string? path, MediaKind kind, int decodeWidth)
    {
        if (kind != MediaKind.Image || string.IsNullOrEmpty(path) || !Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return null;
        }
        return new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical, UriSource = uri };
    }

    /// <summary>Circle of a line in "Was sich ändert": mint surface, neutral panel or amber tint (.folgen li i).</summary>
    public static Brush EffectSurface(EffectSeverity severity) => Ui.Brush(severity switch
    {
        EffectSeverity.Warning => "KvVideoTint12Brush",
        EffectSeverity.Notice => "KvPanelBrush",
        _ => "KvMintSurfaceBrush",
    });

    /// <summary>Border of that circle.</summary>
    public static Brush EffectFrame(EffectSeverity severity) => Ui.Brush(severity switch
    {
        EffectSeverity.Warning => "KvVideoFrame40Brush",
        EffectSeverity.Notice => "KvLineStrongBrush",
        _ => "KvMintFrameBrush",
    });
}
