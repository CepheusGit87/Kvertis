using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>
/// A triangle mesh in Kvertis' canonical model space: millimeters, Z axis up (the convention of 3D printing
/// formats). Readers convert into this space, writers out of it (glTF: meters, Y up). Geometry only: materials,
/// textures, colors and metadata are not carried over (ADR-016).
/// </summary>
internal sealed class Mesh
{
    public List<Vector3> Vertices { get; } = [];

    /// <summary>Three vertex indices per triangle, counter-clockwise seen from outside.</summary>
    public List<int> Indices { get; } = [];

    public int TriangleCount => Indices.Count / 3;

    public void AddTriangle(int a, int b, int c)
    {
        if (a == b || b == c || a == c)
        {
            return; // Degenerate: skipped, it has no area and breaks normals.
        }
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    /// <summary>Axis-aligned bounds; (0,0,0) for an empty mesh.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Vertices.Count == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (min, max);
    }

    public static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        var length = n.Length();
        return length > 0 && float.IsFinite(length) ? n / length : Vector3.Zero;
    }
}

/// <summary>
/// Builds a <see cref="Mesh"/> while reading, merges identical vertices (STL is a triangle soup) and enforces
/// the size limits, so a hostile file cannot exhaust memory.
/// </summary>
internal sealed class MeshBuilder
{
    /// <summary>Upper bound for triangles in one model; about 1 GB of binary STL.</summary>
    public const int MaxTriangles = 20_000_000;

    /// <summary>Upper bound for distinct vertices.</summary>
    public const int MaxVertices = 30_000_000;

    private readonly Dictionary<Vector3, int> _welded = [];
    private readonly string _path;

    public MeshBuilder(string path)
    {
        _path = path;
    }

    public Mesh Mesh { get; } = new();

    /// <summary>Adds a vertex that other triangles may share by index (OBJ, PLY, 3MF, glTF).</summary>
    public int AddVertex(Vector3 v)
    {
        EnsureFinite(v);
        if (Mesh.Vertices.Count >= MaxVertices)
        {
            throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", $"more than {MaxVertices} vertices");
        }
        Mesh.Vertices.Add(v);
        return Mesh.Vertices.Count - 1;
    }

    /// <summary>Adds a vertex, reusing an identical one (STL has no shared vertices).</summary>
    public int AddWelded(Vector3 v)
    {
        EnsureFinite(v);
        if (_welded.TryGetValue(v, out var index))
        {
            return index;
        }
        index = AddVertex(v);
        _welded[v] = index;
        return index;
    }

    public void AddTriangle(int a, int b, int c)
    {
        var count = Mesh.Vertices.Count;
        if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, _path, "model", "vertex index out of range");
        }
        if (Mesh.TriangleCount >= MaxTriangles)
        {
            throw new ConversionException(ConversionErrorCode.FileTooLarge, _path, "model", $"more than {MaxTriangles} triangles");
        }
        Mesh.AddTriangle(a, b, c);
    }

    /// <summary>Returns the mesh; a model without a single triangle is treated as corrupt.</summary>
    public Mesh Build()
    {
        if (Mesh.TriangleCount == 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, _path, "model", "no triangles");
        }
        return Mesh;
    }

    private void EnsureFinite(Vector3 v)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, _path, "model", "vertex is not a finite number");
        }
    }
}

/// <summary>Conversions between Kvertis model space (mm, Z up) and the glTF convention (m, Y up).</summary>
internal static class ModelSpace
{
    /// <summary>glTF (meters, Y up, +Z to the front) → millimeters, Z up. A proper rotation: winding is kept.</summary>
    public static Vector3 FromGltf(Vector3 v) => new Vector3(v.X, -v.Z, v.Y) * 1000f;

    /// <summary>Millimeters, Z up → glTF (meters, Y up).</summary>
    public static Vector3 ToGltf(Vector3 v) => new Vector3(v.X, v.Z, -v.Y) * 0.001f;
}
