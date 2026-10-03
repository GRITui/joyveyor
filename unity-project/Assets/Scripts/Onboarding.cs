using System.IO;
using UnityEngine;
using UnityEngine.UI;

// JoyVeyor v1.0 — Sprint 5 onboarding (jv-design-uxui §5 "Onboarding").
//
// Level 1 is the tutorial. The spec's 5-hint table assumes the player places
// the source and sink, but level 1 PRE-PLACES both as locked pieces
// (level01.jvl: "S 0 0 15" + "K 5 0 10") — the player only draws the belt.
// So the effective level-1 sequence is the 3 hints that actually apply:
//   0 DrawBelt       (enter level 1)  "Hold left-click and drag to draw a belt
//                                       from the source to the sink."
//   1 PressRun       (belt placed)    "Ready? Press RUN to start the clock."
//   2 FirstDelivered (first item in)  "Delivered! Get 10 before time runs out."
//
// One hint on screen at a time: a small dark box with white text + a code-
// drawn arrow anchored toward the relevant element. Each hint fires once and
// is persisted (a bitmask in a small JSON in persistentDataPath) so it does
// not re-fire in later sessions. "Skip hints" (pause screen) marks all done.
//
// Attached by GameScreen.Start to a child of the HUD canvas:
//   onboarding = obGo.AddComponent<Onboarding>(); onboarding.Attach(this);
public class Onboarding : MonoBehaviour
{
    [System.Flags]
    enum HintMask { None = 0, DrawBelt = 1, PressRun = 2, FirstDelivered = 4, All = 7 }

    const int TutorialLevel = 1;

    // Hint copy (no font-glyph risk: plain ASCII only).
    static readonly string[] Texts =
    {
        "Hold left-click and drag to draw a belt from the source to the sink.",
        "Ready? Press RUN to start the clock.",
        "Delivered! Get 10 before time runs out.",
    };
    // Per-hint layout: where the callout box sits, plus which edge the arrow
    // hangs off. Hints 0 and 2 keep their original centre-anchored positions
    // so the default (up arrow, centred) path is unchanged.
    struct HintLayout
    {
        public Vector2 anchorMin, anchorMax, pivot, pos;
        public HintLayout(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos)
        {
            this.anchorMin = anchorMin; this.anchorMax = anchorMax;
            this.pivot = pivot; this.pos = pos;
        }
    }

    static readonly HintLayout[] Layouts =
    {
        // 0 DrawBelt -> bottom-centre, well clear of the hotbar.
        new HintLayout(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                       new Vector2(0.5f, 0.5f), new Vector2(0, -150)),
        // 1 PressRun  -> right edge flush with RUN's right edge (both sit 24px
        // in from the screen edge); bottom 92px above the screen floor = 12px
        // above RUN's top edge (RUN is 64px tall, centred in the 96px bar).
        new HintLayout(new Vector2(1, 0), new Vector2(1, 0),
                       new Vector2(1, 0), new Vector2(-24, 92)),
        // 2 FirstDelivered -> top-centre below the top bar.
        new HintLayout(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                       new Vector2(0.5f, 0.5f), new Vector2(0, 250)),
    };
    // Per-hint arrow direction. Up = apex on the top edge (the pre-existing
    // behaviour); Down = apex on the bottom edge.
    enum ArrowDir { Up, Down }
    static readonly ArrowDir[] ArrowDirs = { ArrowDir.Up, ArrowDir.Down, ArrowDir.Up };
    // x = pixels right of the callout's horizontal CENTRE, y = gap between the
    // callout edge and the arrow base. Hints 0 and 2 keep the original centred
    // arrow (x = 0); hint 1 puts it 48px in from the right edge.
    const float BoxW = 460, BoxH = 64;
    const float ArrowInsetFromRight = 48;
    static readonly Vector2[] ArrowOffsets =
    {
        new Vector2(0, 2),
        new Vector2(BoxW * 0.5f - ArrowInsetFromRight, 2),   // 182px right of centre
        new Vector2(0, 2),
    };

    GameScreen gs;
    int levelIndex = -1;
    int current = -1;          // index of the hint currently shown, -1 = none
    HintMask dismissed;

    GameObject box;
    Text boxText;
    Image arrow;
    static Sprite arrowSprite;

    static string Path =>
        System.IO.Path.Combine(Application.persistentDataPath, "joyveyor_onboarding.json");

    // ---- wiring (called by GameScreen.Start) ----

    public void Attach(GameScreen gs)
    {
        this.gs = gs;
        BuildUi();
        Load();
        Hide();
        if (gs != null)
        {
            gs.OnRunStarted += HandleRunStarted;
            gs.OnFirstDelivered += HandleFirstDelivered;
        }
    }

    void OnDestroy()
    {
        if (gs != null)
        {
            gs.OnRunStarted -= HandleRunStarted;
            gs.OnFirstDelivered -= HandleFirstDelivered;
        }
    }

    // ---- triggers ----

    // Enter / restart a level. GameScreen.StartLevel calls this.
    public void Reset(int level)
    {
        levelIndex = level;
        if (level != TutorialLevel) { current = -1; Hide(); return; }
        // Show the first not-yet-dismissed hint whose trigger is "enter level".
        if ((dismissed & HintMask.DrawBelt) == 0) Show(0);
        else if ((dismissed & HintMask.PressRun) == 0) Show(1);
        else { current = -1; Hide(); }  // only FirstDelivered left: wait for it
    }

    // GameScreen calls these on player placement.
    public void OnSourcePlaced() { /* level 1 source is locked; no-op */ }
    public void OnSinkPlaced()   { /* level 1 sink is locked; no-op */ }

    public void OnBeltPlaced()
    {
        if (levelIndex != TutorialLevel) return;
        if (current == 0)  // DrawBelt shown -> belt placed, advance to PressRun
        {
            Dismiss(HintMask.DrawBelt);
            if ((dismissed & HintMask.PressRun) == 0) Show(1);
            else { current = -1; Hide(); }
        }
    }

    void HandleRunStarted()
    {
        if (levelIndex != TutorialLevel) return;
        if (current == 1)  // PressRun shown -> run started, advance
        {
            Dismiss(HintMask.PressRun);
            current = -1; Hide();  // FirstDelivered waits for its event
        }
    }

    void HandleFirstDelivered()
    {
        if (levelIndex != TutorialLevel) return;
        if ((dismissed & HintMask.FirstDelivered) == 0)
        {
            Show(2);
            Dismiss(HintMask.FirstDelivered);  // fire once; stays visible
        }
    }

    // Pause screen "Skip hints" link.
    public void SkipAll()
    {
        dismissed = HintMask.All;
        Save();
        current = -1;
        Hide();
    }

    // ---- internals ----

    void Show(int i)
    {
        if (box == null) return;
        current = i;
        boxText.text = Texts[i];
        ApplyLayout(Layouts[i]);
        box.SetActive(!suppressed);
        if (arrow != null)
        {
            arrow.gameObject.SetActive(true);
            PositionArrow(ArrowDirs[i], ArrowOffsets[i]);
        }
    }

    void ApplyLayout(HintLayout l)
    {
        var rt = box.GetComponent<RectTransform>();
        rt.anchorMin = l.anchorMin; rt.anchorMax = l.anchorMax;
        rt.pivot = l.pivot;
        rt.anchoredPosition = l.pos;
    }

    // Hang the arrow off the callout edge named by dir and rotate the up-drawn
    // triangle so its apex points away from the box. offset.x is measured from
    // the callout's horizontal CENTRE so it means the same thing for every
    // hint layout (centred or right-pivoted).
    void PositionArrow(ArrowDir dir, Vector2 offset)
    {
        var art = arrow.rectTransform;
        bool down = dir == ArrowDir.Down;
        // anchor to the box centre so offset.x reads as "right of centre"
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
        // down: hang under the box (pivot top-centre); up: above it (pivot bottom-centre)
        art.pivot = down ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        art.anchoredPosition = new Vector2(offset.x, down ? -(BoxH * 0.5f + offset.y)
                                                          : (BoxH * 0.5f + offset.y));
        // apex up -> apex down is a 180 degree flip
        art.localRotation = down ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
    }

    // Public so GameScreen can hide the hint box when a level ends (the
    // "Delivered!" hint otherwise overlaps the complete/failed overlay).
    bool suppressed, hiddenBySuppress;
    // Pause overlay owns the screen: hide the hint box while paused, restore after.
    public void SetSuppressed(bool on)
    {
        if (suppressed == on) return;
        suppressed = on;
        if (box == null) return;
        if (on) { hiddenBySuppress = box.activeSelf; box.SetActive(false); }
        else if (hiddenBySuppress) { box.SetActive(true); hiddenBySuppress = false; }
    }

    public void Hide()
    {
        if (box != null) box.SetActive(false);
    }

    void Dismiss(HintMask m)
    {
        dismissed |= m;
        Save();
    }

    void BuildUi()
    {
        // The hint box anchors to screen corners/edges, so this component's own
        // rect must span the canvas. GameScreen parents a RectTransform-bearing
        // "Onboarding" object, but stretch defensively: a plain Transform parent
        // has no rect, and every anchor would then collapse to the canvas centre.
        var selfRt = transform as RectTransform;
        if (selfRt == null)
            Debug.LogWarning("[Onboarding] parent transform is not a RectTransform — "
                + "hint anchors will resolve against a zero-size rect");
        else
            Stretch(selfRt, 0, 0, 0, 0);

        box = new GameObject("HintBox");
        box.transform.SetParent(transform, false);
        var img = box.AddComponent<Image>();
        img.color = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        img.raycastTarget = false;
        var rt = box.GetComponent<RectTransform>();
        ApplyLayout(Layouts[0]);
        rt.sizeDelta = new Vector2(BoxW, BoxH);

        boxText = MakeText(box.transform, "HintText", Texts[0], 22, TextAnchor.MiddleCenter);
        Stretch(boxText.rectTransform, 14, 8, -14, -8);

        // Code-drawn upward triangle arrow (no font glyph dependency).
        if (arrowSprite == null) arrowSprite = MakeArrowTexture();
        var ago = new GameObject("Arrow");
        ago.transform.SetParent(box.transform, false);
        arrow = ago.AddComponent<Image>();
        arrow.sprite = arrowSprite;
        arrow.color = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        arrow.raycastTarget = false;
        var art = ago.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(0.5f, 1);
        art.anchorMax = new Vector2(0.5f, 1);
        art.pivot = new Vector2(0.5f, 0f);
        art.anchoredPosition = new Vector2(0, 2);   // just above the box
        art.sizeDelta = new Vector2(28, 16);

        box.SetActive(false);
    }

    static Font _font;
    static Font GetFont()
    {
        if (_font != null) return _font;
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch (System.Exception) { }
        if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Helvetica", 16);
        return _font;
    }

    static Text MakeText(Transform parent, string name, string txt, int size, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = txt;
        t.font = GetFont();
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        return t;
    }

    static void Stretch(RectTransform r, float l, float b, float rgt, float t)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b);
        r.offsetMax = new Vector2(rgt, t);
    }

    // Upward-pointing filled triangle, 32x18.
    static Sprite MakeArrowTexture()
    {
        int W = 32, H = 18;
        var tex = new Texture2D(W, H);
        var px = new Color[W * H];
        var c = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        for (int y = 0; y < H; ++y)
            for (int x = 0; x < W; ++x)
            {
                // Triangle: apex at top-center (W/2, H-1), base along y=0.
                float halfWidthAtY = (W * 0.5f) * (y / (float)(H - 1));
                bool inside = Mathf.Abs(x - (W - 1) / 2f) <= halfWidthAtY;
                px[y * W + x] = inside ? c : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
    }

    // ---- persistence (bitmask in a tiny JSON) ----

    void Load()
    {
        dismissed = HintMask.None;
        try
        {
            if (!File.Exists(Path)) return;
            string s = File.ReadAllText(Path);
            int i = s.IndexOf("dismissed");
            if (i < 0) return;
            int c = s.IndexOf(':', i);
            if (c < 0) return;
            string num = s.Substring(c + 1).Trim();
            int end = num.IndexOfAny(new[] { '}', ',', ' ' });
            if (end > 0) num = num.Substring(0, end);
            int v;
            if (int.TryParse(num, out v)) dismissed = (HintMask)v;
        }
        catch (System.Exception) { }
    }

    void Save()
    {
        try
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, "{\"dismissed\":" + (int)dismissed + "}");
        }
        catch (System.Exception) { }
    }
}
