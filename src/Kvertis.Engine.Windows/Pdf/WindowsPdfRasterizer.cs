using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Windows.Data.Pdf;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;

namespace Kvertis.Engine.Windows.Pdf;

/// <summary>Local contract; wired to the engine's PDF rasterizer abstraction by a one-line adapter.</summary>
internal interface IWindowsPdfRasterizer
{
    bool IsAvailable { get; }

    Task RasterizePageAsync(string pdfPath, int pageIndex, int dpi, string outputPngPath, CancellationToken ct);
}

/// <summary>
/// Renders single PDF pages to PNG with the built-in <c>Windows.Data.Pdf</c> renderer. Never supplies a
/// password: encrypted documents are reported as <see cref="ConversionErrorCode.ProtectedFile"/>; the engine's
/// own PDF probe remains the authority on encryption and runs before this class.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsPdfRasterizer : IWindowsPdfRasterizer
{
    public const int MinDpi = 1;
    public const int MaxDpi = 1200;

    private const string Step = "pdf-rasterize";
    private const int BufferSize = 81920;

    // HRESULT_FROM_WIN32(ERROR_WRONG_PASSWORD): what the platform's PDF sample checks for protected files.
    private const int WrongPassword = unchecked((int)0x8007052B);

    // E_FAIL: the same sample treats this as "not a valid PDF".
    private const int GenericFail = unchecked((int)0x80004005);

    public bool IsAvailable
    {
        get
        {
            try
            {
                return ApiInformation.IsTypePresent("Windows.Data.Pdf.PdfDocument");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public async Task RasterizePageAsync(string pdfPath, int pageIndex, int dpi, string outputPngPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(pdfPath);
        ArgumentException.ThrowIfNullOrEmpty(outputPngPath);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, MinDpi);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dpi, MaxDpi);

        var outputCreated = false;
        try
        {
            ct.ThrowIfCancellationRequested();

            await using var input = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            using var inputRas = input.AsRandomAccessStream();
            var document = await PdfDocument.LoadFromStreamAsync(inputRas).AsTask(ct).ConfigureAwait(false);

            if (pageIndex >= document.PageCount)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, pdfPath, Step, $"page {pageIndex} of {document.PageCount}");
            }

            using var page = document.GetPage((uint)pageIndex);

            // PdfPage.Size is in DIPs (1/96 inch).
            var width = Math.Clamp(Math.Round(page.Size.Width * dpi / 96.0), 1.0, uint.MaxValue);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)width,
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };

            await using var output = new FileStream(outputPngPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
            outputCreated = true;
            using var outputRas = output.AsRandomAccessStream();
            await page.RenderToStreamAsync(outputRas, options).AsTask(ct).ConfigureAwait(false);
            await outputRas.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (outputCreated)
            {
                TryDelete(outputPngPath);
            }

            throw ex switch
            {
                ConversionException ce => ce,
                OperationCanceledException => ConversionException.From(ex, pdfPath, Step),
                _ when ex.HResult == WrongPassword
                    => new ConversionException(ConversionErrorCode.ProtectedFile, pdfPath, Step, $"0x{ex.HResult:X8}", ex),
                _ when ex.HResult == GenericFail
                    => new ConversionException(ConversionErrorCode.CorruptFile, pdfPath, Step, $"0x{ex.HResult:X8}", ex),
                _ => ConversionException.From(ex, pdfPath, Step),
            };
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
