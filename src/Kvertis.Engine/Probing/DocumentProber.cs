using DocumentFormat.OpenXml.Packaging;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Probing;

/// <summary>
/// Deep probe for documents, run during detection so the UI can show problems before a job starts.
/// PDF: page count; encrypted → ProtectedFile; unreadable → CorruptFile.
/// DOCX/XLSX/PPTX: encrypted (OLE container or package error naming encryption) → ProtectedFile;
/// broken package → CorruptFile. PageCount is the slide count for PPTX, the worksheet count for XLSX
/// and unknown (null) for DOCX. Plain text formats are not probed.
/// </summary>
public sealed class DocumentProber : IMediaProber
{
    private readonly TimeSpan _timeout;

    public DocumentProber(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? InputLimits.AnalysisTimeoutFor(MediaKind.Document);
    }

    public bool Supports(MediaKind kind) => kind == MediaKind.Document;

    public async Task<InputInfo> ProbeAsync(InputInfo info, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        var format = info.Format;
        var isOffice = format == FormatRegistry.Docx || format == FormatRegistry.Xlsx || format == FormatRegistry.Pptx
                       || format == FormatRegistry.LegacyOffice;
        if (format != FormatRegistry.Pdf && !isOffice)
        {
            return info;
        }

        var work = Task.Run(() => format == FormatRegistry.Pdf ? ProbePdf(info) : ProbeOffice(info), CancellationToken.None);
        try
        {
            return await work.WaitAsync(_timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            ObserveLater(work);
            throw new ConversionException(ConversionErrorCode.Timeout, info.Path, "probe-document", ex.Message, ex);
        }
        catch (OperationCanceledException ex)
        {
            ObserveLater(work);
            throw new ConversionException(ConversionErrorCode.Cancelled, info.Path, "probe-document", inner: ex);
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, info.Path, "probe-document");
        }
    }

    private static InputInfo ProbePdf(InputInfo info)
    {
        using var document = PdfInspection.OpenUnencrypted(info.Path, "probe-document");
        int pages;
        try
        {
            pages = document.NumberOfPages;
        }
        catch (Exception ex)
        {
            throw DocumentErrors.Map(ex, info.Path, "probe-document");
        }
        if (pages <= 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, info.Path, "probe-document", "no pages");
        }
        return info with { PageCount = pages };
    }

    private static InputInfo ProbeOffice(InputInfo info)
    {
        OfficeProtection.EnsureOpenable(info.Path, "probe-document");
        if (info.Format == FormatRegistry.LegacyOffice)
        {
            return info;
        }
        try
        {
            if (info.Format == FormatRegistry.Docx)
            {
                using var doc = WordprocessingDocument.Open(info.Path, false);
                _ = doc.MainDocumentPart ?? throw new InvalidDataException("main document part missing");
                return info;
            }
            if (info.Format == FormatRegistry.Xlsx)
            {
                using var doc = SpreadsheetDocument.Open(info.Path, false);
                var workbook = doc.WorkbookPart ?? throw new InvalidDataException("workbook part missing");
                var sheets = workbook.Workbook?.Sheets?.ChildElements.Count ?? 0;
                return info with { PageCount = sheets };
            }
            using (var doc = PresentationDocument.Open(info.Path, false))
            {
                var presentation = doc.PresentationPart ?? throw new InvalidDataException("presentation part missing");
                var slides = presentation.Presentation?.SlideIdList?.ChildElements.Count ?? 0;
                return info with { PageCount = slides };
            }
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, info.Path, "probe-document");
        }
    }

    /// <summary>The library call cannot be aborted; make sure its eventual failure is observed.</summary>
    private static void ObserveLater(Task task) =>
        task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
