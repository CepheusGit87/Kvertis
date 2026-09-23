using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// PLY (Stanford polygon format) in text, binary little-endian and binary big-endian form. Reads vertex x/y/z
/// and the face index lists; other properties and elements are skipped. Written as binary little-endian.
/// </summary>
internal static class PlyFormat
{
    private const int MaxHeaderLines = 10_000;
    private const long MaxElementCount = 100_000_000;

    private enum PlyEncoding
    {
        Ascii,
        BinaryLittleEndian,
        BinaryBigEndian,
    }

    private sealed record Property(string Name, string Type, string? ListCountType);

    private sealed record Element(string Name, long Count, List<Property> Properties);

    public static Mesh Read(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var (encoding, elements) = ReadHeader(stream, path);
        var builder = new MeshBuilder(path);
        using var reader = encoding == PlyEncoding.Ascii
            ? (IPlyValueReader)new AsciiValueReader(stream, path)
            : new BinaryValueReader(stream, encoding == PlyEncoding.BinaryBigEndian, path);

        var vertexOffset = -1;
        foreach (var element in elements)
        {
            if (element.Name == "vertex")
            {
                vertexOffset = builder.Mesh.Vertices.Count;
                ReadVertices(element, reader, builder, path, ct);
            }
            else if (element.Name == "face")
            {
                if (vertexOffset < 0)
                {
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "faces before vertices");
                }
                ReadFaces(element, reader, builder, vertexOffset, path, ct);
            }
            else
            {
                SkipElement(element, reader, ct);
            }
        }
        return builder.Build();
    }

    private static (PlyEncoding Encoding, List<Element> Elements) ReadHeader(Stream stream, string path)
    {
        PlyEncoding? encoding = null;
        var elements = new List<Element>();
        for (var lineNumber = 0; lineNumber < MaxHeaderLines; lineNumber++)
        {
            var line = ReadHeaderLine(stream, path);
            var parts = line.Split(ModelText.Whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            switch (parts[0])
            {
                case "ply" or "comment" or "obj_info":
                    break;
                case "format" when parts.Length >= 2:
                    encoding = parts[1] switch
                    {
                        "ascii" => PlyEncoding.Ascii,
                        "binary_little_endian" => PlyEncoding.BinaryLittleEndian,
                        "binary_big_endian" => PlyEncoding.BinaryBigEndian,
                        _ => throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"unknown PLY format '{parts[1]}'"),
                    };
                    break;
                case "element" when parts.Length >= 3 && long.TryParse(parts[2], out var count) && count >= 0:
                    // Bounded so a forged count cannot keep the reader spinning; real files stay far below.
                    if (count > MaxElementCount)
                    {
                        throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", $"{count} {parts[1]} records");
                    }
                    elements.Add(new Element(parts[1], count, []));
                    break;
                case "property" when elements.Count > 0 && parts.Length >= 3:
                    var property = parts[1] == "list" && parts.Length >= 5
                        ? new Property(parts[4], parts[3], parts[2])
                        : new Property(parts[2], parts[1], null);
                    ValidateType(property.Type, path);
                    if (property.ListCountType is not null)
                    {
                        ValidateType(property.ListCountType, path);
                    }
                    elements[^1].Properties.Add(property);
                    break;
                case "end_header":
                    return (encoding ?? throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "PLY without format line"), elements);
                default:
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"unexpected PLY header line '{parts[0]}'");
            }
        }
        throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "PLY header too long");
    }

    /// <summary>Reads one header line byte by byte, so the stream stays exactly at the start of the body.</summary>
    private static string ReadHeaderLine(Stream stream, string path)
    {
        var bytes = new List<byte>(64);
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "PLY header not terminated");
            }
            if (b == '\n')
            {
                break;
            }
            if (b != '\r')
            {
                bytes.Add((byte)b);
            }
            if (bytes.Count > 4096)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "PLY header line too long");
            }
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static void ValidateType(string type, string path)
    {
        if (SizeOf(type) == 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"unknown PLY type '{type}'");
        }
    }

    private static int SizeOf(string type) => type switch
    {
        "char" or "int8" or "uchar" or "uint8" => 1,
        "short" or "int16" or "ushort" or "uint16" => 2,
        "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
        "double" or "float64" => 8,
        _ => 0,
    };

    private static void ReadVertices(Element element, IPlyValueReader reader, MeshBuilder builder, string path, CancellationToken ct)
    {
        if (element.Count > MeshBuilder.MaxVertices)
        {
            throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", $"{element.Count} vertices");
        }
        int ix = -1, iy = -1, iz = -1;
        for (var p = 0; p < element.Properties.Count; p++)
        {
            switch (element.Properties[p].Name)
            {
                case "x": ix = p; break;
                case "y": iy = p; break;
                case "z": iz = p; break;
            }
        }
        if (ix < 0 || iy < 0 || iz < 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "vertex without x, y, z");
        }
        Span<double> xyz = stackalloc double[3];
        for (long i = 0; i < element.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            for (var p = 0; p < element.Properties.Count; p++)
            {
                var property = element.Properties[p];
                if (property.ListCountType is not null)
                {
                    SkipList(property, reader);
                    continue;
                }
                var value = reader.Read(property.Type);
                if (p == ix)
                {
                    xyz[0] = value;
                }
                else if (p == iy)
                {
                    xyz[1] = value;
                }
                else if (p == iz)
                {
                    xyz[2] = value;
                }
            }
            reader.EndOfRecord();
            builder.AddVertex(new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]));
        }
    }

    private static void ReadFaces(Element element, IPlyValueReader reader, MeshBuilder builder, int vertexOffset, string path, CancellationToken ct)
    {
        var indexProperty = element.Properties.FindIndex(p => p.ListCountType is not null && p.Name is "vertex_indices" or "vertex_index");
        if (indexProperty < 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "face without vertex index list");
        }
        var face = new List<int>(8);
        for (long i = 0; i < element.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            for (var p = 0; p < element.Properties.Count; p++)
            {
                var property = element.Properties[p];
                if (p != indexProperty)
                {
                    if (property.ListCountType is not null)
                    {
                        SkipList(property, reader);
                    }
                    else
                    {
                        reader.Read(property.Type);
                    }
                    continue;
                }
                var count = ReadListCount(property, reader, path);
                face.Clear();
                for (var k = 0; k < count; k++)
                {
                    var index = reader.Read(property.Type);
                    if (index < 0 || index > int.MaxValue)
                    {
                        throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "vertex index out of range");
                    }
                    face.Add(vertexOffset + (int)index);
                }
                for (var k = 1; k + 1 < face.Count; k++)
                {
                    builder.AddTriangle(face[0], face[k], face[k + 1]);
                }
            }
            reader.EndOfRecord();
        }
    }

    private static void SkipElement(Element element, IPlyValueReader reader, CancellationToken ct)
    {
        for (long i = 0; i < element.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            foreach (var property in element.Properties)
            {
                if (property.ListCountType is not null)
                {
                    SkipList(property, reader);
                }
                else
                {
                    reader.Read(property.Type);
                }
            }
            reader.EndOfRecord();
        }
    }

    private static void SkipList(Property property, IPlyValueReader reader)
    {
        var count = ReadListCount(property, reader, reader.Path);
        for (var k = 0; k < count; k++)
        {
            reader.Read(property.Type);
        }
    }

    private static int ReadListCount(Property property, IPlyValueReader reader, string path)
    {
        var count = reader.Read(property.ListCountType!);
        if (count < 0 || count > 1_000_000)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "invalid list length");
        }
        return (int)count;
    }

    public static void WriteBinary(Mesh mesh, string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var header = new StringBuilder()
            .Append("ply\n")
            .Append("format binary_little_endian 1.0\n")
            .Append("comment Converted with Kvertis. Units: millimeters.\n")
            .Append("element vertex ").Append(mesh.Vertices.Count).Append('\n')
            .Append("property float x\nproperty float y\nproperty float z\n")
            .Append("element face ").Append(mesh.TriangleCount).Append('\n')
            .Append("property list uchar int vertex_indices\n")
            .Append("end_header\n");
        stream.Write(Encoding.ASCII.GetBytes(header.ToString()));

        Span<byte> buffer = stackalloc byte[13];
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var v = mesh.Vertices[i];
            BinaryPrimitives.WriteSingleLittleEndian(buffer, v.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[4..], v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[8..], v.Z);
            stream.Write(buffer[..12]);
        }
        var indices = mesh.Indices;
        buffer[0] = 3;
        for (var t = 0; t < indices.Count; t += 3)
        {
            if ((t & 0x3FFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            BinaryPrimitives.WriteInt32LittleEndian(buffer[1..], indices[t]);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[5..], indices[t + 1]);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[9..], indices[t + 2]);
            stream.Write(buffer);
        }
    }

    private interface IPlyValueReader : IDisposable
    {
        string Path { get; }

        double Read(string type);

        void EndOfRecord();
    }

    private sealed class BinaryValueReader : IPlyValueReader
    {
        private readonly Stream _stream;
        private readonly bool _bigEndian;
        private readonly byte[] _buffer = new byte[8];

        public BinaryValueReader(Stream stream, bool bigEndian, string path)
        {
            _stream = stream;
            _bigEndian = bigEndian;
            Path = path;
        }

        public string Path { get; }

        public double Read(string type)
        {
            var size = SizeOf(type);
            var span = _buffer.AsSpan(0, size);
            try
            {
                _stream.ReadExactly(span);
            }
            catch (EndOfStreamException ex)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, Path, "model", "PLY body shorter than its header says", ex);
            }
            return type switch
            {
                "char" or "int8" => (sbyte)span[0],
                "uchar" or "uint8" => span[0],
                "short" or "int16" => _bigEndian ? BinaryPrimitives.ReadInt16BigEndian(span) : BinaryPrimitives.ReadInt16LittleEndian(span),
                "ushort" or "uint16" => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span),
                "int" or "int32" => _bigEndian ? BinaryPrimitives.ReadInt32BigEndian(span) : BinaryPrimitives.ReadInt32LittleEndian(span),
                "uint" or "uint32" => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span),
                "float" or "float32" => _bigEndian ? BinaryPrimitives.ReadSingleBigEndian(span) : BinaryPrimitives.ReadSingleLittleEndian(span),
                _ => _bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(span) : BinaryPrimitives.ReadDoubleLittleEndian(span),
            };
        }

        public void EndOfRecord()
        {
        }

        public void Dispose()
        {
            // The stream belongs to the caller.
        }
    }

    /// <summary>Text body: whitespace-separated numbers; every element record ends with its line.</summary>
    private sealed class AsciiValueReader : IPlyValueReader
    {
        private readonly StreamReader _reader;
        private string[] _tokens = [];
        private int _next;

        public AsciiValueReader(Stream stream, string path)
        {
            _reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);
            Path = path;
        }

        public string Path { get; }

        public double Read(string type)
        {
            while (_next >= _tokens.Length)
            {
                var line = _reader.ReadLine() ?? throw new ConversionException(ConversionErrorCode.CorruptFile, Path, "model", "PLY body shorter than its header says");
                _tokens = line.Split(ModelText.Whitespace, StringSplitOptions.RemoveEmptyEntries);
                _next = 0;
            }
            var token = _tokens[_next++];
            if (!double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, Path, "model", $"invalid PLY value '{token}'");
            }
            return value;
        }

        public void EndOfRecord()
        {
            // Records are line based; anything left on the line (unknown extras) is dropped.
            _next = _tokens.Length;
        }

        public void Dispose() => _reader.Dispose();
    }
}
