namespace Kvertis.App.Services;

public enum AppPage
{
    Main = 0,
    Settings,
    Pro,
    Licenses,
    About,
}

/// <summary>Page navigation inside the main window's frame. The main page stays cached, so the queue view survives.</summary>
public interface INavigationService
{
    bool CanGoBack { get; }

    event EventHandler? Navigated;

    void Navigate(AppPage page);

    void GoBack();
}
