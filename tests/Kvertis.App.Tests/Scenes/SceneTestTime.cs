using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Tests.Scenes;

/// <summary>Time and plan helpers for the transition, swirl and finale tests.</summary>
internal static class SceneTestTime
{
    /// <summary>Calls <paramref name="update"/> until exactly <paramref name="seconds"/> have passed, in steps of at most <paramref name="step"/>.</summary>
    public static void Run(Action<TimeSpan> update, double seconds, TimeSpan? step = null)
    {
        var dt = step ?? SceneTestData.Step;
        var remaining = TimeSpan.FromSeconds(seconds);
        while (remaining > TimeSpan.Zero)
        {
            var next = remaining < dt ? remaining : dt;
            update(next);
            remaining -= next;
        }
    }

    public static void Run(TransitionScene scene, double seconds) => Run(scene.Update, seconds);

    public static void Run(SwirlScene scene, double seconds, TimeSpan? step = null) => Run(scene.Update, seconds, step);

    /// <summary>A fixed id, so two runs of a test build identical scenes.</summary>
    public static Guid Id(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static readonly MediaKind[] Kinds = [MediaKind.Image, MediaKind.Audio, MediaKind.Video, MediaKind.Document, MediaKind.Model3D];

    /// <summary>A plan of <paramref name="count"/> files cycling through the five kinds.</summary>
    public static List<SwirlFileSpec> Files(int count, Func<int, bool>? ownLocation = null, Func<int, MediaKind>? kind = null)
    {
        var files = new List<SwirlFileSpec>(count);
        for (var i = 0; i < count; i++)
        {
            files.Add(new SwirlFileSpec(
                Id(i + 1),
                kind?.Invoke(i) ?? Kinds[i % Kinds.Length],
                "PNG",
                "WEBP",
                $"file-{i}.png",
                ownLocation?.Invoke(i) ?? false));
        }

        return files;
    }

    public static SwirlScene NewSwirl(float width = 1000f, float height = 420f, int seed = 4711, SwirlBudget? budget = null) =>
        new(width, height, SceneTestData.Palette, new Random(seed), budget ?? new SwirlBudget());
}
