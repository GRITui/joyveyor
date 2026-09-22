using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Sandbox editor: click-to-place grid editor over the C++ sim.
// Keys: 1-6 tools · WASD direction · +/- belt length · Enter demo ·
//       Backspace clear · Space pause · wheel zoom.
// Placement = left click, delete = right click (or tool 6).
[RequireComponent(typeof(JoyveyorRunner))]
public class GridEditor : MonoBehaviour
{
    const int GridW = 24, GridH = 16;

    enum Tool { Belt, Source, Sink, Splitter, Merger, Delete }
    enum Kind { Belt, Source, Sink, Splitter, Merger }

    struct Piece { public Kind kind; public int x, y; public byte dir; public int len; }

    public float cellSize = 1f;
    public int sinkCapacity = 50;

    JoyveyorRunner runner;
    Camera cam;
    Sprite sprite;  // white 1x1 — delete-tool ghost overlay only
    Sprite[] beltFrames;
    Sprite sourceIdle, sourceFlash, sinkBase, splitterSpr, mergerSpr, fillSprite;
    TextMesh hud;
    SpriteRenderer previewSr;
    JVAudio audio;
    readonly List<Piece> pieces = new List<Piece>();
    readonly List<GameObject> visuals = new List<GameObject>();
    readonly List<SpriteRenderer> beltSrs = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> sourceSrs = new List<SpriteRenderer>();
    readonly List<SinkVisual> sinkVisuals = new List<SinkVisual>();
    readonly HashSet<long> fullSinks = new HashSet<long>();  // sinks at capacity (sink_full SFX edge)

    struct SinkVisual { public int x, y; public SpriteRenderer fill; }

    int lastBeltFrame = -1;
    int lastSourceFrame = -1;
    ulong lastSpawned;
    float sourceFlashTimer;  // >0 while the spawn flash is up (120 ms)

    Tool tool = Tool.Belt;
    byte dir = JoyveyorBridge.DirE;
    int beltLen = 3;
    string msg = "";
    float msgTimer = 0f;

    // Sprint 7 perf (named leak): cache the HUD string + the values that feed
    // it, rebuilding only when one of them changes instead of every frame.
    string lastHud;
    Tool lastTool; int lastBeltLen; bool lastPaused;
    ulong hudSpawned, lastDelivered, lastConsumed, lastTick;
    int lastItemCount; bool lastDead; bool lastMsgOn; string lastMsg;

    // ---- Lifecycle ----

    void Start()
    {
        try
        {
            runner = GetComponent<JoyveyorRunner>();
            audio = GetComponent<JVAudio>();
            cam = Camera.main ?? FindObjectOfType<Camera>();
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
            }
            // Camera in front of the grid (content lives at z=0), facing -Z toward it.
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.transform.position = new Vector3(GridW * 0.5f, -GridH * 0.5f, 10f);
            cam.transform.rotation = Quaternion.identity;
            sprite = MakeSprite();
            LoadAtlasSprites();
            BuildGridLines();
            hud = MakeHud();
            previewSr = MakePreview();
            LoadDemo();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GridEditor] Start failed: " + e);
        }
    }

    void OnDestroy()
    {
        ClearVisuals();
    }

    // ---- Input ----

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) { tool = Tool.Belt; Click(); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { tool = Tool.Source; Click(); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { tool = Tool.Sink; Click(); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { tool = Tool.Splitter; Click(); }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { tool = Tool.Merger; Click(); }
        if (Input.GetKeyDown(KeyCode.Alpha6)) { tool = Tool.Delete; Click(); }

        if (Input.GetKeyDown(KeyCode.W)) { dir = JoyveyorBridge.DirN; Click(); }
        if (Input.GetKeyDown(KeyCode.D)) { dir = JoyveyorBridge.DirE; Click(); }
        if (Input.GetKeyDown(KeyCode.S)) { dir = JoyveyorBridge.DirS; Click(); }
        if (Input.GetKeyDown(KeyCode.A)) { dir = JoyveyorBridge.DirW; Click(); }

        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
        {
            beltLen = Mathf.Clamp(beltLen + 1, 1, 20);
            Click();
        }
        if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus))
        {
            beltLen = Mathf.Clamp(beltLen - 1, 1, 20);
            Click();
        }

        if (Input.GetKeyDown(KeyCode.Return)) { LoadDemo(); Click(); }
        if (Input.GetKeyDown(KeyCode.Backspace)) { ClearAll(); Click(); }
        if (Input.GetKeyDown(KeyCode.Space)) { runner.paused = !runner.paused; Click(); }
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && Input.GetKeyDown(KeyCode.S)) SaveLevel();
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && Input.GetKeyDown(KeyCode.O)) LoadLevel();

        if (cam.orthographic)
            cam.orthographicSize = Mathf.Clamp(
                cam.orthographicSize - Input.GetAxis("Mouse ScrollWheel") * 1.5f, 3f, 20f);

        if (!HoverCell(out int cx, out int cy))
        {
            previewSr.enabled = false;
            return;
        }
        UpdatePreview(cx, cy);

        if (Input.GetMouseButtonDown(0)) Place(cx, cy);
        if (Input.GetMouseButtonDown(1)) DeleteAt(cx, cy);
    }

    void LateUpdate()
    {
        if (msgTimer > 0f) msgTimer -= Time.deltaTime;
        if (runner == null || hud == null) return;
        if (HudInputsChanged()) lastHud = BuildHudText();
        hud.text = lastHud;
        UpdateAnimations();
        WatchSinkFull();
    }

    // Sprint 7 perf: true when any value feeding the HUD changed since the last
    // frame (also true on the very first frame). Updates the last* cache.
    bool HudInputsChanged()
    {
        bool dead = JoyveyorBridge.jv_is_deadlocked(runner.World) == 1;
        bool msgOn = msgTimer > 0f;
        bool changed =
            tool != lastTool || beltLen != lastBeltLen || runner.paused != lastPaused ||
            runner.spawned != hudSpawned || runner.delivered != lastDelivered ||
            runner.consumed != lastConsumed || runner.itemCount != lastItemCount ||
            runner.tickCount != lastTick || dead != lastDead || msgOn != lastMsgOn ||
            (msgOn && msg != lastMsg);
        lastTool = tool; lastBeltLen = beltLen; lastPaused = runner.paused;
        hudSpawned = runner.spawned; lastDelivered = runner.delivered;
        lastConsumed = runner.consumed; lastItemCount = runner.itemCount;
        lastTick = runner.tickCount; lastDead = dead; lastMsgOn = msgOn; lastMsg = msg;
        return changed;
    }

    // Sprint 6: belt scroll (frame = tick % 4), source spawn flash (120 ms),
    // sink fill bar (scale = storageCount/capacity, tinted by fill level).
    void UpdateAnimations()
    {
        if (runner == null) return;
        int tick = (int)runner.tickCount;

        int frame = ((tick % 4) + 4) % 4;
        if (frame != lastBeltFrame && beltFrames != null)
        {
            lastBeltFrame = frame;
            var spr = beltFrames[frame];
            for (int i = 0; i < beltSrs.Count; ++i)
                if (beltSrs[i] != null) beltSrs[i].sprite = spr;
        }

        // Source spawn flash: a new item appeared at the source -> flash the
        // source sprite for 120 ms (jv-design-visual-audio §3).
        if (runner.spawned != lastSpawned)
        {
            lastSpawned = runner.spawned;
            sourceFlashTimer = 0.120f;
        }
        if (sourceFlashTimer > 0f) sourceFlashTimer -= Time.deltaTime;
        int srcFrame = sourceFlashTimer > 0f ? 1 : 0;
        if (srcFrame != lastSourceFrame && sourceIdle != null)
        {
            lastSourceFrame = srcFrame;
            var spr = srcFrame == 1 ? sourceFlash : sourceIdle;
            for (int i = 0; i < sourceSrs.Count; ++i)
                if (sourceSrs[i] != null) sourceSrs[i].sprite = spr;
        }

        // Sink fill bar: scale Y by storageCount/capacity; tint blue (0-49%),
        // lighter blue (50-99%), gold #FBBF24 (100%, stays gold while full).
        for (int i = 0; i < sinkVisuals.Count; ++i)
        {
            var sv = sinkVisuals[i];
            if (sv.fill == null) continue;
            uint id = JoyveyorBridge.jv_node_at_cell(runner.World, sv.x, sv.y);
            if (id == JoyveyorBridge.InvalidId) continue;
            ushort count, cap;
            JoyveyorBridge.jv_sink_storage(runner.World, id, out count, out cap);
            float f = cap > 0 ? (float)count / cap : 0f;
            sv.fill.transform.localScale = new Vector3(0.15f, Mathf.Max(0.001f, 0.15f * f), 1f);
            sv.fill.color = f >= 1f ? new Color(0.984f, 0.749f, 0.141f, 1f)  // #FBBF24 gold
                : f >= 0.5f ? new Color(0.376f, 0.647f, 0.980f, 1f)         // #60A5FA lighter
                : new Color(0.231f, 0.510f, 0.965f, 1f);                    // #3B82F6 blue
        }
    }

    // Sink full (Sprint 8): rising 3-note once per sink at capacity
    // (sinks only fill, so the edge fires once per loaded layout).
    void WatchSinkFull()
    {
        if (runner == null || runner.World == IntPtr.Zero) return;
        foreach (var p in pieces)
        {
            if (p.kind != Kind.Sink || fullSinks.Contains(CellKey(p.x, p.y))) continue;
            uint id = JoyveyorBridge.jv_node_at_cell(runner.World, p.x, p.y);
            if (id == JoyveyorBridge.InvalidId) continue;
            ushort count, cap;
            JoyveyorBridge.jv_sink_storage(runner.World, id, out count, out cap);
            if (count >= cap)
            {
                fullSinks.Add(CellKey(p.x, p.y));
                if (audio != null) audio.PlaySinkFull();
            }
        }
    }

    // ---- Actions ----

    void Place(int x, int y)
    {
        if (tool == Tool.Delete) { DeleteAt(x, y); return; }
        if (tool == Tool.Belt && !BeltFits(x, y, dir, beltLen))
        {
            Fail("belt off-grid");
            if (audio != null) audio.PlayInvalid();
            return;
        }
        uint id;
        Kind kind;
        switch (tool)
        {
            case Tool.Source: id = JoyveyorBridge.jv_place_source(runner.World, x, y, 15); kind = Kind.Source; break;
            case Tool.Sink: id = JoyveyorBridge.jv_place_sink(runner.World, x, y, (ushort)sinkCapacity); kind = Kind.Sink; break;
            case Tool.Splitter: id = JoyveyorBridge.jv_place_splitter(runner.World, x, y, JoyveyorBridge.DirE, JoyveyorBridge.DirS); kind = Kind.Splitter; break;
            case Tool.Merger: id = JoyveyorBridge.jv_place_merger(runner.World, x, y, JoyveyorBridge.DirS, JoyveyorBridge.DirW, JoyveyorBridge.DirE); kind = Kind.Merger; break;
            default: id = JoyveyorBridge.jv_place_belt(runner.World, x, y, dir, beltLen); kind = Kind.Belt; break;
        }
        if (id == JoyveyorBridge.InvalidId)
        {
            string why = JoyveyorBridge.LastPlacementError(runner.World);
            Fail(why.Length > 0 ? "placement rejected: " + why : "placement rejected");
            if (audio != null) audio.PlayInvalid();
            return;
        }
        pieces.Add(new Piece { kind = kind, x = x, y = y, dir = dir, len = kind == Kind.Belt ? beltLen : 1 });
        if (audio != null) audio.PlayPlace();
        RebuildVisuals();
    }

    void DeleteAt(int x, int y)
    {
        // Node cell first, else the belt whose footprint contains the cell.
        int i = pieces.FindIndex(p => p.kind != Kind.Belt && p.x == x && p.y == y);
        if (i < 0)
            i = pieces.FindIndex(p => p.kind == Kind.Belt && BeltContains(p, x, y));
        if (i < 0) return;
        Piece p = pieces[i];
        bool ok = p.kind == Kind.Belt
            ? JoyveyorBridge.jv_remove_belt(runner.World, CellBeltId(p.x, p.y)) == 1
            : JoyveyorBridge.jv_remove_node(runner.World, CellNodeId(p.x, p.y)) == 1;
        if (!ok) { Fail(p.kind == Kind.Belt ? "belt busy (has items)" : "node busy (has items)"); if (audio != null) audio.PlayInvalid(); return; }
        pieces.RemoveAt(i);
        if (audio != null) audio.PlayDelete();
        RebuildVisuals();
    }

    void LoadDemo()
    {
        runner.ResetWorld();
        pieces.Clear();
        fullSinks.Clear();
        ResetAnimState();
        pieces.AddRange(DemoPieces());
        runner.PlaceDemoLevel();
        RebuildVisuals();
    }

    void ClearAll()
    {
        runner.ResetWorld();
        pieces.Clear();
        fullSinks.Clear();
        ResetAnimState();
        RebuildVisuals();
    }

    void ResetAnimState()
    {
        lastBeltFrame = -1;
        lastSourceFrame = -1;
        lastSpawned = 0;
        sourceFlashTimer = 0f;
    }

    // ---- Save / Load (README §3.4) ----
    // Layout lives in the C++ world; the editor's `pieces` list is the visual
    // mirror. Save = serialize the world. Load = parse the text into pieces
    // AND apply it to a fresh world (single source of truth for connections).

    const string SavePath = "joyveyor_level.jvl";

    void SaveLevel()
    {
        try
        {
            string text = JoyveyorBridge.SaveLayout(runner.World);
            File.WriteAllText(SavePath, text);
            Fail("saved " + pieces.Count + " pieces -> " + SavePath);
        }
        catch (System.Exception e) { Fail("save failed: " + e.Message); }
    }

    void LoadLevel()
    {
        if (!File.Exists(SavePath)) { Fail("no saved level (" + SavePath + ")"); return; }
        try
        {
            string text = File.ReadAllText(SavePath);
            // C++ world is the source of truth: load the layout directly
            // (preserves exact sink capacity + splitter/merger dirs).
            runner.ResetWorld();
            if (!JoyveyorBridge.LoadLayout(runner.World, text))
            {
                Fail("bad save file");
                return;
            }
            fullSinks.Clear();
            ResetAnimState();
            // Rebuild the visual mirror from the same text.
            if (!ParseLayout(text, out List<Piece> loaded))
            {
                Fail("bad save file (visuals)");
                return;
            }
            pieces.Clear();
            pieces.AddRange(loaded);
            RebuildVisuals();
            Fail("loaded " + pieces.Count + " pieces");
        }
        catch (System.Exception e) { Fail("load failed: " + e.Message); }
    }

    // Parse a #JVL1 layout string into pieces (nodes then belts, file order).
    // Returns false on malformed input.
    static bool ParseLayout(string text, out List<Piece> outPieces)
    {
        outPieces = new List<Piece>();
        string[] lines = text.Replace("\r", "").Split('\n');
        bool first = true;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (first) { first = false; if (line != "#JVL1") return false; continue; }
            string[] t = line.Split(' ');
            if (t.Length < 2) return false;
            if (!int.TryParse(t[1], out int x) || !int.TryParse(t[2], out int y)) return false;
            switch (t[0])
            {
                case "S":
                    outPieces.Add(new Piece { kind = Kind.Source, x = x, y = y, dir = JoyveyorBridge.DirE });
                    break;
                case "K":
                    outPieces.Add(new Piece { kind = Kind.Sink, x = x, y = y, dir = JoyveyorBridge.DirE });
                    break;
                case "T":
                    if (t.Length < 5 || !byte.TryParse(t[3], out byte ta) || !byte.TryParse(t[4], out byte tb)) return false;
                    outPieces.Add(new Piece { kind = Kind.Splitter, x = x, y = y, dir = ta });
                    break;
                case "M":
                    if (t.Length < 4 || !byte.TryParse(t[3], out byte ma)) return false;
                    outPieces.Add(new Piece { kind = Kind.Merger, x = x, y = y, dir = ma });
                    break;
                case "B":
                    if (t.Length < 5 || !byte.TryParse(t[3], out byte bd) || !int.TryParse(t[4], out int bl)) return false;
                    outPieces.Add(new Piece { kind = Kind.Belt, x = x, y = y, dir = bd, len = bl });
                    break;
                default:
                    return false;
            }
        }
        if (first) return false;  // no header
        return true;
    }

    static List<Piece> DemoPieces()
    {
        return new List<Piece>
        {
            new Piece { kind = Kind.Source,   x = 0,  y = 0, dir = JoyveyorBridge.DirE },
            new Piece { kind = Kind.Belt,     x = 1,  y = 0, dir = JoyveyorBridge.DirE, len = 5 },
            new Piece { kind = Kind.Splitter, x = 6,  y = 0, dir = JoyveyorBridge.DirE },
            new Piece { kind = Kind.Belt,     x = 7,  y = 0, dir = JoyveyorBridge.DirE, len = 4 },
            new Piece { kind = Kind.Sink,     x = 11, y = 0, dir = JoyveyorBridge.DirE },
            new Piece { kind = Kind.Belt,     x = 6,  y = 1, dir = JoyveyorBridge.DirS, len = 4 },
            new Piece { kind = Kind.Merger,   x = 6,  y = 5, dir = JoyveyorBridge.DirS },
            new Piece { kind = Kind.Belt,     x = 7,  y = 5, dir = JoyveyorBridge.DirE, len = 4 },
            new Piece { kind = Kind.Sink,     x = 11, y = 5, dir = JoyveyorBridge.DirE },
        };
    }

    // ---- Visuals ----

    // Atlas sprites (Sprint 6): baked by SpriteBaker into Assets/Art/atlas.png.
    // Belt = 4-frame E strip (scroll via .sprite = frame tick%4); N/S/W = the
    // strip rotated. Sink fill = a separate fill-bar SpriteRenderer scaled by
    // storageCount/capacity, tinted blue / lighter / gold. Source = idle +
    // 120 ms spawn flash.

    void LoadAtlasSprites()
    {
        JVArt.EnsureLoaded();
        beltFrames = JVArt.BeltFrames;
        sourceIdle = JVArt.SourceIdle;
        sourceFlash = JVArt.SourceFlash;
        sinkBase = JVArt.SinkBase;
        splitterSpr = JVArt.Splitter;
        mergerSpr = JVArt.Merger;
        // Fill bar: a plain 1x1 white sprite tinted at runtime (no extra
        // atlas region needed; it is clipped to the well by its scale).
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        fillSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f));
    }

    void RebuildVisuals()
    {
        ClearVisuals();
        foreach (var p in pieces) MakePieceVisual(p);
    }

    void ClearVisuals()
    {
        foreach (var go in visuals) Destroy(go);
        visuals.Clear();
        beltSrs.Clear();
        sourceSrs.Clear();
        sinkVisuals.Clear();
    }

    GameObject MakePieceVisual(Piece p)
    {
        bool isBelt = p.kind == Kind.Belt;
        var go = new GameObject(isBelt ? "belt" : p.kind.ToString().ToLower());
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        if (isBelt)
        {
            sr.sprite = beltFrames[0];
            // N/S/W = rotated copies of the E strip (composes with the scale
            // logic: local +X is the travel axis for every direction).
            go.transform.localRotation = Quaternion.Euler(0f, 0f, JVArt.BeltRotation(p.dir));
            beltSrs.Add(sr);
        }
        else
        {
            switch (p.kind)
            {
                case Kind.Source:
                    sr.sprite = sourceIdle;
                    sourceSrs.Add(sr);
                    break;
                case Kind.Sink:
                    sr.sprite = sinkBase;
                    sinkVisuals.Add(MakeSinkFill(p));
                    break;
                case Kind.Splitter: sr.sprite = splitterSpr; break;
                case Kind.Merger: sr.sprite = mergerSpr; break;
            }
        }
        if (isBelt)
        {
            bool horizontal = p.dir == JoyveyorBridge.DirE || p.dir == JoyveyorBridge.DirW;
            go.transform.localScale = new Vector3(horizontal ? p.len : 1f, horizontal ? 1f : p.len, 1f);
            float cx = p.x + (horizontal ? (p.dir == JoyveyorBridge.DirE ? (p.len - 1) / 2f : -(p.len - 1) / 2f) : 0f);
            float cy = p.y + (horizontal ? 0f : (p.dir == JoyveyorBridge.DirS ? (p.len - 1) / 2f : -(p.len - 1) / 2f));
            go.transform.localPosition = new Vector3(cx, -cy, 0f);
        }
        else
        {
            go.transform.localPosition = new Vector3(p.x, -p.y, 0f);
        }
        visuals.Add(go);
        return go;
    }

    // Sink fill bar: a thin SpriteRenderer inside the sink's dark well,
    // anchored at the well's bottom, scaled Y by storageCount/capacity.
    // Well in sprite pixels: x 8..23, y 8..23 (32px cell, 100 px/unit) ->
    // 0.15 x 0.15 units, bottom at local y = -0.075.
    SinkVisual MakeSinkFill(Piece p)
    {
        var go = new GameObject("sink_fill");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = fillSprite;
        sr.sortingOrder = 1;
        sr.transform.localPosition = new Vector3(p.x, -p.y - 0.075f, 0.02f);
        sr.transform.localScale = new Vector3(0.15f, 0.001f, 1f);  // empty
        visuals.Add(go);
        var sv = new SinkVisual { x = p.x, y = p.y, fill = sr };
        sinkVisuals.Add(sv);
        return sv;
    }

    void UpdatePreview(int x, int y)
    {
        if (tool == Tool.Delete)
        {
            previewSr.enabled = true;
            previewSr.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            previewSr.transform.localScale = Vector3.one;
            previewSr.transform.localPosition = new Vector3(x, -y, 0.1f);
            return;
        }
        bool isBelt = tool == Tool.Belt;
        int len = isBelt ? beltLen : 1;
        previewSr.enabled = true;
        previewSr.color = new Color(1f, 1f, 1f, 0.35f);
        if (isBelt)
        {
            bool horizontal = dir == JoyveyorBridge.DirE || dir == JoyveyorBridge.DirW;
            previewSr.transform.localScale = new Vector3(horizontal ? len : 1f, horizontal ? 1f : len, 1f);
            float cx = x + (horizontal ? (dir == JoyveyorBridge.DirE ? (len - 1) / 2f : -(len - 1) / 2f) : 0f);
            float cy = y + (horizontal ? 0f : (dir == JoyveyorBridge.DirS ? (len - 1) / 2f : -(len - 1) / 2f));
            previewSr.transform.localPosition = new Vector3(cx, -cy, 0.1f);
        }
        else
        {
            previewSr.transform.localScale = Vector3.one;
            previewSr.transform.localPosition = new Vector3(x, -y, 0.1f);
        }
    }

    // ---- Grid / HUD ----

    void BuildGridLines()
    {
        var go = new GameObject("GridLines");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh();
        var verts = new List<Vector3>();
        for (int x = 0; x <= GridW; ++x)
        {
            verts.Add(new Vector3(x * cellSize, 0, 0));
            verts.Add(new Vector3(x * cellSize, -GridH * cellSize, 0));
        }
        for (int y = 0; y <= GridH; ++y)
        {
            verts.Add(new Vector3(0, -y * cellSize, 0));
            verts.Add(new Vector3(GridW * cellSize, -y * cellSize, 0));
        }
        mesh.vertices = verts.ToArray();
        var idx = new int[verts.Count];
        for (int i = 0; i < idx.Length; ++i) idx[i] = i;
        mesh.SetIndices(idx, MeshTopology.Lines, 0);
        mf.mesh = mesh;
        mr.material = new Material(Shader.Find("Sprites/Default"));
        mr.material.color = new Color(1f, 1f, 1f, 0.15f);
    }

    TextMesh MakeHud()
    {
        var go = new GameObject("HUD");
        go.transform.SetParent(transform, false);  // world space, same plane as the grid
        go.transform.localPosition = new Vector3(0.3f, -0.3f, 0.1f);  // top-left of grid
        var tm = go.AddComponent<TextMesh>();
        tm.characterSize = 0.22f;
        tm.anchor = TextAnchor.UpperLeft;
        tm.color = Color.white;
        tm.GetComponent<MeshRenderer>().material = new Material(Shader.Find("Sprites/Default"));
        return tm;
    }

    SpriteRenderer MakePreview()
    {
        var go = new GameObject("Preview");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 10;
        sr.enabled = false;
        return sr;
    }

    string BuildHudText()
    {
        string s = "tool: " + tool + "   dir: " + DirName(dir) + "   belt len: " + beltLen
            + (runner.paused ? "   [PAUSED]" : "") + "\n"
            + "spawned " + runner.spawned + "   delivered " + runner.delivered
            + "   consumed " + runner.consumed + "\n"
            + "in-flight " + runner.itemCount + "   tick " + runner.tickCount + "\n";
        if (JoyveyorBridge.jv_is_deadlocked(runner.World) == 1) s += "DEADLOCK!\n";
        if (msgTimer > 0f) s += msg + "\n";
        s += "1-6 tools  WASD dir  +/- len  Enter demo  Bksp clear  Space pause  RMB delete\n"
            + "Ctrl+S save  Ctrl+O load";
        return s;
    }

    void Fail(string m)
    {
        msg = m;
        msgTimer = 2f;
    }

    void Click()
    {
        if (audio != null) audio.PlayUiClick();
    }

    // ---- Helpers ----

    bool HoverCell(out int cx, out int cy)
    {
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        cx = Mathf.FloorToInt(p.x / cellSize);
        cy = Mathf.FloorToInt(-p.y / cellSize);
        return cx >= 0 && cx < GridW && cy >= 0 && cy < GridH;
    }

    bool BeltFits(int x, int y, byte d, int len)
    {
        int ex = x + (d == JoyveyorBridge.DirE ? len - 1 : d == JoyveyorBridge.DirW ? -(len - 1) : 0);
        int ey = y + (d == JoyveyorBridge.DirS ? len - 1 : d == JoyveyorBridge.DirN ? -(len - 1) : 0);
        return ex >= 0 && ex < GridW && ey >= 0 && ey < GridH;
    }

    static bool BeltContains(Piece p, int x, int y)
    {
        if (p.dir == JoyveyorBridge.DirE) return y == p.y && x >= p.x && x < p.x + p.len;
        if (p.dir == JoyveyorBridge.DirW) return y == p.y && x <= p.x && x > p.x - p.len;
        if (p.dir == JoyveyorBridge.DirS) return x == p.x && y >= p.y && y < p.y + p.len;
        return x == p.x && y <= p.y && y > p.y - p.len;  // N
    }

    uint CellBeltId(int x, int y) => JoyveyorBridge.jv_belt_at_cell(runner.World, x, y);
    uint CellNodeId(int x, int y) => JoyveyorBridge.jv_node_at_cell(runner.World, x, y);

    static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;

    static string DirName(byte d)
    {
        switch (d)
        {
            case JoyveyorBridge.DirN: return "N";
            case JoyveyorBridge.DirE: return "E";
            case JoyveyorBridge.DirS: return "S";
            default: return "W";
        }
    }

    static Sprite MakeSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }
}
