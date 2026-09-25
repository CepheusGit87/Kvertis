using Windows.UI.ViewManagement;

namespace Kvertis.App.Services;

/// <summary>
/// The system settings that decide whether the interface may move: "reduce animations"
/// (<see cref="UISettings.AnimationsEnabled"/>) and high contrast. Every animation and every drawing surface
/// (galaxy, pixel swirl, white hole, transitions) asks this service instead of reading the settings itself
/// (docs/06-design.md, "Animationen" and "Barrierefreiheit"; ADR-018).
/// </summary>
public interface IMotionSettings
{
    /// <summary>False when the user turned animations off in Windows.</summary>
    bool AnimationsEnabled { get; }

    /// <summary>True while a high contrast theme is active.</summary>
    bool IsHighContrast { get; }

    /// <summary>True when nothing may move: animations off or high contrast.</summary>
    bool ReducedMotion { get; }

    /// <summary>Raised on the UI thread after one of the values changed.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// <see cref="IMotionSettings"/> from the Windows settings. The system raises its events on a worker thread;
/// the values are read there, then written and announced on the UI thread.
/// </summary>
public sealed class SystemMotionSettings : IMotionSettings
{
    private readonly IUiDispatcher _ui;
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings? _accessibility;

    private bool _animationsEnabled = true;
    private bool _isHighContrast;

    public SystemMotionSettings(IUiDispatcher ui)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));

        // AccessibilitySettings needs a window on some Windows versions; without it high contrast stays false
        // and the interface simply keeps its animations, which is the safe default.
        try
        {
            _accessibility = new AccessibilitySettings();
        }
        catch (Exception)
        {
            _accessibility = null;
        }

        _animationsEnabled = ReadAnimationsEnabled();
        _isHighContrast = ReadHighContrast();

        try
        {
            // AnimationsEnabledChanged only exists from Windows 10 2004 on; the app supports 1809 (ADR-001),
            // where the effects event below is the only signal.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                _uiSettings.AnimationsEnabledChanged += (_, _) => OnSystemChanged();
            }
            // Advanced effects (transparency, shadows) belong to the same group of settings and are read
            // together with the animation setting.
            _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => OnSystemChanged();
        }
        catch (Exception)
        {
            // Without the event the values stay as read at start; a restart picks up a change.
        }

        try
        {
            if (_accessibility is not null)
            {
                _accessibility.HighContrastChanged += (_, _) => OnSystemChanged();
            }
        }
        catch (Exception)
        {
            // Same as above.
        }
    }

    public bool AnimationsEnabled => _animationsEnabled;

    public bool IsHighContrast => _isHighContrast;

    public bool ReducedMotion => !_animationsEnabled || _isHighContrast;

    public event EventHandler? Changed;

    /// <summary>
    /// Runs on the worker thread that raised the system event: the values are read there, but written and
    /// announced on the UI thread, so the fields are only ever written by one thread.
    /// </summary>
    private void OnSystemChanged()
    {
        var animationsEnabled = ReadAnimationsEnabled();
        var isHighContrast = ReadHighContrast();
        _ui.Post(() =>
        {
            if (_animationsEnabled == animationsEnabled && _isHighContrast == isHighContrast)
            {
                return;
            }
            _animationsEnabled = animationsEnabled;
            _isHighContrast = isHighContrast;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool ReadAnimationsEnabled()
    {
        try
        {
            return _uiSettings.AnimationsEnabled;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private bool ReadHighContrast()
    {
        try
        {
            return _accessibility?.HighContrast ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
