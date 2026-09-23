using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// STL, binary and text. No units in the format; Kvertis treats the numbers as millimeters, the convention of
/// 3D printing software. Always written as binary STL (much smaller, universally supported).
/// </summary>
internal static class StlFormat
{
    private const int HeaderLength = 80;
    private const int TriangleRecordLength = 50;

    public static Mesh Read(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var sample = new byte[Math.Min(stream.Length, 84)];
        stream.ReadExactly(sample);
        stream.Position = 0;
        return MagicBytes.IsBinaryStl(sample, stream.Length)
            ? ReadBinary(stream, path, ct)
            : ReadText(stream, path, ct);
    }

    private static Mesh ReadBinary(Stream stream, string path, CancellationToken ct)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        reader.ReadBytes(HeaderLength);
        var count = reader.ReadUInt32();
        if (count > MeshBuilder.MaxTriangles)
        {
            throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", $"{count} triangles");
        }
        var builder = new MeshBuilder(path);
        var record = new byte[TriangleRecordLength];
        for (var i = 0; i < count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            reader.BaseStream.ReadExactly(record);
            // Bytes 0..11: facet normal (recomputed on write, ignored here); 12..47: three vertices; 48..49: attribute.
            var a = builder.AddWelded(ReadVector(record, 12));
            var b = builder.AddWelded(ReadVector(record, 24));
            var c = builder.AddWelded(ReadVector(record, 36));
            builder.AddTriangle(a, b, c);
        }
        return builder.Build();
    }

    private static Vector3 ReadVector(byte[] data, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)),
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8)));

    private static Mesh ReadText(Stream stream, string path, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16, leaveOpen: true);
        var builder = new MeshBuilder(path);
        Span<int> facet = stackalloc int[3];
        var inFacet = 0;
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            if ((++lineNumber & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var span = line.AsSpan().Trim();
            if (span.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                if (inFacet >= 3)
                {
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: more than three vertices in a facet");
                }
                facet[inFacet++] = builder.AddWelded(ParseVector(span[6..], path, lineNumber));
            }
            else if (span.StartsWith("endloop", StringComparison.OrdinalIgnoreCase))
            {
                if (inFacet != 3)
                {
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: facet without three vertices");
                }
                builder.AddTriangle(facet[0], facet[1], facet[2]);
                inFacet = 0;
            }
        }
        return builder.Build();
    }

    private static Vector3 ParseVector(ReadOnlySpan<char> text, string path, int lineNumber)
    {
        Span<Range> parts = stackalloc Range[4];
        if (ModelText.Split(text, parts) != 3
            || !ModelText.TryParseFloat(text[parts[0]], out var x)
            || !ModelText.TryParseFloat(text[parts[1]], out var y)
            || !ModelText.TryParseFloat(text[parts[2]], out var z))
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: invalid vertex");
        }
        return new Vector3(x, y, z);
    }

    public static void WriteBinary(Mesh mesh, string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var header = new byte[HeaderLength];
        // Must not start with "solid", or some programs take the file for text STL.
        Encoding.ASCII.GetBytes("Binary STL").CopyTo(header, 0);
        stream.Write(header);
        Span<byte> buffer = stackalloc byte[TriangleRecordLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)mesh.TriangleCount);
        stream.Write(buffer[..4]);
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        for (var t = 0; t < indices.Count; t += 3)
        {
            if ((t & 0x3FFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var a = vertices[indices[t]];
            var b = vertices[indices[t + 1]];
            var c = vertices[indices[t + 2]];
            WriteVector(buffer, 0, Mesh.FaceNormal(a, b, c));
            WriteVector(buffer, 12, a);
            WriteVector(buffer, 24, b);
            WriteVector(buffer, 36, c);
            buffer[48] = 0;
            buffer[49] = 0;
            stream.Write(buffer);
        }
    }

    private static void WriteVector(Span<byte> buffer, int offset, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(buffer[offset..], v.X);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[(offset + 4)..], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[(offset + 8)..], v.Z);
    }
}
