using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// JoyVeyor v1.0 — Sprint 6: build-time pixel-art atlas baker
// (jv-design-visual-audio §1-3, §7).
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

    static readonly Color BeltBase = Hex("#2A3140");
    static readonly Color BeltChev = Hex("#4A5568");
    static readonly Color SrcBase = Hex("#3DDC6A");
    static readonly Color SrcFlash = Hex("#B6FFCE");
    static readonly Color SinkBase = Hex("#3B82F6");
    static readonly Color SinkWell = Hex("#1E293B");
    static readonly Color SplitBase = Hex("#F59E0B");
    static readonly Color MergBase = Hex("#A855F7");
    static readonly Color CrateBase = Hex("#F5D90A");
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
        // 2px center groove (darker, y=15..16).
        Color groove = new Color(BeltBase.r * 0.55f, BeltBase.g * 0.55f, BeltBase.b * 0.55f, 1f);
        for (int x = 3; x <= 28; ++x)
        {
            P(px, ox, oy, x, 15, groove);
            P(px, ox, oy, x, 16, groove);
        }
        // 2 chevrons pointing +X, offset by 8k px (wrap 32). Each chevron is a
        // 2px-thick ">" 5px tall: tip at (px0+2, 15), arms back to (px0, 15±2).
        for (int c = 0; c < 2; ++c)
        {
            int px0 = (8 + 16 * c + 8 * k) % 32;
            for (int dy = -2; dy <= 2; ++dy)
            {
                int cx = px0 + (2 - Mathf.Abs(dy));
                for (int t = 0; t < 2; ++t)
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
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 5, baseC);
        Color dot = flash ? new Color(0.25f, 0.55f, 0.35f, 1f) : new Color(0.10f, 0.45f, 0.22f, 1f);
        // hopper: 3 dots in a row (conveyor-in feel), y=15, x=9/15/21
        for (int i = 0; i < 3; ++i)
        {
            int x = 9 + 6 * i;
            P(px, ox, oy, x, 14, dot);
            P(px, ox, oy, x + 1, 14, dot);
            P(px, ox, oy, x, 15, dot);
            P(px, ox, oy, x + 1, 15, dot);
            P(px, ox, oy, x, 16, dot);
            P(px, ox, oy, x + 1, 16, dot);
        }
    }

    // ---- Sink: rounded square + dark inner well ----

    static void DrawSink(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 5, SinkBase);
        FillRoundedRect(px, ox, oy, 8, 8, 23, 23, 3, SinkWell);
        // rim highlight on the well's top edge
        for (int x = 9; x <= 22; ++x) P(px, ox, oy, x, 7, Hex("#60A5FA"));
    }

    // ---- Splitter: square + "Y" fork icon (1 in, 2 out) ----

    static void DrawSplitter(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 4, SplitBase);
        // Y: stem from bottom-center up to a fork, two arms to top-left/top-right.
        for (int y = 9; y <= 16; ++y)
        {
            P(px, ox, oy, 15, y, IconDark);
            P(px, ox, oy, 16, y, IconDark);
        }
        for (int i = 0; i <= 4; ++i)
        {
            P(px, ox, oy, 15 - i, 9 - i, IconDark);
            P(px, ox, oy, 16 + i, 9 - i, IconDark);
        }
        // out arrowheads (top-left / top-right)
        P(px, ox, oy, 10, 3, IconDark); P(px, ox, oy, 11, 4, IconDark);
        P(px, ox, oy, 21, 3, IconDark); P(px, ox, oy, 20, 4, IconDark);
    }

    // ---- Merger: square + "^" merge icon (2 in, 1 out) ----

    static void DrawMerger(Color[] px, int ox, int oy)
    {
        FillRoundedRect(px, ox, oy, 2, 2, 29, 29, 4, MergBase);
        // ^: two arms from bottom-left/bottom-right meeting at top-center, stem down.
        for (int i = 0; i <= 4; ++i)
        {
            P(px, ox, oy, 11 + i, 10 + i, IconDark);
            P(px, ox, oy, 20 - i, 10 + i, IconDark);
        }
        for (int y = 14; y <= 21; ++y)
        {
            P(px, ox, oy, 15, y, IconDark);
            P(px, ox, oy, 16, y, IconDark);
        }
        // in arrowheads (bottom-left / bottom-right)
        P(px, ox, oy, 10, 23, IconDark); P(px, ox, oy, 11, 22, IconDark);
        P(px, ox, oy, 21, 23, IconDark); P(px, ox, oy, 20, 22, IconDark);
    }

    // ---- Crate: 16x16 #F5D90A square with a 1px darker border ----

    static void DrawCrate(Color[] px, int ox, int oy)
    {
        for (int y = 0; y < 16; ++y)
            for (int x = 0; x < 16; ++x)
            {
                bool edge = x == 0 || y == 0 || x == 15 || y == 15;
                P(px, ox, oy, x, y, edge ? new Color(CrateBase.r * 0.6f, CrateBase.g * 0.6f, CrateBase.b * 0.4f, 1f) : CrateBase);
            }
        // diagonal strap
        for (int i = 2; i <= 13; ++i) P(px, ox, oy, i, i, new Color(CrateBase.r * 0.6f, CrateBase.g * 0.6f, CrateBase.b * 0.4f, 1f));
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
