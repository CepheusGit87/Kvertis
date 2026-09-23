using System.Text.RegularExpressions;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>Turns a failed ffmpeg/ffprobe run into a <see cref="ConversionException"/> with the most specific code.</summary>
public static partial class FfmpegErrorMapper
{
    public static ConversionException Map(ProcessOutcome outcome, string? filePath, string step)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var stderr = outcome.StandardErrorTail ?? string.Empty;

        if (outcome.TimedOut)
        {
            return new ConversionException(ConversionErrorCode.Timeout, filePath, step, stderr);
        }

        return new ConversionException(Classify(stderr, filePath), filePath, step, stderr);
    }

    // Phrases ffmpeg/ffprobe print for protected content. Matched only outside echoed paths, so a file
    // called "encrypted_notes.mp4" is not mistaken for a protected one.
    private static readonly (string Phrase, StringComparison Comparison)[] ProtectionPhrases =
    [
        ("is encrypted", StringComparison.OrdinalIgnoreCase),
        ("Encrypted", StringComparison.Ordinal),
        ("DRM protected", StringComparison.OrdinalIgnoreCase),
        ("Permission to decrypt", StringComparison.OrdinalIgnoreCase),
        ("decryption key", StringComparison.OrdinalIgnoreCase),
        ("cenc", StringComparison.Ordinal),
    ];

    /// <summary>Maps well-known ffmpeg stderr fragments to an error code; falls back to ToolFailed.</summary>
    /// <param name="inputPath">The input file; stderr lines that mention it (or its name) are ignored for the protection check.</param>
    public static ConversionErrorCode Classify(string stderr, string? inputPath = null)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        if (stderr.Contains("No space left", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.InsufficientDiskSpace;
        }
        if (MentionsProtection(stderr, inputPath))
        {
            return ConversionErrorCode.ProtectedFile;
        }
        if (stderr.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.MissingSystemCodec;
        }
        if (stderr.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.CorruptFile;
        }
        return ConversionErrorCode.ToolFailed;
    }

    private static bool MentionsProtection(string stderr, string? inputPath)
    {
        var echoes = new List<string>(2);
        if (!string.IsNullOrEmpty(inputPath))
        {
            echoes.Add(inputPath);
            if (Path.GetFileName(inputPath) is { Length: > 0 } name)
            {
                echoes.Add(name);
            }
        }
        foreach (var rawLine in stderr.Split('\n'))
        {
            if (echoes.Exists(e => rawLine.Contains(e, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            // ffmpeg quotes every path it echoes ("from '...'", "to '...'"); output paths must not count either.
            var line = QuotedText().Replace(rawLine, "''");
            foreach (var (phrase, comparison) in ProtectionPhrases)
            {
                if (line.Contains(phrase, comparison))
                {
                    return true;
                }
            }
        }
        return false;
    }

    [GeneratedRegex("'[^']*'")]
    private static partial Regex QuotedText();
}
