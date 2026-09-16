using System.Collections.Generic;
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
    Sprite sprite;
    TextMesh hud;
    SpriteRenderer previewSr;
    readonly List<Piece> pieces = new List<Piece>();
    readonly List<GameObject> visuals = new List<GameObject>();

    Tool tool = Tool.Belt;
    byte dir = JoyveyorBridge.DirE;
    int beltLen = 3;
    string msg = "";
    float msgTimer = 0f;

    // ---- Lifecycle ----

    void Start()
    {
        try
        {
            runner = GetComponent<JoyveyorRunner>();
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
        if (Input.GetKeyDown(KeyCode.Alpha1)) tool = Tool.Belt;
        if (Input.GetKeyDown(KeyCode.Alpha2)) tool = Tool.Source;
        if (Input.GetKeyDown(KeyCode.Alpha3)) tool = Tool.Sink;
        if (Input.GetKeyDown(KeyCode.Alpha4)) tool = Tool.Splitter;
        if (Input.GetKeyDown(KeyCode.Alpha5)) tool = Tool.Merger;
        if (Input.GetKeyDown(KeyCode.Alpha6)) tool = Tool.Delete;

        if (Input.GetKeyDown(KeyCode.W)) dir = JoyveyorBridge.DirN;
        if (Input.GetKeyDown(KeyCode.D)) dir = JoyveyorBridge.DirE;
        if (Input.GetKeyDown(KeyCode.S)) dir = JoyveyorBridge.DirS;
        if (Input.GetKeyDown(KeyCode.A)) dir = JoyveyorBridge.DirW;

        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
            beltLen = Mathf.Clamp(beltLen + 1, 1, 20);
        if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus))
            beltLen = Mathf.Clamp(beltLen - 1, 1, 20);

        if (Input.GetKeyDown(KeyCode.Return)) LoadDemo();
        if (Input.GetKeyDown(KeyCode.Backspace)) ClearAll();
        if (Input.GetKeyDown(KeyCode.Space)) runner.paused = !runner.paused;

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
        hud.text = BuildHudText();
    }

    // ---- Actions ----

    void Place(int x, int y)
    {
        if (tool == Tool.Delete) { DeleteAt(x, y); return; }
        if (tool == Tool.Belt && !BeltFits(x, y, dir, beltLen))
        {
            Fail("belt off-grid");
            return;
        }
        uint id;
        Kind kind;
        switch (tool)
        {
            case Tool.Source: id = JoyveyorBridge.jv_place_source(runner.World, x, y); kind = Kind.Source; break;
            case Tool.Sink: id = JoyveyorBridge.jv_place_sink(runner.World, x, y, (ushort)sinkCapacity); kind = Kind.Sink; break;
            case Tool.Splitter: id = JoyveyorBridge.jv_place_splitter(runner.World, x, y, dir, Cw(dir)); kind = Kind.Splitter; break;
            case Tool.Merger: id = JoyveyorBridge.jv_place_merger(runner.World, x, y, dir, Ccw(dir), Opp(dir)); kind = Kind.Merger; break;
            default: id = JoyveyorBridge.jv_place_belt(runner.World, x, y, dir, beltLen); kind = Kind.Belt; break;
        }
        if (id == JoyveyorBridge.InvalidId) { Fail("placement rejected"); return; }
        pieces.Add(new Piece { kind = kind, x = x, y = y, dir = dir, len = kind == Kind.Belt ? beltLen : 1 });
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
        if (!ok) { Fail(p.kind == Kind.Belt ? "belt busy (has items)" : "node busy (has items)"); return; }
        pieces.RemoveAt(i);
        RebuildVisuals();
    }

    void LoadDemo()
    {
        runner.ResetWorld();
        pieces.Clear();
        pieces.AddRange(DemoPieces());
        runner.PlaceDemoLevel();
        RebuildVisuals();
    }

    void ClearAll()
    {
        runner.ResetWorld();
        pieces.Clear();
        RebuildVisuals();
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

    static readonly Color ColorBelt = new Color(0.35f, 0.35f, 0.42f);
    static readonly Color ColorSource = new Color(0.2f, 0.8f, 0.3f);
    static readonly Color ColorSink = new Color(0.3f, 0.5f, 0.9f);
    static readonly Color ColorSplitter = new Color(0.95f, 0.6f, 0.2f);
    static readonly Color ColorMerger = new Color(0.7f, 0.4f, 0.9f);

    void RebuildVisuals()
    {
        ClearVisuals();
        foreach (var p in pieces) visuals.Add(MakePieceVisual(p));
    }

    void ClearVisuals()
    {
        foreach (var go in visuals) Destroy(go);
        visuals.Clear();
    }

    GameObject MakePieceVisual(Piece p)
    {
        bool isBelt = p.kind == Kind.Belt;
        var go = new GameObject(isBelt ? "belt" : p.kind.ToString().ToLower());
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = isBelt ? ColorBelt
            : p.kind == Kind.Source ? ColorSource
            : p.kind == Kind.Sink ? ColorSink
            : p.kind == Kind.Splitter ? ColorSplitter
            : ColorMerger;
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
        return go;
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
        s += "1-6 tools  WASD dir  +/- len  Enter demo  Bksp clear  Space pause  RMB delete";
        return s;
    }

    void Fail(string m)
    {
        msg = m;
        msgTimer = 2f;
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

    static byte Cw(byte d) { return (byte)((d + 1) % 4); }        // N->E->S->W
    static byte Ccw(byte d) { return (byte)((d + 3) % 4); }       // N->W->S->E
    static byte Opp(byte d) { return (byte)((d + 2) % 4); }       // N<->S, E<->W

    static Sprite MakeSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }
}
