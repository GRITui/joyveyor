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
    // Box anchor (canvas-center-relative, ConstantPixelSize) + arrow rotation.
    // 0 DrawBelt -> bottom-center, arrow up (toward the grid).
    // 1 PressRun -> bottom-right near RUN, arrow up.
    // 2 FirstDelivered -> top-center below the top bar, arrow up.
    static readonly Vector2[] Anchors =
    {
        new Vector2(0, -150),
        new Vector2(250, -150),
        new Vector2(0, 250),
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
        var rt = box.GetComponent<RectTransform>();
        rt.anchoredPosition = Anchors[i];
        box.SetActive(true);
        if (arrow != null)
        {
            arrow.gameObject.SetActive(true);
            // All three targets sit above their box; keep the arrow up.
            arrow.transform.localRotation = Quaternion.identity;
        }
    }

    // Public so GameScreen can hide the hint box when a level ends (the
    // "Delivered!" hint otherwise overlaps the complete/failed overlay).
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
        box = new GameObject("HintBox");
        box.transform.SetParent(transform, false);
        var img = box.AddComponent<Image>();
        img.color = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        img.raycastTarget = false;
        var rt = box.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Anchors[0];
        rt.sizeDelta = new Vector2(460, 64);

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
