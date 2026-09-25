using System.Numerics;
using Kvertis.App.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Kvertis.App.Animations;

/// <summary>
/// Composition animations from docs/06-design.md ("Animationen"). Whether anything may move is decided by
/// <see cref="IMotionSettings"/> ("reduce animations" and high contrast); without it only fades are used.
/// </summary>
public static class CardAnimations
{
    private static IMotionSettings? _motion;

    private static readonly TimeSpan DropZoneDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EntranceStagger = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan TiltDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CheckDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan GlowDuration = TimeSpan.FromMilliseconds(1600);

    private const float DropZoneScale = 1.02f;
    private const float EntranceOffset = 12f;
    private const float TiltDegrees = 6f;
    private const int MaxStaggerSteps = 10;

    /// <summary>Set once by <see cref="App"/> after the container exists.</summary>
    public static void Use(IMotionSettings motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        _motion = motion;
    }

    /// <summary>False while the system asks for reduced motion or high contrast.</summary>
    public static bool AnimationsEnabled => _motion is null || !_motion.ReducedMotion;

    /// <summary>Drop zone lifts slightly while files are dragged over it (scale 1.02, 150 ms).</summary>
    public static void DropZoneHover(UIElement element, bool isOver)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        if (!AnimationsEnabled)
        {
            Fade(visual, isOver ? 0.85f : 1f, DropZoneDuration);
            return;
        }
        CenterOn(element, visual);
        var target = isOver ? DropZoneScale : 1f;
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3(target, target, 1f), compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        scale.Duration = DropZoneDuration;
        visual.StartAnimation("Scale", scale);
    }

    /// <summary>A new card slides in from 12 px below and fades in (250 ms, staggered 40 ms per card).</summary>
    public static void CardEntrance(UIElement element, int index)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var delay = TimeSpan.FromTicks(EntranceStagger.Ticks * Math.Clamp(index, 0, MaxStaggerSteps));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = EntranceDuration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);

        if (!AnimationsEnabled)
        {
            return;
        }
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(0f, EntranceOffset, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        slide.Duration = EntranceDuration;
        slide.DelayTime = delay;
        slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>At start the card tilts 6 degrees around the X axis and back (300 ms, once).</summary>
    public static void StartTilt(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!AnimationsEnabled)
        {
            return;
        }
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        CenterOn(element, visual);
        visual.RotationAxis = new Vector3(1f, 0f, 0f);

        var tilt = compositor.CreateScalarKeyFrameAnimation();
        tilt.InsertKeyFrame(0f, 0f);
        tilt.InsertKeyFrame(0.4f, TiltDegrees);
        tilt.InsertKeyFrame(1f, 0f);
        tilt.Duration = TiltDuration;
        visual.StartAnimation("RotationAngleInDegrees", tilt);
    }

    /// <summary>
    /// The light sweep on a running card: <paramref name="glow"/> (a narrow highlight inside the card) wanders
    /// along <paramref name="track"/> repeatedly. Without animations it simply stays visible.
    /// </summary>
    public static void StartProgressGlow(UIElement glow, FrameworkElement track)
    {
        ArgumentNullException.ThrowIfNull(glow);
        ArgumentNullException.ThrowIfNull(track);
        var visual = ElementCompositionPreview.GetElementVisual(glow);
        var compositor = visual.Compositor;
        visual.Opacity = 1f;
        if (!AnimationsEnabled)
        {
            return;
        }
        ElementCompositionPreview.SetIsTranslationEnabled(glow, true);
        var width = (float)Math.Max(0, track.ActualWidth - glow.ActualSize.X);
        var sweep = compositor.CreateVector3KeyFrameAnimation();
        sweep.InsertKeyFrame(0f, Vector3.Zero);
        sweep.InsertKeyFrame(1f, new Vector3(width, 0f, 0f));
        sweep.Duration = GlowDuration;
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", sweep);

        var pulse = compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0f, 0.2f);
        pulse.InsertKeyFrame(0.5f, 1f);
        pulse.InsertKeyFrame(1f, 0.2f);
        pulse.Duration = GlowDuration;
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    public static void StopProgressGlow(UIElement glow)
    {
        ArgumentNullException.ThrowIfNull(glow);
        var visual = ElementCompositionPreview.GetElementVisual(glow);
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        visual.Opacity = 0f;
    }

    /// <summary>The completion check mark scales from 0 to 1 with a slight overshoot (200 ms).</summary>
    public static void CheckMarkPop(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        if (!AnimationsEnabled)
        {
            visual.Opacity = 0f;
            Fade(visual, 1f, CheckDuration);
            return;
        }
        CenterOn(element, visual);
        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0f, new Vector3(0f, 0f, 1f));
        pop.InsertKeyFrame(1f, Vector3.One, compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f)));
        pop.Duration = CheckDuration;
        visual.StartAnimation("Scale", pop);
    }

    private static void CenterOn(UIElement element, Visual visual)
    {
        var size = element.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2f, size.Y / 2f, 0f);
    }

    private static void Fade(Visual visual, float to, TimeSpan duration)
    {
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to);
        fade.Duration = duration;
        visual.StartAnimation("Opacity", fade);
    }
}
