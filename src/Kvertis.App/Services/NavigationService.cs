namespace Kvertis.App.Services;

public enum AppPage
{
    Main = 0,
    Settings,
    Pro,
    Licenses,
    About,
    Target,
    Convert,
}

/// <summary>Page navigation inside the main window's frame. The main page stays cached, so the queue view survives.</summary>
public interface INavigationService
{
    bool CanGoBack { get; }

    /// <summary>The page currently in the frame, null before the first navigation.</summary>
    AppPage? CurrentPage { get; }

    event EventHandler? Navigated;

    /// <param name="keepBackStack">
    /// False leaves no entry in the frame's back stack. The step header uses that, so the back button does not
    /// walk through every click on the three steps.
    /// </param>
    void Navigate(AppPage page, bool keepBackStack = true);

    void GoBack();
}
