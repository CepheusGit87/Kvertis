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
    };

    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack == true;

    public event EventHandler? Navigated;

    public void Attach(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _frame.Navigated += (_, _) => Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void Navigate(AppPage page)
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
        _frame.Navigate(type, null, new DrillInNavigationTransitionInfo());
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }
}
