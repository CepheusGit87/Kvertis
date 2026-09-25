using System.Globalization;
using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Kvertis.App.Rendering;

/// <summary>
/// Draws a <see cref="SwirlScene"/> with Win2D (ADR-023): the halo and the dashed orbit around the black hole,
/// the white hole with its orbits, planets, core and stars in the upper right, the drawn sheets on the inbox
/// stack, the pixel streams with their trails and the black hole on top. Once the scene carries a finale the
/// <see cref="FinaleRenderer"/> takes over the same session. Everything it paints comes from the scene; the
/// renderer owns only device resources.
/// </summary>
/// <remarks>
/// As in the galaxy: no bitmap is ever created (the sheets are drawn from primitives, the pixels come from the
/// computed <see cref="SheetRaster"/>), every glow is a radial gradient, no effect graph per frame.
/// </remarks>
public sealed class SwirlRenderer : IDisposable
{
    private readonly SceneBrushes _brushes = new();
    private readonly FinaleRenderer _finale;

    private CanvasGeometry? _contentClip;
    private CanvasGeometry? _ridge;
    private CanvasGeometry? _play;
    private CanvasGeometry? _hexagon;
    private CanvasLinearGradientBrush? _sky;

    public SwirlRenderer()
    {
        _finale = new FinaleRenderer(_brushes, this);
    }

    /// <summary>
    /// Format of the small part of the counter under the white hole, for example "von {0}"; the number in
    /// front of it is the count of arrived files. Set by the shell from the resources; empty draws no counter.
    /// </summary>
    public string CounterSuffixFormat { get; set; } = string.Empty;

    /// <summary>Called from <c>CreateResources</c>; everything built for the old device is dropped first.</summary>
    public void CreateResources(ICanvasResourceCreator creator)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ReleaseBrushes();
        _brushes.CreateResources(creator);
        _finale.CreateResources(creator);

        // Sheet geometry in sheet coordinates (96 × 124); drawn under a scale/rotate/translate transform.
        _contentClip = CanvasGeometry.CreateRoundedRectangle(creator, 8f, 22f, 80f, 52f, 3f, 3f);
        _ridge = CanvasGeometry.CreatePolygon(creator,
        [
            new Vector2(16f, 22f + 52f * 0.66f), new Vector2(38f, 36f), new Vector2(52f, 52f), new Vector2(64f, 40f), new Vector2(82f, 22f + 52f * 0.66f),
        ]);
        _play = CanvasGeometry.CreatePolygon(creator, [new Vector2(39f, 36f), new Vector2(61f, 48f), new Vector2(39f, 60f)]);
        _hexagon = BuildHexagon(creator, new Vector2(48f, 50f), 15f);
        _sky = new CanvasLinearGradientBrush(creator,
        [
            new CanvasGradientStop { Position = 0f, Color = SceneBrushes.Rgb(0x9CC3EE) },
            new CanvasGradientStop { Position = 0.62f, Color = SceneBrushes.Rgb(0xE6EEF6) },
            new CanvasGradientStop { Position = 0.6201f, Color = SceneBrushes.Rgb(0xD9C9A4) },
            new CanvasGradientStop { Position = 1f, Color = SceneBrushes.Rgb(0xCBB88C) },
        ])
        {
            StartPoint = new Vector2(0f, 22f),
            EndPoint = new Vector2(0f, 74f),
        };
    }

    /// <summary>Drops brushes, geometry and text formats but keeps nothing of the device; call <see cref="CreateResources"/> again after.</summary>
    public void ReleaseBrushes()
    {
        _finale.ReleaseBrushes();
        _brushes.Release();
        _contentClip?.Dispose();
        _contentClip = null;
        _ridge?.Dispose();
        _ridge = null;
        _play?.Dispose();
        _play = null;
        _hexagon?.Dispose();
        _hexagon = null;
        _sky?.Dispose();
        _sky = null;
    }

    public void Dispose()
    {
        ReleaseBrushes();
        _finale.Dispose();
        _brushes.Dispose();
    }

    /// <summary>Paints one frame in the order of the draft: halo, orbit, white hole, stack, pixels, black hole.</summary>
    public void Draw(CanvasDrawingSession session, SwirlScene scene)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scene);
        if (_brushes.Creator is null)
        {
            return;
        }

        var palette = scene.Palette;
        if (scene.Finale is { } finale)
        {
            _finale.Draw(session, scene, finale);
            // Failed files lie on the stack again; the finale leaves them there (draft: bildAt for 'fehler').
            var offset = Matrix3x2.CreateTranslation(finale.ShakeOffset);
            foreach (var sheet in scene.StackSheets)
            {
                DrawSheet(session, sheet.Spec, sheet.Placement, palette, false, 1f, offset);
            }

            return;
        }

        _brushes.Glow(session, scene.Halo);
        DrawOrbitEllipse(session, scene, palette);
        DrawWhiteHole(session, scene.WhiteHole, (float)scene.Time.TotalSeconds, palette, 1f);
        foreach (var sheet in scene.StackSheets)
        {
            DrawSheet(session, sheet.Spec, sheet.Placement, palette, false, 1f, Matrix3x2.Identity);
        }

        DrawPixels(session, scene, palette);
        DrawBlackHole(session, scene.Centre, 1f, palette);
    }

    // ----- swirl -------------------------------------------------------------------------------------

    private void DrawOrbitEllipse(CanvasDrawingSession session, SwirlScene scene, ScenePalette palette)
    {
        var r = SwirlScene.Radius + 24f;
        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateRotation(scene.OrbitRotation, scene.Centre) * previous;
        var colour = SceneBrushes.Argb(palette.LineStrong, 0.6f);
        if (_brushes.Dash26 is { } dash)
        {
            session.DrawEllipse(scene.Centre, r, r * SwirlScene.Flatten, colour, 1f, dash);
        }
        else
        {
            session.DrawEllipse(scene.Centre, r, r * SwirlScene.Flatten, colour, 1f);
        }
        session.Transform = previous;
    }

    private static void DrawPixels(CanvasDrawingSession session, SwirlScene scene, ScenePalette palette)
    {
        var previous = session.Blend;
        var current = previous;
        foreach (var file in scene.Files)
        {
            if (!file.HasPixels)
            {
                continue;
            }

            var pixels = file.Pixels;
            for (var i = 0; i < pixels.Length; i++)
            {
                ref readonly var p = ref pixels[i];
                var blend = p.Additive && palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
                if (blend != current)
                {
                    session.Blend = blend;
                    current = blend;
                }

                var colour = SceneBrushes.Argb(p.Color, p.Alpha);
                if (p.HasPrevious)
                {
                    var d = Vector2.DistanceSquared(p.Position, p.Previous);
                    if (d > 6f && d < 4000f)
                    {
                        session.DrawLine(
                            p.Previous + new Vector2(1f, 1f),
                            p.Position + new Vector2(1f, 1f),
                            SceneBrushes.Argb(p.Color, p.Alpha * 0.35f),
                            1.6f);
                    }
                }

                session.FillRectangle(p.Position.X, p.Position.Y, 2f, 2f, colour);
            }
        }

        session.Blend = previous;
    }

    /// <summary>The same hole as in step 2 and in the overlay (<c>s5Loch</c>), scaled for the dance.</summary>
    internal void DrawBlackHole(CanvasDrawingSession session, Vector2 centre, float scale, ScenePalette palette)
    {
        if (scale <= 0f)
        {
            return;
        }

        var r = SwirlScene.HoleRadius * scale;
        _brushes.Glow(session, centre, r * 3.4f, palette.Audio, 0.22f);
        _brushes.Glow(session, centre, r * 2f, palette.Mint, 0.25f);
        session.FillCircle(centre, r, palette.IsDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 11, 15, 18));
        session.DrawCircle(centre, r + 1.2f, SceneBrushes.Argb(palette.Mint, 0.95f), 1.6f);
    }

    // ----- white hole --------------------------------------------------------------------------------

    private void DrawWhiteHole(CanvasDrawingSession session, WhiteHoleScene hole, float t, ScenePalette palette, float counterAlpha)
    {
        if (hole.N == 0)
        {
            return;
        }

        DrawStars(session, hole.Stars);
        if (hole.Mode == WhiteHoleMode.Dust)
        {
            DrawDustRings(session, hole, hole.Centre, 1f, 1f, palette);
            DrawCore(session, hole.Centre, t, 1f, hole.Share, hole.Pulse, palette);
            foreach (var pulse in hole.Pulses)
            {
                if (pulse.Alpha > 0.01f)
                {
                    session.DrawEllipse(hole.Centre, pulse.RadiusX, pulse.RadiusY, SceneBrushes.Argb(pulse.Color, pulse.Alpha), 1.6f);
                }
            }

            foreach (var planet in hole.Planets)
            {
                DrawPlanet(session, planet.Position, planet.Radius, planet.Color, planet.Failed, 1f, palette);
            }

            DrawCounter(session, hole, palette, counterAlpha);
            return;
        }

        foreach (var orbit in hole.OrbitLines)
        {
            DrawOrbitLine(session, hole.Centre, orbit, 1f, 1f);
        }

        DrawCore(session, hole.Centre, t, 1f, hole.Share, hole.Pulse, palette);
        var faint = SceneBrushes.Argb(palette.LineStrong, 0.95f);
        foreach (var place in hole.EmptyPlaces)
        {
            session.FillCircle(place, 1.4f, faint);
        }

        foreach (var planet in hole.Planets)
        {
            if (planet.ArrivalAlpha > 0f)
            {
                session.DrawCircle(planet.Position, planet.ArrivalRadius, SceneBrushes.Argb(planet.Color, planet.ArrivalAlpha), 1.6f);
            }

            DrawPlanet(session, planet.Position, planet.Radius, planet.Color, planet.Failed, 1f, palette, hole, planet);
        }

        DrawCounter(session, hole, palette, counterAlpha);
    }

    /// <summary>
    /// The dust rings (draft 2 "Staubringe"): a dashed ring per kind, a soft band in the kind colour that grows with
    /// the fill, and the lit grains (1.7 DIP squares, additive in the dark). Drawn around <paramref name="centre"/>
    /// with <paramref name="scale"/> and <paramref name="alphaFactor"/> so the finale can contract and fade them.
    /// </summary>
    internal void DrawDustRings(CanvasDrawingSession session, WhiteHoleScene hole, Vector2 centre, float scale, float alphaFactor, ScenePalette palette)
    {
        if (alphaFactor <= 0.01f || scale <= 0f)
        {
            return;
        }

        var dashed = SceneBrushes.Argb(palette.LineStrong, 0.55f * alphaFactor);
        foreach (var ring in hole.Rings)
        {
            if (_brushes.Dash25 is { } dash)
            {
                session.DrawEllipse(centre, ring.RadiusX * scale, ring.RadiusY * scale, dashed, 1f, dash);
            }
            else
            {
                session.DrawEllipse(centre, ring.RadiusX * scale, ring.RadiusY * scale, dashed, 1f);
            }
        }

        var previous = session.Blend;
        session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        foreach (var ring in hole.Rings)
        {
            if (ring.GlowAlpha > 0f)
            {
                session.DrawEllipse(centre, ring.RadiusX * scale, ring.RadiusY * scale, SceneBrushes.Argb(ring.Color, ring.GlowAlpha * alphaFactor), ring.GlowWidth * scale);
            }
        }

        foreach (var grain in hole.Grains)
        {
            var p = centre + grain.Offset * scale;
            session.FillRectangle(p.X - 0.8f, p.Y - 0.8f, 1.7f, 1.7f, SceneBrushes.Argb(grain.Color, grain.Alpha * alphaFactor));
        }

        session.Blend = previous;
    }

    internal static void DrawStars(CanvasDrawingSession session, IReadOnlyList<SceneStar> stars)
    {
        foreach (var star in stars)
        {
            if (star.Alpha <= 0.01f)
            {
                continue;
            }

            session.FillRectangle(star.Position.X, star.Position.Y, star.Size, star.Size, SceneBrushes.Argb(star.Color, star.Alpha));
        }
    }

    internal void DrawOrbitLine(CanvasDrawingSession session, Vector2 centre, in WhiteHoleOrbit orbit, float scale, float alphaFactor)
    {
        var alpha = orbit.Alpha * alphaFactor;
        if (alpha <= 0.01f)
        {
            return;
        }

        var colour = SceneBrushes.Argb(orbit.Color, alpha);
        if (orbit.Dashed && _brushes.Dash25 is { } dash)
        {
            session.DrawEllipse(centre, orbit.RadiusX * scale, orbit.RadiusY * scale, colour, orbit.Width, dash);
        }
        else
        {
            session.DrawEllipse(centre, orbit.RadiusX * scale, orbit.RadiusY * scale, colour, orbit.Width);
        }
    }

    /// <summary>The white core (<c>kern</c>): mint halo, white halo, cross of rays, disc and mint ring.</summary>
    internal void DrawCore(CanvasDrawingSession session, Vector2 centre, float t, float scale, float share, float pulse, ScenePalette palette)
    {
        var g = (1f + share * 0.35f) * scale;
        var glow = palette.Glow;
        var previous = session.Blend;
        session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        _brushes.Glow(session, centre, (46f + share * 36f) * pulse * scale, palette.Mint, palette.IsDark ? 0.2f : 0.14f);
        _brushes.Glow(session, centre, 20f * g, glow, palette.IsDark ? 0.75f : 0.35f);
        var rays = SceneBrushes.Argb(glow, palette.IsDark ? 0.45f : 0.35f);
        var l = 20f * g * pulse;
        session.DrawLine(centre - new Vector2(l, 0f), centre + new Vector2(l, 0f), rays, 1f);
        session.DrawLine(centre - new Vector2(0f, l * 0.7f), centre + new Vector2(0f, l * 0.7f), rays, 1f);
        session.Blend = previous;
        session.FillCircle(centre, 6.5f * g, Color.FromArgb(255, 255, 255, 255));
        session.DrawCircle(centre, 7.6f * g, SceneBrushes.Argb(palette.Mint, 1f), 1.5f);
    }

    /// <summary>A planet (<c>punkt</c>): tail of eight circles, additive halo, disc; a failed file is a coral ring.</summary>
    internal void DrawPlanet(
        CanvasDrawingSession session,
        Vector2 position,
        float radius,
        SceneColor colour,
        bool failed,
        float alpha,
        ScenePalette palette,
        WhiteHoleScene? tailHole = null,
        WhiteHolePlanet tailOf = default)
    {
        if (alpha <= 0.01f)
        {
            return;
        }

        if (failed)
        {
            session.DrawCircle(position, radius + 1.5f, SceneBrushes.Argb(colour, alpha * 0.8f), 1.4f);
            return;
        }

        if (tailHole is not null)
        {
            for (var i = WhiteHoleScene.TailLength; i >= 1; i--)
            {
                var (point, radiusFactor, tailAlpha) = tailHole.TailPoint(tailOf, i);
                session.FillCircle(point, radius * radiusFactor, SceneBrushes.Argb(colour, alpha * tailAlpha));
            }
        }

        var previous = session.Blend;
        session.Blend = palette.IsDark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        _brushes.Glow(session, position, radius * 4f, colour, 0.5f * alpha);
        session.Blend = previous;
        session.FillCircle(position, radius, SceneBrushes.Argb(colour, alpha));
    }

    /// <summary>"n von N" under the white hole (<c>zaehler</c>); the text of the suffix comes from the resources.</summary>
    internal void DrawCounter(CanvasDrawingSession session, WhiteHoleScene hole, ScenePalette palette, float alpha)
    {
        if (alpha <= 0.01f || hole.N == 0 || _brushes.CounterBig is null || _brushes.CounterSmall is null
            || string.IsNullOrEmpty(CounterSuffixFormat) || _brushes.Creator is null)
        {
            return;
        }

        var big = hole.Arrived.ToString(CultureInfo.CurrentCulture);
        var small = " " + string.Format(CultureInfo.CurrentCulture, CounterSuffixFormat, hole.N);
        var w1 = _brushes.Measure(_brushes.Creator, big, _brushes.CounterBig);
        var w2 = _brushes.Measure(_brushes.Creator, small, _brushes.CounterSmall);
        var y = hole.CounterY;
        var x0 = hole.Centre.X - (w1 + w2) / 2f;
        session.DrawText(big, new Vector2(x0, y - 10f), SceneBrushes.Argb(palette.Ink, alpha), _brushes.CounterBig);
        session.DrawText(small, new Vector2(x0 + w1, y - 6f), SceneBrushes.Argb(palette.Muted, alpha), _brushes.CounterSmall);
    }

    /// <summary>A number centred under a collective planet of the finale ring (dust mode).</summary>
    internal void DrawPlanetCount(CanvasDrawingSession session, Vector2 position, float radius, int count, ScenePalette palette, float alpha)
    {
        if (alpha <= 0.01f || count <= 0 || _brushes.CounterSmall is null || _brushes.Creator is null)
        {
            return;
        }

        var text = count.ToString(CultureInfo.CurrentCulture);
        var w = _brushes.Measure(_brushes.Creator, text, _brushes.CounterSmall);
        session.DrawText(text, new Vector2(position.X - w / 2f, position.Y + radius + 4f), SceneBrushes.Argb(palette.Ink, alpha), _brushes.CounterSmall);
    }

    // ----- sheets ------------------------------------------------------------------------------------

    /// <summary>
    /// A drawn sheet (<c>blattBild</c>), 96 × 124 in sheet coordinates: tab with the format, paper, content
    /// field per kind, format label and file name; the new sheet carries mint tab and frame.
    /// </summary>
    internal void DrawSheet(CanvasDrawingSession session, SwirlFileSpec spec, SheetPlacement placement, ScenePalette palette, bool isNew, float alpha, Matrix3x2 baseTransform)
    {
        if (alpha <= 0.01f || _contentClip is null)
        {
            return;
        }

        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateScale(placement.Scale)
            * Matrix3x2.CreateRotation(placement.Rotation)
            * Matrix3x2.CreateTranslation(placement.TopLeft)
            * baseTransform;

        var kind = palette.For(spec.Kind);
        var tab = isNew ? palette.Mint : kind;
        session.FillRoundedRectangle(0f, 18f, SheetRaster.Width, SheetRaster.Height - 12f, 6f, 6f, SceneBrushes.Argb(palette.Shadow, 0.35f * alpha));
        session.FillRoundedRectangle(6f, 0f, 42f, 18f, 5f, 5f, SceneBrushes.Argb(tab, alpha));
        if (_brushes.SheetTab is not null)
        {
            session.DrawText(isNew ? spec.FormatTo : spec.FormatFrom, new Vector2(12f, 9f), isNew ? SceneBrushes.Argb(palette.OnMint, alpha) : SceneBrushes.Rgba(0x0B0F12, alpha), _brushes.SheetTab);
        }

        session.FillRoundedRectangle(0f, 12f, SheetRaster.Width, SheetRaster.Height - 12f, 6f, 6f, SceneBrushes.Argb(palette.Paper, alpha));
        if (isNew)
        {
            session.DrawRoundedRectangle(1f, 13f, SheetRaster.Width - 2f, SheetRaster.Height - 14f, 5f, 5f, SceneBrushes.Argb(palette.Mint, alpha), 2f);
        }

        using (session.CreateLayer(alpha, _contentClip))
        {
            DrawContent(session, spec.Kind, kind, isNew, palette);
        }

        if (_brushes.SheetLabel is not null)
        {
            session.DrawText(isNew ? spec.FormatTo : spec.FormatFrom, new Vector2(8f, 86f), isNew ? SceneBrushes.Argb(palette.Mint, alpha) : SceneBrushes.Rgba(0x3B4247, alpha), _brushes.SheetLabel);
        }

        if (_brushes.SheetName is not null && !string.IsNullOrEmpty(spec.FileName))
        {
            using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(session, spec.FileName, _brushes.SheetName, SheetRaster.Width - 14f, 10f);
            session.DrawTextLayout(layout, new Vector2(8f, 108f), SceneBrushes.Argb(palette.Muted, alpha));
        }

        session.Transform = previous;
    }

    // Content field (8, 22, 80 × 52) per kind (draft: inhalt); fixed content colours of the stand-in.
    private void DrawContent(CanvasDrawingSession session, MediaKind kind, SceneColor kindColour, bool isNew, ScenePalette palette)
    {
        const float a = 8f, b = 22f, w = 80f, h = 52f;
        switch (kind)
        {
            case MediaKind.Image:
                if (_sky is not null)
                {
                    session.FillRectangle(a, b, w, h, _sky);
                }

                if (_ridge is not null)
                {
                    session.FillGeometry(_ridge, SceneBrushes.Rgb(0x5F7C9C));
                }

                session.FillCircle(new Vector2(a + w - 16f, b + 12f), 7f, SceneBrushes.Rgb(0xF2C46A));
                break;

            case MediaKind.Audio:
                session.FillRectangle(a, b, w, h, SceneBrushes.Rgb(0xF1ECFB));
                var bar = SceneBrushes.Rgb(0x7B61C4);
                ReadOnlySpan<float> bars = [8, 14, 22, 30, 18, 26, 38, 44, 30, 20, 34, 40, 26, 16, 28, 36, 22, 12, 20, 14];
                for (var i = 0; i < bars.Length; i++)
                {
                    var hh = bars[i] * 0.9f;
                    session.FillRectangle(a + 4f + i * 3.7f, b + h / 2f - hh / 2f, 2.4f, hh, bar);
                }

                break;

            case MediaKind.Video:
                session.FillRectangle(a, b, w, h, SceneBrushes.Rgb(0x1D2226));
                if (_play is not null)
                {
                    session.FillGeometry(_play, SceneBrushes.Rgb(0xF2B45A));
                }

                var hole = Color.FromArgb(0x22, 255, 255, 255);
                for (var i = 0; i < 8; i++)
                {
                    session.FillRectangle(a + 3f + i * 10f, b + 3f, 6f, 4f, hole);
                    session.FillRectangle(a + 3f + i * 10f, b + h - 7f, 6f, 4f, hole);
                }

                break;

            case MediaKind.Model3D:
                session.FillRectangle(a, b, w, h, SceneBrushes.Rgb(0xF7EEF4));
                if (_hexagon is not null)
                {
                    session.DrawGeometry(_hexagon, SceneBrushes.Rgb(0x9A2F7D), 1.4f);
                }

                break;

            case MediaKind.Document:
                session.FillRectangle(a, b, w, h, SceneBrushes.Rgb(0xFFFFFF));
                session.FillRectangle(a + 5f, b + 6f, w * 0.6f, 5f, SceneBrushes.Argb(isNew ? palette.Mint : kindColour, 1f));
                ReadOnlySpan<float> lines = [1f, 0.8f, 0.95f, 0.6f, 0.9f, 0.7f];
                var line = SceneBrushes.Rgb(0xC5CCD1);
                for (var i = 0; i < lines.Length; i++)
                {
                    session.FillRectangle(a + 5f, b + 16f + i * 5.5f, (w - 10f) * lines[i], 2.6f, line);
                }

                break;

            default:
                session.FillRectangle(a, b, w, h, SceneBrushes.Argb(palette.Paper, 1f));
                break;
        }
    }

    private static CanvasGeometry BuildHexagon(ICanvasResourceCreator creator, Vector2 centre, float s)
    {
        ReadOnlySpan<Vector2> points =
        [
            new(0f, -s), new(s * 0.9f, -s / 2f), new(s * 0.9f, s / 2f), new(0f, s), new(-s * 0.9f, s / 2f), new(-s * 0.9f, -s / 2f),
        ];
        using var builder = new CanvasPathBuilder(creator);
        builder.BeginFigure(centre + points[0]);
        for (var i = 1; i < points.Length; i++)
        {
            builder.AddLine(centre + points[i]);
        }

        builder.EndFigure(CanvasFigureLoop.Closed);
        foreach (var index in (ReadOnlySpan<int>)[3, 5, 1])
        {
            builder.BeginFigure(centre);
            builder.AddLine(centre + points[index]);
            builder.EndFigure(CanvasFigureLoop.Open);
        }

        return CanvasGeometry.CreatePath(builder);
    }
}
