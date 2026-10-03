using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// JoyVeyor v1.0 — Sprint 6: build-time pixel-art atlas baker
// (jv-design-visual-audio §1-3, §7). Art pass 1 = v2 palette (t_906f0ecf).
//
// Bakes ONE 256x256 atlas (Texture2D.SetPixel loops, point-filtered, no
// mipmaps) to Assets/Art/atlas.png (committed). Contents:
//   (0,0)    belt E-facing 4-frame strip, 128x32 — rounded-rect #2A3140
//            + 2px center groove + 2 chevrons #4A5568 pointing +X; frame k
//            offsets the chevron pattern by 8k px (wrap 32)
//   (128,0)  source idle, 32x32 — rounded square #3DDC6A + hopper icon (3 dots)
//   (160,0)  source spawn-flash, 32x32 — same, tinted #B6FFCE
//   (192,0)  sink base, 32x32 — rounded square #3B82F6 + dark inner well
//   (224,0)  splitter, 32x32 — square #F59E0B + "Y" fork icon
//   (0,32)   merger, 32x32 — square #A855F7 + "^" merge icon
//   (32,32)  item crate, 16x16 — #F5D90A square (v1.0: single item type)
//   (0,64)   hotbar icons, 6 x 16x16 (belt/source/sink/splitter/merger/crate)
//
// Sub-sprite rects are the single source of truth shared with JVArt at
// runtime — do not move regions without updating JVArt.Rects.
public static class SpriteBaker
{
    public const string OutPath = "Assets/Art/atlas.png";

    // ---- Region rects (x, y, w, h) in atlas pixels — mirror of JVArt.Rects ----
    public const int BeltX = 0, BeltY = 0, BeltW = 128, BeltH = 32;
    public const int SrcIdleX = 128, SrcIdleY = 0;
    public const int SrcFlashX = 160, SrcFlashY = 0;
    public const int SinkX = 192, SinkY = 0;
    public const int SplitX = 224, SplitY = 0;
    public const int MergX = 0, MergY = 32;
    public const int CrateX = 32, CrateY = 32;
    public const int HotX = 0, HotY = 64, HotW = 96, HotH = 16;

    [MenuItem("Joyveyor/Bake Atlas (Pixel Art)")]
    public static void BakeAll()
    {
        var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        var px = new Color[256 * 256];  // all transparent

        // Belt E strip: 4 frames of 32px.
        for (int k = 0; k < 4; ++k)
            DrawBeltFrame(px, BeltX + 32 * k, BeltY, k);

        DrawSource(px, SrcIdleX, SrcIdleY, Hex("#3DDC6A"), false);
        DrawSource(px, SrcFlashX, SrcFlashY, Hex("#B6FFCE"), true);
        DrawSink(px, SinkX, SinkY);
        DrawSplitter(px, SplitX, SplitY);
        DrawMerger(px, MergX, MergY);
        DrawCrate(px, CrateX, CrateY);

        // Hotbar: 6 icons, 16x16 each, in one row.
        DrawHotIcon(px, HotX + 0 * 16, HotY, px, BeltX + 0, BeltY, 32, 32);      // belt (frame 0)
        DrawHotIcon(px, HotX + 1 * 16, HotY, px, SrcIdleX, SrcIdleY, 32, 32);    // source
        DrawHotIcon(px, HotX + 2 * 16, HotY, px, SinkX, SinkY, 32, 32);          // sink
        DrawHotIcon(px, HotX + 3 * 16, HotY, px, SplitX, SplitY, 32, 32);        // splitter
        DrawHotIcon(px, HotX + 4 * 16, HotY, px, MergX, MergY, 32, 32);          // merger
        DrawHotIcon(px, HotX + 5 * 16, HotY, px, CrateX, CrateY, 16, 16);        // crate

        for (int i = 0; i < px.Length; ++i) tex.SetPixel(i % 256, i / 256, px[i]);
        tex.Apply(false, false);  // no mipmaps

        Directory.CreateDirectory("Assets/Art");
        File.WriteAllBytes(OutPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        WritePointFilterMeta(OutPath);
        AssetDatabase.Refresh();
        Debug.Log("[SpriteBaker] baked 256x256 atlas -> " + OutPath);
    }

    // ---- Palette (jv-design-visual-audio §2) ----

    static Color Hex(string h)
    {
        return new Color(
            (byte)(Convert.ToInt32(h.Substring(1, 2), 16) * 257) / 255f,
            (byte)(Convert.ToInt32(h.Substring(3, 2), 16) * 257) / 255f,
            (byte)(Convert.ToInt32(h.Substring(5, 2), 16) * 257) / 255f, 1f);
    }

    // v2 palette (art pass 1, card t_906f0ecf): every family gains a top
    // highlight and a bottom shadow so the pieces read as lit 3D boxes
    // instead of flat fills, and the belt is lifted off the near-black
    // background (v1 body was #2A3140 = 1.08:1 against the #0B0D12 board).
    static readonly Color BeltBase = Hex("#3D4A5C");
    static readonly Color BeltHi = Hex("#5A6B7F");
    static readonly Color BeltSh = Hex("#1E2530");
    static readonly Color BeltGroove = Hex("#2A3140");
    static readonly Color BeltChev = Hex("#7A8FA6");
    static readonly Color SrcBase = Hex("#3DDC6A");
    static readonly Color SrcHi = Hex("#6AEB94");
    static readonly Color SrcSh = Hex("#1F9945");
    static readonly Color SrcDot = Hex("#0E5A28");
    static readonly Color SrcFlash = Hex("#B6FFCE");
    static readonly Color SrcFlashHi = Hex("#D8FFE6");
    static readonly Color SrcFlashSh = Hex("#7AD4A0");
    static readonly Color SrcDotF = Hex("#1A6B3A");
    static readonly Color SinkBase = Hex("#3B82F6");
    static readonly Color SinkHi = Hex("#60A5FA");
    static readonly Color SinkSh = Hex("#1D4ED8");
    static readonly Color SinkWell = Hex("#1E293B");
    static readonly Color SplitBase = Hex("#F59E0B");
    static readonly Color SplitHi = Hex("#FBBF24");
    static readonly Color SplitSh = Hex("#B45309");
    static readonly Color MergBase = Hex("#A855F7");
    static readonly Color MergHi = Hex("#C084FC");
    static readonly Color MergSh = Hex("#7E22CE");
    static readonly Color CrateBase = Hex("#F5D90A");
    static readonly Color CrateHi = Hex("#F5E06A");
    static readonly Color CrateSh = Hex("#B8A80A");
    static readonly Color CrateStrap = Hex("#8B7500");
    static readonly Color IconDark = Hex("#1E293B");

    // ---- Pixel helpers (local coords, y down) ----

    static void P(Color[] px, int ox, int oy, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= 256 || y >= 256) return;
        px[(oy + y) * 256 + (ox + x)] = c;
    }

    static bool InRoundedRect(int x, int y, int x0, int y0, int x1, int y1, int r)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        int cx = x < x0 + r ? x0 + r - 1 - x : (x > x1 - r ? x - (x1 - r) : -1);
        int cy = y < y0 + r ? y0 + r - 1 - y : (y > y1 - r ? y - (y1 - r) : -1);
        if (cx >= 0 && cy >= 0) return (cx * cx + cy * cy) <= r * r;
        return true;
    }

    // v2 helper: multiply RGB by f (clamped) to derive highlight/shadow tints
    // from a base color, so each family shades consistently.
    static Color Shade(Color c, float f)
    {
        return new Color(
            Mathf.Clamp01(c.r * f),
            Mathf.Clamp01(c.g * f),
            Mathf.Clamp01(c.b * f), c.a);
    }

    // v2 helper: 1px lit top edge + 2px shadowed bottom edge inside a
    // rounded rect — the shared "this box is lit from above" treatment.
    static void EdgeLight(Color[] px, int ox, int oy, int x0, int y0, int x1, int y1,
                          int r, Color hi, Color sh)
    {
        for (int x = x0; x <= x1; ++x)
        {
            if (InRoundedRect(x, y0, x0, y0, x1, y1, r)) P(px, ox, oy, x, y0, hi);
            if (InRoundedRect(x, y0 + 1, x0, y0, x1, y1, r)) P(px, ox, oy, x, y0 + 1, Shade(hi, 0.82f));
            if (InRoundedRect(x, y1, x0, y0, x1, y1, r)) P(px, ox, oy, x, y1, sh);
            if (InRoundedRect(x, y1 - 1, x0, y0, x1, y1, r)) P(px, ox, oy, x, y1 - 1, Shade(sh, 1.35f));
        }
    }

    static void FillRoundedRect(Color[] px, int ox, int oy, int x0, int y0, int x1, int y1, int r, Color c)
    {
        for (int y = y0; y <= y1; ++y)
            for (int x = x0; x <= x1; ++x)
                if (InRoundedRect(x, y, x0, y0, x1, y1, r)) P(px, ox, oy, x, y, c);
    }

    // ---- Belt: 32x32 frame, rounded-rect fill + groove + 2 chevrons (+X) ----
    // Chevron pattern is a 32px-wide periodic function of (x - 8k): two
    // 2px-wide chevrons at pattern x=8 and x=24, each 4px tall, pointing +X.

    static void DrawBeltFrame(Color[] px, int ox, int oy, int k)
    {
        FillRoundedRect(px, ox, oy, 1, 6, 30, 25, 3, BeltBase);
        // v2: lit top edge + shadowed bottom edge — gives the belt volume so it
        // separates from the board instead of reading as a dark slab.
        EdgeLight(px, ox, oy, 1, 6, 30, 25, 3, BeltHi, BeltSh);
        // 2px center groove (v1 BeltBase is now the groove color).
        for (int x = 3; x <= 28; ++x)
        {
            P(px, ox, oy, x, 15, BeltGroove);
            P(px, ox, oy, x, 16, BeltGroove);
        }
        // 4 chevrons pointing +X, 8px apart, offset by 2k px (wrap 32). Each
        // chevron is a 3px-thick ">" 5px tall: tip at (px0+3, 15), arms
        // back to (px0, 15±2).
        //
        // v2 note — why 4 chevrons at 8px, offset 2px:
        // v1 used 2 chevrons 16px apart offset by 8k, so frames 0 and 2 were
        // pixel-identical (as were 1 and 3) and the belt scrolled at half rate.
        // A frame shift only changes the picture if it moves the chevrons to
        // positions that are NOT already occupied — i.e. the offset step must
        // not be a multiple of the chevron spacing. With 8px spacing, offsets
        // 0/2/4/6 give four genuinely different frames, so tick%4 animates the
        // whole cycle. (Offset 8k would shift by a full period and repeat.)
        for (int c = 0; c < 4; ++c)
        {
            int px0 = (2 + 8 * c + 2 * k) % 32;
            for (int dy = -2; dy <= 2; ++dy)
            {
                int cx = px0 + (3 - Mathf.Abs(dy));
                for (int t = 0; t < 3; ++t)
                {
                    int x = cx + t;
                    if (InRoundedRect(x, 15 + dy, 1, 6, 30, 25, 3)) P(px, ox, oy, x, 15 + dy, BeltChev);
                }
            }
        }
    }

    // ---- Source: rounded square + hopper icon (3 dots) ----

    static void DrawSource(Color[] px, int ox, int oy, Color baseC, bool flash)
    {
        Color hi = flash ? SrcFlashHi : SrcHi;
        Color sh = flash ? SrcFlashSh : SrcSh;
        Color dot = flash ? SrcDotF : SrcDot;
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 5, baseC);
        EdgeLight(px, ox, oy, 2, 2, 29, 29, 5, hi, sh);
        // hopper: 3 dots in a row (conveyor-in feel), y=14..16, x=8/15/22.
        // v2: 3x3 dots instead of v1's 2x2, which read as dirt specks.
        for (int i = 0; i < 3; ++i)
        {
            int x = 8 + 7 * i;
            for (int dy = 0; dy < 3; ++dy)
                for (int dx = 0; dx < 3; ++dx)
                    P(px, ox, oy, x + dx, 13 + dy, dot);
        }
    }

    // ---- Sink: rounded square + dark inner well ----

    static void DrawSink(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 5, SinkBase);
        EdgeLight(px, ox, oy, 2, 2, 29, 29, 5, SinkHi, SinkSh);
        FillRoundedRect(px, ox, oy, 8, 8, 23, 23, 3, SinkWell);
        // rim highlight on the well's top edge
        for (int x = 9; x <= 22; ++x) P(px, ox, oy, x, 7, SinkHi);
        // v2: faint inner bounce on the well's back wall, so the hole has depth
        // instead of reading as a flat dark square.
        for (int x = 10; x <= 21; ++x) P(px, ox, oy, x, 9, Shade(SinkWell, 1.6f));
    }

    // ---- Splitter: square + "Y" fork icon (1 in, 2 out) ----

    static void DrawSplitter(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 4, SplitBase);
        EdgeLight(px, ox, oy, 2, 2, 29, 29, 4, SplitHi, SplitSh);
        // Y: stem from bottom-center up to a fork, two arms to top-left/top-right.
        // v2: 3px thick (was 2px) so the fork reads at hotbar size.
        for (int y = 9; y <= 16; ++y)
            for (int dx = -1; dx <= 1; ++dx)
                P(px, ox, oy, 15 + dx, y, IconDark);
        for (int i = 0; i <= 4; ++i)
        {
            for (int dx = -1; dx <= 0; ++dx) P(px, ox, oy, 15 - i + dx, 9 - i, IconDark);
            for (int dx = 0; dx <= 1; ++dx) P(px, ox, oy, 16 + i + dx, 9 - i, IconDark);
        }
        // out arrowheads (top-left / top-right)
        for (int dx = 0; dx < 2; ++dx)
        {
            P(px, ox, oy, 9 + dx, 2, IconDark); P(px, ox, oy, 10 + dx, 3, IconDark);
            P(px, ox, oy, 20 + dx, 2, IconDark); P(px, ox, oy, 19 + dx, 3, IconDark);
        }
    }

    // ---- Merger: square + "^" merge icon (2 in, 1 out) ----

    static void DrawMerger(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 4, MergBase);
        EdgeLight(px, ox, oy, 2, 2, 29, 29, 4, MergHi, MergSh);
        // ^: two arms from bottom-left/bottom-right meeting at top-center, stem down.
        // v2: 3px thick (was 2px) so the merge reads at hotbar size.
        for (int i = 0; i <= 4; ++i)
        {
            for (int dx = 0; dx <= 1; ++dx)
            {
                P(px, ox, oy, 11 + i + dx, 10 + i, IconDark);
                P(px, ox, oy, 20 - i - dx, 10 + i, IconDark);
            }
        }
        for (int y = 14; y <= 21; ++y)
            for (int dx = -1; dx <= 1; ++dx)
                P(px, ox, oy, 15 + dx, y, IconDark);
        // in arrowheads (bottom-left / bottom-right)
        for (int dx = 0; dx < 2; ++dx)
        {
            P(px, ox, oy, 9 + dx, 23, IconDark); P(px, ox, oy, 10 + dx, 22, IconDark);
            P(px, ox, oy, 20 + dx, 23, IconDark); P(px, ox, oy, 19 + dx, 22, IconDark);
        }
    }

    // ---- Crate: 16x16 lit box (v2: 3D top face + cross straps, was a flat
    // square with a single diagonal) ----

    static void DrawCrate(Color[] px, int ox, int oy)
    {
        // Front face
        for (int y = 0; y < 16; ++y)
            for (int x = 0; x < 16; ++x)
                P(px, ox, oy, x, y, CrateBase);
        // Top face (rows 0-3) — reads as a lit lid seen at a shallow angle.
        for (int y = 0; y < 4; ++y)
            for (int x = 0; x < 16; ++x)
                P(px, ox, oy, x, y, CrateHi);
        // Lid seam
        for (int x = 0; x < 16; ++x) P(px, ox, oy, x, 3, Shade(CrateHi, 0.78f));
        // Bottom edge shadow + left edge shadow, right edge catch-light.
        for (int y = 0; y < 16; ++y)
        {
            P(px, ox, oy, 0, y, CrateSh);
            P(px, ox, oy, 15, y, Shade(CrateBase, 1.15f));
        }
        for (int x = 0; x < 16; ++x) P(px, ox, oy, x, 15, CrateSh);
        // Cross straps (horizontal band rows 8-9, vertical band cols 7-8).
        for (int x = 1; x <= 14; ++x)
        {
            P(px, ox, oy, x, 8, CrateStrap);
            P(px, ox, oy, x, 9, CrateStrap);
        }
        for (int y = 4; y <= 14; ++y)
        {
            P(px, ox, oy, 7, y, CrateStrap);
            P(px, ox, oy, 8, y, CrateStrap);
        }
        // Lit top-left corner of the strap crossing.
        Color strapLit = Shade(CrateStrap, 1.45f);
        for (int x = 7; x <= 8; ++x)
        {
            P(px, ox, oy, x, 8, strapLit);
            P(px, ox, oy, x, 9, strapLit);
        }
    }

    // Copy a 16x16 hotbar icon from a source region (downscale 2x for 32px regions).
    static void DrawHotIcon(Color[] px, int dx, int dy, Color[] src, int sx, int sy, int sw, int sh)
    {
        if (sw == 16 && sh == 16)
        {
            for (int y = 0; y < 16; ++y)
                for (int x = 0; x < 16; ++x)
                    P(px, dx, dy, x, y, src[(sy + y) * 256 + (sx + x)]);
        }
        else  // 32x32 -> 16x16: 2x2 block average (keeps the pixel-art look chunky)
        {
            for (int y = 0; y < 16; ++y)
                for (int x = 0; x < 16; ++x)
                {
                    Color a = src[(sy + 2 * y) * 256 + (sx + 2 * x)];
                    Color b = src[(sy + 2 * y + 1) * 256 + (sx + 2 * x)];
                    Color c = src[(sy + 2 * y) * 256 + (sx + 2 * x + 1)];
                    Color d = src[(sy + 2 * y + 1) * 256 + (sx + 2 * x + 1)];
                    Color avg = new Color(
                        (a.r + b.r + c.r + d.r) * 0.25f,
                        (a.g + b.g + c.g + d.g) * 0.25f,
                        (a.b + b.b + c.b + d.b) * 0.25f,
                        (a.a + b.a + c.a + d.a) * 0.25f);
                    P(px, dx, dy, x, y, avg);
                }
        }
    }

    // Point-filtered, no mipmaps, sprite mode — written as a sibling .meta so
    // the committed atlas imports correctly without opening the editor.
    static void WritePointFilterMeta(string pngPath)
    {
        string meta = pngPath + ".meta";
        if (File.Exists(meta))
        {
            string t = File.ReadAllText(meta);
            if (t.Contains("filterMode: 0") && t.Contains("enableMipMap: 0") && t.Contains("spriteMode: 1"))
                return;  // already correct
            t = t.Replace("filterMode: 1", "filterMode: 0");
            t = t.Replace("enableMipMap: 1", "enableMipMap: 0");
            t = t.Replace("spriteMode: 0", "spriteMode: 1");
            File.WriteAllText(meta, t);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("fileFormatVersion: 2");
        sb.AppendLine("guid: " + Guid.NewGuid().ToString("N"));
        sb.AppendLine("TextureImporter:");
        sb.AppendLine("  internalIDToNameTable: []");
        sb.AppendLine("  externalObjects: {}");
        sb.AppendLine("  serializedVersion: 13");
        sb.AppendLine("  mipmaps:");
        sb.AppendLine("    mipMapMode: 0");
        sb.AppendLine("    enableMipMap: 0");
        sb.AppendLine("    sRGBTexture: 1");
        sb.AppendLine("    linearTexture: 0");
        sb.AppendLine("    fadeOut: 0");
        sb.AppendLine("    borderMipMap: 0");
        sb.AppendLine("    mipMapsPreserveCoverage: 0");
        sb.AppendLine("    alphaTestReferenceValue: 0.5");
        sb.AppendLine("    mipMapFadeDistanceStart: 1");
        sb.AppendLine("    mipMapFadeDistanceEnd: 3");
        sb.AppendLine("  bumpmap:");
        sb.AppendLine("    convertToNormalMap: 0");
        sb.AppendLine("    externalNormalMap: 0");
        sb.AppendLine("    heightScale: 0.25");
        sb.AppendLine("    normalMapFilter: 0");
        sb.AppendLine("    flipGreenChannel: 0");
        sb.AppendLine("  isReadable: 1");
        sb.AppendLine("  streamingMipmaps: 0");
        sb.AppendLine("  streamingMipmapsPriority: 0");
        sb.AppendLine("  vTOnly: 0");
        sb.AppendLine("  ignoreMipmapLimit: 0");
        sb.AppendLine("  grayScaleToAlpha: 0");
        sb.AppendLine("  generateCubemap: 6");
        sb.AppendLine("  cubemapConvolution: 0");
        sb.AppendLine("  seamlessCubemap: 0");
        sb.AppendLine("  textureFormat: 1");
        sb.AppendLine("  maxTextureSize: 2048");
        sb.AppendLine("  textureSettings:");
        sb.AppendLine("    serializedVersion: 2");
        sb.AppendLine("    filterMode: 0");
        sb.AppendLine("    aniso: 1");
        sb.AppendLine("    mipBias: 0");
        sb.AppendLine("    wrapU: 1");
        sb.AppendLine("    wrapV: 1");
        sb.AppendLine("    wrapW: 0");
        sb.AppendLine("  nPOTScale: 1");
        sb.AppendLine("  lightmap: 0");
        sb.AppendLine("  compressionQuality: 50");
        sb.AppendLine("  spriteMode: 1");
        sb.AppendLine("  spriteExtrude: 1");
        sb.AppendLine("  spriteMeshType: 1");
        sb.AppendLine("  alignment: 0");
        sb.AppendLine("  spritePivot: {x: 0.5, y: 0.5}");
        sb.AppendLine("  spritePixelsToUnits: 100");
        sb.AppendLine("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}");
        sb.AppendLine("  spriteGenerateFallbackPhysicsShape: 1");
        sb.AppendLine("  alphaUsage: 1");
        sb.AppendLine("  alphaIsTransparency: 1");
        sb.AppendLine("  spriteTessellationMethod: 0");
        sb.AppendLine("  spriteTessellationDetail: -1");
        sb.AppendLine("  spriteGeometrySubdivision: -1");
        sb.AppendLine("  textureType: 8");
        sb.AppendLine("  textureShape: 1");
        sb.AppendLine("  singleChannelComponent: 0");
        sb.AppendLine("  flipbookRows: 1");
        sb.AppendLine("  flipbookColumns: 1");
        sb.AppendLine("  maxTextureSizeSet: 0");
        sb.AppendLine("  compressionQualitySet: 0");
        sb.AppendLine("  textureFormatSet: 0");
        sb.AppendLine("  ignorePngGamma: 0");
        sb.AppendLine("  applyGammaDecoding: 0");
        sb.AppendLine("  swizzle: 50462976");
        sb.AppendLine("  cookieLightType: 0");
        sb.AppendLine("  platformSettings:");
        sb.AppendLine("  - serializedVersion: 4");
        sb.AppendLine("    buildTarget: DefaultTexturePlatform");
        sb.AppendLine("    maxTextureSize: 2048");
        sb.AppendLine("    resizeAlgorithm: 0");
        sb.AppendLine("    textureFormat: -1");
        sb.AppendLine("    textureCompression: 1");
        sb.AppendLine("    compressionQuality: 50");
        sb.AppendLine("    crunchedCompression: 0");
        sb.AppendLine("    allowsAlphaSplitting: 0");
        sb.AppendLine("    overridden: 0");
        sb.AppendLine("    ignorePlatformSupport: 0");
        sb.AppendLine("  spriteSheet:");
        sb.AppendLine("    serializedVersion: 2");
        sb.AppendLine("    sprites: []");
        sb.AppendLine("    outline: []");
        sb.AppendLine("    customData: ");
        sb.AppendLine("    physicsShape: []");
        sb.AppendLine("    bones: []");
        sb.AppendLine("    spriteID: ");
        sb.AppendLine("    internalID: 0");
        sb.AppendLine("    vertices: []");
        sb.AppendLine("    indices: []");
        sb.AppendLine("    edges: []");
        sb.AppendLine("    weights: []");
        sb.AppendLine("    secondaryTextures: []");
        sb.AppendLine("    spriteCustomMetadata:");
        sb.AppendLine("      entries: []");
        sb.AppendLine("    nameFileIdTable: {}");
        sb.AppendLine("  mipmapLimitGroupName: ");
        sb.AppendLine("  pSDRemoveMatte: 0");
        sb.AppendLine("  userData: ");
        sb.AppendLine("  assetBundleName: ");
        sb.AppendLine("  assetBundleVariant: ");
        File.WriteAllText(meta, sb.ToString());
    }
}
