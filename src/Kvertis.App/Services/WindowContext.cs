using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kvertis.App.Services;

/// <summary>
/// The main window and its UI thread. Set once by <see cref="App"/> after the window exists; services that
/// need a window handle (pickers, Store purchase) or a XamlRoot (dialogs) read it from here.
/// </summary>
public interface IWindowContext
{
    Window? Window { get; }

    /// <summary>Win32 handle of the main window, 0 before it exists.</summary>
    nint Handle { get; }

    XamlRoot? XamlRoot { get; }
}

public sealed class WindowContext : IWindowContext
{
    public Window? Window { get; private set; }

    public nint Handle { get; private set; }

    public XamlRoot? XamlRoot => Window?.Content?.XamlRoot;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Window = window;
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
    }
}

public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public UiDispatcher(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public bool HasThreadAccess => _queue.HasThreadAccess;

    /// <summary>Always queues, even on the UI thread, so handlers never run inside the caller's operation.</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.TryEnqueue(() => action());
    }
}
