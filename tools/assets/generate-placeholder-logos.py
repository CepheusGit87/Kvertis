#!/usr/bin/env python3
"""Generates the placeholder logo PNGs for src/Kvertis.App/Assets (docs/06-design.md, "Logo und Icon").

Pure Python (zlib only), no imaging library needed. The geometry matches Assets/Logo.svg: a flat accent
square with a white circle and a white rounded square that overlap ("two shapes turning into each other").
These are placeholders and are replaced by the final artwork later.
"""
import os
import struct
import zlib

ACCENT = (0x2F, 0x5D, 0xA8)   # neutral blue placeholder, not a brand color
LIGHT = (0xFF, 0xFF, 0xFF)
OVERLAP = (0xA9, 0xC1, 0xE8)
SUPERSAMPLE = 4

# (file name, width, height)
TARGETS = [
    ("StoreLogo.png", 50, 50),
    ("Square150x150Logo.png", 150, 150),
    ("Square44x44Logo.png", 44, 44),
    ("Wide310x150Logo.png", 310, 150),
    ("SplashScreen.png", 620, 300),
    ("LockScreenLogo.png", 24, 24),
]


def inside_rounded_rect(x, y, left, top, right, bottom, radius):
    if x < left or x > right or y < top or y > bottom:
        return False
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def sample(u, v, aspect):
    """Colour at normalised coordinates (0..1 on the shorter side, centred), or None for transparent."""
    # Background: rounded accent square covering the whole canvas.
    if not inside_rounded_rect(u, v, 0.0, 0.0, aspect[0], aspect[1], 0.18 * min(aspect)):
        return None
    # Mark in a unit square centred on the canvas.
    ox = (aspect[0] - 1.0) / 2.0
    oy = (aspect[1] - 1.0) / 2.0
    x, y = u - ox, v - oy
    in_circle = (x - 0.40) ** 2 + (y - 0.42) ** 2 <= 0.20 ** 2
    in_square = inside_rounded_rect(x, y, 0.42, 0.40, 0.76, 0.74, 0.06)
    if in_circle and in_square:
        return OVERLAP
    if in_circle or in_square:
        return LIGHT
    return ACCENT


def render(width, height):
    short = float(min(width, height))
    aspect = (width / short, height / short)
    rows = []
    n = SUPERSAMPLE
    for py in range(height):
        row = bytearray([0])  # filter type 0
        for px in range(width):
            r = g = b = a = 0
            for sy in range(n):
                for sx in range(n):
                    u = (px + (sx + 0.5) / n) / short
                    v = (py + (sy + 0.5) / n) / short
                    c = sample(u, v, aspect)
                    if c is not None:
                        r += c[0]
                        g += c[1]
                        b += c[2]
                        a += 255
            count = n * n
            covered = a // 255
            if covered:
                row += bytes((r // covered, g // covered, b // covered, a // count))
            else:
                row += bytes((0, 0, 0, 0))
        rows.append(bytes(row))
    return b"".join(rows)


def png(width, height, raw):
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    root = os.path.dirname(os.path.abspath(__file__))
    out = os.path.normpath(os.path.join(root, "..", "..", "src", "Kvertis.App", "Assets"))
    os.makedirs(out, exist_ok=True)
    for name, width, height in TARGETS:
        with open(os.path.join(out, name), "wb") as f:
            f.write(png(width, height, render(width, height)))
        print(f"{name}: {width}x{height}")


if __name__ == "__main__":
    main()
