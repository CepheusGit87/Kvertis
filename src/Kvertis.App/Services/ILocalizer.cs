namespace Kvertis.App.Services;

/// <summary>
/// Reads UI strings from Strings/&lt;language&gt;/Resources.resw. Code never contains visible text (ADR-004).
/// The interface lives in its own file without any WinUI type, so view models that only format text can be
/// tested without the app host.
/// </summary>
public interface ILocalizer
{
    /// <summary>Returns the string for <paramref name="key"/>, or the key itself when it is missing (visible in testing).</summary>
    string Get(string key);

    string Format(string key, params object?[] args);
}
