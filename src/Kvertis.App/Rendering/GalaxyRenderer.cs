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

    private static readonly SceneColor White = new(255, 255, 255);

    private readonly Dictionary<int, CanvasRadialGradientBrush> _glows = [];
    private readonly Dictionary<string, float> _textWidths = new(StringComparer.Ordinal);

    private ICanvasResourceCreator? _creator;
    private CanvasTextFormat? _chipFormat;
    private CanvasTextFormat? _subFormat;
    private CanvasRadialGradientBrush? _planetShade;
    private CanvasRadialGradientBrush? _moonShade;

    /// <summary>
    /// Called from <c>CreateResources</c>: everything built for the old device is dropped first, so a device
    /// loss or a theme change simply rebuilds the brushes.
    /// </summary>
    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ReleaseBrushes();
        _creator = creator;
        // Draft: sphere shading white .4 at the light spot, clear at 45 %, black .72 at the rim; moon #f2f2f2 to #55585c.
        _planetShade = new CanvasRadialGradientBrush(
            creator,
            [
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(102, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(0, 0, 0, 0) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(184, 0, 0, 0) },
            ]);
        _moonShade = new CanvasRadialGradientBrush(
            creator,
            [
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(255, 242, 242, 242) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(255, 85, 88, 92) },
            ]);
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
        _planetShade?.Dispose();
        _planetShade = null;
        _moonShade?.Dispose();
        _moonShade = null;
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
        DrawHoleGlows(session, scene, palette);
        DrawWhirl(session, scene, palette);
        DrawPlanets(session, scene);
        DrawBursts(session, scene);
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
            var alpha = (0.16f + 0.3f * zoomed + 0.3f * hover + 0.15f * scene.DragOver) * scene.OrbitAlpha[k] * scene.GimmickOrbitAlpha;
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
            var alpha = Math.Clamp(since / 0.3f, 0f, 1f) * Math.Clamp((ChipSeconds - since) / 0.5f, 0f, 1f) * scene.GimmickOrbitAlpha;
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

    /// <summary>The glows that lie under everything at the hole: the flash of an impact and the drag halo.</summary>
    private void DrawHoleGlows(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var centre = scene.Layout.Center;
        var scale = scene.Layout.Scale;

        if (ImpactFlashShare(scene, out var q))
        {
            Glow(session, centre, (40f + q * 90f) * scale, scene.HoleFlash.Color, 0.7f * (1f - q));
        }

        // Dragging files over the window pulls a wide mint halo out of the hole.
        if (scene.DragOver > 0.01f)
        {
            Glow(session, centre, 160f * scale * scene.DragOver, palette.Mint, 0.12f * scene.DragOver);
        }
    }

    /// <summary>
    /// The hole in its four looks: quiet, charging (larger, shaking, glowing), exploding (white flash, mint
    /// and violet rings) and being born again (a bright point that grows into the hole).
    /// </summary>
    private void DrawHole(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var centre = scene.Layout.Center;
        var scale = scene.Layout.Scale;
        var seconds = (float)scene.Time.TotalSeconds;
        var radius = scene.HoleRadius;

        if (ImpactFlashShare(scene, out var flash))
        {
            radius *= 1f + 0.25f * MathF.Sin(MathF.PI * flash);
        }

        switch (scene.Gimmick)
        {
            case GimmickState.BigBang:
            {
                var q = Math.Clamp(scene.BangElapsed / GalaxyScene.ExplosionSeconds, 0f, 1f);
                Glow(session, centre, (60f + q * 700f) * scale, White, 0.95f * (1f - q) * (1f - q));
                Glow(session, centre, (40f + q * 420f) * scale, palette.Mint, 0.6f * (1f - q));
                ReadOnlySpan<float> starts = [0f, 0.18f, 0.36f];
                for (var i = 0; i < starts.Length; i++)
                {
                    var s = Math.Clamp((q - starts[i]) / (1f - starts[i]), 0f, 1f);
                    if (s <= 0f)
                    {
                        continue;
                    }

                    var rx = (10f + s * 900f) * scale;
                    session.DrawEllipse(centre, rx, rx * 0.42f, Argb(i == 1 ? palette.Audio : palette.Mint, (1f - s) * 0.8f), 3f * (1f - s) + 0.5f);
                }

                return;
            }

            case GimmickState.NewUniverse:
            {
                var q = scene.BangElapsed;
                if (q < 0.5f)
                {
                    var s = q / 0.5f;
                    Glow(session, centre, (6f + 26f * s) * scale, White, 0.9f * s);
                    return;
                }

                var t = Math.Clamp((q - 0.5f) / 1.2f, 0f, 1f);
                Glow(session, centre, (30f + 40f * (1f - t)) * scale, White, 0.5f * (1f - t));
                DrawHoleDisc(session, centre, radius * Math.Max(0.05f, GalaxyMotion.EaseBack(t)), palette);
                return;
            }
        }

        var charge = scene.HoleCharge;
        if (charge <= 0.005f)
        {
            DrawHoleDisc(session, centre, radius, palette);
            return;
        }

        // The shake grows steadily: hardly felt at first, violent at the end.
        var shaken = centre + scene.Shake;
        var grown = radius * (1f + 0.9f * charge) * (1f + 0.06f * charge * charge * MathF.Sin(seconds * (8f + 40f * charge)));
        Glow(session, shaken, (60f + 200f * charge) * scale, palette.Audio, 0.3f * charge);
        Glow(session, shaken, (30f + 90f * charge) * scale, palette.Mint, 0.35f * charge * (0.75f + 0.25f * MathF.Sin(seconds * (6f + 20f * charge))));
        DrawHoleDisc(session, shaken, grown, palette);
    }

    /// <summary>0..1 through the 0.6 s flash after an impact; false when there is none. Never subtract from a null "never".</summary>
    private static bool ImpactFlashShare(GalaxyScene scene, out float share)
    {
        share = 0f;
        if (scene.HoleFlash.At is not { } at || at > scene.Time)
        {
            return false;
        }

        var since = (float)(scene.Time - at).TotalSeconds;
        if (since >= GalaxyScene.ImpactFlashSeconds)
        {
            return false;
        }

        share = since / GalaxyScene.ImpactFlashSeconds;
        return true;
    }

    private void DrawHoleDisc(CanvasDrawingSession session, Vector2 centre, float radius, ScenePalette palette)
    {
        Glow(session, centre, radius * 3.2f, palette.Audio, 0.18f);
        Glow(session, centre, radius * 1.9f, palette.Mint, 0.22f);

        // The disc is black in the dark theme and near black in the light one, never the background colour:
        // the hole has to read as a hole on both.
        session.FillCircle(centre, radius, palette.IsDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 11, 15, 18));

        // Draft: a 12 px shadow blur on the ring. Same trick as the orbits, a wider faint ring instead.
        session.DrawCircle(centre, radius + 1.5f, Argb(palette.Mint, 0.22f), 8f);
        session.DrawCircle(centre, radius + 1.5f, Argb(palette.Mint, 0.95f), 2f);
    }

    // ----- gimmicks: whirl, planets, bursts ----------------------------------------------------------

    /// <summary>Draft planetZeichnen, first part: a turning disc in the plane of the orbits whose core sinks in.</summary>
    private void DrawWhirl(CanvasDrawingSession session, GalaxyScene scene, ScenePalette palette)
    {
        var charge = scene.WhirlCharge;
        if (charge <= 0.01f)
        {
            return;
        }

        var scale = scene.Layout.Scale;
        var colour = palette.For(GalaxyLayout.KindOf(scene.WhirlOrbit));
        var centre = scene.WhirlPosition;
        var reach = (75f + 70f * charge) * scale;
        var coreY = centre.Y + 12f * charge * charge * scale;
        Glow(session, new Vector2(centre.X, coreY), (10f + 34f * charge) * scale, colour, 0.5f * charge);

        ReadOnlySpan<float> shares = [0.85f, 0.6f, 0.38f];
        for (var i = 0; i < shares.Length; i++)
        {
            var q = shares[i];
            var rr = reach * q * (1f - 0.45f * charge * (1f - q));
            var width = 1f + charge * (1.5f - i * 0.3f);
            // Win2D measures dashes in stroke widths, the draft in pixels.
            using var dashes = new CanvasStrokeStyle
            {
                CustomDashStyle = [rr * 0.5f / width, rr * 0.35f / width],
                DashOffset = -scene.WhirlSpin * rr * (0.4f + i * 0.3f) / width,
                DashCap = CanvasCapStyle.Round,
            };
            session.DrawEllipse(
                new Vector2(centre.X, float.Lerp(centre.Y, coreY, 1f - q)),
                rr,
                rr * 0.4f,
                Argb(colour, (0.12f + 0.25f * charge) * (1f - i * 0.15f)),
                width,
                dashes);
        }

        if (charge > 0.45f)
        {
            Glow(session, new Vector2(centre.X, coreY), (4f + 10f * charge) * scale, White, (charge - 0.45f) * 1.6f);
        }
    }

    /// <summary>
    /// Draft planetZeichnen, second part: the self-formed planets with the light coming from the hole. Each
    /// one is a clipped sphere with bands, craters or clouds, a shading gradient, and ring or moons in front
    /// and behind.
    /// </summary>
    private void DrawPlanets(CanvasDrawingSession session, GalaxyScene scene)
    {
        if (scene.Planets.Count == 0 || _planetShade is null || _moonShade is null)
        {
            return;
        }

        var layout = scene.Layout;
        var seconds = (float)scene.Time.TotalSeconds;
        var hole = layout.Center;

        foreach (var planet in scene.Planets)
        {
            var orbit = planet.Orbit;
            var alpha = scene.OrbitAlpha[orbit] * scene.GimmickOrbitAlpha;
            if (alpha < 0.02f)
            {
                continue;
            }

            var particleScale = MathF.Sqrt(Math.Clamp(scene.OrbitRadiusX[orbit] / (GalaxyLayout.BaseRadiusInner * layout.Scale), 0.5f, 2.6f));
            var age = Math.Clamp((float)(scene.Time - planet.BornAt).TotalSeconds / GalaxyScene.PlanetSettleSeconds, 0f, 1f);
            var settle = age < 1f ? GalaxyMotion.EaseBack(age) : 1f;
            var r = planet.Radius * layout.Scale * particleScale * settle * planet.Shrink;
            if (r < 0.6f)
            {
                continue;
            }

            var position = planet.Position;

            // The trail of light while it falls into the hole.
            var trail = planet.Trail;
            for (var i = 0; i < trail.Count; i++)
            {
                var share = i / 14f;
                Glow(session, trail[i], r * (0.6f + share * 1.4f), planet.Primary, 0.05f + 0.25f * share);
            }

            var light = hole - position;
            var length = light.Length();
            light = length > 0.001f ? light / length : Vector2.Zero;

            using var fade = session.CreateLayer(Math.Clamp(alpha, 0f, 1f));

            Glow(session, position, r * 2.8f, planet.Primary, 0.22f);
            if (planet.IsGlowing)
            {
                Glow(session, position, r * 2f, new SceneColor(255, 120, 60), 0.35f + 0.1f * MathF.Sin(seconds * 3f));
            }

            var moonPhase = seconds * 0.9f + planet.MoonPhase;
            for (var i = 0; i < planet.Moons; i++)
            {
                DrawMoon(session, planet, position, r, light, moonPhase + i * MathF.PI * 0.8f, i, front: false);
            }

            if (planet.HasRing)
            {
                DrawRing(session, planet, position, r, front: false);
            }

            DrawSphere(session, planet, position, r, light, seconds);

            if (planet.HasRing)
            {
                DrawRing(session, planet, position, r, front: true);
            }

            for (var i = 0; i < planet.Moons; i++)
            {
                DrawMoon(session, planet, position, r, light, moonPhase + i * MathF.PI * 0.8f, i, front: true);
            }
        }
    }

    private void DrawSphere(CanvasDrawingSession session, GalaxyPlanet planet, Vector2 position, float r, Vector2 light, float seconds)
    {
        using var disc = CanvasGeometry.CreateCircle(session, position, r);
        using var clip = session.CreateLayer(1f, disc);

        session.FillCircle(position, r, Argb(planet.Primary, 1f));

        var bands = planet.Bands;
        if (bands > 0)
        {
            var previous = session.Transform;
            session.Transform = Matrix3x2.CreateRotation(planet.Tilt, position) * previous;
            for (var i = 0; i < bands; i++)
            {
                var by = (-1f + (2f * i + 1f) / bands) * r;
                var h = r * (0.12f + 0.1f * ((i * 37 + (int)planet.Seed) % 3) / 3f);
                session.FillRectangle(
                    position.X - r,
                    position.Y + by - h / 2f + MathF.Sin(seconds * 0.6f + i) * 0.6f,
                    r * 2f,
                    h,
                    Argb(planet.Secondary, 0.28f + 0.1f * (i % 2)));
            }

            session.Transform = previous;
        }

        for (var i = 0; i < planet.Craters; i++)
        {
            var q = MathF.Sin(planet.Seed + i * 12.9898f) * 43758.5453f;
            var u = q - MathF.Floor(q);
            var v = (q * 7.3f) - MathF.Floor(q * 7.3f);
            session.FillCircle(
                position.X + (u - 0.5f) * r * 1.5f,
                position.Y + (v - 0.5f) * r * 1.5f,
                r * (0.12f + 0.1f * u),
                Argb(planet.Secondary, 0.45f));
        }

        if (planet.HasClouds)
        {
            var width = Math.Max(1f, r * 0.12f);
            for (var i = 0; i < 3; i++)
            {
                var wy = position.Y + (i - 1) * r * 0.5f;
                var off = (seconds * 3f + i * 7f + planet.Seed) % (r * 4f) - r * 2f;
                using var builder = new CanvasPathBuilder(session);
                builder.BeginFigure(position.X - r + off, wy);
                builder.AddQuadraticBezier(
                    new Vector2(position.X + off * 0.3f, wy - r * 0.2f),
                    new Vector2(position.X + r * 0.6f + off, wy + r * 0.05f));
                builder.EndFigure(CanvasFigureLoop.Open);
                using var cloud = CanvasGeometry.CreatePath(builder);
                session.DrawGeometry(cloud, Color.FromArgb(115, 255, 255, 255), width);
            }
        }

        // Light and shadow: bright towards the hole, dark on the far side.
        var shade = _planetShade!;
        shade.Center = position;
        shade.RadiusX = r * 1.15f;
        shade.RadiusY = r * 1.15f;
        shade.OriginOffset = light * (r * 0.45f);
        session.FillCircle(position, r, shade);
    }

    private static void DrawRing(CanvasDrawingSession session, GalaxyPlanet planet, Vector2 position, float r, bool front)
    {
        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateRotation(planet.Tilt, position) * previous;
        using (var outer = HalfEllipse(session, position, r * 2.05f, r * 0.5f, front))
        {
            session.DrawGeometry(outer, Argb(Mix(planet.Primary, new SceneColor(255, 255, 255), 0.3f), 0.55f), Math.Max(1f, r * 0.16f));
        }

        using (var inner = HalfEllipse(session, position, r * 2.45f, r * 0.6f, front))
        {
            session.DrawGeometry(inner, Argb(planet.Primary, 0.35f), Math.Max(0.6f, r * 0.07f));
        }

        session.Transform = previous;
    }

    /// <summary>The lower (front) or upper (back) half of an ellipse as a polyline of 24 segments.</summary>
    private static CanvasGeometry HalfEllipse(ICanvasResourceCreator creator, Vector2 centre, float rx, float ry, bool front)
    {
        const int segments = 24;
        var builder = new CanvasPathBuilder(creator);
        var start = front ? 0f : MathF.PI;
        for (var j = 0; j <= segments; j++)
        {
            var a = start + j / (float)segments * MathF.PI;
            var point = centre + new Vector2(rx * MathF.Cos(a), ry * MathF.Sin(a));
            if (j == 0)
            {
                builder.BeginFigure(point);
            }
            else
            {
                builder.AddLine(point);
            }
        }

        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private void DrawMoon(CanvasDrawingSession session, GalaxyPlanet planet, Vector2 position, float r, Vector2 light, float angle, int index, bool front)
    {
        if ((MathF.Sin(angle) >= 0f) != front)
        {
            return;
        }

        var centre = position + new Vector2(MathF.Cos(angle) * r * (2.3f + index * 0.7f), MathF.Sin(angle) * r * 0.7f);
        var shade = _moonShade!;
        shade.Center = centre;
        shade.RadiusX = r * 0.32f;
        shade.RadiusY = r * 0.32f;
        shade.OriginOffset = light * 2f;
        session.FillCircle(centre, r * 0.3f, shade);
    }

    /// <summary>Draft "minis": the small explosion at a birth and at an impact, two glows and two rings.</summary>
    private void DrawBursts(CanvasDrawingSession session, GalaxyScene scene)
    {
        if (scene.Bursts.Count == 0)
        {
            return;
        }

        var scale = scene.Layout.Scale;
        foreach (var burst in scene.Bursts)
        {
            var q = Math.Clamp((float)(scene.Time - burst.At).TotalSeconds / GalaxyScene.BurstSeconds, 0f, 1f);
            var g = burst.Size * scale;
            Glow(session, burst.Position, (16f + q * 130f) * g, White, 0.95f * (1f - q) * (1f - q));
            Glow(session, burst.Position, (12f + q * 80f) * g, burst.Color, 0.7f * (1f - q));
            ReadOnlySpan<float> starts = [0f, 0.2f];
            foreach (var v in starts)
            {
                var s = Math.Clamp((q - v) / (1f - v), 0f, 1f);
                if (s <= 0f)
                {
                    continue;
                }

                var rx = (8f + s * 110f) * g;
                session.DrawEllipse(burst.Position, rx, rx * 0.4f, Argb(burst.Color, 0.9f * (1f - s)), 2f * (1f - s) + 0.5f);
            }
        }
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
