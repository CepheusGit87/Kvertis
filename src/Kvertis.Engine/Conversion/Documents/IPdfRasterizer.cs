namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Renders one PDF page to a PNG file. The real implementation uses the PDF renderer of the
/// operating system and lives in Kvertis.Engine.Windows; the engine itself has none.
/// </summary>
public interface IPdfRasterizer
{
    bool IsAvailable { get; }

    /// <summary>Renders page <paramref name="pageIndex"/> (0-based) at <paramref name="dpi"/> into a PNG file.</summary>
    Task RasterizePageAsync(string pdfPath, int pageIndex, int dpi, string outputPngPath, CancellationToken ct);
}

/// <summary>Used where no system PDF renderer exists (Linux, tests). PDF → image then reports MissingSystemCodec.</summary>
public sealed class NullPdfRasterizer : IPdfRasterizer
{
    public static readonly NullPdfRasterizer Instance = new();

    public bool IsAvailable => false;

    public Task RasterizePageAsync(string pdfPath, int pageIndex, int dpi, string outputPngPath, CancellationToken ct) =>
        throw new NotSupportedException("No PDF rasterizer available on this platform.");
}
