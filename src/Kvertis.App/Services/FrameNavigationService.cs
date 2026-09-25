using Kvertis.App.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Kvertis.App.Services;

/// <summary><see cref="INavigationService"/> over the main window's <see cref="Frame"/>.</summary>
public sealed class FrameNavigationService : INavigationService
{
    private static readonly Dictionary<AppPage, Type> Pages = new()
    {
        [AppPage.Main] = typeof(MainPage),
        [AppPage.Settings] = typeof(SettingsPage),
        [AppPage.Pro] = typeof(ProPage),
        [AppPage.Licenses] = typeof(LicensesPage),
        [AppPage.About] = typeof(AboutPage),
        [AppPage.Target] = typeof(TargetPage),
        [AppPage.Convert] = typeof(ConvertPage),
    };

    private readonly IMotionSettings _motion;

    private Frame? _frame;
    private bool _dropNextBackEntry;

    public FrameNavigationService(IMotionSettings motion)
    {
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
    }

    public bool CanGoBack => _frame?.CanGoBack == true;

    public AppPage? CurrentPage
    {
        get
        {
            var type = _frame?.CurrentSourcePageType;
            if (type is null)
            {
                return null;
            }
            foreach (var pair in Pages)
            {
                if (pair.Value == type)
                {
                    return pair.Key;
                }
            }
            return null;
        }
    }

    public event EventHandler? Navigated;

    public void Attach(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _frame.Navigated += OnFrameNavigated;
    }

    public void Navigate(AppPage page, bool keepBackStack = true) => Navigate(page, keepBackStack, suppressTransition: false);

    /// <summary>
    /// <paramref name="suppressTransition"/> exchanges the pages at once, without the Fluent transition: the
    /// drawn overlay of ADR-023 flies while the new page is already in the frame.
    /// </summary>
    public void Navigate(AppPage page, bool keepBackStack, bool suppressTransition)
    {
        if (_frame is null)
        {
            return;
        }
        var type = Pages[page];
        if (_frame.CurrentSourcePageType == type)
        {
            return;
        }
        // With "reduce animations" or high contrast the pages are exchanged without a transition (ADR-018).
        NavigationTransitionInfo transition = _motion.ReducedMotion || suppressTransition
            ? new SuppressNavigationTransitionInfo()
            : new DrillInNavigationTransitionInfo();
        _dropNextBackEntry = !keepBackStack;
        if (!_frame.Navigate(type, null, transition))
        {
            _dropNextBackEntry = false;
        }
    }

    private void OnFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_dropNextBackEntry)
        {
            _dropNextBackEntry = false;
            // Remove the entry this navigation just pushed, before anyone reads CanGoBack.
            if (_frame is not null && _frame.BackStack.Count > 0)
            {
                _frame.BackStack.RemoveAt(_frame.BackStack.Count - 1);
            }
        }
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }
}
