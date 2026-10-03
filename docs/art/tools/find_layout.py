#!/usr/bin/env python3
"""Find where content actually lives in each atlas, and test the y-flip question."""
from PIL import Image

V1 = "/Users/grit/repos/joyveyor/docs/art/before/atlas_v1.png"
V2 = "/Users/grit/repos/joyveyor/unity-project/Assets/Art/atlas.png"

def content_map(path, label):
    im = Image.open(path).convert("RGBA")
    px = im.load()
    print(f"=== {label} ({im.size}) ===")
    rows = []
    for y in range(256):
        n = sum(1 for x in range(256) if px[x, y][3] > 0)
        rows.append(n)
    bands = []
    start = None
    for y, n in enumerate(rows):
        if n > 0 and start is None:
            start = y
        elif n == 0 and start is not None:
            bands.append((start, y - 1))
            start = None
    if start is not None:
        bands.append((start, 255))
    for a, b in bands:
        print(f"  content rows {a}..{b}  (height {b-a+1})")
    # columns within first band
    if bands:
        a, b = bands[0]
        cols = []
        st = None
        for x in range(256):
            n = sum(1 for y in range(a, b + 1) if px[x, y][3] > 0)
            if n > 0 and st is None:
                st = x
            elif n == 0 and st is not None:
                cols.append((st, x - 1))
                st = None
        if st is not None:
            cols.append((st, 255))
        print(f"  first band column runs: {cols[:12]}")
    print()

content_map(V1, "v1 (committed S6 atlas)")
content_map(V2, "v2 (my new atlas)")