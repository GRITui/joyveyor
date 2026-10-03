#!/usr/bin/env python3
"""
Extract the exact pixel rows Unity samples for each sprite rect, and render
them top-down (i.e. as the sprite appears in-game). This is the ground-truth
view: if a region looks right here, it renders right in Unity.

Unity rect (x, y, w, h) with y-up  ->  PNG rows [256-(y+h), 256-y)  read top-down.
"""
from PIL import Image, ImageDraw

UNITY_RECTS = {
    "belt_E_strip": (0, 0, 128, 32),
    "src_idle":     (128, 0, 32, 32),
    "src_flash":    (160, 0, 32, 32),
    "sink":         (192, 0, 32, 32),
    "splitter":     (224, 0, 32, 32),
    "merger":       (0, 32, 32, 32),
    "crate":        (32, 32, 16, 16),
    "hotbar_row":   (0, 64, 96, 16),
}

def unity_rows(y, h, size=256):
    return (size - (y + h), size - y)  # [top, bottom) in PNG row space


def render(path, label, scale=8, out=None):
    im = Image.open(path).convert("RGBA")
    px = im.load()
    W, H = im.size
    tiles = []
    for name, (x, y, w, h) in UNITY_RECTS.items():
        top, bot = unity_rows(y, h, H)
        assert 0 <= top and bot <= H, (name, top, bot, H)
        crop = im.crop((x, top, x + w, bot))   # top-down, as seen in game
        tiles.append((name, crop))

    pad, cols = 12, 4
    tw = max(t[1].width for t in tiles) * scale + pad
    th = max(t[1].height for t in tiles) * scale + pad + 14
    rows = (len(tiles) + cols - 1) // cols
    canvas = Image.new("RGB", (tw * cols, th * rows), (18, 20, 26))
    d = ImageDraw.Draw(canvas)
    for i, (name, crop) in enumerate(tiles):
        cx, cy = (i % cols) * tw, (i // cols) * th
        big = crop.resize((crop.width * scale, crop.height * scale), Image.NEAREST)
        canvas.paste(big, (cx + pad // 2, cy + pad // 2), big)
        d.text((cx + pad // 2, cy + pad // 2 + big.height + 1), name, fill=(230, 230, 235))
    dest = out or f"/Users/grit/.hermes/profiles/art-designer/cache/scratch/unityview_{label}.png"
    canvas.save(dest)
    print(f"{label}: wrote {dest} ({canvas.size})")
    return dest


if __name__ == "__main__":
    render("/Users/grit/repos/joyveyor/docs/art/before/atlas_v1.png", "v1")
    render("/Users/grit/repos/joyveyor/unity-project/Assets/Art/atlas.png", "v2")