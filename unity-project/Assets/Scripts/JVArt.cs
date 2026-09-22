using UnityEngine;

// JoyVeyor v1.0 — Sprint 6: runtime access to the baked pixel-art atlas
// (Assets/Art/atlas.png, baked by SpriteBaker — jv-design-visual-audio §1-3, §7).
//
// One 256x256 atlas, point-filtered, no mipmaps, 100 px/unit. Sub-sprite
// rects mirror SpriteBaker's region constants — keep them in sync.
//
// The texture arrives via AtlasHolder (scene reference, so the player
// build includes it). Belt scroll: set .sprite to frame `tick % 4`
// (7.5 fps at the 30 Hz sim tick). N/S/W belts = the E strip rotated
// 90/180/270° — composes with the existing scale logic (scale is applied
// in local space, so a rotated sprite's local X is the belt's travel
// axis for every direction).
public static class JVArt
{
    public static Sprite[] BeltFrames;   // 4, E-facing
    public static Sprite SourceIdle;
    public static Sprite SourceFlash;
    public static Sprite SinkBase;
    public static Sprite Splitter;
    public static Sprite Merger;
    public static Sprite Crate;
    public static Sprite[] Hotbar;       // 6 icons (belt, source, sink, splitter, merger, crate)
    public static Texture2D Atlas;       // non-null when the atlas loaded

    static bool loaded;
    static Texture2D pendingTex;

    // ---- Region rects (atlas pixels) — mirror of SpriteBaker constants ----
    static readonly Rect BeltRect = new Rect(0, 0, 128, 32);
    static readonly Rect SrcIdleRect = new Rect(128, 0, 32, 32);
    static readonly Rect SrcFlashRect = new Rect(160, 0, 32, 32);
    static readonly Rect SinkRect = new Rect(192, 0, 32, 32);
    static readonly Rect SplitRect = new Rect(224, 0, 32, 32);
    static readonly Rect MergRect = new Rect(0, 32, 32, 32);
    static readonly Rect CrateRect = new Rect(32, 32, 16, 16);
    static readonly Rect HotRect = new Rect(0, 64, 96, 16);

    // Called by AtlasHolder.Awake when the scene provides the atlas.
    public static void SetAtlasTexture(Texture2D t)
    {
        if (t == null) return;
        pendingTex = t;
        if (loaded)
        {
            if (Atlas == t) return;      // already built from this atlas
            BuildSprites(t);             // was on fallbacks — rebuild from the real atlas
            return;
        }
        EnsureLoaded();
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        if (pendingTex == null)
        {
            // Robust to component ordering (OnEnable can run before
            // AtlasHolder.Awake): grab the scene-provided atlas directly.
            var holder = Object.FindAnyObjectByType<AtlasHolder>();
            if (holder != null && holder.atlas != null) pendingTex = holder.atlas;
        }
        if (pendingTex != null) BuildSprites(pendingTex);
        else BuildFallbacks();
    }

    // Belt frame for a sim tick: frame k offsets the chevron pattern by 8k px.
    public static Sprite BeltFrame(int tick)
    {
        EnsureLoaded();
        if (BeltFrames == null) return null;
        return BeltFrames[((tick % 4) + 4) % 4];
    }

    // Rotation (local Z) that turns the E-facing strip into the belt's travel
    // direction. Local +X = travel axis for every direction after rotation,
    // so the existing horizontal/vertical scale logic composes unchanged.
    public static float BeltRotation(byte dir)
    {
        switch (dir)
        {
            case JoyveyorBridge.DirN: return 90f;
            case JoyveyorBridge.DirS: return -90f;
            case JoyveyorBridge.DirW: return 180f;
            default: return 0f;  // E
        }
    }

    static void BuildSprites(Texture2D t)
    {
        Atlas = t;

        BeltFrames = new Sprite[4];
        for (int k = 0; k < 4; ++k)
            BeltFrames[k] = Sub(new Rect(BeltRect.x + 32 * k, BeltRect.y, 32, 32), "belt_e" + k);
        SourceIdle = Sub(SrcIdleRect, "source_idle");
        SourceFlash = Sub(SrcFlashRect, "source_flash");
        SinkBase = Sub(SinkRect, "sink");
        Splitter = Sub(SplitRect, "splitter");
        Merger = Sub(MergRect, "merger");
        Crate = Sub(CrateRect, "crate");
        Hotbar = new Sprite[6];
        for (int i = 0; i < 6; ++i)
            Hotbar[i] = Sub(new Rect(HotRect.x + 16 * i, HotRect.y, 16, 16), "hot" + i);
    }

    static Sprite Sub(Rect r, string name)
    {
        // All sub-sprites share the one atlas texture, so Unity's default
        // sprite material batches them (§7) — no per-sprite material needed.
        var s = Sprite.Create(Atlas, r, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(0, 0, 0, 0));
        s.name = name;
        return s;
    }

    // White 1x1 fallbacks (pre-S6 look) so the game still runs if the
    // atlas is missing from a checkout.
    static void BuildFallbacks()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        BeltFrames = new Sprite[4];
        for (int k = 0; k < 4; ++k)
        {
            BeltFrames[k] = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            BeltFrames[k].name = "belt_fallback" + k;
        }
        SourceIdle = SourceFlash = SinkBase = Splitter = Merger = Crate =
            Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        Hotbar = new Sprite[6];
        for (int i = 0; i < 6; ++i)
            Hotbar[i] = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }
}
