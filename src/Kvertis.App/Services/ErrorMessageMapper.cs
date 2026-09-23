using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Services;

/// <summary>A localized error: what happened, then what the user can do (docs/06-design.md, "Sprache").</summary>
public sealed record ErrorMessage(string Title, string Body);

/// <summary>
/// Maps <see cref="ConversionErrorCode"/> to the resource keys Error_{Code}_Title and Error_{Code}_Body (ADR-004).
/// Every enum value has both keys in DE and EN; tools/compliance/check-resw.py verifies that.
/// </summary>
public sealed class ErrorMessageMapper
{
    private readonly ILocalizer _loc;

    public ErrorMessageMapper(ILocalizer loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
    }

    public static string TitleKey(ConversionErrorCode code) => $"Error_{code}_Title";

    public static string BodyKey(ConversionErrorCode code) => $"Error_{code}_Body";

    public ErrorMessage Map(ConversionErrorCode code)
    {
        if (!Enum.IsDefined(code))
        {
            code = ConversionErrorCode.Unknown;
        }
        return new ErrorMessage(_loc.Get(TitleKey(code)), _loc.Get(BodyKey(code)));
    }
}
