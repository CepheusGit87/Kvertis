using System.Numerics;
using Kvertis.App.Scenes;

namespace Kvertis.App.Tests.Scenes;

/// <summary>Shared helpers for the scene tests: a fixed palette, a fixed seed and a fixed time step.</summary>
internal static class SceneTestData
{
    /// <summary>The dark tokens of <c>Themes/KvertisColors.xaml</c>.</summary>
    public static readonly ScenePalette Palette = new(
        Image: new SceneColor(0x6B, 0xA7, 0xFF),
        Audio: new SceneColor(0xB2, 0x8C, 0xFF),
        Video: new SceneColor(0xF2, 0xB4, 0x5A),
        Document: new SceneColor(0x4F, 0xC3, 0xE8),
        Model3D: new SceneColor(0xF0, 0x8F, 0xD0),
        Mint: new SceneColor(0x6F, 0xE0, 0xBF),
        Error: new SceneColor(0xF0, 0x7A, 0x6A),
        Ink: new SceneColor(0xE7, 0xEC, 0xEE),
        Background: new SceneColor(0x0C, 0x0F, 0x11),
        Muted: new SceneColor(0x8B, 0x95, 0x9C),
        IsDark: true);

    /// <summary>A 40 fps step: 0.025 s is exact in ticks, so the tests hit 0.1 s and 0.375 s precisely.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(0.025);

    public static GalaxyScene NewScene(float width = 700f, float height = 280f, int seed = 4711, GalaxyBudget? budget = null) =>
        new(new GalaxyLayout(width, height), Palette, new Random(seed), budget ?? new GalaxyBudget());

    /// <summary>Advances the scene in fixed steps; the sum is exactly <paramref name="seconds"/>.</summary>
    public static void Advance(GalaxyScene scene, double seconds, TimeSpan? step = null)
    {
        var dt = step ?? Step;
        var frames = (int)Math.Round(seconds / dt.TotalSeconds);
        for (var i = 0; i < frames; i++)
        {
            scene.Update(dt);
        }
    }

    /// <summary>The position a dust particle would have without any pointer or drag force.</summary>
    public static Vector2 DustBase(GalaxyScene scene, in GalaxyParticle particle) =>
        scene.OrbitPoint(
            particle.Orbit,
            scene.OrbitPhase[particle.Orbit] + particle.R1 * MathF.PI * 2f,
            (particle.R2 - 0.5f) * 14f * scene.Layout.Scale);
}
