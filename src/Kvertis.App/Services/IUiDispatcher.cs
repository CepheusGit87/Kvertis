namespace Kvertis.App.Services;

/// <summary>
/// Runs work on the UI thread. The queue raises its events on worker threads. Kept in its own file without
/// any WinUI type, so view model logic that only needs to marshal a call can be tested without the app host.
/// </summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    void Post(Action action);
}
