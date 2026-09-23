using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.IO;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// 3D models between STL, 3MF, OBJ, PLY and glTF/GLB (ADR-016). Own parsers and writers, no library. Only the
/// triangle geometry is carried over; colors, materials, textures and all metadata are dropped (so metadata is
/// always removed, whatever the setting). Units: millimeters, Z up; glTF is converted to and from meters, Y up.
/// Linked files (OBJ material files, glTF images) are never opened; external glTF buffers only from the model's
/// own folder.
/// </summary>
public sealed class ModelConverter : IConverter
{
    private static readonly HashSet<FormatId> Readable =
        [FormatRegistry.Stl, FormatRegistry.ThreeMf, FormatRegistry.Obj, FormatRegistry.Ply, FormatRegistry.Glb, FormatRegistry.Gltf];

    private static readonly HashSet<FormatId> Writable =
        [FormatRegistry.Stl, FormatRegistry.ThreeMf, FormatRegistry.Obj, FormatRegistry.Ply, FormatRegistry.Glb];

    public string Name => "model3d";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Kind == MediaKind.Model3D && Readable.Contains(input.Format) && Writable.Contains(output);
    }

    public Task<ConversionResult> ConvertAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Supports(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "model", $"{input.Format} -> {settings.Output}");
        }
        return RunAsync(() => Convert(input, outputPath, settings.Output, progress, ct), input.Path);
    }

    /// <summary>No preview in this version; a 3D preview would need a renderer.</summary>
    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private static ConversionResult Convert(InputInfo input, string outputPath, FormatId output, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        ct.ThrowIfCancellationRequested();

        var mesh = Read(input, ct);
        progress?.Report(new ConversionProgress(0.5, ConversionPhase.Converting));
        ct.ThrowIfCancellationRequested();

        // Rough upper bound for the disk check: text OBJ is the largest writer (~40 bytes per vertex and face).
        var expected = (mesh.Vertices.Count + mesh.TriangleCount) * 40L;
        using var target = ConversionOutput.Begin(outputPath, expected);
        Write(mesh, output, target.TempPath, ct);
        progress?.Report(new ConversionProgress(0.95, ConversionPhase.Finalizing));
        var bytes = target.Commit();
        progress?.Report(ConversionProgress.Complete);
        return new ConversionResult(outputPath, input.SizeBytes, bytes, watch.Elapsed);
    }

    internal static Mesh Read(InputInfo input, CancellationToken ct)
    {
        if (input.Format == FormatRegistry.Stl)
        {
            return StlFormat.Read(input.Path, ct);
        }
        if (input.Format == FormatRegistry.ThreeMf)
        {
            return ThreeMfFormat.Read(input.Path, ct);
        }
        if (input.Format == FormatRegistry.Obj)
        {
            return ObjFormat.Read(input.Path, ct);
        }
        if (input.Format == FormatRegistry.Ply)
        {
            return PlyFormat.Read(input.Path, ct);
        }
        return GltfFormat.Read(input.Path, ct);
    }

    internal static void Write(Mesh mesh, FormatId output, string path, CancellationToken ct)
    {
        if (output == FormatRegistry.Stl)
        {
            StlFormat.WriteBinary(mesh, path, ct);
        }
        else if (output == FormatRegistry.ThreeMf)
        {
            ThreeMfFormat.Write(mesh, path, ct);
        }
        else if (output == FormatRegistry.Obj)
        {
            ObjFormat.Write(mesh, path, ct);
        }
        else if (output == FormatRegistry.Ply)
        {
            PlyFormat.WriteBinary(mesh, path, ct);
        }
        else
        {
            GltfFormat.WriteGlb(mesh, path, ct);
        }
    }

    private static async Task<ConversionResult> RunAsync(Func<ConversionResult> work, string path)
    {
        try
        {
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw Map(ex, path);
        }
    }

    private static ConversionException Map(Exception ex, string path) => ex switch
    {
        OperationCanceledException => new ConversionException(ConversionErrorCode.Cancelled, path, "model", inner: ex),
        OutOfMemoryException => new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", ex.Message, ex),
        EndOfStreamException or InvalidDataException or JsonException or XmlException or FormatException
            or ArgumentException or IndexOutOfRangeException or OverflowException or KeyNotFoundException
            => new ConversionException(ConversionErrorCode.CorruptFile, path, "model", ex.Message, ex),
        _ => ConversionException.From(ex, path, "model"),
    };
}
