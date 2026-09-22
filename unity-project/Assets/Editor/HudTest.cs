using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless HUD / input / feedback test (v1.0 S4). Same marker + domain-reload
// pattern as GameTest / CaptureHero. Boots the in-game GameScreen on top of the
// S3 GameSession (level 1), places the reference belt through the game layer,
// then asserts the UGUI HUD tracks the sim:
//   * build phase: top bar (timer 15:00, delivered 0/10) + hotbar + RUN shown
//   * RUN enabled once a source AND a sink exist
//   * a 1600x900 screenshot of the build-phase screen is saved (top bar +
//     hotbar + RUN must be visible)
//   * start run -> timer counts down and delivered increments; the HUD
//     "delivered" text is asserted to match the sim (jv_sink_storage)
// Exits the editor with 0 on PASS, 1 on FAIL.
//
// GameScreen is added by THIS probe (not baked into the scene) so the existing
// PlayTest / CaptureHero / GameTest probes keep running the freeform sandbox.
[InitializeOnLoad]
public static class HudTest
{
    public const string Marker  = "/tmp/jv_hudtest_active";
    public const string OutPath = "/tmp/joyveyor_hud.png";
    static int frames;
    static System.Diagnostics.Stopwatch started;

    [MenuItem("Joyveyor/Headless HUD Test")]
    static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Single);
        File.WriteAllText(Marker, "");
        frames = 0;
        started = System.Diagnostics.Stopwatch.StartNew();
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        if (!File.Exists(Marker)) return;
        if (started == null) started = System.Diagnostics.Stopwatch.StartNew(); // reload wipes statics
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Marker)) return;
        frames++;
        if (frames == 10 && !EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        if (EditorApplication.isPlaying)
        {
            var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
            if (sim != null && sim.GetComponent<HudTestProbe>() == null)
                sim.gameObject.AddComponent<HudTestProbe>();
        }
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(90))
        {
            Debug.LogError("[HudTest] watchdog: probe never finished");
            File.Delete(Marker);
            EditorApplication.Exit(2);
        }
    }
}

public class HudTestProbe : MonoBehaviour
{
    enum Stage { Init, BuildChecks, Running, Done }
    Stage stage = Stage.Init;
    int frameCount;
    int ticks;
    JoyveyorRunner runner;
    GameSession session;
    GameScreen screen;

    void Awake()
    {
        // Clean slate so level 1 is unlocked by default (deterministic).
        try { if (File.Exists(ProgressStore.SavePath)) File.Delete(ProgressStore.SavePath); }
        catch (System.Exception) { }

        runner = GetComponent<JoyveyorRunner>();
        if (runner == null) { Fail("no JoyveyorRunner"); return; }
        if (runner.GetComponent<GameSession>() == null) runner.gameObject.AddComponent<GameSession>();
        // Add the in-game screen. Its Awake disables the sandbox GridEditor
        // (before GridEditor.Start can load the demo) and frames the camera.
        if (runner.GetComponent<GameScreen>() == null) runner.gameObject.AddComponent<GameScreen>();
    }

    void Start()
    {
        session = runner.GetComponent<GameSession>();
        screen = runner.GetComponent<GameScreen>();
    }

    void Update()
    {
        if (stage == Stage.Done) return;
        frameCount++;
        // Give GameScreen.Start a few frames to load level 1 + build the HUD.
        if (stage == Stage.Init && frameCount >= 8)
        {
            if (session == null || screen == null) { Fail("GameScreen/GameSession not ready"); return; }
            if (session.phase != GameSession.Phase.Build) { Fail("not in Build after boot"); return; }

            // Place the reference belt (B 1 0 1 4) through the game layer.
            if (!session.PlacePiece('B', 1, 0, JoyveyorBridge.DirE, 4))
            { Fail("reference belt rejected: " + session.Message); return; }
            session.ClearMessage();

            // Build-phase HUD assertions.
            bool ok = true;
            ok &= Expect(screen.topBar != null && screen.topBar.activeSelf, "top bar missing/hidden");
            ok &= Expect(screen.hotbarRoot != null && screen.hotbarRoot.activeSelf, "hotbar missing/hidden");
            ok &= Expect(screen.runButton != null && screen.runButton.interactable, "RUN not enabled (source+sink present)");
            ok &= Expect(screen.timerText != null && screen.timerText.text == "0:18",
                "timer=" + (screen.timerText != null ? screen.timerText.text : "null") + " want 0:18");
            ok &= Expect(screen.deliveredText != null && screen.deliveredText.text == "0/10",
                "delivered=" + (screen.deliveredText != null ? screen.deliveredText.text : "null") + " want 0/10");
            if (!ok) { Fail("build-phase HUD assertions"); return; }

            DumpSprites();
            SaveShot();
            stage = Stage.BuildChecks;

            // Start the run; the HUD must track the sim from here.
            screen.StartRun();
            if (session.phase != GameSession.Phase.Run) { Fail("StartRun did not enter Run"); return; }
            stage = Stage.Running;
            ticks = 0;
            Debug.Log("[HudTest] build OK — running level 1 (screenshot " + HudTest.OutPath + ")");
        }
    }

    void FixedUpdate()
    {
        if (stage != Stage.Running) return;
        ticks++;
        int delivered = 0, total = 0;
        SumSinks(out delivered, out total);
        // delivered is stable for ~15 ticks between source spawns, so once it's
        // > 0 the HUD (updated in LateUpdate) will match within a frame.
        if (delivered > 0 && screen.deliveredText.text == delivered + "/" + total)
        {
            bool ok = true;
            ok &= Expect(total == 10, "total=" + total + " want 10");
            ok &= Expect(delivered > 0, "delivered=" + delivered + " want > 0");
            ok &= Expect(screen.hotbarRoot == null || !screen.hotbarRoot.activeSelf, "hotbar visible in run phase");
            ok &= Expect(screen.timerText.text != "0:18", "timer did not count down");
            ok &= Expect(TimerMatchesSim(), "timer text " + screen.timerText.text + " != sim");
            Finish(ok, delivered, total);
        }
        if (ticks > 1200) Fail("watchdog: delivered never > 0 or HUD never matched sim");
    }

    bool TimerMatchesSim()
    {
        // HUD timer = FormatTime(TimeLimit - Ticks). Allow the current or the
        // previous tick to absorb the one-frame FixedUpdate/LateUpdate lag.
        uint limit = session.Level.TimeLimit;
        uint t = session.Ticks;
        string a = FormatTime(limit > t ? limit - t : 0);
        string b = FormatTime(limit > t - 1 ? limit - (t - 1) : 0);
        return screen.timerText.text == a || screen.timerText.text == b;
    }

    static string FormatTime(uint ticks)
    {
        int sec = (int)(ticks / 50);  // 50 Hz fixed timestep -> 1 s = 50 ticks
        return (sec / 60) + ":" + ((sec % 60) < 10 ? "0" : "") + (sec % 60);
    }

    void SumSinks(out int delivered, out int total)
    {
        delivered = 0; total = 0;
        var sinks = new System.Collections.Generic.List<Vector2Int>();
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

    void DumpSprites()
    {
        var srs = Object.FindObjectsOfType<SpriteRenderer>();
        Debug.Log("[HudTest] === " + srs.Length + " SpriteRenderers ===");
        foreach (var sr in srs)
        {
            if (!sr.gameObject.activeInHierarchy) continue;
            var wp = sr.transform.position;
            var c = sr.color;
            var par = sr.transform.parent != null ? sr.transform.parent.name : "-";
            Debug.Log(string.Format("[HudTest]   {0} wp=({1:F1},{2:F1},{3:F1}) scale=({4:F1},{5:F1}) color=({6:F2},{7:F2},{8:F2}) parent={9}",
                sr.name, wp.x, wp.y, wp.z, sr.transform.localScale.x, sr.transform.localScale.y,
                c.r, c.g, c.b, par));
        }
        // Also dump ALL children of Sim (any component) to catch non-SR
        // objects (TextMesh HUD, meshes) that the SR dump misses.
        var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
        if (sim != null)
        {
            Debug.Log("[HudTest] === Sim children (" + sim.transform.childCount + ") ===");
            foreach (Transform ch in sim.transform)
            {
                var coms = ch.GetComponents<Component>();
                var names = new System.Text.StringBuilder();
                foreach (var c2 in coms)
                    if (c2 != null && c2.GetType().Namespace != "UnityEngine" ||
                        (c2 is SpriteRenderer || c2 is TextMesh || c2 is MeshFilter || c2 is AudioSource))
                        names.Append(c2.GetType().Name + " ");
                Debug.Log(string.Format("[HudTest]   child '{0}' active={1} wp=({2:F1},{3:F1},{4:F1}) comps=[{5}]",
                    ch.name, ch.gameObject.activeInHierarchy,
                    ch.position.x, ch.position.y, ch.position.z, names));
            }
        }
    }

    void SaveShot()
    {
        var cam = Camera.main;
        if (cam == null) { var g = new GameObject("Main Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); }
        cam.orthographic = true;
        // Frame level 1's content (source x=0, sink x=5, both at y=0 -> world
        // (0..5, 0)) so the build-phase shot shows the actual level, not just
        // the HUD. Content sits in the upper-middle, clear of the top bar.
        cam.orthographicSize = 5.5f;
        // This Unity build's camera looks along +forward (identity rotation),
        // so the camera must sit at NEGATIVE z to look toward the grid at z=0.
        // (The scene's own Main Camera is at z=-10 and renders the grid.)
        cam.transform.position = new Vector3(2.5f, -2.5f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.backgroundColor = new Color(0.07f, 0.065f, 0.06f);

        // --- diagnostics: is each piece actually in front of the camera? ---
        var cf = cam.transform.forward;
        var cp = cam.transform.position;
        Debug.Log(string.Format("[HudTest] cam pos=({0:F1},{1:F1},{2:F1}) fwd=({3:F2},{4:F2},{5:F2}) near={6:F2} far={7:F2} cull={8}",
            cp.x, cp.y, cp.z, cf.x, cf.y, cf.z, cam.nearClipPlane, cam.farClipPlane, cam.cullingMask));
        foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (!sr.gameObject.activeInHierarchy) continue;
            var wp = sr.transform.position;
            var to = wp - cp;
            var dot = Vector3.Dot(to, cf);
            Debug.Log(string.Format("[HudTest]   {0} wp=({1:F1},{2:F1},{3:F1}) dist={4:F1} inFront={5}",
                sr.name, wp.x, wp.y, wp.z, to.magnitude, dot > 0f));
        }

        var rt = RenderTexture.GetTemporary(1600, 900, 24);
        var prev = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        File.WriteAllBytes(HudTest.OutPath, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[HudTest] saved " + HudTest.OutPath);
    }

    void Finish(bool ok, int delivered, int total)
    {
        stage = Stage.Done;
        File.Delete(HudTest.Marker);
        Debug.Log("[HudTest] " + (ok ? "PASS" : "FAIL")
            + "  HUD delivered=" + screen.deliveredText.text + " (sim " + delivered + "/" + total + ")"
            + " timer=" + screen.timerText.text
            + " hotbarActive=" + (screen.hotbarRoot != null && screen.hotbarRoot.activeSelf));
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Expect(bool cond, string what)
    {
        if (!cond) Debug.LogError("[HudTest] " + what);
        return cond;
    }

    void Fail(string what)
    {
        Debug.LogError("[HudTest] FAIL: " + what);
        try { File.Delete(HudTest.Marker); } catch (System.Exception) { }
        EditorApplication.Exit(1);
    }
}
