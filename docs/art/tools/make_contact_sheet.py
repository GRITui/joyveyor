#!/usr/bin/env python3
"""
Before/after art-pass-1 contact sheet for card t_906f0ecf.

Two equal panels side by side (BEFORE | AFTER), each a grid of sprite crops.
Every crop uses Unity's y-up rect convention, so this is exactly what the game
renders — not what the raw PNG happens to look like.
"""
from PIL import Image, ImageDraw, ImageFont

V1 = "/Users/grit/repos/joyveyor/docs/art/before/atlas_v1.png"
V2 = "/Users/grit/repos/joyveyor/unity-project/Assets/Art/atlas.png"
OUT = "/Users/grit/repos/joyveyor/docs/art/atlas-v1-vs-v2.png"

# (label, unity rect x, y, w, h) — rects unchanged from S6 commit 1a1cc9c
REGIONS = [
    ("belt 1/4", 0, 0, 32, 32),
    ("belt 2/4", 32, 0, 32, 32),
    ("belt 3/4", 64, 0, 32, 32),
    ("belt 4/4", 96, 0, 32, 32),
    ("source", 128, 0, 32, 32),
    ("source flash", 160, 0, 32, 32),
    ("sink", 192, 0, 32, 32),
    ("splitter", 224, 0, 32, 32),
    ("merger", 0, 32, 32, 32),
    ("item crate", 32, 32, 16, 16),
    ("hotbar: 6 icons", 0, 64, 96, 16),
]

SCALE = 5
COLS = 4
PAD = 14
BG = (14, 16, 21)
TILE = 32 * SCALE          # grid cells reserve a 32px-equivalent tile
LABEL_H = 18

# The 96px-wide hotbar strip gets its own full-width row: at SCALE it is
# 480px across and would overflow a grid cell and collide with its neighbour.
GRID = [
    ("belt 1/4", 0, 0, 32, 32),
    ("belt 2/4", 32, 0, 32, 32),
    ("belt 3/4", 64, 0, 32, 32),
    ("belt 4/4", 96, 0, 32, 32),
    ("source", 128, 0, 32, 32),
    ("source flash", 160, 0, 32, 32),
    ("sink", 192, 0, 32, 32),
    ("splitter", 224, 0, 32, 32),
    ("merger", 0, 32, 32, 32),
    ("item crate", 32, 32, 16, 16),
]
WIDE = [("hotbar strip - 6 icons at their real 16px size", 0, 64, 96, 16)]


def crop_unity(im, x, y, w, h):
    """Unity rect (y-up, bottom-left origin) -> PNG crop, top-down."""
    H = im.height
    return im.crop((x, H - (y + h), x + w, H - y))


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf",
              "/System/Library/Fonts/Supplemental/Arial.ttf",
              "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, sz)
        except OSError:
            pass
    return ImageFont.load_default()


f_title, f_head = font(26), font(17)
f_lab, f_note = font(12), font(12)

rows = (len(GRID) + COLS - 1) // COLS
cell_w = TILE + PAD
cell_h = TILE + LABEL_H + PAD
panel_w = COLS * cell_w + PAD
header_h = 96
notes_h = 92
# +1 row for the full-width hotbar strip
H = header_h + rows * cell_h + cell_h + notes_h
W = panel_w * 2 + 34

canvas = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(canvas)

v1 = Image.open(V1).convert("RGBA")
v2 = Image.open(V2).convert("RGBA")

d.text((PAD, 12), "JoyVeyor art pass 1 — sprite atlas v1 vs v2", font=f_title,
       fill=(240, 242, 246))
d.text((PAD, 44), "Card t_906f0ecf  ·  every crop is sampled the way Unity does "
                  "(y-up rects), so this is exactly what renders in-game",
       font=f_note, fill=(146, 154, 166))

panels = [("BEFORE — S6 atlas (commit 1a1cc9c)", v1, 16, (168, 176, 188)),
          ("AFTER — re-baked SpriteBaker v2", v2, panel_w + 34, (118, 218, 152))]

for title, im, ox0, tcol in panels:
    d.text((ox0, header_h - 34), title, font=f_head, fill=tcol)
    for i, (name, x, y, w, h) in enumerate(GRID):
        r, c = divmod(i, COLS)
        x0 = ox0 + PAD + c * cell_w
        y0 = header_h + r * cell_h
        crop = crop_unity(im, x, y, w, h)
        # Centre each sprite in its fixed 32px-equivalent tile.
        big = crop.resize((w * SCALE, h * SCALE), Image.NEAREST)
        px = x0 + (TILE - big.width) // 2
        py = y0 + (TILE - big.height) // 2
        canvas.paste(big, (px, py), big)
        d.text((x0, y0 + TILE + 3), name, font=f_lab, fill=(198, 204, 214))
    # Full-width row for the 96px hotbar strip.
    for name, x, y, w, h in WIDE:
        wy = header_h + rows * cell_h
        crop = crop_unity(im, x, y, w, h)
        big = crop.resize((w * SCALE, h * SCALE), Image.NEAREST)
        canvas.paste(big, (ox0 + PAD, wy), big)
        d.text((ox0 + PAD, wy + big.height + 3), name, font=f_lab,
               fill=(198, 204, 214))

# panel divider
d.line([(panel_w + 17, header_h - 44),
        (panel_w + 17, header_h + rows * cell_h + cell_h)],
       fill=(48, 54, 64), width=2)

d.text((PAD, H - notes_h + 6), "What changed", font=f_head, fill=(240, 242, 246))
notes = [
    "Belts  body #2A3140 -> #3D4A5C (1.49:1 -> 2.16:1 against the near-black board); chevrons #4A5568 -> #7A8FA6, 3px thick.",
    "       Belt scroll FIXED: v1 drew 2 chevrons 16px apart offset 8k, so frames 0=2 and 1=3 (2/4 unique). v2 = 4 at 8px offset 2k -> 4/4 unique.",
    "Pieces source, sink, splitter, merger each gain a lit top edge and a shadowed bottom edge, so they read as boxes rather than flat fills.",
    "Item   the crate becomes a 3D box with a lit lid and cross straps, replacing a flat square with a single diagonal strap.",
]
for i, n in enumerate(notes):
    d.text((PAD, H - notes_h + 30 + i * 15), n, font=f_note, fill=(158, 166, 178))

canvas.save(OUT)
print(f"wrote {OUT} ({canvas.size})  panels={panel_w}x{rows*cell_h} canvas={W}x{H}")