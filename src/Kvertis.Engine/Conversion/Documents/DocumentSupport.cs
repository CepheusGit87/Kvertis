using System.Diagnostics;
using System.IO.Packaging;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.IO;
using UglyToad.PdfPig.Exceptions;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Error mapping shared by the document converters and the document prober.</summary>
internal static class DocumentErrors
{
    /// <summary>
    /// Maps library exceptions to engine codes. Anything that says "encrypted" or "password" is
    /// treated as a protected file; parse failures are corrupt files.
    /// </summary>
    public static ConversionException Map(Exception ex, string path, string step)
    {
        switch (ex)
        {
            case ConversionException ce:
                return ce;
            case OperationCanceledException:
                return new ConversionException(ConversionErrorCode.Cancelled, path, step, inner: ex);
            case PdfDocumentEncryptedException:
                return new ConversionException(ConversionErrorCode.ProtectedFile, path, step, "encrypted pdf", ex);
        }

        if (MentionsProtection(ex))
        {
            return new ConversionException(ConversionErrorCode.ProtectedFile, path, step, ex.Message, ex);
        }

        return ex switch
        {
            UglyToad.PdfPig.Core.PdfDocumentFormatException or UglyToad.PdfPig.Core.PdfDocumentStackDepthException
                or UglyToad.PdfPig.Fonts.InvalidFontFormatException or UglyToad.PdfPig.Fonts.CorruptCompressedDataException or OpenXmlPackageException or FileFormatException or InvalidDataException
                or XmlException or InvalidOperationException or FormatException or ArgumentException
                or NotSupportedException or EndOfStreamException or IndexOutOfRangeException or NullReferenceException
                or KeyNotFoundException or OverflowException
                => new ConversionException(ConversionErrorCode.CorruptFile, path, step, ex.Message, ex),
            _ => ConversionException.From(ex, path, step),
        };
    }

    private static bool MentionsProtection(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || m.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Detects encrypted office documents. An encrypted DOCX/XLSX/PPTX is not a ZIP package but an OLE
/// compound file holding the streams "EncryptionInfo" and "EncryptedPackage". We only look for those
/// stream names; nothing is ever decrypted.
/// </summary>
public static class OfficeProtection
{
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] EncryptedPackageName = Encoding.Unicode.GetBytes("EncryptedPackage");
    private static readonly byte[] EncryptionInfoName = Encoding.Unicode.GetBytes("EncryptionInfo");

    /// <summary>True when the file is an OLE compound file (legacy DOC/XLS/PPT or encrypted OOXML).</summary>
    public static bool IsOleContainer(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[8];
            return fs.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8 && header.SequenceEqual(OleSignature);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the file is an OLE container that holds an encrypted OOXML package.</summary>
    public static bool IsEncryptedOfficeFile(string path)
    {
        if (!IsOleContainer(path))
        {
            return false;
        }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Contains(fs, EncryptedPackageName) || (fs.Seek(0, SeekOrigin.Begin) == 0 && Contains(fs, EncryptionInfoName));
    }

    private static bool Contains(Stream stream, byte[] needle)
    {
        var buffer = new byte[1 << 16];
        var carry = 0;
        int read;
        while ((read = stream.Read(buffer, carry, buffer.Length - carry)) > 0)
        {
            var total = carry + read;
            if (buffer.AsSpan(0, total).IndexOf(needle) >= 0)
            {
                return true;
            }
            carry = Math.Min(needle.Length - 1, total);
            Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
        }
        return false;
    }

    /// <summary>Throws ProtectedFile for encrypted office documents and UnsupportedFormat for other OLE files.</summary>
    internal static void EnsureOpenable(string path, string step)
    {
        if (IsEncryptedOfficeFile(path))
        {
            throw new ConversionException(ConversionErrorCode.ProtectedFile, path, step, "encrypted office file");
        }
        if (IsOleContainer(path))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, step, "legacy binary office format");
        }
    }
}

/// <summary>
/// A set of committed output files for converters that produce more than one file (pages, sheets).
/// Every file goes through <see cref="ConversionOutput"/>. If the job fails or is cancelled before
/// <see cref="Complete"/>, the files committed so far are removed again.
/// </summary>
internal sealed class OutputSet : IDisposable
{
    private readonly List<string> _files = [];
    private bool _completed;

    public IReadOnlyList<string> Files => _files;
    public long TotalBytes { get; private set; }

    public void Write(string finalPath, long expectedBytes, Action<string> writeTemp)
    {
        using var output = ConversionOutput.Begin(finalPath, expectedBytes);
        writeTemp(output.TempPath);
        TotalBytes += output.Commit();
        _files.Add(finalPath);
    }

    public async Task WriteAsync(string finalPath, long expectedBytes, Func<string, Task> writeTemp)
    {
        using var output = ConversionOutput.Begin(finalPath, expectedBytes);
        await writeTemp(output.TempPath).ConfigureAwait(false);
        TotalBytes += output.Commit();
        _files.Add(finalPath);
    }

    public ConversionResult Complete(InputInfo input, Stopwatch watch)
    {
        _completed = true;
        return new ConversionResult(_files[0], input.SizeBytes, TotalBytes, watch.Elapsed);
    }

    public void Dispose()
    {
        if (_completed)
        {
            return;
        }
        foreach (var file in _files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}

internal static class DocumentPaths
{
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>"out/report.png", 0 → "out/report_p001.png".</summary>
    public static string PagePath(string outputPath, int pageIndex) =>
        Suffixed(outputPath, "_p" + (pageIndex + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// <see cref="PagePath"/>, made unique like every other output name: an existing "report_p001.png"
    /// is never overwritten, the page becomes "report_p001_1.png" instead.
    /// </summary>
    public static string UniquePagePath(string outputPath, int pageIndex)
    {
        var page = PagePath(outputPath, pageIndex);
        return Naming.OutputNamePattern.EnsureUnique(Path.GetDirectoryName(page) ?? string.Empty, Path.GetFileName(page));
    }

    /// <summary>"out/data.csv", "Sheet 1" → "out/data_Sheet 1.csv".</summary>
    public static string Suffixed(string outputPath, string suffix)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, stem + suffix + Path.GetExtension(outputPath));
    }

    /// <summary>Makes a sheet or slide name safe as part of a file name on every platform.</summary>
    public static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(c < 0x20 || Array.IndexOf(InvalidNameChars, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim().TrimEnd('.');
        if (result.Length > 64)
        {
            result = result[..64];
        }
        return result.Length == 0 ? "_" : result;
    }
}

internal static class ProgressExtensions
{
    public static void ReportUnit(this IProgress<ConversionProgress>? progress, int done, int total)
    {
        if (progress is null)
        {
            return;
        }
        var fraction = total <= 0 ? 0.5 : Math.Clamp((double)done / total, 0, 1) * 0.95;
        progress.Report(new ConversionProgress(fraction, ConversionPhase.Converting));
    }
}

internal static class DocumentText
{
    /// <summary>UTF-8 with BOM: CSV and TXT outputs open correctly in Windows editors and spreadsheets.</summary>
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>UTF-8 without BOM for Markdown and HTML (HTML declares its charset).</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Plain text uses CRLF (Windows convention); Markdown and HTML use LF.</summary>
    public static StreamWriter CreateWriter(string path, Encoding encoding, string newLine = "\n") =>
        new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16), encoding, 1 << 16)
        {
            NewLine = newLine,
        };

    public const string Crlf = "\r\n";
}

internal static class DocumentTasks
{
    /// <summary>
    /// Runs synchronous library work on the thread pool. The work checks the token itself between
    /// units; every failure leaves as a ConversionException (cancellation as Cancelled).
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<T> work, string path, string step)
    {
        try
        {
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, path, step);
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> work, string path, string step)
    {
        try
        {
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, path, step);
        }
    }
}
