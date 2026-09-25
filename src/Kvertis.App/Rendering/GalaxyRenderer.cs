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
/// Draws a <see cref="GalaxyScene"/> with Win2D (ADR-022). The renderer owns no state of its own beyond
/// cached brushes and text widths; everything it paints comes from the scene it is handed.
/// </summary>
/// <remarks>
/// Two rules of the licence review hold here without exception: no bitmap is ever created from a user's file
/// (<c>CanvasBitmap</c> and <c>CanvasImageSource</c> do not appear in this folder at all; thumbnails stay XAML
/// <c>Image</c> elements in the trays), and every glow is a <see cref="CanvasRadialGradientBrush"/> rather than
/// a blur effect, so no effect graph is built per frame.
/// </remarks>
public sealed class GalaxyRenderer : IDisposable
{
    /// <summary>Segments of one orbit polyline (worksheet: 110).</summary>
    private const int OrbitSegments = 110;

    /// <summary>Segments of one way through the hole.</summary>
    private const int PathSegments = 40;

    /// <summary>Travelling dots on one way.</summary>
    private const int PathDots = 7;

    /// <summary>How long the arrival chip stays, in seconds.</summary>
    private const float ChipSeconds = 2.8f;

    private readonly Dictionary<int, CanvasRadialGradientBrush> _glows = [];
    private readonly Dictionary<string, float> _textWidths = new(StringComparer.Ordinal);

    private ICanvasResourceCreator? _creator;
    private CanvasTextFormat? _chipFormat;
    private CanvasTextFormat? _subFormat;

    /// <summary>
    /// Called from <c>CreateResources</c>: everything built for the old device is dropped first, so a device
    /// loss or a theme change simply rebuilds the brushes.
    /// </summary>
    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ReleaseBrushes();
        _creator = creator;
        _chipFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display",
            FontSize = 9.5f,
            FontWeight = new FontWeight(600),
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        _subFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Text",
            FontSize = 10f,
            FontWeight = new FontWeight(500),
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
    }

    /// <summary>
    /// Drops the cached brushes, text formats and measured widths but keeps the device: a theme change needs
    /// new colours, not a new device. The caller has to run <see cref="CreateResources"/> again right after,
    /// otherwise nothing is drawn.
    /// </summary>
    public void ReleaseBrushes()
    {
        foreach (var brush in _glows.Values)
        {
            brush.Dispose();
        }

        _glows.Clear();
        _textWidths.Clear();
        _chipFormat?.Dispose();
        _chipFormat = null;
        _subFormat?.Dispose();
        _subFormat = null;
    }

    /// <summary>Drops everything including the device; the renderer draws nothing after this.</summary>
    public void Dispose()
    {
        ReleaseBrushes();
        _creator = null;
    }

    /// <summary>
    /// Paints one frame. Order as in the draft: orbits, particles, approaching files with trail and chip,
    /// arrival chips, the ways through the hole, then the hole itself on top.
    /// </summary>
    public void Draw(CanvasDrawingSession session, GalaxyScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        if (_creator is null)
        {
            return;
        }

        var palette = scene.Palette;
        DrawOrbits(session, scene, palette);
        DrawParticles(session, scene, palette);
        DrawApproaches(session, scene, palette);
        DrawArrivalChips(session, scene, palette);
        DrawPaths(session, scene, palette);
        DrawHole(session, scene, palette);
    }

    // ----- orbits ------------------------------------------------------------------------------------

    private static void DrawOrbits(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var zoomOrbit = scene.ZoomKind is { } kind ? GalaxyLayout.OrbitOf(kind) : -1;
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            var hover = scene.OrbitHover[k];
            var zoomed = k == zoomOrbit ? scene.ZoomProgress : 0f;
            var alpha = (0.16f + 0.3f * zoomed + 0.3f * hover + 0.15f * scene.DragOver) * scene.OrbitAlpha[k];
            if (alpha < 0.01f || scene.OrbitRadiusX[k] > scene.Layout.Width * 2f)
            {
                continue;
            }

            using var path = BuildOrbit(session, scene, k);
            var colour = palette.For(GalaxyLayout.KindOf(k));
            var width = 1f + 0.8f * zoomed + 0.8f * hover;

            // The draft used a shadow blur here. A blur effect per frame is forbidden (ADR-022), so the halo is
            // a second, much wider and fainter stroke of the same path: same impression, no effect graph.
            var halo = 10f * Math.Max(zoomed, hover);
            if (halo > 0.2f)
            {
                session.DrawGeometry(path, Argb(colour, alpha * 0.25f), width + halo);
            }

            session.DrawGeometry(path, Argb(colour, alpha), width);
        }
    }

    private static CanvasGeometry BuildOrbit(ICanvasResourceCreator creator, GalaxyScene scene, int orbit)
    {
        var builder = new CanvasPathBuilder(creator);
        for (var j = 0; j <= OrbitSegments; j++)
        {
            var point = scene.OrbitPoint(orbit, j / (float)OrbitSegments * MathF.Tau);
            if (j == 0)
            {
                builder.BeginFigure(point);
            }
            else
            {
                builder.AddLine(point);
            }
        }

        builder.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(builder);
    }

    // ----- particles ---------------------------------------------------------------------------------

    private static void DrawParticles(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var previous = session.Blend;
        // In the dark theme the particles add up to a glow, in the light theme they would wash the surface out.
        session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        var particles = scene.Particles;
        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var particle = ref particles[i];
            if (particle.Alpha < 0.01f || particle.Size <= 0f)
            {
                continue;
            }

            var half = particle.Size / 2f;
            session.FillRectangle(
                particle.Position.X - half,
                particle.Position.Y - half,
                particle.Size,
                particle.Size,
                Argb(particle.Color, particle.Alpha));
        }

        session.Blend = previous;
    }

    // ----- approaching files -------------------------------------------------------------------------

    private void DrawApproaches(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        foreach (var body in scene.Bodies)
        {
            if (body.Phase != BodyPhase.Approaching)
            {
                continue;
            }

            var colour = body.IsRejected ? palette.Error : palette.For(body.Kind);
            DrawTrail(session, body, colour, palette.IsDark);
            Glow(session, body.Position, 16f * scene.Layout.Scale, colour, 0.5f);
            DrawChip(session, body.Position - new Vector2(0f, 14f * scene.Layout.Scale), body.FormatLabel, null, colour, palette, 1f, 0.9f);
        }
    }

    private static void DrawTrail(CanvasDrawingSession session, GalaxyBody body, SceneColor colour, bool isDark)
    {
        var trail = body.Trail;
        if (trail.Count == 0)
        {
            return;
        }

        var previous = session.Blend;
        session.Blend = isDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        for (var i = 0; i < trail.Count; i++)
        {
            var share = (i + 1) / (float)trail.Count;
            session.FillCircle(trail[i], 1f + share * 3f, Argb(colour, share * 0.5f));
        }

        session.Blend = previous;
    }

    private void DrawArrivalChips(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        foreach (var body in scene.Bodies)
        {
            if (body.Phase is not (BodyPhase.Orbiting or BodyPhase.Rejected) || body.ArrivedAt <= TimeSpan.Zero)
            {
                continue;
            }

            var since = (float)(scene.Time - body.ArrivedAt).TotalSeconds;
            if (since < 0f || since > ChipSeconds)
            {
                continue;
            }

            var orbit = body.Orbit;
            if (orbit >= 0 && scene.OrbitAlpha[orbit] < 0.3f)
            {
                continue;
            }

            // 0.3 s in, 0.5 s out (worksheet "Ankunft").
            var alpha = Math.Clamp(since / 0.3f, 0f, 1f) * Math.Clamp((ChipSeconds - since) / 0.5f, 0f, 1f);
            var colour = body.IsRejected ? palette.Error : palette.For(body.Kind);
            DrawChip(
                session,
                body.Position - new Vector2(0f, 20f * scene.Layout.Scale),
                body.FormatLabel,
                body.Name,
                colour,
                palette,
                alpha,
                1f);
        }
    }

    // ----- ways through the hole ---------------------------------------------------------------------

    private void DrawPaths(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        if (scene.Paths.Count == 0)
        {
            return;
        }

        var visible = Math.Clamp((scene.ZoomProgress - 0.6f) / 0.4f, 0f, 1f);
        if (visible < 0.02f)
        {
            return;
        }

        var kindColour = scene.ZoomKind is { } kind ? palette.For(kind) : palette.Mint;
        var seconds = (float)scene.Time.TotalSeconds;
        foreach (var path in scene.Paths)
        {
            var colour = path.IsRecommended ? palette.Mint : kindColour;
            var alpha = (path.IsRecommended ? 0.55f : 0.25f) * visible;
            using var geometry = BuildPath(session, path);
            session.DrawGeometry(geometry, Argb(colour, alpha), path.IsRecommended ? 2f : 1f);

            var previous = session.Blend;
            session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
            for (var i = 0; i < PathDots; i++)
            {
                var t = (seconds * 0.45f + i / (float)PathDots) % 1f;
                // After 40 % of the way a dot takes on the mint of the target side.
                var mix = Math.Clamp((t - 0.4f) / 0.2f, 0f, 1f);
                session.FillCircle(
                    path.PointAt(t),
                    path.IsRecommended ? 2.6f : 2f,
                    Argb(Mix(colour, palette.Mint, mix), 0.9f * visible));
            }

            session.Blend = previous;
        }
    }

    private static CanvasGeometry BuildPath(ICanvasResourceCreator creator, GalaxyPath path)
    {
        var builder = new CanvasPathBuilder(creator);
        builder.BeginFigure(path.PointAt(0f));
        for (var i = 1; i <= PathSegments; i++)
        {
            builder.AddLine(path.PointAt(i / (float)PathSegments));
        }

        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    // ----- the hole ----------------------------------------------------------------------------------

    private void DrawHole(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var centre = scene.Layout.Center;
        var radius = scene.HoleRadius;

        // Dragging files over the window pulls a wide mint halo out of the hole.
        if (scene.DragOver > 0.01f)
        {
            Glow(session, centre, 160f * scene.Layout.Scale * scene.DragOver, palette.Mint, 0.12f * scene.DragOver);
        }

        Glow(session, centre, radius * 3.2f, palette.Audio, 0.18f);
        Glow(session, centre, radius * 1.9f, palette.Mint, 0.22f);

        // The disc is black in the dark theme and near black in the light one, never the background colour:
        // the hole has to read as a hole on both.
        session.FillCircle(centre, radius, palette.IsDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 11, 15, 18));

        // Draft: a 12 px shadow blur on the ring. Same trick as the orbits, a wider faint ring instead.
        session.DrawCircle(centre, radius + 1.5f, Argb(palette.Mint, 0.22f), 8f);
        session.DrawCircle(centre, radius + 1.5f, Argb(palette.Mint, 0.95f), 2f);
    }

    // ----- primitives --------------------------------------------------------------------------------

    private void Glow(CanvasDrawingSession session, Vector2 centre, float radius, SceneColor colour, float alpha)
    {
        if (alpha <= 0.005f || radius <= 0f)
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
        session.FillCircle(centre, radius, brush);
    }

    /// <summary>One radial gradient per colour, cached for the lifetime of the device.</summary>
    private CanvasRadialGradientBrush? GlowBrush(SceneColor colour)
    {
        if (_creator is null)
        {
            return null;
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

        return brush;
    }

    /// <summary>
    /// A format badge: rounded rectangle in the kind colour with the format above it and, after arrival, the
    /// file name below. The only text the renderer ever draws; the trays are XAML.
    /// </summary>
    private void DrawChip(
        CanvasDrawingSession session,
        Vector2 centre,
        string text,
        string? subtitle,
        SceneColor colour,
        ScenePalette palette,
        float alpha,
        float scale)
    {
        if (alpha <= 0.02f || string.IsNullOrEmpty(text) || _chipFormat is null)
        {
            return;
        }

        var width = MeasureChip(session, text) * scale + 10f * scale;
        var height = 15f * scale;
        var box = new Windows.Foundation.Rect(centre.X - width / 2f, centre.Y - height / 2f, width, height);
        var radius = 4f * scale;

        session.FillRoundedRectangle(box, radius, radius, Argb(palette.Background, 0.9f * alpha));
        session.FillRoundedRectangle(box, radius, radius, Argb(colour, 0.14f * alpha));
        session.DrawRoundedRectangle(box, radius, radius, Argb(colour, 0.7f * alpha), 1f);

        using (var layout = new CanvasTextLayout(session, text, _chipFormat, width, height))
        {
            session.DrawTextLayout(layout, new Vector2((float)box.X, (float)box.Y), Argb(colour, alpha));
        }

        if (subtitle is { Length: > 0 } && _subFormat is not null)
        {
            using var sub = new CanvasTextLayout(session, subtitle, _subFormat, 260f, 16f);
            session.DrawTextLayout(
                sub,
                new Vector2(centre.X - 130f, centre.Y + height * 0.5f + 2f * scale),
                Argb(palette.Muted, alpha));
        }
    }

    private float MeasureChip(ICanvasResourceCreator creator, string text)
    {
        if (_textWidths.TryGetValue(text, out var width))
        {
            return width;
        }

        using var layout = new CanvasTextLayout(creator, text, _chipFormat, 400f, 40f);
        width = (float)layout.LayoutBounds.Width;
        _textWidths[text] = width;
        return width;
    }

    private static Color Argb(SceneColor colour, float alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0f, 1f) * 255f), colour.R, colour.G, colour.B);

    private static SceneColor Mix(SceneColor from, SceneColor to, float t) => new(
        (byte)float.Lerp(from.R, to.R, t),
        (byte)float.Lerp(from.G, to.G, t),
        (byte)float.Lerp(from.B, to.B, t));
}
