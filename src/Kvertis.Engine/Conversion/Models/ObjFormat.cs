using System.Numerics;
using System.Text;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// Wavefront-style OBJ text: vertices ("v") and faces ("f"). Polygons are split into triangles as a fan.
/// Texture coordinates, normals, groups and material references are ignored; material files are never opened.
/// No units in the format; the numbers are treated as millimeters like STL.
/// </summary>
internal static class ObjFormat
{
    public static Mesh Read(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, new FileStreamOptions { BufferSize = 1 << 16 });
        var builder = new MeshBuilder(path);
        Span<Range> parts = stackalloc Range[5];
        var face = new List<int>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            if ((++lineNumber & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var span = line.AsSpan().Trim();
            if (span.Length < 2 || span[0] == '#')
            {
                continue;
            }
            if (span[0] == 'v' && span[1] is ' ' or '\t')
            {
                // "v x y z [w]" or "v x y z r g b" (vertex colors): only the first three numbers count.
                var count = ModelText.Split(span[1..], parts);
                var rest = span[1..];
                if (count < 3
                    || !ModelText.TryParseFloat(rest[parts[0]], out var x)
                    || !ModelText.TryParseFloat(rest[parts[1]], out var y)
                    || !ModelText.TryParseFloat(rest[parts[2]], out var z))
                {
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: invalid vertex");
                }
                builder.AddVertex(new Vector3(x, y, z));
            }
            else if (span[0] == 'f' && span[1] is ' ' or '\t')
            {
                ReadFace(span[1..], builder, face, path, lineNumber);
            }
        }
        return builder.Build();
    }

    private static void ReadFace(ReadOnlySpan<char> text, MeshBuilder builder, List<int> face, string path, int lineNumber)
    {
        face.Clear();
        var vertexCount = builder.Mesh.Vertices.Count;
        foreach (var token in text.ToString().Split(ModelText.Whitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            // "i", "i/t", "i/t/n" or "i//n"; negative indices count back from the last vertex read so far.
            var slash = token.IndexOf('/', StringComparison.Ordinal);
            var indexText = slash < 0 ? token.AsSpan() : token.AsSpan(0, slash);
            if (!ModelText.TryParseInt(indexText, out var index) || index == 0)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: invalid face");
            }
            face.Add(index > 0 ? index - 1 : vertexCount + index);
        }
        if (face.Count < 3)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"line {lineNumber}: face with fewer than three vertices");
        }
        for (var i = 1; i + 1 < face.Count; i++)
        {
            builder.AddTriangle(face[0], face[i], face[i + 1]);
        }
    }

    public static void Write(Mesh mesh, string path, CancellationToken ct)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 16) { NewLine = "\n" };
        writer.WriteLine("# Converted with Kvertis. Units: millimeters.");
        var line = new StringBuilder(64);
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var v = mesh.Vertices[i];
            line.Clear().Append("v ").Append(ModelText.Format(v.X)).Append(' ').Append(ModelText.Format(v.Y)).Append(' ').Append(ModelText.Format(v.Z));
            writer.WriteLine(line);
        }
        var indices = mesh.Indices;
        for (var t = 0; t < indices.Count; t += 3)
        {
            if ((t & 0x3FFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            line.Clear().Append("f ").Append(indices[t] + 1).Append(' ').Append(indices[t + 1] + 1).Append(' ').Append(indices[t + 2] + 1);
            writer.WriteLine(line);
        }
    }
}
