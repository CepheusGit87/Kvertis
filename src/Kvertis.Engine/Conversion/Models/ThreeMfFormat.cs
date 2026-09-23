using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Xml;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// 3MF (3D Manufacturing Format, core specification): a ZIP package with an XML model part. Reads the meshes of
/// all build items including components and transforms; production, beam, slice and material extensions are
/// ignored. Writes one mesh object in millimeters.
/// </summary>
internal static class ThreeMfFormat
{
    private const string CoreNamespace = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string ModelRelationshipType = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ModelPath = "3D/3dmodel.model";

    /// <summary>Upper bound for the uncompressed model XML; protects against ZIP bombs.</summary>
    private const long MaxModelPartBytes = 4L * 1024 * 1024 * 1024;

    private const int MaxComponentDepth = 16;

    private sealed class ModelObject
    {
        public List<Vector3> Vertices { get; } = [];
        public List<int> Triangles { get; } = [];
        public List<(int ObjectId, Matrix4x4 Transform)> Components { get; } = [];
    }

    public static Mesh Read(string path, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = FindModelPart(zip)
                    ?? throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "3MF without model part");
        using var raw = entry.Open();
        using var limited = new LimitedReadStream(raw, MaxModelPartBytes, path);
        using var xml = XmlReader.Create(limited, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });

        var scale = 1f;
        var objects = new Dictionary<int, ModelObject>();
        var build = new List<(int ObjectId, Matrix4x4 Transform)>();
        ModelObject? current = null;
        var inBuild = false;
        var nodes = 0L;
        while (xml.Read())
        {
            if ((++nodes & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            if (xml.NodeType == XmlNodeType.EndElement)
            {
                if (xml.LocalName == "object")
                {
                    current = null;
                }
                else if (xml.LocalName == "build")
                {
                    inBuild = false;
                }
                continue;
            }
            if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != CoreNamespace)
            {
                continue;
            }
            switch (xml.LocalName)
            {
                case "model":
                    scale = UnitScale(xml.GetAttribute("unit"), path);
                    break;
                case "object":
                    current = new ModelObject();
                    objects[RequiredInt(xml, "id", path)] = current;
                    if (xml.IsEmptyElement)
                    {
                        current = null;
                    }
                    break;
                case "vertex" when current is not null:
                    current.Vertices.Add(new Vector3(RequiredFloat(xml, "x", path), RequiredFloat(xml, "y", path), RequiredFloat(xml, "z", path)));
                    if (current.Vertices.Count > MeshBuilder.MaxVertices)
                    {
                        throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", $"more than {MeshBuilder.MaxVertices} vertices");
                    }
                    break;
                case "triangle" when current is not null:
                    current.Triangles.Add(RequiredInt(xml, "v1", path));
                    current.Triangles.Add(RequiredInt(xml, "v2", path));
                    current.Triangles.Add(RequiredInt(xml, "v3", path));
                    if (current.Triangles.Count / 3 > MeshBuilder.MaxTriangles)
                    {
                        throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "model", $"more than {MeshBuilder.MaxTriangles} triangles");
                    }
                    break;
                case "component" when current is not null:
                    // Components in other model parts (production extension, p:path) are not followed.
                    if (xml.GetAttribute("path", "http://schemas.microsoft.com/3dmanufacturing/production/2015/06") is null)
                    {
                        current.Components.Add((RequiredInt(xml, "objectid", path), Transform(xml.GetAttribute("transform"), path)));
                    }
                    break;
                case "build":
                    inBuild = !xml.IsEmptyElement;
                    break;
                case "item" when inBuild:
                    if (xml.GetAttribute("path", "http://schemas.microsoft.com/3dmanufacturing/production/2015/06") is null)
                    {
                        build.Add((RequiredInt(xml, "objectid", path), Transform(xml.GetAttribute("transform"), path)));
                    }
                    break;
            }
        }

        var builder = new MeshBuilder(path);
        foreach (var (objectId, transform) in build)
        {
            AddObject(objectId, transform * Matrix4x4.CreateScale(scale), objects, builder, 0, path, ct);
        }
        return builder.Build();
    }

    private static ZipArchiveEntry? FindModelPart(ZipArchive zip)
    {
        // The package relationships name the start part; fall back to the conventional location.
        var rels = zip.GetEntry("_rels/.rels");
        if (rels is not null && rels.Length < 1024 * 1024)
        {
            try
            {
                using var stream = rels.Open();
                using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                while (xml.Read())
                {
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship" && xml.NamespaceURI == RelationshipsNamespace
                        && xml.GetAttribute("Type") == ModelRelationshipType && xml.GetAttribute("Target") is { } target)
                    {
                        if (zip.GetEntry(target.TrimStart('/')) is { } entry)
                        {
                            return entry;
                        }
                    }
                }
            }
            catch (XmlException)
            {
                // Broken relationships: use the conventional path below.
            }
        }
        return zip.GetEntry(ModelPath)
               ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddObject(int objectId, Matrix4x4 transform, Dictionary<int, ModelObject> objects, MeshBuilder builder, int depth, string path, CancellationToken ct)
    {
        if (depth > MaxComponentDepth)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "components nested too deeply");
        }
        if (!objects.TryGetValue(objectId, out var model))
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"object {objectId} not found");
        }
        ct.ThrowIfCancellationRequested();
        if (model.Triangles.Count > 0)
        {
            var offset = builder.Mesh.Vertices.Count;
            foreach (var v in model.Vertices)
            {
                builder.AddVertex(Vector3.Transform(v, transform));
            }
            // A mirroring transform turns the triangles inside out; swap two corners to keep them facing outward.
            var mirrored = transform.GetDeterminant() < 0;
            var triangles = model.Triangles;
            for (var t = 0; t + 2 < triangles.Count; t += 3)
            {
                if (triangles[t] >= model.Vertices.Count || triangles[t + 1] >= model.Vertices.Count || triangles[t + 2] >= model.Vertices.Count
                    || triangles[t] < 0 || triangles[t + 1] < 0 || triangles[t + 2] < 0)
                {
                    throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "vertex index out of range");
                }
                if (mirrored)
                {
                    builder.AddTriangle(offset + triangles[t], offset + triangles[t + 2], offset + triangles[t + 1]);
                }
                else
                {
                    builder.AddTriangle(offset + triangles[t], offset + triangles[t + 1], offset + triangles[t + 2]);
                }
            }
        }
        foreach (var (childId, childTransform) in model.Components)
        {
            AddObject(childId, childTransform * transform, objects, builder, depth + 1, path, ct);
        }
    }

    private static float UnitScale(string? unit, string path) => unit switch
    {
        null or "millimeter" => 1f,
        "micron" => 0.001f,
        "centimeter" => 10f,
        "inch" => 25.4f,
        "foot" => 304.8f,
        "meter" => 1000f,
        _ => throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"unknown unit '{unit}'"),
    };

    /// <summary>3MF transform: 12 numbers, a 4×3 matrix applied to row vectors (the System.Numerics convention).</summary>
    private static Matrix4x4 Transform(string? text, string path)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Matrix4x4.Identity;
        }
        var parts = text.Split(ModelText.WhitespaceAndNewLines, StringSplitOptions.RemoveEmptyEntries);
        var m = new float[12];
        if (parts.Length != 12 || parts.Select((p, i) => ModelText.TryParseFloat(p, out m[i])).Any(ok => !ok))
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", "invalid transform");
        }
        return new Matrix4x4(
            m[0], m[1], m[2], 0,
            m[3], m[4], m[5], 0,
            m[6], m[7], m[8], 0,
            m[9], m[10], m[11], 1);
    }

    private static int RequiredInt(XmlReader xml, string name, string path) =>
        int.TryParse(xml.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"invalid or missing '{name}'");

    private static float RequiredFloat(XmlReader xml, string name, string path) =>
        ModelText.TryParseFloat(xml.GetAttribute(name), out var value)
            ? value
            : throw new ConversionException(ConversionErrorCode.CorruptFile, path, "model", $"invalid or missing '{name}'");

    public static void Write(Mesh mesh, string path, CancellationToken ct)
    {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteText(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>" +
                "</Types>\n");
            WriteText(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                $"<Relationships xmlns=\"{RelationshipsNamespace}\">" +
                $"<Relationship Target=\"/{ModelPath}\" Id=\"rel0\" Type=\"{ModelRelationshipType}\"/>" +
                "</Relationships>\n");

            var entry = zip.CreateEntry(ModelPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
            xml.WriteStartDocument();
            xml.WriteStartElement("model", CoreNamespace);
            xml.WriteAttributeString("unit", "millimeter");
            xml.WriteAttributeString("xml", "lang", null, "en-US");
            xml.WriteStartElement("resources", CoreNamespace);
            xml.WriteStartElement("object", CoreNamespace);
            xml.WriteAttributeString("id", "1");
            xml.WriteAttributeString("type", "model");
            xml.WriteStartElement("mesh", CoreNamespace);
            xml.WriteStartElement("vertices", CoreNamespace);
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                if ((i & 0xFFFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }
                var v = mesh.Vertices[i];
                xml.WriteStartElement("vertex", CoreNamespace);
                xml.WriteAttributeString("x", ModelText.Format(v.X));
                xml.WriteAttributeString("y", ModelText.Format(v.Y));
                xml.WriteAttributeString("z", ModelText.Format(v.Z));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteStartElement("triangles", CoreNamespace);
            var indices = mesh.Indices;
            for (var t = 0; t < indices.Count; t += 3)
            {
                if ((t & 0x3FFFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }
                xml.WriteStartElement("triangle", CoreNamespace);
                xml.WriteAttributeString("v1", indices[t].ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("v2", indices[t + 1].ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("v3", indices[t + 2].ToString(CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // triangles
            xml.WriteEndElement(); // mesh
            xml.WriteEndElement(); // object
            xml.WriteEndElement(); // resources
            xml.WriteStartElement("build", CoreNamespace);
            xml.WriteStartElement("item", CoreNamespace);
            xml.WriteAttributeString("objectid", "1");
            xml.WriteEndElement();
            xml.WriteEndElement(); // build
            xml.WriteEndElement(); // model
            xml.WriteEndDocument();
        }
    }

    private static void WriteText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(new UTF8Encoding(false).GetBytes(content));
    }

    /// <summary>Read-only pass-through stream that fails once more than a set number of bytes came through.</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private readonly string _path;
        private long _read;

        public LimitedReadStream(Stream inner, long limit, string path)
        {
            _inner = inner;
            _limit = limit;
            _path = path;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(_inner.Read(buffer));

        private int Count(int n)
        {
            _read += n;
            if (_read > _limit)
            {
                throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", "3MF model part too large");
            }
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
