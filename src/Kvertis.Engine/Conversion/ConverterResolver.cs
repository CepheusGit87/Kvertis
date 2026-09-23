using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion;

/// <summary>Picks the first registered converter that supports the input/output pair. Order of registration is priority.</summary>
public sealed class ConverterResolver : IConverterResolver
{
    private readonly List<IConverter> _converters;

    public ConverterResolver(IEnumerable<IConverter> converters)
    {
        _converters = converters.ToList();
    }

    public IReadOnlyList<IConverter> All => _converters;

    public IConverter? Resolve(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _converters.FirstOrDefault(c => c.Supports(input, output));
    }
}
