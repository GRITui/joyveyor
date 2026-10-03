# Art pass 1 — belts, items, pieces

Card: `t_906f0ecf` (RESEARCH-jv4, step 4 of 4: research → UX audit → owner playtest → art).
Author: agent-N (art-designer). Date: 2026-10-03.
Base: `main` @ `43c3cc5`, atlas generator from S6 commit `1a1cc9c`.

**No game code was edited.** Only the atlas generator (`SpriteBaker.cs`, an
editor-only bake script) and its committed output `Assets/Art/atlas.png` changed.
Sprite dimensions, region rects and atlas keys are byte-identical, so `JVArt.cs`,
`GridEditor.cs` and the HUD bind unchanged — nothing in the sim or HUD moved.

The owner decides whether this looks better. No agent approves it.

## Where things are

| What | Path |
|---|---|
| Generator (source of truth) | `unity-project/Assets/Editor/SpriteBaker.cs` |
| Baked output | `unity-project/Assets/Art/atlas.png` (256×256, committed) |
| Before atlas | `docs/art/before/atlas_v1.png` |
| Contact sheet | `docs/art/atlas-v1-vs-v2.png` |
| After HUD/menu shots | `docs/art/after/*.png` |
| Verify + contact-sheet tools | `docs/art/tools/*.py` |

Rebake with:

```sh
/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath unity-project \
  -executeMethod SpriteBaker.BakeAll -logFile /tmp/jv_bake.log
```

## Read this before you compare the PNGs

`JVArt` samples the atlas with **y-up** rects (bottom-left origin) while a PNG
reads top-down. So the committed atlas's sprites sit in rows 176–253 of the file,
not rows 0–79 — the raw file looks upside-down until you account for it.
`docs/art/tools/unity_view.py` crops every region the way Unity does, so the
contact sheet shows exactly what renders in-game. **Comparing raw PNG rows
against each other gives the wrong answer.**

## What changed, by priority

### 1. Belts — the priority, and a real bug fixed

- **Body lifted**: `#2A3140` → `#3D4A5C`. Against the near-black board the old
  body measured **1.49:1** contrast and the chevrons **1.13:1** — effectively
  invisible. The new body is **2.16:1** and the chevrons **5.84:1**.
- **Chevrons brighter and thicker**: `#4A5568` → `#7A8FA6`, 2px → 3px, so the
  travel direction reads at a glance.
- **Belt scroll was broken (pre-existing).** v1 drew 2 chevrons 16px apart with
  a per-frame offset of `8k` px. Since 8 is half the 16px spacing, frames 0 and 2
  were pixel-identical, as were 1 and 3 — the belt animated at **half rate** and
  `tick % 4` never showed a third or fourth state. A frame step only changes the
  picture if it moves chevrons to positions that are not already occupied.
  v2 uses 4 chevrons 8px apart with a `2k` px offset: measured **4/4 unique
  frames** (v1 was 2/4). This is the one change that alters what is on screen
  frame-to-frame, so it is called out rather than buried in a palette note.

### 2. Items (the crate)

Flat yellow square with a single diagonal strap → a 3D box: lit top face with a
seam, shadowed bottom and left edges, catch-light on the right, and crossed
horizontal + vertical straps with a lit crossing point.

### 3. Pieces (source, sink, splitter, merger)

Every family gains a 1px lit top edge and a 2px shadowed bottom edge, so they
read as boxes lit from above instead of flat fills. Shared `EdgeLight()` helper,
so all four shade identically. Also:

- Source hopper dots 2×2 → 3×3 (v1 read as specks of dirt).
- Sink well gets a faint inner bounce so the hole has depth.
- Splitter `Y` and merger `^` icons 2px → 3px thick, so the fork/merge reads at
  16px hotbar size.

### Not done in this pass

**Backgrounds and title screen.** Both are code-drawn, not atlas art: the board
is a `MeshRenderer` grid at `new Color(1,1,1,0.15f)` (`GridEditor.cs:591`) and
the title is a `MakeText()` label over a flat background (`MenuFlow.cs:249`).
Giving either of them real art means adding scene objects and UI layers — that
is gameplay/UI code, which this card forbids. Filed as a JV-feat card below.

## Verification actually run

| Gate | Result |
|---|---|
| `SpriteBaker.BakeAll` | exit 0, `[SpriteBaker] baked 256x256 atlas` |
| C++ suite (`ctest`) | **11/11 passed** |
| `PlayTest.Run` | exit 0 — `ticks=600 spawned=24 delivered=3 invariants=OK` |
| `MenuTest.Run` | exit 0 — PASS, 7 shots written |
| `HudTest.Run` | exit 0 — PASS, `delivered=1/10 timer=0:15` |
| `shotcheck.py joyveyor_hud.png` | 1600×900, **560 distinct colors** (a real render, not a clear frame) |
| Region-by-region pixel check | every region keeps its exact opaque-pixel count → no atlas key drift |
| Belt frame uniqueness | v1 2/4 → v2 **4/4** |

Crops in this document and the contact sheet were sampled with Unity's y-up
convention, so they show what renders — not what the raw PNG looks like.

## Honest limit

Headless captures prove layout, HUD and atlas correctness, **not** how the lit
edges feel in motion. Per the `joyveyor-scene-capture` caveat, in-game conveyor
sprites do not render in this headless context, so the belt and crate need an
eyeball in the real build (`open dist/JoyVeyor.app`) before anyone calls this
better. That judgement is the owner's.