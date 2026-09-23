using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// glTF 2.0, as binary GLB or as JSON (.gltf). Reads the triangle geometry of the default scene with all node
/// transforms. Buffers may be embedded (GLB chunk, base64 data URI) or separate files next to the .gltf; nothing
/// outside that folder is opened and there is no network access. Files that require extensions (mesh
/// compression and similar) are refused with UnsupportedFormat. Written as GLB: one mesh, positions and indices.
/// </summary>
internal static class GltfFormat
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint ChunkJson = 0x4E4F534A; // "JSON"
    private const uint ChunkBin = 0x004E4942; // "BIN\0"
    private const int ComponentFloat = 5126;
    private const int ComponentUnsignedByte = 5121;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const int MaxNodeDepth = 64;

    public static Mesh Read(string path, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(path);
        byte[] json;
        byte[]? glbBinary = null;
        if (bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic)
        {
            (json, glbBinary) = SplitGlb(bytes, path);
        }
        else
        {
            json = bytes;
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.TryGetProperty("extensionsRequired", out var required) && required.ValueKind == JsonValueKind.Array && required.GetArrayLength() > 0)
        {
            var names = string.Join(", ", required.EnumerateArray().Select(e => e.GetString()));
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "model", $"glTF requires extensions: {names}");
        }
        var reader = new GltfReader(root, glbBinary, path, ct);
        return reader.Read();
    }

    private static (byte[] Json, byte[]? Binary) SplitGlb(byte[] bytes, string path)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2)
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "model", "only glTF 2.0 is supported");
        }
        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)), (uint)bytes.Length);
        byte[]? json = null;
        byte[]? binary = null;
        var offset = 12;
        while (offset + 8 <= length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            offset += 8;
            if (chunkLength > (uint)(length - offset))
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "GLB chunk exceeds the file");
            }
            var data = bytes.AsSpan(offset, (int)chunkLength).ToArray();
            if (chunkType == ChunkJson && json is null)
            {
                json = data;
            }
            else if (chunkType == ChunkBin && binary is null)
            {
                binary = data;
            }
            offset += (int)chunkLength;
        }
        return (json ?? throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "GLB without JSON chunk"), binary);
    }

    private sealed class GltfReader
    {
        private readonly JsonElement _root;
        private readonly byte[]? _glbBinary;
        private readonly string _path;
        private readonly CancellationToken _ct;
        private readonly Dictionary<int, byte[]> _buffers = [];
        private readonly Dictionary<string, List<JsonElement>> _arrays = [];
        private readonly MeshBuilder _builder;
        private int _visitedNodes;

        public GltfReader(JsonElement root, byte[]? glbBinary, string path, CancellationToken ct)
        {
            _root = root;
            _glbBinary = glbBinary;
            _path = path;
            _ct = ct;
            _builder = new MeshBuilder(path);
        }

        public Mesh Read()
        {
            var nodes = Array("nodes");
            var sceneIndex = _root.TryGetProperty("scene", out var s) && s.TryGetInt32(out var si) ? si : 0;
            var scenes = Array("scenes");
            if (scenes.Count > 0)
            {
                if (sceneIndex < 0 || sceneIndex >= scenes.Count)
                {
                    throw Corrupt("scene index out of range");
                }
                foreach (var nodeIndex in Indices(scenes[sceneIndex], "nodes"))
                {
                    AddNode(nodes, nodeIndex, Matrix4x4.Identity, 0);
                }
            }
            else
            {
                // No scene: the specification leaves rendering open; take every mesh once, untransformed.
                for (var m = 0; m < Array("meshes").Count; m++)
                {
                    AddMesh(m, Matrix4x4.Identity);
                }
            }
            return _builder.Build();
        }

        private void AddNode(IReadOnlyList<JsonElement> nodes, int index, Matrix4x4 parent, int depth)
        {
            if (index < 0 || index >= nodes.Count)
            {
                throw Corrupt("node index out of range");
            }
            if (depth > MaxNodeDepth || ++_visitedNodes > 1_000_000)
            {
                throw Corrupt("node hierarchy too deep or cyclic");
            }
            _ct.ThrowIfCancellationRequested();
            var node = nodes[index];
            var world = LocalTransform(node) * parent;
            if (node.TryGetProperty("mesh", out var mesh) && mesh.TryGetInt32(out var meshIndex))
            {
                AddMesh(meshIndex, world);
            }
            foreach (var child in Indices(node, "children"))
            {
                AddNode(nodes, child, world, depth + 1);
            }
        }

        private Matrix4x4 LocalTransform(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out var matrix))
            {
                // Column-major in glTF; read in order it is exactly the row-vector matrix System.Numerics uses.
                var m = Floats(matrix, 16);
                return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            }
            var scale = node.TryGetProperty("scale", out var sc) ? Floats(sc, 3) : [1, 1, 1];
            var rotation = node.TryGetProperty("rotation", out var r) ? Floats(r, 4) : [0, 0, 0, 1];
            var translation = node.TryGetProperty("translation", out var t) ? Floats(t, 3) : [0, 0, 0];
            return Matrix4x4.CreateScale(scale[0], scale[1], scale[2])
                   * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])))
                   * Matrix4x4.CreateTranslation(translation[0], translation[1], translation[2]);
        }

        private void AddMesh(int meshIndex, Matrix4x4 world)
        {
            var meshes = Array("meshes");
            if (meshIndex < 0 || meshIndex >= meshes.Count)
            {
                throw Corrupt("mesh index out of range");
            }
            if (!meshes[meshIndex].TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var mirrored = world.GetDeterminant() < 0;
            foreach (var primitive in primitives.EnumerateArray())
            {
                var mode = primitive.TryGetProperty("mode", out var mo) && mo.TryGetInt32(out var mv) ? mv : 4;
                if (mode is not (4 or 5 or 6))
                {
                    continue; // Points and lines have no surface.
                }
                if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var pos) || !pos.TryGetInt32(out var positionAccessor))
                {
                    continue;
                }
                var positions = ReadPositions(positionAccessor);
                var offset = _builder.Mesh.Vertices.Count;
                foreach (var p in positions)
                {
                    _builder.AddVertex(ModelSpace.FromGltf(Vector3.Transform(p, world)));
                }
                var indices = primitive.TryGetProperty("indices", out var ix) && ix.TryGetInt32(out var indexAccessor)
                    ? ReadIndices(indexAccessor)
                    : Enumerable.Range(0, positions.Count).ToArray();
                AddTriangles(indices, mode, offset, mirrored);
            }
        }

        private void AddTriangles(int[] indices, int mode, int offset, bool mirrored)
        {
            void Add(int a, int b, int c)
            {
                if (mirrored)
                {
                    _builder.AddTriangle(offset + a, offset + c, offset + b);
                }
                else
                {
                    _builder.AddTriangle(offset + a, offset + b, offset + c);
                }
            }

            switch (mode)
            {
                case 4:
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Add(indices[i], indices[i + 1], indices[i + 2]);
                    }
                    break;
                case 5: // Strip: every other triangle is flipped so all keep the same orientation.
                    for (var i = 0; i + 2 < indices.Length; i++)
                    {
                        if (i % 2 == 0)
                        {
                            Add(indices[i], indices[i + 1], indices[i + 2]);
                        }
                        else
                        {
                            Add(indices[i + 1], indices[i], indices[i + 2]);
                        }
                    }
                    break;
                default: // Fan
                    for (var i = 1; i + 1 < indices.Length; i++)
                    {
                        Add(indices[0], indices[i], indices[i + 1]);
                    }
                    break;
            }
        }

        private List<Vector3> ReadPositions(int accessorIndex)
        {
            var a = Accessor(accessorIndex);
            if (a.ComponentType != ComponentFloat || a.Type != "VEC3")
            {
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, _path, "model", "positions are not float VEC3");
            }
            var (data, start, stride, count) = Checked(a, 12);
            if (count > MeshBuilder.MaxVertices)
            {
                throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", $"{count} vertices");
            }
            var result = new List<Vector3>(count);
            for (var i = 0; i < count; i++)
            {
                var o = start + (i * (long)stride);
                var span = data.AsSpan((int)o, 12);
                result.Add(new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(span),
                    BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[8..])));
            }
            return result;
        }

        private int[] ReadIndices(int accessorIndex)
        {
            var a = Accessor(accessorIndex);
            var size = a.ComponentType switch
            {
                ComponentUnsignedByte => 1,
                ComponentUnsignedShort => 2,
                ComponentUnsignedInt => 4,
                _ => throw Corrupt("invalid index component type"),
            };
            if (a.Type != "SCALAR")
            {
                throw Corrupt("indices are not SCALAR");
            }
            var (data, start, stride, count) = Checked(a, size);
            if (count / 3 > MeshBuilder.MaxTriangles * 2L)
            {
                throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", $"{count} indices");
            }
            var result = new int[count];
            for (var i = 0; i < count; i++)
            {
                var o = (int)(start + (i * (long)stride));
                var value = size switch
                {
                    1 => data[o],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o)),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o)),
                };
                if (value > int.MaxValue)
                {
                    throw Corrupt("index out of range");
                }
                result[i] = (int)value;
            }
            return result;
        }

        private sealed record AccessorView(byte[] Buffer, long ViewStart, long ViewEnd, long Start, int? Stride, int Count, int ComponentType, string Type);

        private AccessorView Accessor(int index)
        {
            var accessors = Array("accessors");
            if (index < 0 || index >= accessors.Count)
            {
                throw Corrupt("accessor index out of range");
            }
            var accessor = accessors[index];
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, _path, "model", "sparse accessors are not supported");
            }
            var count = Int(accessor, "count") ?? throw Corrupt("accessor without count");
            var componentType = Int(accessor, "componentType") ?? throw Corrupt("accessor without componentType");
            var type = accessor.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var viewIndex = Int(accessor, "bufferView") ?? throw new ConversionException(ConversionErrorCode.UnsupportedFormat, _path, "model", "accessor without buffer view");
            var views = Array("bufferViews");
            if (viewIndex < 0 || viewIndex >= views.Count)
            {
                throw Corrupt("buffer view index out of range");
            }
            var view = views[viewIndex];
            var buffer = Buffer(Int(view, "buffer") ?? throw Corrupt("buffer view without buffer"));
            long viewOffset = Int(view, "byteOffset") ?? 0;
            long viewLength = Int(view, "byteLength") ?? throw Corrupt("buffer view without byteLength");
            if (viewOffset < 0 || viewLength < 0 || viewOffset + viewLength > buffer.Length || count < 0)
            {
                throw Corrupt("buffer view exceeds its buffer");
            }
            var start = viewOffset + (Int(accessor, "byteOffset") ?? 0);
            return new AccessorView(buffer, viewOffset, viewOffset + viewLength, start, Int(view, "byteStride"), count, componentType, type);
        }

        /// <summary>Resolves the stride (tightly packed when the view has none) and checks every element lies inside the view.</summary>
        private (byte[] Data, long Start, int Stride, int Count) Checked(AccessorView a, int elementSize)
        {
            var stride = a.Stride ?? elementSize;
            if (a.Count > 0 && (a.Start < a.ViewStart || stride < elementSize || a.Start + ((a.Count - 1) * (long)stride) + elementSize > a.ViewEnd))
            {
                throw Corrupt("accessor exceeds its buffer view");
            }
            return (a.Buffer, a.Start, stride, a.Count);
        }

        private byte[] Buffer(int index)
        {
            if (_buffers.TryGetValue(index, out var cached))
            {
                return cached;
            }
            var buffers = Array("buffers");
            if (index < 0 || index >= buffers.Count)
            {
                throw Corrupt("buffer index out of range");
            }
            byte[] data;
            if (buffers[index].TryGetProperty("uri", out var uriElement) && uriElement.GetString() is { } uri)
            {
                data = LoadUri(uri);
            }
            else
            {
                data = _glbBinary ?? throw Corrupt("buffer without data");
            }
            _buffers[index] = data;
            return data;
        }

        private byte[] LoadUri(string uri)
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = uri.IndexOf(',', StringComparison.Ordinal);
                if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    throw Corrupt("unsupported data URI");
                }
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            // Only plain relative file names inside the folder of the .gltf file: no scheme, no rooted path, no "..".
            var relative = Uri.UnescapeDataString(uri);
            if (relative.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative.StartsWith('/') || relative.StartsWith('\\'))
            {
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, _path, "model", "buffer outside the model folder");
            }
            var folder = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(_path)) ?? ".");
            var full = Path.GetFullPath(Path.Combine(folder, relative));
            if (!full.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, _path, "model", "buffer outside the model folder");
            }
            if (!File.Exists(full))
            {
                throw new ConversionException(ConversionErrorCode.InputNotReadable, _path, "model", $"missing buffer file '{Path.GetFileName(full)}'");
            }
            if (new FileInfo(full).Length > int.MaxValue)
            {
                throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", "buffer file too large");
            }
            return File.ReadAllBytes(full);
        }

        private List<JsonElement> Array(string name)
        {
            if (!_arrays.TryGetValue(name, out var list))
            {
                list = _root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
                _arrays[name] = list;
            }
            return list;
        }

        private static IEnumerable<int> Indices(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetInt32(out var value))
                {
                    yield return value;
                }
            }
        }

        private float[] Floats(JsonElement array, int expected)
        {
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != expected)
            {
                throw Corrupt("invalid transform");
            }
            var result = new float[expected];
            var i = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (!item.TryGetSingle(out result[i++]) || !float.IsFinite(result[i - 1]))
                {
                    throw Corrupt("invalid transform");
                }
            }
            return result;
        }

        private static int? Int(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;

        private ConversionException Corrupt(string detail) => new(ConversionErrorCode.CorruptFile, _path, "model", detail);
    }

    public static void WriteGlb(Mesh mesh, string path, CancellationToken ct)
    {
        var vertexCount = mesh.Vertices.Count;
        var indexCount = mesh.Indices.Count;
        var positionBytes = vertexCount * 12;
        var indexBytes = indexCount * 4;
        var binary = new byte[positionBytes + indexBytes];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < vertexCount; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var v = ModelSpace.ToGltf(mesh.Vertices[i]);
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
            var span = binary.AsSpan(i * 12, 12);
            BinaryPrimitives.WriteSingleLittleEndian(span, v.X);
            BinaryPrimitives.WriteSingleLittleEndian(span[4..], v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..], v.Z);
        }
        for (var i = 0; i < indexCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(positionBytes + (i * 4), 4), (uint)mesh.Indices[i]);
        }

        using var jsonStream = new MemoryStream();
        using (var json = new Utf8JsonWriter(jsonStream))
        {
            json.WriteStartObject();
            json.WriteStartObject("asset");
            json.WriteString("version", "2.0");
            json.WriteString("generator", "Kvertis");
            json.WriteEndObject();
            json.WriteNumber("scene", 0);
            json.WriteStartArray("scenes");
            json.WriteStartObject();
            json.WriteStartArray("nodes");
            json.WriteNumberValue(0);
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartArray("nodes");
            json.WriteStartObject();
            json.WriteNumber("mesh", 0);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartArray("meshes");
            json.WriteStartObject();
            json.WriteStartArray("primitives");
            json.WriteStartObject();
            json.WriteStartObject("attributes");
            json.WriteNumber("POSITION", 0);
            json.WriteEndObject();
            json.WriteNumber("indices", 1);
            json.WriteNumber("mode", 4);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartArray("accessors");
            json.WriteStartObject();
            json.WriteNumber("bufferView", 0);
            json.WriteNumber("componentType", ComponentFloat);
            json.WriteNumber("count", vertexCount);
            json.WriteString("type", "VEC3");
            WriteVector("min", min);
            WriteVector("max", max);
            json.WriteEndObject();
            json.WriteStartObject();
            json.WriteNumber("bufferView", 1);
            json.WriteNumber("componentType", ComponentUnsignedInt);
            json.WriteNumber("count", indexCount);
            json.WriteString("type", "SCALAR");
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartArray("bufferViews");
            WriteView(0, positionBytes, 34962);
            WriteView(positionBytes, indexBytes, 34963);
            json.WriteEndArray();
            json.WriteStartArray("buffers");
            json.WriteStartObject();
            json.WriteNumber("byteLength", binary.Length);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();

            void WriteVector(string name, Vector3 v)
            {
                json.WriteStartArray(name);
                json.WriteNumberValue(v.X);
                json.WriteNumberValue(v.Y);
                json.WriteNumberValue(v.Z);
                json.WriteEndArray();
            }

            void WriteView(int offset, int length, int target)
            {
                json.WriteStartObject();
                json.WriteNumber("buffer", 0);
                json.WriteNumber("byteOffset", offset);
                json.WriteNumber("byteLength", length);
                json.WriteNumber("target", target);
                json.WriteEndObject();
            }
        }

        var jsonBytes = jsonStream.ToArray();
        var jsonPadded = Align4(jsonBytes.Length);
        var binPadded = Align4(binary.Length);
        var total = 12 + 8 + jsonPadded + 8 + binPadded;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)total);
        stream.Write(header);
        WriteChunk(stream, ChunkJson, jsonBytes, jsonPadded, (byte)' ');
        WriteChunk(stream, ChunkBin, binary, binPadded, 0);
    }

    private static int Align4(int length) => (length + 3) & ~3;

    private static void WriteChunk(Stream stream, uint type, byte[] data, int paddedLength, byte padding)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)paddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], type);
        stream.Write(header);
        stream.Write(data);
        for (var i = data.Length; i < paddedLength; i++)
        {
            stream.WriteByte(padding);
        }
    }
}
