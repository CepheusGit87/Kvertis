using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Kvertis.App.Services;

/// <summary>Reads UI strings from Strings/&lt;language&gt;/Resources.resw. Code never contains visible text.</summary>
public interface ILocalizer
{
    /// <summary>Returns the string for <paramref name="key"/>, or the key itself when it is missing (visible in testing).</summary>
    string Get(string key);

    string Format(string key, params object?[] args);
}

public sealed class ResourceLocalizer : ILocalizer
{
    private readonly ResourceLoader _loader = new();

    public string Get(string key)
    {
        try
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
