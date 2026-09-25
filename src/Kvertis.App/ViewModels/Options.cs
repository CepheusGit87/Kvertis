using Kvertis.Engine.Abstractions;

namespace Kvertis.App.ViewModels;

/// <summary>An output format the user can pick for one file.</summary>
public sealed class FormatOption
{
    public FormatOption(FormatId id, string label, bool isSuggested)
    {
        Id = id;
        Label = label;
        IsSuggested = isSuggested;
    }

    public FormatId Id { get; }

    /// <summary>Format name such as "JPG" (a format designation, not translated).</summary>
    public string Label { get; }

    public bool IsSuggested { get; }

    public override string ToString() => Label;
}

/// <summary>A localized choice in a combo box (unit, language, theme).</summary>
public sealed class ChoiceOption
{
    public ChoiceOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
