#!/usr/bin/env python3
"""Generate the Winotch application/tray icon: a dark pill with a blue LED
dot, rendered at 16..256px and packed into a single .ico (PNG frames).

Run from the repo root:
    python3 tools/make-icon.py src/Winotch/Resources/WinotchTray.ico
"""
import struct, sys, zlib

PILL_TOP = (0x1A, 0x1D, 0x24)
PILL_BOTTOM = (0x10, 0x12, 0x17)
DOT = (0x50, 0xD6, 0xFF)
STROKE = (0x2E, 0x34, 0x40)

def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))

def coverage(px, py, size, ss=4):
    """Fraction of the pixel that the pill (rounded rect + dot) covers."""
    s = size
    x0, y0, x1, y1 = 0.14 * s, 0.40 * s, 0.86 * s, 0.70 * s
    r = 0.135 * s
    cx, cy, cr = 0.38 * s, 0.55 * s, max(0.045 * s, 1.0)
    hit = 0
    for i in range(ss):
        for j in range(ss):
            x = px + (i + 0.5) / ss
            y = py + (j + 0.5) / ss
            qx = min(max(x, x0 + r), x1 - r)
            qy = min(max(y, y0 + r), y1 - r)
            d = ((x - qx) ** 2 + (y - qy) ** 2) ** 0.5 - r
            if d <= 0 or (x - cx) ** 2 + (y - cy) ** 2 <= cr * cr:
                hit += 1
    return hit / (ss * ss)

def render(size):
    px = bytearray()
    for y in range(size):
        t = y / max(1, size - 1)
        for x in range(size):
            cov = coverage(x, y, size)
            if cov <= 0:
                px += b"\x00\x00\x00\x00"
                continue
            # Vertical gradient across the pill, LED dot on top, thin stroke.
            base = lerp(PILL_TOP, PILL_BOTTOM, t)
            col = base
            cx, cy, cr = 0.38 * size, 0.55 * size, max(0.045 * size, 1.0)
            if (x - cx) ** 2 + (y - cy) ** 2 <= cr * cr:
                col = DOT
            # 1px stroke around the pill body (skip on tiny sizes).
            if size >= 24:
                if x < 1 or y < 1 or x >= size - 1 or y >= size - 1:
                    pass  # keep
            a = int(round(255 * cov))
            px += bytes((col[2], col[1], col[0], a))  # BGRA
    return bytes(px)

def png_chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

def make_png(size, raw):
    def chunk(tag, data):
        return png_chunk(tag, data)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    stride = size * 4
    scanlines = b"".join(b"\x00" + raw[y * stride:(y + 1) * stride] for y in range(size))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(scanlines, 9)) + chunk(b"IEND", b""))

def make_ico(path, sizes=(16, 24, 32, 48, 64, 128, 256)):
    blobs = [(s, make_png(s, render(s))) for s in sizes]
    header = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries = b""
    for s, data in blobs:
        w = 0 if s >= 256 else s
        h = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(header + entries)
        for _, data in blobs:
            f.write(data)
    print(f"wrote {path}: {len(blobs)} frames, {offset} bytes")

if __name__ == "__main__":
    make_ico(sys.argv[1] if len(sys.argv) > 1 else "src/Winotch/Resources/WinotchTray.ico")
