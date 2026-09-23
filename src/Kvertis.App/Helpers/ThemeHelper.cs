using Kvertis.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Globalization;

namespace Kvertis.App.Helpers;

/// <summary>Applies theme and language choices from the settings.</summary>
public static class ThemeHelper
{
    public static void Apply(Window? window, AppTheme theme)
    {
        if (window?.Content is not FrameworkElement root)
        {
            return;
        }
        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Caption buttons (minimize, maximize, close) follow the app theme, not only the system theme.
        window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    /// <summary>
    /// Sets the UI language. Empty = follow Windows. Called before the first window exists; a change at runtime
    /// takes full effect after a restart.
    /// </summary>
    public static void ApplyLanguage(string? languageTag)
    {
        var tag = languageTag ?? string.Empty;
        try
        {
            // Stored per package by Windows; also read by the resource system of the Windows App SDK.
            ApplicationLanguages.PrimaryLanguageOverride = tag;
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Unpackaged debug runs have no package identity; the system language is used then.
        }
    }
}
