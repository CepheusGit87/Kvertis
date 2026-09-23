namespace Kvertis.Engine.Abstractions;

/// <summary>
/// The only exception type the engine lets escape to callers. Carries a code, the file and the
/// step that failed, and a short (truncated) tool output for diagnostics. No user-facing text.
/// </summary>
public sealed class ConversionException : Exception
{
    private const int MaxDetailLength = 2000;

    public ConversionErrorCode Code { get; }
    public string? FilePath { get; }
    public string? Step { get; }
    public string? Detail { get; }

    public ConversionException(ConversionErrorCode code, string? filePath = null, string? step = null, string? detail = null, Exception? inner = null)
        : base($"{code}: {step ?? "conversion"} failed for '{filePath ?? "?"}'", inner)
    {
        Code = code;
        FilePath = filePath;
        Step = step;
        Detail = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail;
    }

    public ConversionException() : this(ConversionErrorCode.Unknown) { }

    public ConversionException(string message) : this(ConversionErrorCode.Unknown, detail: message) { }

    public ConversionException(string message, Exception innerException) : this(ConversionErrorCode.Unknown, detail: message, inner: innerException) { }

    /// <summary>Maps common BCL exceptions to a code. Used by converters as a last resort.</summary>
    public static ConversionException From(Exception ex, string? filePath, string step)
    {
        return ex switch
        {
            ConversionException ce => ce,
            OperationCanceledException => new ConversionException(ConversionErrorCode.Cancelled, filePath, step, inner: ex),
            TimeoutException => new ConversionException(ConversionErrorCode.Timeout, filePath, step, ex.Message, ex),
            FileNotFoundException or DirectoryNotFoundException => new ConversionException(ConversionErrorCode.InputNotReadable, filePath, step, ex.Message, ex),
            UnauthorizedAccessException => new ConversionException(ConversionErrorCode.OutputNotWritable, filePath, step, ex.Message, ex),
            IOException io when IsDiskFull(io) => new ConversionException(ConversionErrorCode.InsufficientDiskSpace, filePath, step, ex.Message, ex),
            _ => new ConversionException(ConversionErrorCode.Unknown, filePath, step, ex.Message, ex),
        };
    }

    private static bool IsDiskFull(IOException io)
    {
        // HRESULT 0x80070070 (ERROR_DISK_FULL) / 0x80070027 (ERROR_HANDLE_DISK_FULL); ENOSPC on Unix is 28.
        var hr = io.HResult & 0xFFFF;
        return hr is 0x70 or 0x27 || (!OperatingSystem.IsWindows() && io.HResult == 28);
    }
}
