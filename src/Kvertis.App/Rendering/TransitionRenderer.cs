using System.Numerics;
using Kvertis.App.Scenes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Kvertis.App.Rendering;

/// <summary>
/// Draws a <see cref="TransitionScene"/> with Win2D (ADR-023). Everything on screen is a stand-in built from
/// primitives: a tray head, a kind symbol and a sheet are rounded rectangles, circles and text, never a copy
/// of the XAML element they stand for. No bitmap is created here, every glow is a radial gradient.
/// </summary>
public sealed class TransitionRenderer : IDisposable
{
    private const string TextFamily = "Segoe UI Variable Text";
    private const string DisplayFamily = "Segoe UI Variable Display";

    /// <summary>The design's fixed content colours of the sheet: tab text and the big format label.</summary>
    private static readonly Color TabInk = Color.FromArgb(255, 0x0B, 0x0F, 0x12);
    private static readonly Color LabelInk = Color.FromArgb(255, 0x3B, 0x42, 0x47);

    private readonly Dictionary<int, CanvasRadialGradientBrush> _glows = [];

    private ICanvasResourceCreator? _creator;
    private CanvasTextFormat? _trayName;
    private CanvasTextFormat? _trayCount;
    private CanvasTextFormat? _symbolName;
    private CanvasTextFormat? _sheetTab;
    private CanvasTextFormat? _sheetLabel;
    private CanvasTextFormat? _sheetName;

    /// <summary>Called from <c>CreateResources</c>; everything built for the old device is dropped first.</summary>
    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ReleaseBrushes();
        _creator = creator;
        _trayName = Format(TextFamily, 12f, 600, CanvasHorizontalAlignment.Left);
        _trayCount = Format(DisplayFamily, 18f, 700, CanvasHorizontalAlignment.Right);
        _symbolName = Format(TextFamily, 12f, 600, CanvasHorizontalAlignment.Left);
        _sheetTab = Format(TextFamily, 9f, 700, CanvasHorizontalAlignment.Center);
        _sheetLabel = Format(DisplayFamily, 17f, 700, CanvasHorizontalAlignment.Left);
        _sheetName = Format(TextFamily, 7f, 500, CanvasHorizontalAlignment.Left);
        _sheetName.TrimmingGranularity = CanvasTextTrimmingGranularity.Character;
        _sheetName.TrimmingSign = CanvasTrimmingSign.Ellipsis;
    }

    /// <summary>Drops brushes and formats but keeps the device (theme change); <see cref="CreateResources"/> has to follow.</summary>
    public void ReleaseBrushes()
    {
        foreach (var brush in _glows.Values)
        {
            brush.Dispose();
        }

        _glows.Clear();
        _trayName?.Dispose();
        _trayName = null;
        _trayCount?.Dispose();
        _trayCount = null;
        _symbolName?.Dispose();
        _symbolName = null;
        _sheetTab?.Dispose();
        _sheetTab = null;
        _sheetLabel?.Dispose();
        _sheetLabel = null;
        _sheetName?.Dispose();
        _sheetName = null;
    }

    public void Dispose()
    {
        ReleaseBrushes();
        _creator = null;
    }

    /// <summary>One frame, in the order of the worksheet: rings, ghosts with alpha B, ghosts with alpha A, sparks, hole.</summary>
    public void Draw(CanvasDrawingSession session, TransitionScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        if (_creator is null)
        {
            return;
        }

        var palette = scene.Palette;
        foreach (var ring in scene.Rings)
        {
            if (ring.Alpha > 0.01f && ring.Radius > 0.3f)
            {
                session.DrawEllipse(ring.Centre, ring.Radius, ring.Radius * ring.Flatten, Argb(ring.Color, ring.Alpha), ring.Width);
            }
        }

        foreach (var ghost in scene.Ghosts)
        {
            if (ghost.ShapeB is { } shapeB && ghost.AlphaB > 0.01f)
            {
                DrawGhost(session, ghost, shapeB, ghost.NaturalSizeB, ghost.AlphaB, palette);
            }
        }

        foreach (var ghost in scene.Ghosts)
        {
            if (ghost.AlphaA > 0.01f)
            {
                DrawGhost(session, ghost, ghost.Spec.Shape, ghost.NaturalSizeA, ghost.AlphaA, palette);
            }
        }

        if (scene.Sparks.Count > 0)
        {
            var previous = session.Blend;
            session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
            foreach (var spark in scene.Sparks)
            {
                if (spark.Alpha > 0.01f)
                {
                    var half = spark.Size / 2f;
                    session.FillRectangle(spark.Position.X - half, spark.Position.Y - half, spark.Size, spark.Size, Argb(spark.Color, spark.Alpha));
                }
            }

            session.Blend = previous;
        }

        if (scene.Hole is { } hole)
        {
            DrawHole(session, hole, palette);
        }
    }

    // ----- ghosts ------------------------------------------------------------------------------------

    /// <summary>
    /// A ghost is drawn in its natural box around the origin and placed with one transform: scale by width
    /// over natural width (times the stretch of the station), rotation, translation to the position.
    /// </summary>
    private void DrawGhost(CanvasDrawingSession session, in GhostState ghost, GhostShape shape, Vector2 natural, float alpha, ScenePalette palette)
    {
        if (natural.X <= 0.5f || natural.Y <= 0.5f || ghost.Width <= 0.1f)
        {
            return;
        }

        var scale = ghost.Width / natural.X;
        var sx = scale * ghost.ScaleX;
        var sy = scale * ghost.ScaleY;
        if (sx <= 0.001f || sy <= 0.001f)
        {
            return;
        }

        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateScale(sx, sy) * Matrix3x2.CreateRotation(ghost.Rotation) * Matrix3x2.CreateTranslation(ghost.Position) * previous;
        using (session.CreateLayer(Math.Clamp(alpha, 0f, 1f)))
        {
            switch (shape)
            {
                case GhostShape.TrayHeader:
                    DrawTrayHeader(session, ghost.Spec, natural, palette);
                    break;
                case GhostShape.KindSymbol:
                    DrawKindSymbol(session, ghost.Spec, natural, palette);
                    break;
                default:
                    DrawSheet(session, ghost.Spec, palette);
                    break;
            }
        }

        session.Transform = previous;
    }

    /// <summary>Card with border, colour dot, kind name and the count in the kind colour (like <c>TrayControl</c>'s head).</summary>
    private void DrawTrayHeader(CanvasDrawingSession session, GhostSpec spec, Vector2 natural, ScenePalette palette)
    {
        var w = natural.X;
        var h = natural.Y;
        var box = new Rect(-w / 2f, -h / 2f, w, h);
        var colour = palette.For(spec.Kind);
        session.FillRoundedRectangle(box, 8f, 8f, Argb(palette.Panel, 1f));
        session.DrawRoundedRectangle(box, 8f, 8f, Argb(palette.Line, 1f), 1f);
        session.FillCircle(new Vector2(-w / 2f + 14f, 0f), 5f, Argb(colour, 1f));

        if (_trayName is not null)
        {
            var textWidth = Math.Max(10f, w - 26f - 12f - 40f);
            using var name = new CanvasTextLayout(session, spec.Label, _trayName, textWidth, h);
            session.DrawTextLayout(name, new Vector2(-w / 2f + 26f, -h / 2f), Argb(palette.Ink, 1f));
        }

        if (_trayCount is not null && spec.Count is { Length: > 0 })
        {
            using var count = new CanvasTextLayout(session, spec.Count, _trayCount, 60f, h);
            session.DrawTextLayout(count, new Vector2(w / 2f - 10f - 60f, -h / 2f), Argb(colour, 1f));
        }
    }

    /// <summary>Filled circle with a ring in the kind colour and the name next to it (the entry of the kind list, later the universe).</summary>
    private void DrawKindSymbol(CanvasDrawingSession session, GhostSpec spec, Vector2 natural, ScenePalette palette)
    {
        var w = natural.X;
        var h = natural.Y;
        var colour = palette.For(spec.Kind);
        var centre = new Vector2(-w / 2f + 12f, 0f);
        session.FillCircle(centre, 12f, Argb(colour, 0.9f));
        session.DrawCircle(centre, 16f, Argb(colour, 0.55f), 1.5f);

        if (_symbolName is not null)
        {
            using var name = new CanvasTextLayout(session, spec.Label, _symbolName, Math.Max(10f, w - 34f), h);
            session.DrawTextLayout(name, new Vector2(-w / 2f + 34f, -h / 2f), Argb(palette.Ink, 1f));
        }
    }

    /// <summary>The drawn sheet, 96 × 124 around the origin (worksheet "Geister und Blatt", row <c>Sheet</c>).</summary>
    private void DrawSheet(CanvasDrawingSession session, GhostSpec spec, ScenePalette palette)
    {
        const float w = SheetRaster.Width;
        const float h = SheetRaster.Height;
        var left = -w / 2f;
        var top = -h / 2f;
        var colour = palette.For(spec.Kind);

        // Shadow as an offset rectangle, no drop-shadow effect.
        session.FillRoundedRectangle(new Rect(left, top + 12f + 6f, w, h - 12f), 6f, 6f, Argb(palette.Shadow, 0.35f));
        // Tab, then paper over its lower half.
        session.FillRoundedRectangle(new Rect(left + 6f, top, 42f, 18f), 5f, 5f, Argb(colour, 1f));
        if (_sheetTab is not null && spec.FormatLabel is { Length: > 0 })
        {
            using var tab = new CanvasTextLayout(session, spec.FormatLabel, _sheetTab, 42f, 12f);
            session.DrawTextLayout(tab, new Vector2(left + 6f, top), TabInk);
        }

        session.FillRoundedRectangle(new Rect(left, top + 12f, w, h - 12f), 6f, 6f, Argb(palette.Paper, 1f));
        DrawSheetContent(session, spec.Kind, new Rect(left + 8f, top + 22f, 80f, 52f), colour, palette);

        if (_sheetLabel is not null && spec.FormatLabel is { Length: > 0 })
        {
            using var label = new CanvasTextLayout(session, spec.FormatLabel, _sheetLabel, w - 16f, 22f);
            session.DrawTextLayout(label, new Vector2(left + 8f, top + 92f), LabelInk);
        }

        if (_sheetName is not null && spec.FileName is { Length: > 0 })
        {
            using var name = new CanvasTextLayout(session, spec.FileName, _sheetName, w - 16f, 10f);
            session.DrawTextLayout(name, new Vector2(left + 8f, top + 113f), Argb(palette.Muted, 1f));
        }
    }

    /// <summary>The content field of a sheet per kind, the fixed design colours of <c>SheetRaster</c> in flat form.</summary>
    private static void DrawSheetContent(CanvasDrawingSession session, Engine.Abstractions.MediaKind kind, Rect field, SceneColor colour, ScenePalette palette)
    {
        var x = (float)field.X;
        var y = (float)field.Y;
        var w = (float)field.Width;
        var h = (float)field.Height;
        switch (kind)
        {
            case Engine.Abstractions.MediaKind.Image:
                session.FillRoundedRectangle(field, 3f, 3f, Rgb(0x9CC3EE));
                session.FillRectangle(x, y + h * 0.62f, w, h * 0.38f, Rgb(0xD9C9A4));
                session.FillCircle(new Vector2(x + w - 16f, y + 12f), 6f, Rgb(0xF2C46A));
                session.FillRectangle(x, y + h * 0.5f, w, h * 0.14f, Rgb(0x5F7C9C));
                break;
            case Engine.Abstractions.MediaKind.Audio:
                session.FillRoundedRectangle(field, 3f, 3f, Rgb(0xF1ECFB));
                for (var i = 0; i < 20; i++)
                {
                    var bar = (8f + 36f * (0.5f + 0.5f * MathF.Sin(i * 1.7f))) * 0.9f;
                    session.FillRectangle(x + 2f + i * 3.8f, y + h - 2f - bar, 2.4f, bar, Rgb(0x7B61C4));
                }

                break;
            case Engine.Abstractions.MediaKind.Video:
                session.FillRoundedRectangle(field, 3f, 3f, Rgb(0x1D2226));
                using (var builder = new Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder(session))
                {
                    var cx = x + w / 2f;
                    var cy = y + h / 2f;
                    builder.BeginFigure(cx - 7f, cy - 9f);
                    builder.AddLine(cx + 9f, cy);
                    builder.AddLine(cx - 7f, cy + 9f);
                    builder.EndFigure(Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Closed);
                    using var triangle = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(builder);
                    session.FillGeometry(triangle, Rgb(0xF2B45A));
                }

                break;
            case Engine.Abstractions.MediaKind.Document:
                session.FillRoundedRectangle(field, 3f, 3f, Rgb(0xFFFFFF));
                session.FillRectangle(x + 6f, y + 6f, w - 12f, 5f, Argb(colour, 1f));
                for (var i = 0; i < 6; i++)
                {
                    session.FillRectangle(x + 6f, y + 16f + i * 6f, w - 12f - (i % 3) * 8f, 2f, Rgb(0xC5CCD1));
                }

                break;
            case Engine.Abstractions.MediaKind.Model3D:
                session.FillRoundedRectangle(field, 3f, 3f, Rgb(0xF7EEF4));
                using (var builder = new Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder(session))
                {
                    var cx = x + w / 2f;
                    var cy = y + h / 2f;
                    for (var i = 0; i < 6; i++)
                    {
                        var angle = i * MathF.PI / 3f - MathF.PI / 6f;
                        var p = new Vector2(cx + MathF.Cos(angle) * 14f, cy + MathF.Sin(angle) * 14f);
                        if (i == 0)
                        {
                            builder.BeginFigure(p);
                        }
                        else
                        {
                            builder.AddLine(p);
                        }
                    }

                    builder.EndFigure(Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Closed);
                    using var hexagon = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(builder);
                    session.FillGeometry(hexagon, Rgb(0x9A2F7D));
                }

                break;
            default:
                session.FillRoundedRectangle(field, 3f, 3f, Argb(palette.PaperLine, 1f));
                break;
        }
    }

    // ----- the hole ----------------------------------------------------------------------------------

    /// <summary>Same hole as in the galaxy (<c>lochMalen</c>): violet and mint halo, dark disc, mint ring; faded as a whole.</summary>
    private void DrawHole(CanvasDrawingSession session, in HoleState hole, ScenePalette palette)
    {
        if (hole.Radius <= 0.3f || hole.Alpha <= 0.01f)
        {
            return;
        }

        using (session.CreateLayer(Math.Clamp(hole.Alpha, 0f, 1f)))
        {
            Glow(session, hole.Position, hole.Radius * 3.2f, palette.Audio, 0.18f);
            Glow(session, hole.Position, hole.Radius * 1.9f, palette.Mint, 0.22f);
            session.FillCircle(hole.Position, hole.Radius, palette.IsDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 11, 15, 18));
            // The design blurs the ring by 12 px; a wider faint ring gives the same impression without an effect.
            session.DrawCircle(hole.Position, hole.Radius + 1.5f, Argb(palette.Mint, 0.22f), 8f);
            session.DrawCircle(hole.Position, hole.Radius + 1.5f, Argb(palette.Mint, 0.95f), 2f);
        }
    }

    // ----- primitives --------------------------------------------------------------------------------

    private void Glow(CanvasDrawingSession session, Vector2 centre, float radius, SceneColor colour, float alpha)
    {
        if (alpha <= 0.005f || radius <= 0f || _creator is null)
        {
            return;
        }

        var key = (colour.R << 16) | (colour.G << 8) | colour.B;
        if (!_glows.TryGetValue(key, out var brush))
        {
            brush = new CanvasRadialGradientBrush(
                _creator,
                Color.FromArgb(255, colour.R, colour.G, colour.B),
                Color.FromArgb(0, colour.R, colour.G, colour.B));
            _glows[key] = brush;
        }

        brush.Center = centre;
        brush.RadiusX = radius;
        brush.RadiusY = radius;
        brush.Opacity = Math.Clamp(alpha, 0f, 1f);
        session.FillCircle(centre, radius, brush);
    }

    private static CanvasTextFormat Format(string family, float size, ushort weight, CanvasHorizontalAlignment horizontal) => new()
    {
        FontFamily = family,
        FontSize = size,
        FontWeight = new FontWeight(weight),
        HorizontalAlignment = horizontal,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private static Color Argb(SceneColor colour, float alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0f, 1f) * 255f), colour.R, colour.G, colour.B);

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
