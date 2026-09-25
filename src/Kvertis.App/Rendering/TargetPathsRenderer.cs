using System.Numerics;
using Kvertis.App.Scenes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Kvertis.App.Rendering;

/// <summary>
/// Draws a <see cref="TargetPathsScene"/> with Win2D (ADR-022): orbit, dust, the faint lines to unused
/// targets, the ways through the hole, the hole and the planets, in the order of the draft's
/// <c>zoomZeichnen()</c>. No text, no bitmaps; glows and the planet shading are radial gradients, never
/// blur effects.
/// </summary>
public sealed class TargetPathsRenderer : IDisposable
{
    /// <summary>Segments per Bézier half when a way is flattened for the stroke.</summary>
    private const int PathSegments = 28;

    private readonly Dictionary<int, CanvasRadialGradientBrush> _glows = [];
    private readonly Dictionary<int, CanvasRadialGradientBrush> _spheres = [];

    private ICanvasResourceCreator? _creator;
    private CanvasStrokeStyle? _dashed;

    /// <summary>Called from <c>CreateResources</c>; everything for the old device is dropped first.</summary>
    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ReleaseBrushes();
        _creator = creator;
        _dashed = new CanvasStrokeStyle { CustomDashStyle = [5f, 4f] };
    }

    /// <summary>Drops the cached brushes but keeps the device; the caller runs <see cref="CreateResources"/> again right after.</summary>
    public void ReleaseBrushes()
    {
        foreach (var brush in _glows.Values)
        {
            brush.Dispose();
        }

        foreach (var brush in _spheres.Values)
        {
            brush.Dispose();
        }

        _glows.Clear();
        _spheres.Clear();
        _dashed?.Dispose();
        _dashed = null;
    }

    public void Dispose()
    {
        ReleaseBrushes();
        _creator = null;
    }

    public void Draw(CanvasDrawingSession session, TargetPathsScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        if (_creator is null)
        {
            return;
        }

        var palette = scene.Palette;
        var z = scene.Reveal;
        if (scene.Kind is not null && z > 0.001f)
        {
            DrawOrbit(session, scene, palette, z);
            DrawDust(session, scene, palette, z);
            DrawIdleTargets(session, scene, palette, z);
            DrawPaths(session, scene, palette, z);
        }

        DrawHole(session, scene, palette);

        if (scene.Kind is not null && z > 0.001f)
        {
            DrawPlanets(session, scene, z);
        }
    }

    // ----- orbit and dust ----------------------------------------------------------------------------

    private static void DrawOrbit(CanvasDrawingSession session, TargetPathsScene scene, ScenePalette palette, float z)
    {
        var colour = scene.KindColor;
        session.DrawEllipse(scene.Hole, scene.OrbitRadiusX, scene.OrbitRadiusY, Argb(colour, 0.4f * z), 1.3f);
    }

    private static void DrawDust(CanvasDrawingSession session, TargetPathsScene scene, ScenePalette palette, float z)
    {
        var previous = session.Blend;
        session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        var colour = Argb(scene.KindColor, 0.5f * z);
        for (var i = 0; i < TargetPathsScene.DustCount; i++)
        {
            var point = scene.DustPoint(i);
            session.FillRectangle(point.X - 1f, point.Y - 1f, 2f, 2f, colour);
        }

        session.Blend = previous;
    }

    // ----- ways --------------------------------------------------------------------------------------

    private void DrawIdleTargets(CanvasDrawingSession session, TargetPathsScene scene, ScenePalette palette, float z)
    {
        var hole = scene.Hole;
        foreach (var target in scene.IdleTargets)
        {
            using var geometry = HalfPath(session, hole, new Vector2(hole.X + TargetPath.ControlOffset, hole.Y), new Vector2(target.X - TargetPath.ControlOffset, target.Y), target);
            session.DrawGeometry(geometry, Argb(palette.LineStrong, 0.22f * z), 1f);
        }
    }

    private void DrawPaths(CanvasDrawingSession session, TargetPathsScene scene, ScenePalette palette, float z)
    {
        var hole = scene.Hole;
        var kindColour = scene.KindColor;
        var seconds = (float)scene.Time.TotalSeconds;
        var activity = scene.Activity;
        for (var j = 0; j < scene.Paths.Count; j++)
        {
            var path = scene.Paths[j];
            // First half: file card into the hole, in the kind colour.
            using (var into = HalfPath(session, path.From, new Vector2(path.From.X + TargetPath.ControlOffset, path.From.Y), new Vector2(hole.X - TargetPath.ControlOffset, hole.Y), hole))
            {
                session.DrawGeometry(into, Argb(kindColour, 0.55f * z), 1.4f);
            }

            if (!path.HasTarget)
            {
                continue;
            }

            // Second half: hole to the target, mint; amber and dashed for a file with its own choice.
            var outColour = path.IsOwnChoice ? palette.Video : palette.Mint;
            using (var outOf = HalfPath(session, hole, new Vector2(hole.X + TargetPath.ControlOffset, hole.Y), new Vector2(path.To.X - TargetPath.ControlOffset, path.To.Y), path.To))
            {
                if (path.IsOwnChoice && _dashed is not null)
                {
                    session.DrawGeometry(outOf, Argb(outColour, 0.8f * z), 2f, _dashed);
                }
                else
                {
                    session.DrawGeometry(outOf, Argb(outColour, 0.8f * z), 2f);
                }
            }

            if (activity <= 0.01f)
            {
                continue;
            }

            // Dots run for a moment after a change (draft: T * .55, alpha .9 * sin(π t)); never forever.
            var previous = session.Blend;
            session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
            var shift = seconds + j * 0.2f;
            DrawDots(session, path, hole, 5, shift, 0f, kindColour, 1.7f, z * activity);
            DrawDots(session, path, hole, 6, shift, 0.5f, outColour, 2.3f, z * activity);
            session.Blend = previous;
        }
    }

    private static void DrawDots(CanvasDrawingSession session, TargetPath path, Vector2 hole, int count, float shift, float halfStart, SceneColor colour, float radius, float alpha)
    {
        for (var i = 0; i < count; i++)
        {
            var t = (shift * 0.55f + i / (float)count) % 1f;
            var point = path.PointAt(hole, halfStart + t * 0.5f);
            session.FillCircle(point, radius, Argb(colour, 0.9f * MathF.Sin(MathF.PI * t) * alpha));
        }
    }

    private static CanvasGeometry HalfPath(ICanvasResourceCreator creator, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var builder = new CanvasPathBuilder(creator);
        builder.BeginFigure(a);
        for (var i = 1; i <= PathSegments; i++)
        {
            builder.AddLine(TargetPath.Cubic(a, b, c, d, i / (float)PathSegments));
        }

        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    // ----- hole and planets --------------------------------------------------------------------------

    private void DrawHole(CanvasDrawingSession session, TargetPathsScene scene, ScenePalette palette)
    {
        var centre = scene.Hole;
        var radius = scene.HoleRadius;
        Glow(session, centre, radius * 3.4f, palette.Audio, 0.22f);
        Glow(session, centre, radius * 2f, palette.Mint, 0.25f);
        session.FillCircle(centre, radius, palette.IsDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 11, 15, 18));
        session.DrawCircle(centre, radius + 1.2f, Argb(palette.Mint, 0.95f), 1.6f);
    }

    private void DrawPlanets(CanvasDrawingSession session, TargetPathsScene scene, float z)
    {
        var colour = scene.KindColor;
        foreach (var planet in scene.Planets)
        {
            if (planet.Radius < 0.5f)
            {
                continue;
            }

            var brush = SphereBrush(colour);
            if (brush is null)
            {
                continue;
            }

            // Draft kugel(): light spot at (-.35 r, -.4 r), the colour at 55 %, the colour at 35 % on the rim.
            brush.Center = planet.Position;
            brush.RadiusX = planet.Radius;
            brush.RadiusY = planet.Radius;
            brush.OriginOffset = new Vector2(-planet.Radius * 0.35f, -planet.Radius * 0.4f);
            brush.Opacity = Math.Clamp(z, 0f, 1f);
            session.FillCircle(planet.Position, planet.Radius, brush);
        }
    }

    // ----- primitives --------------------------------------------------------------------------------

    private void Glow(CanvasDrawingSession session, Vector2 centre, float radius, SceneColor colour, float alpha)
    {
        if (alpha <= 0.005f || radius <= 0f || GlowBrush(colour) is not { } brush)
        {
            return;
        }

        brush.Center = centre;
        brush.RadiusX = radius;
        brush.RadiusY = radius;
        brush.Opacity = Math.Clamp(alpha, 0f, 1f);
        session.FillCircle(centre, radius, brush);
    }

    private CanvasRadialGradientBrush? GlowBrush(SceneColor colour)
    {
        if (_creator is null)
        {
            return null;
        }

        var key = Key(colour);
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

    private CanvasRadialGradientBrush? SphereBrush(SceneColor colour)
    {
        if (_creator is null)
        {
            return null;
        }

        var key = Key(colour);
        if (!_spheres.TryGetValue(key, out var brush))
        {
            var light = Color.FromArgb(255, Lift(colour.R), Lift(colour.G), Lift(colour.B));
            var body = Color.FromArgb(255, colour.R, colour.G, colour.B);
            var rim = Color.FromArgb(255, (byte)(colour.R * 0.35f), (byte)(colour.G * 0.35f), (byte)(colour.B * 0.35f));
            brush = new CanvasRadialGradientBrush(
                _creator,
                [
                    new CanvasGradientStop { Position = 0f, Color = light },
                    new CanvasGradientStop { Position = 0.55f, Color = body },
                    new CanvasGradientStop { Position = 1f, Color = rim },
                ]);
            _spheres[key] = brush;
        }

        return brush;
    }

    private static byte Lift(byte channel) => (byte)Math.Min(255, channel + 70);

    private static int Key(SceneColor colour) => (colour.R << 16) | (colour.G << 8) | colour.B;

    private static Color Argb(SceneColor colour, float alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0f, 1f) * 255f), colour.R, colour.G, colour.B);
}
