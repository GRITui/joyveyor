using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// JoyVeyor v1.0 — Sprint 4: the in-game level screen (jv-design-uxui §HUD,
// §Input Model, §Feedback States). Sits on top of the S3 GameSession game
// layer and the C++ sim (via JoyveyorRunner).
//
//   * World-space grid + locked/player pieces (kept world-space, as the spec
//     requires — only the HUD chrome is UGUI).
//   * UGUI HUD on a Screen Space – Camera canvas (top bar: timer / delivered
//     + progress / restart + pause; bottom bar: 6-slot hotbar + RUN, build
//     phase only; JAMMED banner; pause overlay). Screen Space – Camera (not
//     Overlay) so the headless hero capture (cam.Render -> RenderTexture)
//     actually sees it.
//   * Mouse-first input: left-drag draws a belt (drag dir + length, clamp
//     1-20); left-click places; right-click deletes; R rotate 90 CW; Z
//     single-level undo; Esc pause; Enter start run; 1-6 + WASD fallback.
//
// The sandbox (GridEditor) stays the scene default so the existing
// PlayTest / CaptureHero / GameTest probes keep running the freeform demo.
// GameScreen takes over only when a level is started (StartLevel), which is
// what the S4 headless probes + the future level-select (S5) do.
public class GameScreen : MonoBehaviour
{
    public const int GridW = 24, GridH = 16;
    const float Cell = 1f;

    public enum Tool { Belt, Source, Sink, Splitter, Merger, Delete }
    enum Kind { Belt, Source, Sink, Splitter, Merger }

    public GameSession session;
    public Tool tool = Tool.Belt;
    public byte dir = JoyveyorBridge.DirE;
    public int beltLen = 3;   // default belt length (spec: click = length 3)
    public bool runReady;     // RUN enabled (>=1 source AND >=1 sink)

    // S5: flow + onboarding hooks (MenuFlow drives the screen; the onboarding
    // probes drive placement through these public actions so the hint engine
    // sees the same events a player's clicks would).
    public event System.Action OnRunStarted;
    public event System.Action OnFirstDelivered;
    // S5: flow events — MenuFlow listens to route the screen.
    public event System.Action OnNextLevel;
    public event System.Action OnQuitToMenu;
    public void SelectToolPublic(Tool t) { SelectTool(t); }
    public void PlaceSourceAt(int x, int y) { tool = Tool.Source; PlaceAt(x, y); }
    public void PlaceSinkAt(int x, int y) { tool = Tool.Sink; PlaceAt(x, y); }

    // HUD Text refs the headless probe reads (acceptance: HUD text matches sim).
    public Text timerText;
    public Text deliveredText;
    public Image progressBarFill;
    public Text runLabel;
    public Button runButton;
    public GameObject hotbarRoot;
    public GameObject topBar;
    public GameObject jammedBanner;
    public GameObject pauseOverlay;
    public Canvas hudCanvas;

    // S5: onboarding (level 1 only) + flow accessors for MenuFlow.
    public Onboarding onboarding;
    Onboarding Onboarding => onboarding;
    public GameObject completeOverlay;
    public GameObject failedOverlay;
    public Text deliveredRow;
    public Text timeLeftRow;
    public Text efficiencyRow;
    public Text totalRow;
    public Text failedDelivered;
    public Button nextButton;
    public Button replayButton;
    public Button retryButton;
    public Button failedMenuButton;
    public Button[] pauseScaleButtons;
    public Image[] pauseScaleHi;
    public Text skipHintsLink;
    public int textScaleIndex = 1;   // 0=S 1=M 2=L (persisted by MenuFlow)
    public int CurrentLevelIndex => session != null ? session.LevelIndex : 1;
    public int DeliveredCount { get { int d, t; SumSinks(out d, out t); return d; } }
    public int TotalCount { get { int d, t; SumSinks(out d, out t); return t; } }
    public void SetTextScale(int i)
    {
        textScaleIndex = Mathf.Clamp(i, 0, 2);
        if (hudCanvas != null)
        {
            var scaler = hudCanvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.scaleFactor = new[] { 0.85f, 1f, 1.2f }[textScaleIndex];
        }
        if (pauseScaleHi != null)
            for (int k = 0; k < pauseScaleHi.Length; ++k)
                if (pauseScaleHi[k] != null) pauseScaleHi[k].enabled = k == textScaleIndex;
    }

    JoyveyorRunner runner;
    Camera cam;
    Sprite sprite;
    JVAudio audio;
    GridEditor gridEditor;
    SpriteRenderer previewSr;
    readonly List<GameObject> visuals = new List<GameObject>();
    readonly List<GameObject> sinkBars = new List<GameObject>();
    readonly List<Vector2Int> sinkCells = new List<Vector2Int>();

    // Single-level undo: last place OR delete (spec: one stack level).
    struct UndoRec { public bool isPlace; public char tag; public int a, b, c, d, e; }
    UndoRec? lastAction;

    // Drag-to-draw belt state.
    bool dragging;
    int dragX, dragY;

    // S5: first-delivery onboarding edge + star-pop coroutine + shown stars.
    bool firstDeliveredFired;
    Coroutine starPop;
    public int ShownStars;

    // ---- Lifecycle ----

    void Awake()
    {
        try
        {
            runner = GetComponent<JoyveyorRunner>();
            audio = GetComponent<JVAudio>();
            // Take over from the sandbox. Drop the GridEditor component so it
            // can't steal input or rebuild its demo. (Destroy is deferred to end
            // of frame, so GridEditor.Start may still run this frame and create
            // sandbox visuals — Start below sweeps them.)
            var ge = GetComponent<GridEditor>();
            if (ge != null) Destroy(ge);
            gridEditor = null;

            cam = Camera.main ?? FindObjectOfType<Camera>();
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
            }
            FrameCamera();
            sprite = MakeSprite();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameScreen] Awake failed: " + e);
        }
    }

    void Start()
    {
        try
        {
            // Sweep sandbox artifacts. GridEditor.Start (same object, ran
            // before this Start) may have created demo pieces, GridLines,
            // the TextMesh HUD, and a Preview — none of which belong to the
            // in-game screen. Destroy every child EXCEPT the JVAudio/* audio
            // objects (JVAudio still needs them). GameScreen has created
            // NONE of its own visuals yet, so this is safe.
            var self = transform;
            var toDestroy = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in self)
            {
                var n = child.name;
                if (n.StartsWith("JVAudio/")) continue;   // keep audio
                toDestroy.Add(child.gameObject);
            }
            foreach (var go in toDestroy) Destroy(go);

            // Recreate GameScreen's own world visuals.
            BuildGridLines();
            previewSr = MakePreview();

            session = runner.GetComponent<GameSession>();
            if (session == null) session = runner.gameObject.AddComponent<GameSession>();
            session.OnPhaseChanged += OnPhaseChanged;
            BuildHud();
            // S5: onboarding (level 1 only) — attached to the HUD canvas.
            var obGo = new GameObject("Onboarding");
            obGo.transform.SetParent(hudCanvas.transform, false);
            onboarding = obGo.AddComponent<Onboarding>();
            onboarding.Attach(this);
            // Default to level 1 (the tutorial). Probes call StartLevel(n).
            StartLevel(1);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameScreen] Start failed: " + e);
        }
    }

    void OnDestroy()
    {
        if (session != null) session.OnPhaseChanged -= OnPhaseChanged;
        ClearVisuals();
    }

    // ---- Level lifecycle ----

    public void StartLevel(int index)
    {
        if (session.LoadLevel(index))
        {
            lastAction = null;
            if (Onboarding != null) Onboarding.Reset(index);
            RefreshVisuals();
            RefreshHud();
        }
        else
        {
            Debug.LogError("[GameScreen] StartLevel(" + index + ") failed (locked or missing)");
        }
    }

    void OnPhaseChanged()
    {
        RefreshVisuals();
        RefreshHud();
        // S5: entering Build (restart / level start) hides any end overlay.
        if (session.phase == GameSession.Phase.Build)
        {
            HideOverlays();
            firstDeliveredFired = false;
        }
        else if (session.phase == GameSession.Phase.Complete)
        {
            ShowCompleteOverlay();
        }
        else if (session.phase == GameSession.Phase.Failed)
        {
            ShowFailedOverlay();
        }
    }

    // ---- Camera ----

    void FrameCamera()
    {
        cam.orthographic = true;
        cam.orthographicSize = 9f;
        // This build's camera looks along +forward (identity rotation), so it
        // must sit at NEGATIVE z to look toward the grid at z=0 — matches the
        // scene's own Main Camera (z=-10) and GridEditor.
        cam.transform.position = new Vector3(GridW * 0.5f, -GridH * 0.5f, -10f);
        cam.transform.rotation = Quaternion.identity;
    }

    // ---- Input (mouse-first; keyboard fallback) ----

    void Update()
    {
        if (session == null || session.Level == null) return;
        bool ui = PointerOverUI();

        if (!ui)
        {
            // Select piece: 1-6 (keyboard fallback for hotbar click).
            for (int i = 1; i <= 6; ++i)
                if (Input.GetKeyDown(KeyCode.Alpha1 + (i - 1))) SelectTool((Tool)(i - 1));
            // Rotate belt 90 CW.
            if (Input.GetKeyDown(KeyCode.R)) { dir = RotateCW(dir); if (audio != null) audio.PlayUiClick(); }
            // Single-level undo.
            if (Input.GetKeyDown(KeyCode.Z)) Undo();
            // Pause (Esc).
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            // Start run (Enter — was "load demo").
            if (Input.GetKeyDown(KeyCode.Return) && session.phase == GameSession.Phase.Build) StartRun();
            // Restart (Backspace — now = restart, was "clear").
            if (Input.GetKeyDown(KeyCode.Backspace)) Restart();
            // WASD absolute-dir fallback (re-aims the ghost).
            if (Input.GetKeyDown(KeyCode.W)) dir = JoyveyorBridge.DirN;
            if (Input.GetKeyDown(KeyCode.D)) dir = JoyveyorBridge.DirE;
            if (Input.GetKeyDown(KeyCode.S)) dir = JoyveyorBridge.DirS;
            if (Input.GetKeyDown(KeyCode.A)) dir = JoyveyorBridge.DirW;
            // Wheel zoom (kept, undocumented).
            if (cam.orthographic)
                cam.orthographicSize = Mathf.Clamp(
                    cam.orthographicSize - Input.GetAxis("Mouse ScrollWheel") * 1.5f, 3f, 20f);
        }

        if (session.phase != GameSession.Phase.Build)
        {
            previewSr.enabled = false;
            dragging = false;
            return;
        }

        if (!HoverCell(out int cx, out int cy))
        {
            previewSr.enabled = false;
            return;
        }

        if (!ui && Input.GetMouseButtonDown(0))
        {
            if (tool == Tool.Belt) { dragging = true; dragX = cx; dragY = cy; }
            else PlaceAt(cx, cy);
        }
        if (!ui && Input.GetMouseButtonDown(1)) DeleteAt(cx, cy);
        if (dragging && Input.GetMouseButtonUp(0))
        {
            dragging = false;
            ComputeDrag(dragX, dragY, cx, cy, out byte dd, out int ll);
            PlaceBelt(dragX, dragY, dd, ll);
        }

        UpdateGhost(cx, cy);
    }

    void LateUpdate()
    {
        if (session == null || session.Level == null) return;
        UpdateHud();
        UpdateSinkFillBars();
    }

    void SelectTool(Tool t)
    {
        tool = t;
        if (audio != null) audio.PlayUiClick();
        RefreshHud();
    }

    static byte RotateCW(byte d)
    {
        switch (d)
        {
            case JoyveyorBridge.DirN: return JoyveyorBridge.DirE;
            case JoyveyorBridge.DirE: return JoyveyorBridge.DirS;
            case JoyveyorBridge.DirS: return JoyveyorBridge.DirW;
            default: return JoyveyorBridge.DirN;
        }
    }

    // Drag vector -> belt direction + length (clamped 1-20).
    static void ComputeDrag(int x0, int y0, int x1, int y1, out byte dir, out int len)
    {
        int dx = x1 - x0, dy = y1 - y0;
        int ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
        if (ax == 0 && ay == 0) { dir = JoyveyorBridge.DirE; len = 3; return; }  // click = default 3
        if (ax >= ay)
        {
            dir = dx >= 0 ? JoyveyorBridge.DirE : JoyveyorBridge.DirW;
            len = Mathf.Clamp(ax, 1, 20);
        }
        else
        {
            dir = dy >= 0 ? JoyveyorBridge.DirN : JoyveyorBridge.DirS;
            len = Mathf.Clamp(ay, 1, 20);
        }
    }

    void PlaceAt(int x, int y)
    {
        if (tool == Tool.Delete) { DeleteAt(x, y); return; }
        if (tool == Tool.Belt) { PlaceBelt(x, y, dir, beltLen); return; }
        char tag; int c = 0, d = 0, e = 0;
        switch (tool)
        {
            case Tool.Source: tag = 'S'; c = 15; break;
            case Tool.Sink: tag = 'K'; c = 10; break;
            case Tool.Splitter: tag = 'T'; c = (int)JoyveyorBridge.DirE; d = (int)JoyveyorBridge.DirS; break;
            case Tool.Merger: tag = 'M'; c = (int)JoyveyorBridge.DirS; d = (int)JoyveyorBridge.DirW; e = (int)JoyveyorBridge.DirE; break;
            default: return;
        }
        if (session.PlacePiece(tag, x, y, c, d, e))
        {
            lastAction = new UndoRec { isPlace = true, tag = tag, a = x, b = y, c = c, d = d, e = e };
            if (tag == 'S') Onboarding?.OnSourcePlaced();
            else if (tag == 'K') Onboarding?.OnSinkPlaced();
            RefreshVisuals();
            RefreshHud();
        }
        else if (audio != null) audio.PlayInvalid();
    }

    void PlaceBelt(int x, int y, byte d, int len)
    {
        len = Mathf.Clamp(len, 1, 20);
        if (session.PlacePiece('B', x, y, (int)d, len))
        {
            lastAction = new UndoRec { isPlace = true, tag = 'B', a = x, b = y, c = (int)d, d = len };
            Onboarding?.OnBeltPlaced();
            RefreshVisuals();
            RefreshHud();
        }
        else if (audio != null) audio.PlayInvalid();
    }

    void DeleteAt(int x, int y)
    {
        // Node cell first, else the belt whose footprint contains the cell.
        var all = AllPieces();
        int idx = -1;
        for (int i = 0; i < all.Count; ++i)
            if (all[i].kind != Kind.Belt && all[i].x == x && all[i].y == y) { idx = i; break; }
        if (idx < 0)
            for (int i = 0; i < all.Count; ++i)
                if (all[i].kind == Kind.Belt && BeltContains(all[i], x, y)) { idx = i; break; }
        if (idx < 0) return;
        var p = all[idx];
        if (IsLocked(p.x, p.y))
        {
            if (audio != null) audio.PlayInvalid();
            return;  // locked pieces are never removable
        }
        if (session.RemovePiece(p.tag, p.x, p.y))
        {
            lastAction = new UndoRec { isPlace = false, tag = p.tag, a = p.x, b = p.y, c = p.c, d = p.d, e = p.e };
            RefreshVisuals();
            RefreshHud();
        }
        else if (audio != null) audio.PlayInvalid();
    }

    void Undo()
    {
        if (lastAction == null) return;
        var u = lastAction.Value;
        if (u.isPlace) session.RemovePiece(u.tag, u.a, u.b);
        else session.PlacePiece(u.tag, u.a, u.b, u.c, u.d, u.e);
        lastAction = null;
        RefreshVisuals();
        RefreshHud();
    }

    public void StartRun()
    {
        if (session.phase == GameSession.Phase.Build)
        {
            if (audio != null) audio.PlayUiClick();
            session.StartRun();
            if (OnRunStarted != null) OnRunStarted();
        }
    }

    public void Restart()
    {
        session.StartBuild();
        RefreshVisuals();
        RefreshHud();
    }

    public void TogglePause()
    {
        if (session.phase == GameSession.Phase.Run) session.Pause();
        RefreshHud();
    }

    public void QuitToMenu()
    {
        // S5: MenuFlow takes over (hides the game, shows the main menu).
        // The sandbox reload below is the no-MenuFlow fallback (dev probes).
        if (OnQuitToMenu != null) { OnQuitToMenu(); return; }
        StartCoroutine(ReloadSandbox());
    }

    IEnumerator ReloadSandbox()
    {
        yield return new WaitForSeconds(0.05f);
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/Scenes/Sandbox.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
#endif
    }

    // ---- Ghost preview (valid = 50% white silhouette; invalid = red) ----

    void UpdateGhost(int x, int y)
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
        bool valid = isBelt ? (CellFree(x, y) && BeltFits(x, y, dir, len)) : CellFree(x, y);
        previewSr.enabled = true;
        previewSr.color = valid ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 0.2f, 0.2f, 0.5f);
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

    bool CellFree(int x, int y)
    {
        if (x < 0 || x >= GridW || y < 0 || y >= GridH) return false;
        return JoyveyorBridge.jv_node_at_cell(runner.World, x, y) == JoyveyorBridge.InvalidId
            && JoyveyorBridge.jv_belt_at_cell(runner.World, x, y) == JoyveyorBridge.InvalidId;
    }

    bool BeltFits(int x, int y, byte d, int len)
    {
        int ex = x + (d == JoyveyorBridge.DirE ? len - 1 : d == JoyveyorBridge.DirW ? -(len - 1) : 0);
        int ey = y + (d == JoyveyorBridge.DirS ? len - 1 : d == JoyveyorBridge.DirN ? -(len - 1) : 0);
        return ex >= 0 && ex < GridW && ey >= 0 && ey < GridH;
    }

    // ---- World visuals (grid + locked + player pieces) ----

    struct PieceV { public Kind kind; public char tag; public int x, y, c, d, e; public byte dir; public int len; }

    List<PieceV> AllPieces()
    {
        var list = new List<PieceV>();
        if (session == null || session.Level == null) return list;
        foreach (var p in session.Level.Locked)
            list.Add(new PieceV { kind = KindOf(p.Tag), tag = p.Tag, x = p.A, y = p.B, c = p.C, d = p.D, e = p.E, dir = (byte)p.C, len = p.Tag == 'B' ? p.D : 1 });
        foreach (var p in session.PlayerPieces)
            list.Add(new PieceV { kind = KindOf(p.Tag), tag = p.Tag, x = p.A, y = p.B, c = p.C, d = p.D, e = p.E, dir = (byte)p.C, len = p.Tag == 'B' ? p.D : 1 });
        return list;
    }

    static Kind KindOf(char tag)
    {
        switch (tag)
        {
            case 'S': return Kind.Source;
            case 'K': return Kind.Sink;
            case 'T': return Kind.Splitter;
            case 'M': return Kind.Merger;
            default: return Kind.Belt;
        }
    }

    bool IsLocked(int x, int y)
    {
        if (session == null || session.Level == null) return false;
        foreach (var p in session.Level.Locked)
            if (p.A == x && p.B == y) return true;
        return false;
    }

    void RefreshVisuals()
    {
        ClearVisuals();
        foreach (var p in AllPieces()) visuals.Add(MakePieceVisual(p));
        RebuildSinkBars();
    }

    void ClearVisuals()
    {
        foreach (var go in visuals) Destroy(go);
        visuals.Clear();
        foreach (var go in sinkBars) Destroy(go);
        sinkBars.Clear();
        sinkCells.Clear();
    }

    static readonly Color ColorBelt = new Color(0.35f, 0.35f, 0.42f);
    static readonly Color ColorSource = new Color(0.2f, 0.8f, 0.3f);
    static readonly Color ColorSink = new Color(0.3f, 0.5f, 0.9f);
    static readonly Color ColorSplitter = new Color(0.95f, 0.6f, 0.2f);
    static readonly Color ColorMerger = new Color(0.7f, 0.4f, 0.9f);

    GameObject MakePieceVisual(PieceV p)
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

    // ---- Sink fill bars (world-space, below each sink) ----

    void RebuildSinkBars()
    {
        foreach (var go in sinkBars) Destroy(go);
        sinkBars.Clear();
        sinkCells.Clear();
        if (session == null || session.Level == null) return;
        foreach (var p in session.Level.Locked) if (p.Tag == 'K') sinkCells.Add(new Vector2Int(p.A, p.B));
        foreach (var p in session.PlayerPieces) if (p.Tag == 'K') sinkCells.Add(new Vector2Int(p.A, p.B));
        foreach (var s in sinkCells) sinkBars.Add(MakeSinkBar(s.x, s.y));
    }

    GameObject MakeSinkBar(int x, int y)
    {
        var go = new GameObject("sinkfill");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(0.3f, 0.9f, 0.5f, 0.9f);
        go.transform.localPosition = new Vector3(x, -y - 0.35f, 0.05f);
        go.transform.localScale = new Vector3(0.8f, 0.12f, 1f);
        return go;
    }

    void UpdateSinkFillBars()
    {
        if (session == null || session.Level == null) return;
        for (int i = 0; i < sinkBars.Count && i < sinkCells.Count; ++i)
        {
            var s = sinkCells[i];
            uint id = JoyveyorBridge.jv_node_at_cell(runner.World, s.x, s.y);
            if (id == JoyveyorBridge.InvalidId) continue;
            ushort count, cap;
            JoyveyorBridge.jv_sink_storage(runner.World, id, out count, out cap);
            float f = cap > 0 ? (float)count / cap : 0f;
            sinkBars[i].transform.localScale = new Vector3(0.8f * Mathf.Clamp01(f), 0.12f, 1f);
            sinkBars[i].GetComponent<SpriteRenderer>().color = f >= 1f
                ? new Color(0.3f, 1f, 0.5f, 0.9f) : new Color(0.3f, 0.9f, 0.5f, 0.9f);
        }
    }

    // ---- HUD (UGUI, Screen Space – Camera) ----

    void BuildHud()
    {
        var canvasGo = new GameObject("HudCanvas");
        // Screen Space – Camera (a screen-space UGUI canvas). The spec names
        // "Screen Space Overlay," but an Overlay canvas renders in a separate
        // screen pass that cam.Render()->RenderTexture (the proven CaptureHero
        // mechanism) does NOT capture, so the required build-phase screenshot
        // would be blank. Camera mode is still a screen-space canvas and IS
        // captured by the main camera's RenderTexture, so the HUD shows in the
        // headless screenshot.
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        canvasGo.AddComponent<GraphicRaycaster>();
        hudCanvas = canvas;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        topBar = MakeTopBar(canvasGo.transform);
        var bottomBar = MakeBottomBar(canvasGo.transform);
        jammedBanner = MakeJammedBanner(canvasGo.transform);
        pauseOverlay = MakePauseOverlay(canvasGo.transform);
        pauseOverlay.SetActive(false);
        completeOverlay = MakeCompleteOverlay(canvasGo.transform);
        completeOverlay.SetActive(false);
        failedOverlay = MakeFailedOverlay(canvasGo.transform);
        failedOverlay.SetActive(false);
        bottomBar.SetActive(true);
    }

    static readonly Color BarBg = new Color(0.06f, 0.06f, 0.08f, 0.82f);
    static readonly Color SlotBg = new Color(0.12f, 0.12f, 0.15f, 0.95f);
    static readonly Color SelHi = new Color(1f, 0.85f, 0.2f, 1f);

    GameObject MakeTopBar(Transform parent)
    {
        var bar = MakeImage(parent, "TopBar", BarBg);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 70);

        timerText = MakeText(bar.transform, "Timer", "0:00", 34, TextAnchor.MiddleLeft);
        var trt = timerText.rectTransform;
        trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(0, 1);
        trt.pivot = new Vector2(0, 0.5f);
        trt.anchoredPosition = new Vector2(24, 0);
        trt.sizeDelta = new Vector2(200, 0);

        deliveredText = MakeText(bar.transform, "Delivered", "0/0", 30, TextAnchor.MiddleCenter);
        var drt = deliveredText.rectTransform;
        drt.anchorMin = new Vector2(0.5f, 0); drt.anchorMax = new Vector2(0.5f, 1);
        drt.pivot = new Vector2(0.5f, 0.6f);
        drt.anchoredPosition = new Vector2(0, 12);
        drt.sizeDelta = new Vector2(300, 40);

        progressBarFill = MakeProgressBar(bar.transform);

        var restart = MakeButton(bar.transform, "Restart", "\u21BB", 40);
        PositionTopRight(restart, 84);
        restart.GetComponent<Button>().onClick.AddListener(() => Restart());
        var pause = MakeButton(bar.transform, "Pause", "\u23F8", 40);
        PositionTopRight(pause, 0);
        pause.GetComponent<Button>().onClick.AddListener(() => TogglePause());
        return bar;
    }

    void PositionTopRight(GameObject go, float rightOffset)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-24 - rightOffset, 0);
        rt.sizeDelta = new Vector2(56, 56);
    }

    Image MakeProgressBar(Transform parent)
    {
        var bg = MakeImage(parent, "ProgBg", new Color(0.2f, 0.2f, 0.24f, 1f));
        var rt = bg.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 6);
        rt.sizeDelta = new Vector2(360, 8);
        var fill = MakeImage(bg.transform, "ProgFill", new Color(0.3f, 0.85f, 0.4f, 1f));
        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0.5f); frt.anchorMax = new Vector2(0, 0.5f);
        frt.pivot = new Vector2(0, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(0, 8);
        return fill.GetComponent<Image>();
    }

    GameObject MakeBottomBar(Transform parent)
    {
        var bar = MakeImage(parent, "BottomBar", BarBg);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 96);

        hotbarRoot = MakeHotbar(bar.transform);
        var run = MakeButton(bar.transform, "Run", "\u25B6 RUN", 30);
        runLabel = run.GetComponentInChildren<Text>();
        var rrt = run.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(1, 0.5f); rrt.anchorMax = new Vector2(1, 0.5f);
        rrt.pivot = new Vector2(1, 0.5f);
        rrt.anchoredPosition = new Vector2(-24, 0);
        rrt.sizeDelta = new Vector2(180, 64);
        runButton = run.GetComponent<Button>();
        runButton.onClick.AddListener(() => StartRun());
        return bar;
    }

    readonly string[] SlotIcons = { "\u25AD", "\u25CF", "\u25F1", "\u25C7", "\u25C6", "\u2715" };
    readonly Color[] SlotColors = { ColorBelt, ColorSource, ColorSink, ColorSplitter, ColorMerger, new Color(0.8f, 0.3f, 0.3f) };
    Image[] slotHi = new Image[6];

    GameObject MakeHotbar(Transform parent)
    {
        var root = new GameObject("Hotbar");
        root.transform.SetParent(parent, false);
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-140, 0);
        rt.sizeDelta = new Vector2(6 * 72 + 5 * 8, 72);
        for (int i = 0; i < 6; ++i)
        {
            var slot = MakeButton(root.transform, "Slot" + (i + 1), "", 0);
            var srt = slot.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0.5f); srt.anchorMax = new Vector2(0, 0.5f);
            srt.pivot = new Vector2(0, 0.5f);
            srt.anchoredPosition = new Vector2(i * (72 + 8), 0);
            srt.sizeDelta = new Vector2(72, 72);
            slot.GetComponent<Image>().color = SlotBg;
            // icon (colored)
            var icon = MakeText(slot.transform, "Icon", SlotIcons[i], 34, TextAnchor.MiddleCenter);
            icon.color = SlotColors[i];
            Stretch(icon.rectTransform, 6, 6, -6, -6);
            // number badge
            var badge = MakeText(slot.transform, "Badge", (i + 1).ToString(), 18, TextAnchor.UpperLeft);
            badge.color = new Color(1f, 1f, 1f, 0.85f);
            var brt = badge.rectTransform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0, 1);
            brt.anchoredPosition = new Vector2(4, -4);
            brt.sizeDelta = new Vector2(20, 20);
            // selection highlight (full-slot outline)
            var hi = MakeImage(slot.transform, "Hi", SelHi);
            var hiImg = hi.GetComponent<Image>();
            hiImg.raycastTarget = false;
            hiImg.enabled = false;
            Stretch(hi.GetComponent<RectTransform>(), 0, 0, 0, 0);
            slotHi[i] = hiImg;
            slot.GetComponent<Button>().onClick.AddListener(() => SelectTool((Tool)i));
        }
        return root;
    }

    GameObject MakeJammedBanner(Transform parent)
    {
        var b = MakeImage(parent, "JammedBanner", new Color(0.7f, 0.1f, 0.1f, 0.92f));
        var rt = b.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -84);
        rt.sizeDelta = new Vector2(560, 48);
        var t = MakeText(b.transform, "JammedText", "JAMMED \u2014 delete a piece or restart", 26, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, 0, 0, 0, 0);
        b.SetActive(false);
        return b;
    }

    // S5: pause doubles as settings (text scale) + key reference + "skip
    // hints" link (jv-design-uxui §Wireframes "Pause").
    GameObject MakePauseOverlay(Transform parent)
    {
        var ov = MakeImage(parent, "PauseOverlay", new Color(0f, 0f, 0f, 0.6f));
        Stretch(ov.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var title = MakeText(ov.transform, "PauseTitle", "PAUSED", 64, TextAnchor.MiddleCenter);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f); trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0, 250);
        trt.sizeDelta = new Vector2(600, 80);
        var resume = MakeButton(ov.transform, "Resume", "RESUME", 30);
        CenterButton(resume, 110);
        resume.GetComponent<Button>().onClick.AddListener(() => TogglePause());
        var restart = MakeButton(ov.transform, "Restart", "RESTART", 30);
        CenterButton(restart, 30);
        restart.GetComponent<Button>().onClick.AddListener(() => Restart());
        var quit = MakeButton(ov.transform, "Quit", "QUIT TO MENU", 26);
        CenterButton(quit, -50);
        quit.GetComponent<Button>().onClick.AddListener(() => QuitToMenu());

        // Text size: [S] [M] [L] — one global canvas multiplier.
        var cap = MakeText(ov.transform, "TextScaleCap", "TEXT SIZE", 20, TextAnchor.MiddleRight);
        var crt = cap.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(1, 0.5f);
        crt.anchoredPosition = new Vector2(-210, -130);
        crt.sizeDelta = new Vector2(140, 40);
        pauseScaleButtons = new Button[3];
        pauseScaleHi = new Image[3];
        string[] labels = { "S", "M", "L" };
        for (int i = 0; i < 3; ++i)
        {
            var b = MakeButton(ov.transform, "Scale" + labels[i], labels[i], 26);
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(-140 + i * 64, -130);
            brt.sizeDelta = new Vector2(56, 48);
            var hi = MakeImage(b.transform, "Hi", SelHi);
            var hiImg = hi.GetComponent<Image>();
            hiImg.raycastTarget = false;
            hiImg.enabled = false;
            Stretch(hi.GetComponent<RectTransform>(), 0, 0, 0, 0);
            pauseScaleHi[i] = hiImg;
            int idx = i;
            b.GetComponent<Button>().onClick.AddListener(() => SetTextScale(idx));
            pauseScaleButtons[i] = b.GetComponent<Button>();
        }

        // Key reference (jv-design-uxui §Wireframes "Pause").
        var keys1 = MakeText(ov.transform, "Keys1", "Keys: R rotate \u00B7 Z undo \u00B7 1-6 tools", 20, TextAnchor.MiddleCenter);
        var k1rt = keys1.rectTransform;
        k1rt.anchorMin = new Vector2(0.5f, 0.5f); k1rt.anchorMax = new Vector2(0.5f, 0.5f);
        k1rt.pivot = new Vector2(0.5f, 0.5f);
        k1rt.anchoredPosition = new Vector2(0, -190);
        k1rt.sizeDelta = new Vector2(700, 30);
        var keys2 = MakeText(ov.transform, "Keys2", "WASD dir \u00B7 right-click delete \u00B7 Esc pause \u00B7 Enter run", 20, TextAnchor.MiddleCenter);
        var k2rt = keys2.rectTransform;
        k2rt.anchorMin = new Vector2(0.5f, 0.5f); k2rt.anchorMax = new Vector2(0.5f, 0.5f);
        k2rt.pivot = new Vector2(0.5f, 0.5f);
        k2rt.anchoredPosition = new Vector2(0, -222);
        k2rt.sizeDelta = new Vector2(700, 30);

        // "Skip hints" link (onboarding, level 1).
        skipHintsLink = MakeText(ov.transform, "SkipHints", "Skip hints", 20, TextAnchor.MiddleCenter);
        skipHintsLink.color = new Color(0.6f, 0.75f, 1f, 1f);
        var srt = skipHintsLink.rectTransform;
        srt.anchorMin = new Vector2(0.5f, 0.5f); srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(0, -262);
        srt.sizeDelta = new Vector2(200, 32);
        var skipBtn = MakeButton(ov.transform, "SkipHintsBtn", "", 0);
        var sbt = skipBtn.GetComponent<RectTransform>();
        sbt.anchorMin = new Vector2(0.5f, 0.5f); sbt.anchorMax = new Vector2(0.5f, 0.5f);
        sbt.pivot = new Vector2(0.5f, 0.5f);
        sbt.anchoredPosition = new Vector2(0, -262);
        sbt.sizeDelta = new Vector2(200, 44);   // >= 44 px hit area
        skipBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);  // invisible, clickable
        skipBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (onboarding != null) onboarding.SkipAll();
        });
        return ov;
    }

    void CenterButton(GameObject go, float yOff)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, yOff);
        rt.sizeDelta = new Vector2(320, 64);
    }

    // ---- S5: overlay control (driven by GameSession phase + MenuFlow) ----

    // Hide every end-of-round overlay (called on Build / level start / resume).
    public void HideOverlays()
    {
        if (completeOverlay != null) completeOverlay.SetActive(false);
        if (failedOverlay != null) failedOverlay.SetActive(false);
    }

    // Level complete: fill the score rows, show the overlay, pop the stars.
    public void ShowCompleteOverlay()
    {
        if (completeOverlay == null || session == null) return;
        int d, t; SumSinks(out d, out t);
        uint timeLeft = session.CompletionTicks < session.Level.TimeLimit
            ? session.Level.TimeLimit - session.CompletionTicks : 0;
        uint eff = session.Level.Budget > (uint)session.PiecesUsed
            ? (uint)session.Level.Budget - (uint)session.PiecesUsed : 0;
        if (deliveredRow != null) deliveredRow.text = "Delivered  " + d + "/" + t;
        if (timeLeftRow != null) timeLeftRow.text = "Time left  +" + (timeLeft * 10);
        if (efficiencyRow != null) efficiencyRow.text = "Efficiency  +" + (eff * 50);
        if (totalRow != null) totalRow.text = "Total        " + session.Score;
        if (onboarding != null) onboarding.Hide();   // "Delivered!" hint overlaps the title
        completeOverlay.SetActive(true);
        if (starPop != null) StopCoroutine(starPop);
        starPop = StartCoroutine(StarPop(session.Stars));
    }

    // Stars pop in sequence, 300 ms apart (jv-design-uxui §Wireframes).
    IEnumerator StarPop(int stars)
    {
        ShownStars = 0;
        for (int i = 0; i < starImages.Length; ++i)
        {
            if (starImages[i] == null) continue;
            starImages[i].sprite = StarSprite(i < stars);
            starImages[i].enabled = false;
        }
        for (int i = 0; i < stars && i < starImages.Length; ++i)
        {
            yield return new WaitForSeconds(0.3f);
            if (starImages[i] != null) starImages[i].enabled = true;
            ShownStars = i + 1;
        }
    }

    // Level failed: show delivered n/total + the failed overlay.
    public void ShowFailedOverlay()
    {
        if (failedOverlay == null || session == null) return;
        int d, t; SumSinks(out d, out t);
        if (failedDelivered != null) failedDelivered.text = d + " / " + t + " delivered";
        if (onboarding != null) onboarding.Hide();   // hint box overlaps the failed overlay
        failedOverlay.SetActive(true);
    }

    // Public pause toggle for MenuFlow / probes (delegates to session).
    public void PauseGame() { TogglePause(); }

    // ---- S5: Level complete (jv-design-uxui §Wireframes "Level complete") ----
    // Stars pop in sequence (300 ms apart); score rows reuse numbers the
    // player already saw. REPLAY / NEXT (Next advances without re-entering
    // select — MenuFlow.NextLevel drives that).

    Image[] starImages;

    GameObject MakeCompleteOverlay(Transform parent)
    {
        var ov = MakeImage(parent, "CompleteOverlay", new Color(0f, 0f, 0f, 0.72f));
        Stretch(ov.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var title = MakeText(ov.transform, "Title", "LEVEL COMPLETE", 56, TextAnchor.MiddleCenter);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f); trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0, 250);
        trt.sizeDelta = new Vector2(700, 80);

        // Star row: three code-drawn star sprites (filled vs empty outline —
        // shape, not color-only). Popped in sequence by MenuFlow.
        var row = new GameObject("StarRow");
        row.transform.SetParent(ov.transform, false);
        var rrt = row.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.5f, 0.5f); rrt.anchorMax = new Vector2(0.5f, 0.5f);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = new Vector2(0, 130);
        rrt.sizeDelta = new Vector2(3 * 110 + 2 * 30, 110);
        starImages = new Image[3];
        for (int i = 0; i < 3; ++i)
        {
            var go = new GameObject("Star" + (i + 1));
            go.transform.SetParent(row.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = StarSprite(true);  // placeholder; real fill set on show
            var grt = img.rectTransform;
            grt.anchorMin = new Vector2(0, 0.5f); grt.anchorMax = new Vector2(0, 0.5f);
            grt.pivot = new Vector2(0, 0.5f);
            grt.anchoredPosition = new Vector2(i * 140, 0);
            grt.sizeDelta = new Vector2(110, 110);
            img.raycastTarget = false;
            img.enabled = false;
            starImages[i] = img;
        }

        deliveredRow = MakeScoreRow(ov.transform, "DeliveredRow", "Delivered", 0);
        timeLeftRow = MakeScoreRow(ov.transform, "TimeLeftRow", "Time left", -70);
        efficiencyRow = MakeScoreRow(ov.transform, "EfficiencyRow", "Efficiency", -140);
        totalRow = MakeScoreRow(ov.transform, "TotalRow", "Total", -210);

        replayButton = MakeButton(ov.transform, "Replay", "REPLAY", 30).GetComponent<Button>();
        var prt = replayButton.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(-170, -300);
        prt.sizeDelta = new Vector2(280, 64);
        replayButton.onClick.AddListener(() =>
        {
            if (onboarding != null) onboarding.Reset(session.LevelIndex);
            Restart();
            if (completeOverlay != null) completeOverlay.SetActive(false);
        });
        nextButton = MakeButton(ov.transform, "Next", "NEXT", 30).GetComponent<Button>();
        var nrt = nextButton.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0.5f, 0.5f); nrt.anchorMax = new Vector2(0.5f, 0.5f);
        nrt.pivot = new Vector2(0.5f, 0.5f);
        nrt.anchoredPosition = new Vector2(170, -300);
        nrt.sizeDelta = new Vector2(280, 64);
        nextButton.onClick.AddListener(() =>
        {
            if (completeOverlay != null) completeOverlay.SetActive(false);
            if (OnNextLevel != null) OnNextLevel();
        });
        return ov;
    }

    Text MakeScoreRow(Transform parent, string name, string label, float yOff)
    {
        var t = MakeText(parent, name, label + "  ", 30, TextAnchor.MiddleCenter);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, yOff);
        rt.sizeDelta = new Vector2(600, 44);
        return t;
    }

    // ---- S5: Level failed (jv-design-uxui §Wireframes "Level failed") ----

    GameObject MakeFailedOverlay(Transform parent)
    {
        var ov = MakeImage(parent, "FailedOverlay", new Color(0.16f, 0.03f, 0.03f, 0.88f));
        Stretch(ov.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var title = MakeText(ov.transform, "Title", "TIME'S UP", 64, TextAnchor.MiddleCenter);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f); trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0, 140);
        trt.sizeDelta = new Vector2(700, 90);
        failedDelivered = MakeText(ov.transform, "Delivered", "0 / 0 delivered", 34, TextAnchor.MiddleCenter);
        var drt = failedDelivered.rectTransform;
        drt.anchorMin = new Vector2(0.5f, 0.5f); drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.anchoredPosition = new Vector2(0, 20);
        drt.sizeDelta = new Vector2(600, 50);
        retryButton = MakeButton(ov.transform, "Retry", "RETRY", 30).GetComponent<Button>();
        var rrt = retryButton.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.5f, 0.5f); rrt.anchorMax = new Vector2(0.5f, 0.5f);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = new Vector2(-170, -120);
        rrt.sizeDelta = new Vector2(280, 64);
        retryButton.onClick.AddListener(() =>
        {
            Restart();
            if (failedOverlay != null) failedOverlay.SetActive(false);
        });
        failedMenuButton = MakeButton(ov.transform, "Menu", "MENU", 30).GetComponent<Button>();
        var mrt = failedMenuButton.GetComponent<RectTransform>();
        mrt.anchorMin = new Vector2(0.5f, 0.5f); mrt.anchorMax = new Vector2(0.5f, 0.5f);
        mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.anchoredPosition = new Vector2(170, -120);
        mrt.sizeDelta = new Vector2(280, 64);
        failedMenuButton.onClick.AddListener(() =>
        {
            if (failedOverlay != null) failedOverlay.SetActive(false);
            QuitToMenu();
        });
        return ov;
    }

    // Code-drawn star sprite (5-point). Filled = solid gold; empty = outline
    // only. Shape-based, so star state is never color-only (a11y §7).
    static Sprite _starFilled, _starEmpty;
    public static Sprite StarSprite(bool filled)
    {
        if (filled)
        {
            if (_starFilled == null) _starFilled = MakeStarTexture(true);
            return _starFilled;
        }
        if (_starEmpty == null) _starEmpty = MakeStarTexture(false);
        return _starEmpty;
    }

    static Sprite MakeStarTexture(bool filled)
    {
        int S = 64;
        var tex = new Texture2D(S, S);
        var px = new Color[S * S];
        float cx = S / 2f, cy = S / 2f, R = S * 0.48f, r = R * 0.42f;
        for (int y = 0; y < S; ++y)
            for (int x = 0; x < S; ++x)
            {
                float dx = x - cx + 0.5f, dy = y - cy + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                // 5-point star radius: alternates R (points) / r (valleys) every 36°.
                float k = Mathf.Abs(ang % (Mathf.PI * 2f / 5f) - Mathf.PI / 5f);
                float starR = Mathf.Lerp(r, R, Mathf.Clamp01(k / (Mathf.PI / 5f)));
                bool inside = dist <= starR;
                bool edge = inside && dist >= starR - 3.5f;
                Color c = Color.clear;
                if (filled && inside) c = new Color(1f, 0.84f, 0.2f, 1f);
                else if (!filled && edge) c = new Color(0.85f, 0.85f, 0.9f, 1f);
                px[y * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
    }

    // S5: code-drawn padlock (level select, locked slots). No font glyph.
    static Sprite _padlock;
    public static Sprite PadlockSprite
    {
        get { if (_padlock == null) _padlock = MakePadlockTexture(); return _padlock; }
    }

    static Sprite MakePadlockTexture()
    {
        int S = 64;
        var tex = new Texture2D(S, S);
        var px = new Color[S * S];
        var c = new Color(0.6f, 0.6f, 0.66f, 1f);
        float cx = S / 2f;
        for (int y = 0; y < S; ++y)
            for (int x = 0; x < S; ++x)
            {
                bool body = x >= 16 && x <= 48 && y >= 12 && y <= 42;
                // Shackle: upper semicircle, center (32,42), outer r 14 / inner r 9.
                float dx = x - cx, dy = y - 42;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                bool shackle = dy >= 0 && dist <= 14 && dist >= 9;
                px[y * S + x] = (body || shackle) ? c : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
    }

    // ---- HUD builders ----

    // Stretch a RectTransform to fill its parent with the given insets.
    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    GameObject MakeImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = true;
        return go;
    }

    static Font _font;
    static Font GetFont()
    {
        if (_font != null) return _font;
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (System.Exception) { }
        if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Helvetica", 16);
        return _font;
    }

    Text MakeText(Transform parent, string name, string txt, int size, TextAnchor align)
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

    GameObject MakeButton(Transform parent, string name, string label, int size)
    {
        var go = MakeImage(parent, name, new Color(0.16f, 0.16f, 0.2f, 1f));
        go.AddComponent<Button>();
        if (!string.IsNullOrEmpty(label))
        {
            var t = MakeText(go.transform, "Label", label, size, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4, 4, -4, -4);
        }
        return go;
    }

    // ---- HUD per-frame refresh ----

    void UpdateHud()
    {
        var lv = session.Level;
        uint remaining = session.phase == GameSession.Phase.Build ? lv.TimeLimit : lv.TimeLimit - session.Ticks;
        bool low = session.phase == GameSession.Phase.Run && remaining <= 10;
        string tstr = FormatTime(remaining);
        if (low) tstr = "!" + tstr;
        timerText.text = tstr;
        float pulse = low ? (0.6f + 0.4f * Mathf.Sin(Time.time * 8f)) : 1f;
        timerText.color = low ? new Color(1f, 0.25f, 0.25f, pulse) : Color.white;

        int delivered = 0, total = 0;
        SumSinks(out delivered, out total);
        deliveredText.text = delivered + "/" + total;
        // S5: onboarding hint #5 — first item delivered this run (level 1).
        if (!firstDeliveredFired && delivered > 0)
        {
            firstDeliveredFired = true;
            if (OnFirstDelivered != null) OnFirstDelivered();
        }
        if (progressBarFill != null)
        {
            float f = total > 0 ? (float)delivered / total : 0f;
            progressBarFill.rectTransform.sizeDelta = new Vector2(360f * Mathf.Clamp01(f), 8f);
        }

        if (jammedBanner != null)
            jammedBanner.SetActive(JoyveyorBridge.jv_is_deadlocked(runner.World) == 1);

        if (pauseOverlay != null) pauseOverlay.SetActive(session.Paused && session.phase == GameSession.Phase.Run);

        if (runButton != null && session.phase == GameSession.Phase.Build)
        {
            bool hasSrc = HasPiece('S'), hasSnk = HasPiece('K');
            runReady = hasSrc && hasSnk;
            runButton.interactable = runReady;
            runLabel.text = runReady ? "\u25B6 RUN"
                : !hasSrc && !hasSnk ? "place a source and a sink"
                : !hasSrc ? "place a source" : "place a sink";
        }
    }

    void SumSinks(out int delivered, out int total)
    {
        delivered = 0; total = 0;
        var sinks = new List<Vector2Int>();
        foreach (var p in session.Level.Locked) if (p.Tag == 'K') sinks.Add(new Vector2Int(p.A, p.B));
        foreach (var p in session.PlayerPieces) if (p.Tag == 'K') sinks.Add(new Vector2Int(p.A, p.B));
        foreach (var s in sinks)
        {
            uint id = JoyveyorBridge.jv_node_at_cell(runner.World, s.x, s.y);
            if (id == JoyveyorBridge.InvalidId) continue;
            ushort count, cap;
            JoyveyorBridge.jv_sink_storage(runner.World, id, out count, out cap);
            delivered += count; total += cap;
        }
    }

    bool HasPiece(char tag)
    {
        foreach (var p in session.Level.Locked) if (p.Tag == tag) return true;
        foreach (var p in session.PlayerPieces) if (p.Tag == tag) return true;
        return false;
    }

    static string FormatTime(uint ticks)
    {
        int sec = (int)(ticks / 50);  // 50 Hz fixed timestep -> 1 s = 50 ticks
        return (sec / 60) + ":" + ((sec % 60) < 10 ? "0" : "") + (sec % 60);
    }

    void RefreshHud()
    {
        if (hotbarRoot != null && topBar != null)
            hotbarRoot.SetActive(session.phase == GameSession.Phase.Build);
        for (int i = 0; i < slotHi.Length; ++i)
            if (slotHi[i] != null) slotHi[i].enabled = (tool == (Tool)i);
        UpdateHud();
    }

    // ---- Helpers ----

    static bool PointerOverUI()
    {
        var es = FindObjectOfType<EventSystem>();
        return es != null && es.IsPointerOverGameObject();
    }

    bool HoverCell(out int cx, out int cy)
    {
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        cx = Mathf.FloorToInt(p.x / Cell);
        cy = Mathf.FloorToInt(-p.y / Cell);
        return cx >= 0 && cx < GridW && cy >= 0 && cy < GridH;
    }

    static bool BeltContains(PieceV p, int x, int y)
    {
        if (p.dir == JoyveyorBridge.DirE) return y == p.y && x >= p.x && x < p.x + p.len;
        if (p.dir == JoyveyorBridge.DirW) return y == p.y && x <= p.x && x > p.x - p.len;
        if (p.dir == JoyveyorBridge.DirS) return x == p.x && y >= p.y && y < p.y + p.len;
        return x == p.x && y <= p.y && y > p.y - p.len;  // N
    }

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
            verts.Add(new Vector3(x * Cell, 0, 0));
            verts.Add(new Vector3(x * Cell, -GridH * Cell, 0));
        }
        for (int y = 0; y <= GridH; ++y)
        {
            verts.Add(new Vector3(0, -y * Cell, 0));
            verts.Add(new Vector3(GridW * Cell, -y * Cell, 0));
        }
        mesh.vertices = verts.ToArray();
        var idx = new int[verts.Count];
        for (int i = 0; i < idx.Length; ++i) idx[i] = i;
        mesh.SetIndices(idx, MeshTopology.Lines, 0);
        mf.mesh = mesh;
        mr.material = new Material(Shader.Find("Sprites/Default"));
        mr.material.color = new Color(1f, 1f, 1f, 0.15f);
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

    static Sprite MakeSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }
}
