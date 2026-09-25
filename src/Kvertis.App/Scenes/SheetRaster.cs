using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>
/// A cell of the drawn sheet: its grid position, the colours of the old and the new sheet plus three fixed
/// random numbers (worksheet "Pixelwirbel"). Immutable raster data; the per-frame position of the pixel lives
/// in <see cref="SwirlPixel"/>.
/// </summary>
public readonly record struct SheetCell(byte Gx, byte Gy, SceneColor C0, SceneColor C1, float A0, float A1, float R1, float R2, float R3)
{
    /// <summary>Top-left corner of the cell in sheet coordinates (96 × 124).</summary>
    public Vector2 Local => new(Gx * SheetRaster.Step, Gy * SheetRaster.Step);
}

/// <summary>Where a sheet lies: top-left corner, scale of the 96 × 124 sheet and rotation around the corner.</summary>
public readonly record struct SheetPlacement(Vector2 TopLeft, float Scale, float Rotation)
{
    /// <summary>The middle of the unrotated box, as the design measures a stack place (<c>M(T)</c>).</summary>
    public Vector2 Centre => TopLeft + new Vector2(SheetRaster.Width, SheetRaster.Height) * (Scale * 0.5f);

    /// <summary>Size of the box on the surface.</summary>
    public Vector2 Size => new Vector2(SheetRaster.Width, SheetRaster.Height) * Scale;

    /// <summary>The surface position of a cell of a sheet lying here (the design's <c>pt(q, p)</c>).</summary>
    public Vector2 PointOf(in SheetCell cell)
    {
        var local = cell.Local * Scale;
        if (Rotation == 0f)
        {
            return TopLeft + local;
        }

        var c = MathF.Cos(Rotation);
        var s = MathF.Sin(Rotation);
        return TopLeft + new Vector2(local.X * c - local.Y * s, local.X * s + local.Y * c);
    }
}

/// <summary>
/// The pixel raster of a drawn sheet, computed from the media kind alone: no bitmap, no preview image
/// (ADR-023). 24 × 31 cells of 4 DIP; the empty corners beside the tab are left out.
/// </summary>
public static class SheetRaster
{
    public const int Width = 96;
    public const int Height = 124;
    public const int Step = 4;
    public const int Columns = Width / Step;
    public const int Rows = Height / Step;

    // Fixed content colours of the mock-up (design lines 4381-4403); content of the stand-in, not UI tokens.
    private static readonly SceneColor SkyTop = SceneColor.FromHex(0x9CC3EE);
    private static readonly SceneColor SkyBottom = SceneColor.FromHex(0xE6EEF6);
    private static readonly SceneColor GroundTop = SceneColor.FromHex(0xD9C9A4);
    private static readonly SceneColor GroundBottom = SceneColor.FromHex(0xCBB88C);
    private static readonly SceneColor Mountain = SceneColor.FromHex(0x5F7C9C);
    private static readonly SceneColor Sun = SceneColor.FromHex(0xF2C46A);
    private static readonly SceneColor AudioBack = SceneColor.FromHex(0xF1ECFB);
    private static readonly SceneColor AudioBar = SceneColor.FromHex(0x7B61C4);
    private static readonly SceneColor VideoBack = SceneColor.FromHex(0x1D2226);
    private static readonly SceneColor VideoPlay = SceneColor.FromHex(0xF2B45A);
    private static readonly SceneColor DocumentBack = SceneColor.FromHex(0xFFFFFF);
    private static readonly SceneColor DocumentLine = SceneColor.FromHex(0xC5CCD1);
    private static readonly SceneColor ModelBack = SceneColor.FromHex(0xF7EEF4);
    private static readonly SceneColor ModelLine = SceneColor.FromHex(0x9A2F7D);
    private static readonly SceneColor LabelText = SceneColor.FromHex(0x3B4247);

    private static readonly float[] AudioBars = [8, 14, 22, 30, 18, 26, 38, 44, 30, 20, 34, 40, 26, 16, 28, 36, 22, 12, 20, 14];
    private static readonly float[] DocumentLines = [1f, 0.8f, 0.95f, 0.6f, 0.9f, 0.7f];

    // Fixed text pattern: format label (two rows) and file name (one row), starting at column 2.
    private const string LabelMask = "1101101110";
    private const string NameMask = "111011011110110101";

    private const float ContentX = 8f;
    private const float ContentY = 22f;
    private const float ContentWidth = 80f;
    private const float ContentHeight = 52f;

    /// <summary>
    /// The cells of one sheet in row order. Deterministic for a seed: the three random numbers per cell are
    /// drawn from <paramref name="random"/> in that order.
    /// </summary>
    public static SheetCell[] Build(MediaKind kind, ScenePalette palette, Random random)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(random);

        var kindColour = palette.For(kind);
        var cells = new List<SheetCell>(Columns * Rows);
        for (var gy = 0; gy < Rows; gy++)
        {
            for (var gx = 0; gx < Columns; gx++)
            {
                if (!TryColours(kind, kindColour, palette, gx, gy, out var c0, out var c1))
                {
                    continue;
                }

                cells.Add(new SheetCell((byte)gx, (byte)gy, c0, c1, 1f, 1f, random.NextSingle(), random.NextSingle(), random.NextSingle()));
            }
        }

        return [.. cells];
    }

    /// <summary>True for the cells of the tab (kind colour, mint on the new sheet).</summary>
    public static bool IsTab(int gx, int gy) => gy <= 3 && gx >= 1 && gx <= 11;

    private static bool TryColours(MediaKind kind, SceneColor kindColour, ScenePalette palette, int gx, int gy, out SceneColor c0, out SceneColor c1)
    {
        if (IsTab(gx, gy))
        {
            c0 = kindColour;
            c1 = palette.Mint;
            return true;
        }

        if (gy <= 2)
        {
            c0 = c1 = default;
            return false;
        }

        if (gx == 0 || gx == Columns - 1 || gy == 3 || gy == Rows - 1)
        {
            c0 = palette.Paper;
            c1 = palette.Mint;
            return true;
        }

        if (gy >= 5 && gy <= 18 && gx >= 2 && gx <= 21)
        {
            var x = gx * Step + Step / 2f - ContentX;
            var y = gy * Step + Step / 2f - ContentY;
            (c0, c1) = Content(kind, kindColour, palette, x, y);
            return true;
        }

        c0 = c1 = palette.Paper;
        if (gy >= 24)
        {
            var column = gx - 2;
            if ((gy == 24 || gy == 25) && column >= 0 && column < LabelMask.Length && LabelMask[column] == '1')
            {
                c0 = c1 = LabelText;
            }
            else if (gy == 28 && column >= 0 && column < NameMask.Length && NameMask[column] == '1')
            {
                c0 = c1 = palette.Muted;
            }
        }

        return true;
    }

    // x, y: sample point relative to the content field (80 × 52).
    private static (SceneColor Old, SceneColor New) Content(MediaKind kind, SceneColor kindColour, ScenePalette palette, float x, float y)
    {
        const float w = ContentWidth;
        const float h = ContentHeight;
        switch (kind)
        {
            case MediaKind.Image:
            {
                var v = y / h;
                var colour = v < 0.62f
                    ? SceneColor.Lerp(SkyTop, SkyBottom, v / 0.62f)
                    : SceneColor.Lerp(GroundTop, GroundBottom, (v - 0.62f) / 0.38f);
                ReadOnlySpan<Vector2> ridge =
                [
                    new(8f, h * 0.66f), new(30f, 14f), new(44f, 30f), new(56f, 18f), new(w - 6f, h * 0.66f),
                ];
                if (InPolygon(ridge, new Vector2(x, y)))
                {
                    colour = Mountain;
                }

                if (Vector2.DistanceSquared(new Vector2(x, y), new Vector2(w - 16f, 12f)) <= 49f)
                {
                    colour = Sun;
                }

                return (colour, colour);
            }

            case MediaKind.Audio:
            {
                var colour = AudioBack;
                for (var i = 0; i < AudioBars.Length; i++)
                {
                    var left = 4f + i * 3.7f;
                    var half = AudioBars[i] * 0.9f / 2f;
                    if (x >= left && x <= left + 2.4f && MathF.Abs(y - h / 2f) <= half)
                    {
                        colour = AudioBar;
                        break;
                    }
                }

                return (colour, colour);
            }

            case MediaKind.Video:
            {
                ReadOnlySpan<Vector2> play = [new(w / 2f - 9f, h / 2f - 12f), new(w / 2f + 13f, h / 2f), new(w / 2f - 9f, h / 2f + 12f)];
                var colour = InPolygon(play, new Vector2(x, y)) ? VideoPlay : VideoBack;
                return (colour, colour);
            }

            case MediaKind.Model3D:
            {
                var centre = new Vector2(w / 2f, h / 2f + 2f);
                const float s = 15f;
                ReadOnlySpan<Vector2> hexagon =
                [
                    new(0f, -s), new(s * 0.9f, -s / 2f), new(s * 0.9f, s / 2f), new(0f, s), new(-s * 0.9f, s / 2f), new(-s * 0.9f, -s / 2f),
                ];
                var p = new Vector2(x, y) - centre;
                var near = false;
                for (var i = 0; i < hexagon.Length && !near; i++)
                {
                    near = DistanceToSegment(p, hexagon[i], hexagon[(i + 1) % hexagon.Length]) <= 1.6f;
                }

                near = near
                    || DistanceToSegment(p, Vector2.Zero, hexagon[3]) <= 1.6f
                    || DistanceToSegment(p, Vector2.Zero, hexagon[5]) <= 1.6f
                    || DistanceToSegment(p, Vector2.Zero, hexagon[1]) <= 1.6f;
                var colour = near ? ModelLine : ModelBack;
                return (colour, colour);
            }

            case MediaKind.Document:
            {
                if (x >= 5f && x <= 5f + w * 0.6f && y >= 6f && y <= 11f)
                {
                    return (kindColour, palette.Mint);
                }

                for (var i = 0; i < DocumentLines.Length; i++)
                {
                    var top = 16f + i * 5.5f;
                    if (y >= top && y <= top + 2.6f && x >= 5f && x <= 5f + (w - 10f) * DocumentLines[i])
                    {
                        return (DocumentLine, DocumentLine);
                    }
                }

                return (DocumentBack, DocumentBack);
            }

            default:
                return (palette.Paper, palette.Paper);
        }
    }

    private static bool InPolygon(ReadOnlySpan<Vector2> polygon, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
