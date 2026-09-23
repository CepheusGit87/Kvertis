using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Conversion.Models;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tests.Documents;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Models;

/// <summary>3D conversions (ADR-016): detection, every reader and writer, units and axes, refusals.</summary>
public sealed class ModelConverterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FormatDetector _detector = new(new FormatRegistry(), []);
    private readonly ModelConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>A 10 × 20 × 30 mm box with one corner at the origin: 8 vertices, 12 outward-facing triangles.</summary>
    internal static Mesh Box()
    {
        var mesh = new Mesh();
        foreach (var z in new[] { 0f, 30f })
        {
            foreach (var y in new[] { 0f, 20f })
            {
                foreach (var x in new[] { 0f, 10f })
                {
                    mesh.Vertices.Add(new Vector3(x, y, z));
                }
            }
        }
        // Vertex index = x + 2y + 4z (each 0/1).
        int[][] quads =
        [
            [0, 2, 3, 1], // bottom (z = 0), facing -Z
            [4, 5, 7, 6], // top, +Z
            [0, 1, 5, 4], // front (y = 0), -Y
            [2, 6, 7, 3], // back, +Y
            [0, 4, 6, 2], // left (x = 0), -X
            [1, 3, 7, 5], // right, +X
        ];
        foreach (var q in quads)
        {
            mesh.AddTriangle(q[0], q[1], q[2]);
            mesh.AddTriangle(q[0], q[2], q[3]);
        }
        return mesh;
    }

    private string WriteModel(string name, FormatId format)
    {
        var path = _dir.File(name);
        ModelConverter.Write(Box(), format, path, CancellationToken.None);
        return path;
    }

    private async Task<Mesh> ReadBack(string path)
    {
        var info = await _detector.DetectAsync(path, CancellationToken.None);
        info.Kind.ShouldBe(MediaKind.Model3D);
        return ModelConverter.Read(info, CancellationToken.None);
    }

    private static void ShouldBeTheBox(Mesh mesh)
    {
        mesh.TriangleCount.ShouldBe(12);
        var (min, max) = mesh.Bounds();
        Vector3.Distance(min, Vector3.Zero).ShouldBeLessThan(1e-3f);
        Vector3.Distance(max, new Vector3(10, 20, 30)).ShouldBeLessThan(1e-3f);
        SignedVolume(mesh).ShouldBe(6000f, 0.5f); // Positive: triangles still face outward.
    }

    /// <summary>Divergence theorem; positive for a closed mesh whose triangles face outward.</summary>
    private static float SignedVolume(Mesh mesh)
    {
        var volume = 0f;
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Vertices[mesh.Indices[t]];
            var b = mesh.Vertices[mesh.Indices[t + 1]];
            var c = mesh.Vertices[mesh.Indices[t + 2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
        }
        return volume;
    }

    public static TheoryData<string, string> Pairs()
    {
        string[] inputs = ["stl", "3mf", "obj", "ply", "glb"];
        string[] outputs = ["stl", "3mf", "obj", "ply", "glb"];
        var data = new TheoryData<string, string>();
        foreach (var i in inputs)
        {
            foreach (var o in outputs)
            {
                data.Add(i, o);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Every_format_converts_to_every_other_without_losing_shape_size_or_orientation(string from, string to)
    {
        var input = WriteModel("in." + from, new FormatId(from));
        var info = await _detector.DetectAsync(input, CancellationToken.None);
        info.Format.ShouldBe(new FormatId(from));

        var output = _dir.File("out." + to);
        var result = await _converter.ConvertAsync(info, output, new ConversionSettings(new FormatId(to)), new NullProgress(), CancellationToken.None);

        result.OutputBytes.ShouldBeGreaterThan(0);
        File.Exists(output + ".kvertis-tmp").ShouldBeFalse();
        ShouldBeTheBox(await ReadBack(output));
    }

    [Fact]
    public async Task Registry_offers_all_model_formats_and_the_resolver_finds_the_converter()
    {
        var registry = new FormatRegistry();
        var info = await _detector.DetectAsync(WriteModel("a.stl", FormatRegistry.Stl), CancellationToken.None);
        var suggestion = registry.Suggest(info)!;
        suggestion.Default.ShouldBe(FormatRegistry.ThreeMf);
        suggestion.Options.Select(o => o.Id).ShouldBe(["3mf", "obj", "ply", "glb", "stl"]);

        IConverterResolver resolver = new ConverterResolver([_converter]);
        foreach (var option in suggestion.Options)
        {
            resolver.CanConvert(info, option).ShouldBeTrue();
        }
        resolver.CanConvert(info, FormatRegistry.Gltf).ShouldBeFalse(); // Read only.
        resolver.CanConvert(info, FormatRegistry.Png).ShouldBeFalse();
    }

    [Fact]
    public void Model_converter_ignores_other_media()
    {
        var png = new InputInfo("x.png", FormatRegistry.Png, MediaKind.Image, 1, null, null, null, null, []);
        _converter.Supports(png, FormatRegistry.Stl).ShouldBeFalse();
    }

    [Fact]
    public async Task Binary_stl_whose_header_starts_with_solid_is_still_binary()
    {
        var path = WriteModel("trap.stl", FormatRegistry.Stl);
        var bytes = File.ReadAllBytes(path);
        Encoding.ASCII.GetBytes("solid trap header").CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);

        ShouldBeTheBox(await ReadBack(path));
    }

    [Fact]
    public async Task Text_stl_is_read()
    {
        var text = new StringBuilder("solid box\n");
        var box = Box();
        for (var t = 0; t < box.Indices.Count; t += 3)
        {
            text.Append("  facet normal 0 0 0\n    outer loop\n");
            for (var k = 0; k < 3; k++)
            {
                var v = box.Vertices[box.Indices[t + k]];
                text.Append(FormattableString.Invariant($"      vertex {v.X} {v.Y} {v.Z}\n"));
            }
            text.Append("    endloop\n  endfacet\n");
        }
        text.Append("endsolid box\n");
        var path = _dir.File("text.stl");
        File.WriteAllText(path, text.ToString());

        var mesh = await ReadBack(path);
        ShouldBeTheBox(mesh);
        mesh.Vertices.Count.ShouldBe(8); // Identical corners are merged.
    }

    [Fact]
    public async Task Obj_with_quads_slashes_and_negative_indices_is_read()
    {
        var path = _dir.File("quads.obj");
        File.WriteAllText(path, """
            # box
            mtllib box.mtl
            o Box
            v 0 0 0
            v 10 0 0
            v 0 20 0
            v 10 20 0
            v 0 0 30
            v 10 0 30
            v 0 20 30
            v 10 20 30
            vt 0 0
            vn 0 0 1
            usemtl none
            f 1/1/1 3/1/1 4/1/1 2/1/1
            f 5//1 6//1 8//1 7//1
            f 1 2 6 5
            f -6 -2 -1 -5
            f 1 5 7 3
            f 2 4 8 6
            """);

        ShouldBeTheBox(await ReadBack(path));
    }

    [Fact]
    public async Task Obj_extension_with_other_text_stays_text()
    {
        var path = _dir.File("notes.obj");
        File.WriteAllText(path, "Shopping list\nmilk\n");
        (await _detector.DetectAsync(path, CancellationToken.None)).Format.ShouldBe(FormatRegistry.Txt);
    }

    [Fact]
    public async Task Json_data_file_is_not_taken_for_a_model()
    {
        var path = _dir.File("config.json");
        File.WriteAllText(path, "{ \"asset\": { \"version\": \"2.0\" }, \"name\": \"not a model\" }");
        (await _detector.DetectAsync(path, CancellationToken.None)).Format.ShouldBe(FormatRegistry.Txt);
    }

    [Fact]
    public async Task Obj_face_pointing_past_the_vertices_is_corrupt()
    {
        var path = _dir.File("broken.obj");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 9\n");
        var info = await _detector.DetectAsync(path, CancellationToken.None);

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("x.stl"), new ConversionSettings(FormatRegistry.Stl), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
        File.Exists(_dir.File("x.stl")).ShouldBeFalse();
    }

    [Fact]
    public async Task Text_ply_with_extra_properties_and_elements_is_read()
    {
        var box = Box();
        var text = new StringBuilder();
        text.Append("ply\nformat ascii 1.0\ncomment made by hand\n");
        text.Append("element vertex 8\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\n");
        text.Append("element face 12\nproperty uchar flags\nproperty list uchar int vertex_indices\n");
        text.Append("element edge 1\nproperty int vertex1\nproperty int vertex2\nend_header\n");
        foreach (var v in box.Vertices)
        {
            text.Append(FormattableString.Invariant($"{v.X} {v.Y} {v.Z} 255 0 0\n"));
        }
        for (var t = 0; t < box.Indices.Count; t += 3)
        {
            text.Append(FormattableString.Invariant($"7 3 {box.Indices[t]} {box.Indices[t + 1]} {box.Indices[t + 2]}\n"));
        }
        text.Append("0 1\n");
        var path = _dir.File("hand.ply");
        File.WriteAllText(path, text.ToString());

        ShouldBeTheBox(await ReadBack(path));
    }

    [Fact]
    public async Task Big_endian_ply_with_double_coordinates_is_read()
    {
        var box = Box();
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("ply\nformat binary_big_endian 1.0\nelement vertex 8\nproperty double x\nproperty double y\nproperty double z\nelement face 12\nproperty list uchar uint vertex_indices\nend_header\n"));
        var buffer = new byte[8];
        foreach (var v in box.Vertices)
        {
            foreach (var c in new[] { v.X, v.Y, v.Z })
            {
                BinaryPrimitives.WriteDoubleBigEndian(buffer, c);
                stream.Write(buffer);
            }
        }
        for (var t = 0; t < box.Indices.Count; t += 3)
        {
            stream.WriteByte(3);
            for (var k = 0; k < 3; k++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)box.Indices[t + k]);
                stream.Write(buffer, 0, 4);
            }
        }
        var path = _dir.File("big.ply");
        File.WriteAllBytes(path, stream.ToArray());

        ShouldBeTheBox(await ReadBack(path));
    }

    [Fact]
    public async Task Threemf_units_components_and_mirroring_transforms_are_applied()
    {
        // Half-size box in centimeters (0.5 × 1 × 1.5 cm), mirrored along X and moved back by 1 cm via a component.
        var box = Box();
        var vertices = string.Concat(box.Vertices.Select(v => FormattableString.Invariant($"<vertex x=\"{v.X / 20}\" y=\"{v.Y / 20}\" z=\"{v.Z / 20}\"/>")));
        var triangles = string.Concat(Enumerable.Range(0, 12).Select(t => $"<triangle v1=\"{box.Indices[t * 3]}\" v2=\"{box.Indices[(t * 3) + 1]}\" v3=\"{box.Indices[(t * 3) + 2]}\"/>"));
        var model = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <model unit="centimeter" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">
              <resources>
                <object id="1" type="model"><mesh><vertices>{vertices}</vertices><triangles>{triangles}</triangles></mesh></object>
                <object id="2" type="model"><components><component objectid="1" transform="-2 0 0 0 2 0 0 0 2 1 0 0"/></components></object>
              </resources>
              <build><item objectid="2"/></build>
            </model>
            """;
        var path = _dir.File("parts.3mf");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            WriteEntry(zip, "_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/parts.model\" Id=\"r\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
            WriteEntry(zip, "3D/parts.model", model);
        }

        // Scaled ×2 and mirrored: x from 1 - 1 cm = 0 to 1 cm = 10 mm; y 0..20 mm; z 0..30 mm.
        ShouldBeTheBox(await ReadBack(path));
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Fact]
    public async Task Glb_is_written_in_meters_with_y_up()
    {
        var path = WriteModel("box.glb", FormatRegistry.Glb);
        var bytes = File.ReadAllBytes(path);
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        using var json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        var position = json.RootElement.GetProperty("accessors")[0];
        var max = position.GetProperty("max").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        var min = position.GetProperty("min").EnumerateArray().Select(e => e.GetSingle()).ToArray();

        // 10 × 20 × 30 mm, Z up → 0.01 × 0.03 × 0.02 m, Y up (depth runs toward -Z).
        max[0].ShouldBe(0.01f, 1e-6f);
        max[1].ShouldBe(0.03f, 1e-6f);
        min[2].ShouldBe(-0.02f, 1e-6f);
        ShouldBeTheBox(await ReadBack(path));
    }

    private string WriteGltf(string name, string bufferUri, byte[]? externalBuffer = null, string extra = "")
    {
        var box = Box();
        var data = new byte[(box.Vertices.Count * 12) + (box.Indices.Count * 2)];
        for (var i = 0; i < box.Vertices.Count; i++)
        {
            // Stored in glTF space: meters, Y up.
            var v = box.Vertices[i];
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 12), v.X / 1000);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 4), v.Z / 1000);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 8), -v.Y / 1000);
        }
        for (var i = 0; i < box.Indices.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((box.Vertices.Count * 12) + (i * 2)), (ushort)box.Indices[i]);
        }
        if (externalBuffer is not null)
        {
            data.CopyTo(externalBuffer, 0);
        }
        var uri = bufferUri == "data" ? "data:application/octet-stream;base64," + Convert.ToBase64String(data) : bufferUri;
        var json = $$"""
            {
              "asset": { "version": "2.0" },{{extra}}
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "children": [ 1 ], "translation": [ 0, 0, 0 ] }, { "mesh": 0, "rotation": [ 0, 0, 0, 1 ] } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 8, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": 36, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 96 },
                { "buffer": 0, "byteOffset": 96, "byteLength": 72 }
              ],
              "buffers": [ { "byteLength": {{data.Length}}, "uri": "{{uri}}" } ]
            }
            """;
        var path = _dir.File(name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Gltf_with_embedded_buffer_is_read()
    {
        var path = WriteGltf("embedded.gltf", "data");
        var info = await _detector.DetectAsync(path, CancellationToken.None);
        info.Format.ShouldBe(FormatRegistry.Gltf);
        new FormatRegistry().Suggest(info)!.Default.ShouldBe(FormatRegistry.Glb);
        ShouldBeTheBox(await ReadBack(path));
    }

    [Fact]
    public async Task Gltf_with_buffer_file_next_to_it_is_read()
    {
        var buffer = new byte[168];
        var path = WriteGltf("external.gltf", "box%20data.bin", buffer);
        File.WriteAllBytes(_dir.File("box data.bin"), buffer);
        ShouldBeTheBox(await ReadBack(path));
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("/etc/passwd")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://example.invalid/box.bin")]
    public async Task Gltf_buffers_outside_the_model_folder_are_refused(string uri)
    {
        var path = WriteGltf("escape.gltf", uri);
        var info = await _detector.DetectAsync(path, CancellationToken.None);

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("x.stl"), new ConversionSettings(FormatRegistry.Stl), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public async Task Gltf_that_requires_an_extension_is_refused()
    {
        var path = WriteGltf("draco.gltf", "data", extra: "\n  \"extensionsRequired\": [ \"KHR_draco_mesh_compression\" ],");
        var info = await _detector.DetectAsync(path, CancellationToken.None);

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("x.stl"), new ConversionSettings(FormatRegistry.Stl), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        ex.Detail!.ShouldContain("KHR_draco_mesh_compression");
    }

    [Fact]
    public async Task Stl_without_triangles_is_corrupt()
    {
        var path = _dir.File("empty.stl");
        File.WriteAllText(path, "solid nothing\nendsolid nothing\n");
        var info = await _detector.DetectAsync(path, CancellationToken.None);
        info.Format.ShouldBe(FormatRegistry.Stl);

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("x.obj"), new ConversionSettings(FormatRegistry.Obj), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Forged_ply_counts_are_refused_as_too_large()
    {
        var path = _dir.File("huge.ply");
        File.WriteAllText(path, "ply\nformat ascii 1.0\nelement vertex 999999999999\nproperty float x\nproperty float y\nproperty float z\nend_header\n");
        var info = await _detector.DetectAsync(path, CancellationToken.None);

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("x.stl"), new ConversionSettings(FormatRegistry.Stl), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.FileTooLarge);
    }

    [Fact]
    public async Task Cancellation_leaves_no_output()
    {
        var info = await _detector.DetectAsync(WriteModel("c.stl", FormatRegistry.Stl), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, _dir.File("c.obj"), new ConversionSettings(FormatRegistry.Obj), new NullProgress(), cts.Token));
        ex.Code.ShouldBe(ConversionErrorCode.Cancelled);
        File.Exists(_dir.File("c.obj")).ShouldBeFalse();
    }

    [Fact]
    public async Task Existing_target_is_never_overwritten()
    {
        var info = await _detector.DetectAsync(WriteModel("keep.stl", FormatRegistry.Stl), CancellationToken.None);
        var target = _dir.File("keep.obj");
        File.WriteAllText(target, "original");

        var ex = await Should.ThrowAsync<ConversionException>(() => _converter.ConvertAsync(info, target, new ConversionSettings(FormatRegistry.Obj), new NullProgress(), CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.OutputExists);
        File.ReadAllText(target).ShouldBe("original");
    }
}
