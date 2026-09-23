using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Services;

/// <summary>Simple modal messages. Texts are already localized by the caller.</summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string body);

    /// <summary>Returns true when the user chose the primary button.</summary>
    Task<bool> ConfirmAsync(string title, string body, string primaryButton, string closeButton);

    /// <summary>Shows a dialog built by a view (preview). Returns false when no window is available.</summary>
    Task<bool> ShowAsync(ContentDialog dialog);
}

public sealed class DialogService : IDialogService
{
    private readonly IWindowContext _window;
    private readonly ILocalizer _loc;
    private bool _isOpen;

    public DialogService(IWindowContext window, ILocalizer loc)
    {
        _window = window;
        _loc = loc;
    }

    public async Task ShowMessageAsync(string title, string body)
    {
        var dialog = Create(title, body);
        dialog.CloseButtonText = _loc.Get("Dialog_Ok_Button");
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowAsync(dialog);
    }

    public async Task<bool> ConfirmAsync(string title, string body, string primaryButton, string closeButton)
    {
        var dialog = Create(title, body);
        dialog.PrimaryButtonText = primaryButton;
        dialog.CloseButtonText = closeButton;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await ShowAsync(dialog) && dialog.Tag is ContentDialogResult.Primary;
    }

    public async Task<bool> ShowAsync(ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var root = _window.XamlRoot;
        if (root is null || _isOpen)
        {
            // Only one ContentDialog may be open at a time.
            return false;
        }
        dialog.XamlRoot = root;
        if (_window.Window?.Content is FrameworkElement content)
        {
            dialog.RequestedTheme = content.ActualTheme;
        }
        _isOpen = true;
        try
        {
            var result = await dialog.ShowAsync();
            dialog.Tag = result;
            return true;
        }
        finally
        {
            _isOpen = false;
        }
    }

    private static ContentDialog Create(string title, string body) => new()
    {
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
    };
}
