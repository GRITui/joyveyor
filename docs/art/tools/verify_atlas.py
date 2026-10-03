#!/usr/bin/env python3
"""
Verify the baked atlas v2 against v1 in Unity's own coordinate space.

Unity samples texture rects with a y-up (bottom-left) origin, so a rect
(x, y, w, h) covers PNG rows [H-(y+h), H-y). All crops here use that mapping
— this is what the player actually sees.
"""
from PIL import Image

V1 = "/Users/grit/repos/joyveyor/docs/art/before/atlas_v1.png"
V2 = "/Users/grit/repos/joyveyor/unity-project/Assets/Art/atlas.png"

REGIONS = [
    ("belt0",   0, 0, 32, 32), ("belt1",   32, 0, 32, 32),
    ("belt2",   64, 0, 32, 32), ("belt3",   96, 0, 32, 32),
    ("src_idle", 128, 0, 32, 32), ("src_flash", 160, 0, 32, 32),
    ("sink",   192, 0, 32, 32), ("splitter", 224, 0, 32, 32),
    ("merger",  0, 32, 32, 32), ("crate",   32, 32, 16, 16),
    ("hot_belt", 0, 64, 16, 16), ("hot_src", 16, 64, 16, 16),
    ("hot_sink", 32, 64, 16, 16), ("hot_spl", 48, 64, 16, 16),
    ("hot_mer", 64, 64, 16, 16), ("hot_crate", 80, 64, 16, 16),
]

BG = (11, 13, 18)   # near-black board the belts sit on


def crop_unity(im, x, y, w, h):
    H = im.height
    return im.crop((x, H - (y + h), x + w, H - y))


def relum(c):
    def f(v):
        v /= 255.0
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2])


def ratio(a, b):
    la, lb = relum(a), relum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def stats(im, x, y, w, h):
    c = crop_unity(im, x, y, w, h)
    px = [p for p in c.getdata() if p[3] > 0]
    if not px:
        return 0, (0, 0, 0), 0
    m = tuple(sum(p[i] for p in px) // len(px) for i in range(3))
    return len(px), m, len(set(p[:3] for p in px))


v1 = Image.open(V1).convert("RGBA")
v2 = Image.open(V2).convert("RGBA")
print(f"sizes: v1={v1.size} v2={v2.size}")
if v1.size != v2.size:
    raise SystemExit("SIZE MISMATCH - would break atlas keys")

hdr = (f'{"region":<11}{"v1_px":>7}{"v2_px":>7}{"v1_mean":>17}'
       f'{"v2_mean":>17}{"v1_col":>8}{"v2_col":>8}')
print(hdr)
print("-" * len(hdr))
fail = []
for n, x, y, w, h in REGIONS:
    a, ma, ua = stats(v1, x, y, w, h)
    b, mb, ub = stats(v2, x, y, w, h)
    print(f'{n:<11}{a:>7}{b:>7}{str(ma):>17}{str(mb):>17}{ua:>8}{ub:>8}')
    if a != b:
        fail.append(f"{n}: opaque px changed {a} -> {b}")
    if b == 0:
        fail.append(f"{n}: EMPTY in v2")

print()
print("=== Belt legibility vs board %s ===" % (BG,))
for label, im in (("v1", v1), ("v2", v2)):
    c = crop_unity(im, 0, 0, 32, 32).convert("RGB")
    body = c.getpixel((6, 10))
    chev = c.getpixel((11, 15))
    print(f'{label}: body{body} vs bg {ratio(body, BG):5.2f}:1 | '
          f'chev{chev} vs bg {ratio(chev, BG):5.2f}:1 | '
          f'chev vs body {ratio(chev, body):5.2f}:1')

# The 4 belt frames must differ from each other (the scroll animation).
print()
print("=== Belt animation frames distinct? ===")
for label, im in (("v1", v1), ("v2", v2)):
    sigs = []
    for k in range(4):
        c = crop_unity(im, 32 * k, 0, 32, 32).convert("RGB")
        sigs.append(tuple(c.getdata()))
    uniq = len(set(sigs))
    print(f"{label}: {uniq}/4 unique frames" + ("" if uniq == 4 else "  <-- STATIC"))

print()
print("=== Gate ===")
if fail:
    for f in fail:
        print("FAIL:", f)
    raise SystemExit(1)
print("PASS: every region keeps its opaque-pixel count (no atlas key drift)")