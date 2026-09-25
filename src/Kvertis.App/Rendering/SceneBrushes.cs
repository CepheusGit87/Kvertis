using System.Numerics;
using Kvertis.App.Scenes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;
using Windows.UI.Text;

namespace Kvertis.App.Rendering;

/// <summary>
/// The device resources the swirl and the finale share: one radial glow brush per colour, the dashed stroke
/// styles and the text formats. Built in <c>CreateResources</c>, dropped as a whole on a theme change or a
/// device loss (ADR-022). Glows are radial gradients, never blur effects; no bitmap is ever created here.
/// </summary>
internal sealed class SceneBrushes : IDisposable
{
    private const string Mono = "Consolas";

    private readonly Dictionary<int, CanvasRadialGradientBrush> _glows = [];
    private readonly Dictionary<string, float> _textWidths = new(StringComparer.Ordinal);

    public ICanvasResourceCreator? Creator { get; private set; }

    /// <summary>Dashed 2/6: the orbit ellipse of the swirl.</summary>
    public CanvasStrokeStyle? Dash26 { get; private set; }

    /// <summary>Dashed 2/5: an empty orbit of the white hole.</summary>
    public CanvasStrokeStyle? Dash25 { get; private set; }

    /// <summary>Round caps and joins: hole trails and the check mark.</summary>
    public CanvasStrokeStyle? Round { get; private set; }

    public CanvasTextFormat? SheetTab { get; private set; }

    public CanvasTextFormat? SheetLabel { get; private set; }

    public CanvasTextFormat? SheetName { get; private set; }

    public CanvasTextFormat? CounterBig { get; private set; }

    public CanvasTextFormat? CounterSmall { get; private set; }

    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        Release();
        Creator = creator;
        Dash26 = new CanvasStrokeStyle { CustomDashStyle = [2f, 6f] };
        Dash25 = new CanvasStrokeStyle { CustomDashStyle = [2f, 5f] };
        Round = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };
        SheetTab = Format(Mono, 9f, 700, CanvasVerticalAlignment.Center);
        SheetLabel = Format(Mono, 17f, 700, CanvasVerticalAlignment.Top);
        SheetName = Format(Mono, 7f, 500, CanvasVerticalAlignment.Top);
        SheetName.TrimmingGranularity = CanvasTextTrimmingGranularity.Character;
        SheetName.TrimmingSign = CanvasTrimmingSign.Ellipsis;
        CounterBig = Format(Mono, 15f, 600, CanvasVerticalAlignment.Center);
        CounterSmall = Format(Mono, 11f, 500, CanvasVerticalAlignment.Center);
    }

    public void Release()
    {
        foreach (var brush in _glows.Values)
        {
            brush.Dispose();
        }

        _glows.Clear();
        _textWidths.Clear();
        Dash26?.Dispose();
        Dash26 = null;
        Dash25?.Dispose();
        Dash25 = null;
        Round?.Dispose();
        Round = null;
        SheetTab?.Dispose();
        SheetTab = null;
        SheetLabel?.Dispose();
        SheetLabel = null;
        SheetName?.Dispose();
        SheetName = null;
        CounterBig?.Dispose();
        CounterBig = null;
        CounterSmall?.Dispose();
        CounterSmall = null;
    }

    public void Dispose()
    {
        Release();
        Creator = null;
    }

    /// <summary>A soft radial glow; <paramref name="scaleY"/> squashes it vertically.</summary>
    public void Glow(CanvasDrawingSession session, Vector2 centre, float radius, SceneColor colour, float alpha, float scaleY = 1f)
    {
        if (alpha <= 0.004f || radius <= 0f)
        {
            return;
        }

        var brush = GlowBrush(colour);
        if (brush is null)
        {
            return;
        }

        brush.Center = centre;
        brush.RadiusX = radius;
        brush.RadiusY = radius;
        brush.Opacity = Math.Clamp(alpha, 0f, 1f);
        if (scaleY == 1f)
        {
            session.FillCircle(centre, radius, brush);
            return;
        }

        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateScale(1f, scaleY, centre) * previous;
        session.FillCircle(centre, radius, brush);
        session.Transform = previous;
    }

    public void Glow(CanvasDrawingSession session, in SceneGlow glow) =>
        Glow(session, glow.Centre, glow.Radius, glow.Color, glow.Alpha, glow.ScaleY);

    /// <summary>One radial gradient per colour, cached for the lifetime of the device.</summary>
    public CanvasRadialGradientBrush? GlowBrush(SceneColor colour)
    {
        if (Creator is null)
        {
            return null;
        }

        var key = (colour.R << 16) | (colour.G << 8) | colour.B;
        if (!_glows.TryGetValue(key, out var brush))
        {
            brush = new CanvasRadialGradientBrush(
                Creator,
                Color.FromArgb(255, colour.R, colour.G, colour.B),
                Color.FromArgb(0, colour.R, colour.G, colour.B));
            _glows[key] = brush;
        }

        return brush;
    }

    /// <summary>Width of <paramref name="text"/> in <paramref name="format"/>, cached per string.</summary>
    public float Measure(ICanvasResourceCreator creator, string text, CanvasTextFormat format)
    {
        var key = format.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + text;
        if (_textWidths.TryGetValue(key, out var width))
        {
            return width;
        }

        using var layout = new CanvasTextLayout(creator, text, format, 600f, 60f);
        width = (float)layout.LayoutBounds.Width;
        _textWidths[key] = width;
        return width;
    }

    public static Color Argb(SceneColor colour, float alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0f, 1f) * 255f), colour.R, colour.G, colour.B);

    public static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color Rgba(uint rgb, float alpha) => Color.FromArgb((byte)(Math.Clamp(alpha, 0f, 1f) * 255f), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static CanvasTextFormat Format(string family, float size, ushort weight, CanvasVerticalAlignment vertical) => new()
    {
        FontFamily = family,
        FontSize = size,
        FontWeight = new FontWeight(weight),
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = vertical,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };
}
