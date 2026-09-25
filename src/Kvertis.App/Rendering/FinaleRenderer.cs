using System.Numerics;
using Kvertis.App.Scenes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Windows.UI;

namespace Kvertis.App.Rendering;

/// <summary>
/// Draws the <see cref="FinaleScene"/> (worksheet "Abschluss") on the swirl surface: run-up with dust and
/// orbits, the dance of the two holes with their trails, the silence, the supernova with its light streak,
/// shock waves and sparks, the full star field, the ring of planets and the check mark. All values come from
/// the scene; the shake of the canvas is a translation of the whole drawing, the shake of the page is XAML.
/// </summary>
internal sealed class FinaleRenderer : IDisposable
{
    private readonly SceneBrushes _brushes;
    private readonly SwirlRenderer _swirl;

    private CanvasRadialGradientBrush? _nova;
    private CanvasLinearGradientBrush? _streak;
    private CanvasRadialGradientBrush? _backdrop;
    private bool _brushesDark;
    private ScenePalette? _brushPalette;

    public FinaleRenderer(SceneBrushes brushes, SwirlRenderer swirl)
    {
        _brushes = brushes;
        _swirl = swirl;
    }

    public void CreateResources(ICanvasResourceCreator creator)
    {
        ReleaseBrushes();
    }

    public void ReleaseBrushes()
    {
        _nova?.Dispose();
        _nova = null;
        _streak?.Dispose();
        _streak = null;
        _backdrop?.Dispose();
        _backdrop = null;
        _brushPalette = null;
    }

    public void Dispose() => ReleaseBrushes();

    public void Draw(CanvasDrawingSession session, SwirlScene scene, FinaleScene finale)
    {
        var palette = scene.Palette;
        EnsureGradients(palette);
        var hole = scene.WhiteHole;
        var t = (float)scene.Time.TotalSeconds;
        var dark = palette.IsDark;
        var additive = dark ? CanvasBlend.Add : CanvasBlend.SourceOver;
        var shake = Matrix3x2.CreateTranslation(finale.ShakeOffset);
        var previousTransform = session.Transform;
        session.Transform = shake * previousTransform;

        SwirlRenderer.DrawStars(session, hole.Stars);
        SwirlRenderer.DrawStars(session, finale.FullStars);
        DrawBackdrop(session, finale, palette);

        var holes = finale.CurrentHoles;
        if (finale.PlanetsVisible)
        {
            var orbits = hole.OrbitLines;
            for (var j = 0; j < orbits.Count; j++)
            {
                _swirl.DrawOrbitLine(session, holes.White, orbits[j], finale.OrbitScale, finale.OrbitAlpha);
            }

            if (finale.IsDust)
            {
                // The dust rings contract with the orbits and fade with them; their grains ride along.
                _swirl.DrawDustRings(session, hole, holes.White, finale.OrbitScale, finale.OrbitAlpha, palette);
            }

            if (finale.Dust.Count > 0)
            {
                session.Blend = additive;
                foreach (var grain in finale.Dust)
                {
                    if (grain.Alpha <= 0.01f)
                    {
                        continue;
                    }

                    var colour = SceneBrushes.Argb(grain.Color, grain.Alpha);
                    session.DrawLine(grain.Tail, grain.Head, colour, 1.2f);
                    session.FillRectangle(grain.Head.X - 1f, grain.Head.Y - 1f, 2f, 2f, colour);
                }

                session.Blend = CanvasBlend.SourceOver;
            }

            session.Blend = additive;
            _brushes.Glow(session, finale.WhiteHalo);
            session.Blend = CanvasBlend.SourceOver;

            var drawn = finale.TrailDrawn;
            var radius = finale.PlanetRadius;
            foreach (var planet in finale.Planets)
            {
                if (!planet.Failed && planet.Alpha > 0.01f)
                {
                    session.Blend = additive;
                    var trail = planet.Trail;
                    var n = Math.Min(drawn, trail.Length);
                    for (var i = 0; i < n; i++)
                    {
                        var alpha = planet.Alpha * 0.42f * (1f - (i + 1f) / (n + 1f));
                        session.FillCircle(holes.White + trail[i], radius * (1f - (i + 1f) / (n + 4f)), SceneBrushes.Argb(planet.Color, alpha));
                    }

                    session.Blend = CanvasBlend.SourceOver;
                }

                _swirl.DrawPlanet(session, planet.Position, radius, planet.Color, planet.Failed, planet.Alpha, palette);
            }

            if (finale.HoleTrails.Count > 0)
            {
                session.Blend = additive;
                foreach (var segment in finale.HoleTrails)
                {
                    RoundLine(session, segment.From, segment.To, SceneBrushes.Argb(segment.Color, segment.Alpha), segment.Width);
                }

                session.Blend = CanvasBlend.SourceOver;
            }

            session.Blend = additive;
            _brushes.Glow(session, finale.MergeGlow);
            session.Blend = CanvasBlend.SourceOver;

            _swirl.DrawCore(session, holes.White, t, finale.CoreScale, 1f, hole.Pulse, palette);
            _swirl.DrawBlackHole(session, holes.Black, finale.BlackScale, palette);
            _swirl.DrawCounter(session, hole, palette, finale.CounterAlpha);
        }

        DrawNova(session, finale, palette, additive);

        var c = finale.EndCentre;
        var radii = finale.RingRadii;
        if (finale.RingLineAlpha > 0.005f)
        {
            session.DrawEllipse(c, radii.X, radii.Y, SceneBrushes.Argb(palette.Mint, finale.RingLineAlpha), 1f);
        }

        foreach (var planet in finale.Planets)
        {
            _swirl.DrawPlanet(session, planet.RingPosition, finale.PlanetRadius, planet.Color, planet.Failed, planet.RingAlpha, palette);
            if (planet.Count > 0)
            {
                // Dust mode: the collective planet carries the number of its files once it sits on the ring.
                _swirl.DrawPlanetCount(session, planet.RingPosition, finale.PlanetRadius, planet.Count, palette, planet.RingAlpha * finale.RingLineAlpha / 0.22f);
            }
        }

        session.Transform = previousTransform;
        DrawCheck(session, finale, palette, additive);
    }

    // ----- supernova ---------------------------------------------------------------------------------

    private void DrawNova(CanvasDrawingSession session, FinaleScene finale, ScenePalette palette, CanvasBlend additive)
    {
        var c = finale.EndCentre;
        if (finale.SilenceGlow is { } silence)
        {
            session.Blend = additive;
            _brushes.Glow(session, silence);
            session.Blend = CanvasBlend.SourceOver;
            return;
        }

        if (finale.Nova is { } nova && _nova is not null)
        {
            session.Blend = additive;
            _nova.Center = c;
            _nova.RadiusX = nova.Radius;
            _nova.RadiusY = nova.Radius;
            _nova.Opacity = nova.Alpha;
            var previous = session.Transform;
            session.Transform = Matrix3x2.CreateScale(1f, nova.ScaleY, c) * previous;
            session.FillCircle(c, nova.Radius, _nova);
            session.Transform = previous;

            if (finale.StreakAlpha > 0.005f && _streak is not null)
            {
                _streak.StartPoint = new Vector2(0f, c.Y);
                _streak.EndPoint = new Vector2(finale.Width, c.Y);
                _streak.Opacity = finale.StreakAlpha;
                session.FillRectangle(0f, c.Y - 1.2f, finale.Width, 2.4f, _streak);
                _brushes.Glow(session, c, finale.Width * 0.5f, palette.Mint, 0.5f * finale.StreakAlpha, 0.05f);
            }

            session.Blend = CanvasBlend.SourceOver;
        }

        if (finale.E < 0f)
        {
            return;
        }

        // Explosion: halos, flash, waves, sparks.
        session.Blend = additive;
        if (finale.ExplosionWhite is { } white)
        {
            _brushes.Glow(session, white);
        }

        if (finale.ExplosionMint is { } mint)
        {
            _brushes.Glow(session, mint);
        }

        if (finale.FlashAlpha > 0.004f)
        {
            session.FillRectangle(-finale.Width, -finale.Height, finale.Width * 3f, finale.Height * 3f, SceneBrushes.Argb(palette.Glow, finale.FlashAlpha));
        }

        session.Blend = CanvasBlend.SourceOver;
        foreach (var wave in finale.Waves)
        {
            session.DrawEllipse(wave.Centre, wave.Radius, wave.Radius * wave.Flatten, SceneBrushes.Argb(wave.Color, wave.Alpha), wave.Width);
        }

        if (finale.Sparks.Count > 0)
        {
            session.Blend = additive;
            foreach (var spark in finale.Sparks)
            {
                if (spark.Alpha <= 0.01f)
                {
                    continue;
                }

                session.DrawLine(spark.From, spark.To, SceneBrushes.Argb(spark.Color, spark.Alpha), spark.Width);
            }

            session.Blend = CanvasBlend.SourceOver;
        }
    }

    // ----- backdrop and check ------------------------------------------------------------------------

    /// <summary>Soft ellipse of the background behind the report (draft: <c>grund</c>).</summary>
    private void DrawBackdrop(CanvasDrawingSession session, FinaleScene finale, ScenePalette palette)
    {
        if (finale.BackdropAlpha <= 0.005f || _backdrop is null)
        {
            return;
        }

        var centre = new Vector2(finale.Width / 2f, 168f);
        _backdrop.Center = centre;
        _backdrop.RadiusX = 250f;
        _backdrop.RadiusY = 250f;
        _backdrop.Opacity = finale.BackdropAlpha;
        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateScale(1f, 0.42f, centre) * previous;
        session.FillCircle(centre, 250f, _backdrop);
        session.Transform = previous;
    }

    private void DrawCheck(CanvasDrawingSession session, FinaleScene finale, ScenePalette palette, CanvasBlend additive)
    {
        if (finale.CheckDiscRadius <= 0f && finale.CheckHaloAlpha <= 0f)
        {
            return;
        }

        var centre = finale.CheckCentre;
        session.Blend = additive;
        _brushes.Glow(session, centre, 58f, palette.Mint, finale.CheckHaloAlpha);
        session.Blend = CanvasBlend.SourceOver;
        session.FillCircle(centre, finale.CheckDiscRadius, SceneBrushes.Argb(palette.Mint, 1f));

        var h = finale.CheckStroke;
        if (h <= 0f)
        {
            return;
        }

        const float firstPart = 0.4f;
        var points = FinaleScene.CheckPoints;
        var colour = SceneBrushes.Argb(palette.OnMint, 1f);
        var p0 = centre + points[0];
        var p1 = centre + points[1];
        if (h < firstPart)
        {
            RoundLine(session, p0, Vector2.Lerp(p0, p1, h / firstPart), colour, 3f);
            return;
        }

        var p2 = centre + points[2];
        var m = (h - firstPart) / (1f - firstPart);
        RoundLine(session, p0, p1, colour, 3f);
        RoundLine(session, p1, Vector2.Lerp(p1, p2, m), colour, 3f);
    }

    private void RoundLine(CanvasDrawingSession session, Vector2 from, Vector2 to, Color colour, float width)
    {
        if (_brushes.Round is { } round)
        {
            session.DrawLine(from, to, colour, width, round);
        }
        else
        {
            session.DrawLine(from, to, colour, width);
        }
    }

    // The nova gradient, the streak and the backdrop depend on the palette; they are rebuilt when it changes.
    private void EnsureGradients(ScenePalette palette)
    {
        if (_brushes.Creator is not { } creator)
        {
            return;
        }

        if (_nova is not null && ReferenceEquals(_brushPalette, palette) && _brushesDark == palette.IsDark)
        {
            return;
        }

        _nova?.Dispose();
        _streak?.Dispose();
        _backdrop?.Dispose();
        var glow = palette.Glow;
        _nova = new CanvasRadialGradientBrush(creator,
        [
            new CanvasGradientStop { Position = 0f, Color = SceneBrushes.Argb(glow, 0.7f) },
            new CanvasGradientStop { Position = 0.55f, Color = SceneBrushes.Argb(palette.Mint, 0.35f) },
            new CanvasGradientStop { Position = 0.85f, Color = SceneBrushes.Argb(palette.Audio, 0.25f) },
            new CanvasGradientStop { Position = 1f, Color = SceneBrushes.Argb(palette.Audio, 0f) },
        ]);
        _streak = new CanvasLinearGradientBrush(creator,
        [
            new CanvasGradientStop { Position = 0f, Color = SceneBrushes.Argb(palette.Mint, 0f) },
            new CanvasGradientStop { Position = 0.5f, Color = SceneBrushes.Argb(glow, 0.9f) },
            new CanvasGradientStop { Position = 1f, Color = SceneBrushes.Argb(palette.Mint, 0f) },
        ]);
        _backdrop = new CanvasRadialGradientBrush(creator,
        [
            new CanvasGradientStop { Position = 0f, Color = SceneBrushes.Argb(palette.Background, 0.9f) },
            new CanvasGradientStop { Position = 0.6f, Color = SceneBrushes.Argb(palette.Background, 0.7f) },
            new CanvasGradientStop { Position = 1f, Color = SceneBrushes.Argb(palette.Background, 0f) },
        ]);
        _brushPalette = palette;
        _brushesDark = palette.IsDark;
    }
}
