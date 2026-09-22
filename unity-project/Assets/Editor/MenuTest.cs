using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless menu-flow test (v1.0 S5). Same marker + domain-reload pattern as
// HudTest / GameTest. Boots MenuFlow (which owns the in-game GameScreen) on
// top of the S3 GameSession, then walks the state machine end-to-end:
//
//   boot -> main menu -> level select -> level 1 build
//        -> run (reference belt) -> complete overlay (correct stars)
//        -> next level unlocked in progress JSON
//        -> force a fail -> failed overlay
//
// Asserts the UGUI chrome at each step and saves a 1600x900 screenshot of:
//   main menu, level select, level 1 build, complete, failed.
// Exits the editor with 0 on PASS, 1 on FAIL.
//
// Level 1 (level01.jvl) pre-places the source (S 0 0 15) and sink (K 5 0 10)
// as locked pieces; the only player action is the reference belt
// (B 1 0 1 4). A win with the belt completes in <= ParTime with 1 player
// piece (<= ParPieces) => 3 stars, score 6580 (matches GameTest).
[InitializeOnLoad]
public static class MenuTest
{
    public const string Marker  = "/tmp/jv_menutest_active";
    public const string ShotMenu    = "/tmp/joyveyor_menu.png";
    public const string ShotSelect  = "/tmp/joyveyor_levelselect.png";
    public const string ShotBuild   = "/tmp/joyveyor_build.png";
    public const string ShotPause   = "/tmp/joyveyor_pause.png";
    public const string ShotComplete= "/tmp/joyveyor_complete.png";
    public const string ShotFailed  = "/tmp/joyveyor_failed.png";
    static int frames;
    static System.Diagnostics.Stopwatch started;

    [MenuItem("Joyveyor/Headless Menu Test")]
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
            if (sim != null && sim.GetComponent<MenuTestProbe>() == null)
                sim.gameObject.AddComponent<MenuTestProbe>();
        }
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(120))
        {
            Debug.LogError("[MenuTest] watchdog: probe never finished");
            File.Delete(Marker);
            EditorApplication.Exit(2);
        }
    }
}

public class MenuTestProbe : MonoBehaviour
{
    enum Stage { Boot, MainMenu, LevelSelect, Build, Pause, Running, Complete, Next, Failed, Done }
    Stage stage = Stage.Boot;
    int frameCount;
    int waitFrames;
    int pauseStep;            // sub-state within Stage.Pause
    bool forcedFail;
    System.Diagnostics.Stopwatch completeSw;  // real-time deadline for the star-pop
    JoyveyorRunner runner;
    GameSession session;
    GameScreen screen;
    MenuFlow flow;

    void Awake()
    {
        // Clean slate so level 1 is unlocked by default (deterministic).
        try
        {
            if (File.Exists(ProgressStore.SavePath)) File.Delete(ProgressStore.SavePath);
            string onb = System.IO.Path.Combine(Application.persistentDataPath, "joyveyor_onboarding.json");
            if (File.Exists(onb)) File.Delete(onb);
        }
        catch (System.Exception) { }

        runner = GetComponent<JoyveyorRunner>();
        if (runner == null) { Fail("no JoyveyorRunner"); return; }
        if (runner.GetComponent<GameSession>() == null) runner.gameObject.AddComponent<GameSession>();
        // GameScreen first so its Start (BuildHud + default level 1) runs
        // before MenuFlow.Start builds the menu chrome on top.
        if (runner.GetComponent<GameScreen>() == null) runner.gameObject.AddComponent<GameScreen>();
        if (runner.GetComponent<MenuFlow>() == null) runner.gameObject.AddComponent<MenuFlow>();
    }

    void Start()
    {
        session = runner.GetComponent<GameSession>();
        screen = runner.GetComponent<GameScreen>();
        flow = runner.GetComponent<MenuFlow>();
    }

    void Update()
    {
        if (stage == Stage.Done) return;
        frameCount++;
        waitFrames++;

        // Give MenuFlow.Start + GameScreen.Start a few frames to build.
        if (stage == Stage.Boot)
        {
            if (frameCount < 12) return;
            if (flow == null || flow.menuCanvas == null) { Fail("MenuFlow not ready"); return; }
            if (flow.Current != MenuFlow.Screen.Boot) { Fail("not in Boot after start"); return; }
            // Boot auto-advances after ~3s; fast-forward to the main menu.
            flow.ShowMainMenu();
            stage = Stage.MainMenu;
            return;
        }

        if (stage == Stage.MainMenu)
        {
            if (waitFrames < 3) return;
            bool ok = true;
            ok &= Expect(flow.Current == MenuFlow.Screen.MainMenu, "not MainMenu");
            ok &= Expect(flow.mainMenu != null && flow.mainMenu.activeSelf, "main menu hidden");
            ok &= Expect(flow.versionTag != null && flow.versionTag.text == "v1.0",
                "versionTag=" + (flow.versionTag != null ? flow.versionTag.text : "null"));
            if (!ok) { Fail("main menu assertions"); return; }
            SaveShot(MenuTest.ShotMenu);
            flow.ShowLevelSelect();
            stage = Stage.LevelSelect;
            waitFrames = 0;
            return;
        }

        if (stage == Stage.LevelSelect)
        {
            if (waitFrames < 3) return;
            bool ok = true;
            ok &= Expect(flow.Current == MenuFlow.Screen.LevelSelect, "not LevelSelect");
            ok &= Expect(flow.levelButtons != null && flow.levelButtons.Length == 10,
                "levelButtons=" + (flow.levelButtons != null ? flow.levelButtons.Length : -1));
            ok &= Expect(flow.levelButtons[0].interactable, "level 1 should be unlocked");
            ok &= Expect(!flow.levelButtons[1].interactable, "level 2 should be locked (no win yet)");
            ok &= Expect(flow.levelLockImages[1] != null && flow.levelLockImages[1].gameObject.activeSelf,
                "level 2 padlock not shown");
            ok &= Expect(flow.levelStarImages[0] != null && flow.levelStarImages[2] != null,
                "star images not built");
            ok &= Expect(flow.levelStarImages[0].sprite == GameScreen.StarSprite(false),
                "level 1 should show 0 stars before a win");
            if (!ok) { Fail("level select assertions"); return; }
            SaveShot(MenuTest.ShotSelect);
            flow.StartLevel(1);
            stage = Stage.Build;
            waitFrames = 0;
            return;
        }

        if (stage == Stage.Build)
        {
            if (waitFrames < 3) return;
            bool ok = true;
            ok &= Expect(flow.Current == MenuFlow.Screen.InGame, "not InGame after StartLevel(1)");
            ok &= Expect(flow.menuCanvas != null && !flow.menuCanvas.gameObject.activeSelf,
                "menu canvas still visible in-game");
            ok &= Expect(session != null && session.phase == GameSession.Phase.Build,
                "not in Build (phase=" + (session != null ? session.phase.ToString() : "null") + ")");
            ok &= Expect(session != null && session.LevelIndex == 1, "LevelIndex=" + (session != null ? session.LevelIndex : -1));
            ok &= Expect(screen.runButton != null && screen.runButton.interactable,
                "RUN not enabled (source+sink present)");
            if (!ok) { Fail("level 1 build assertions"); return; }
            SaveShot(MenuTest.ShotBuild);

            // Place the reference belt (B 1 0 1 4) through the game layer.
            if (!session.PlacePiece('B', 1, 0, JoyveyorBridge.DirE, 4))
            { Fail("reference belt rejected: " + session.Message); return; }
            session.ClearMessage();
            screen.StartRun();
            if (session.phase != GameSession.Phase.Run) { Fail("StartRun did not enter Run"); return; }
            stage = Stage.Pause;
            waitFrames = 0;
            Debug.Log("[MenuTest] build OK — running level 1");
            return;
        }

        if (stage == Stage.Pause)
        {
            // Capture the pause overlay (required screenshot). Pause only
            // works in the Run phase; the overlay shows when Paused && Run.
            // Sub-stepped so each action runs exactly once.
            if (pauseStep == 0)
            {
                if (waitFrames < 2) return;
                if (session.phase != GameSession.Phase.Run) { Fail("not in Run before pause"); return; }
                screen.TogglePause();
                pauseStep = 1;
                return;
            }
            if (pauseStep == 1)
            {
                if (waitFrames < 4) return;
                bool ok = true;
                ok &= Expect(session.Paused, "session not paused after TogglePause");
                ok &= Expect(screen.pauseOverlay != null && screen.pauseOverlay.activeSelf,
                    "pause overlay not shown");
                if (!ok) { Fail("pause assertions"); return; }
                SaveShot(MenuTest.ShotPause);
                pauseStep = 2;
                return;
            }
            if (pauseStep == 2)
            {
                screen.TogglePause();            // resume
                pauseStep = 3;
                return;
            }
            if (pauseStep == 3)
            {
                if (waitFrames < 6) return;
                if (!Expect(!session.Paused, "session still paused after resume")) { Fail("pause resume"); return; }
                stage = Stage.Running;
                waitFrames = 0;
                pauseStep = 0;
                return;
            }
            return;
        }

        if (stage == Stage.Running)
        {
            // Headless batchmode throttles FixedUpdate hard (~0.3 Hz here), so
            // the sim would take ~15 min to fill the sink. Pump the public
            // Tick() directly: CompletionTicks/score/stars are still computed
            // by the real GameSession (sim tick count to fill the sink is
            // fixed by the level), only wall-clock speed changes.
            int pump = 0;
            while (session.phase == GameSession.Phase.Run && pump < 500)
            {
                session.Tick();
                pump++;
            }
            if (session.phase == GameSession.Phase.Complete)
            {
                Debug.Log("[MenuTest] completed after " + pump + " pumped ticks, "
                    + "CompletionTicks=" + session.CompletionTicks
                    + " runner.spawned=" + runner.spawned
                    + " runner.delivered=" + runner.delivered);
                stage = Stage.Complete;
                waitFrames = 0;
                completeSw = System.Diagnostics.Stopwatch.StartNew();
            }
            else if (session.phase == GameSession.Phase.Failed)
            {
                Fail("run failed instead of completing (Ticks=" + session.Ticks + ")");
            }
            else if (pump >= 500)
            {
                int del = 0, tot = 0;
                SumSinks(out del, out tot);
                Fail("never reached Complete after 500 ticks (Ticks=" + session.Ticks
                    + " sink=" + del + "/" + tot
                    + " runner.spawned=" + runner.spawned
                    + " runner.delivered=" + runner.delivered + ")");
            }
            return;
        }

        if (stage == Stage.Complete)
        {
            // Wait for the stars to finish popping before the shot. The
            // StarPop coroutine advances on REAL time (WaitForSeconds), but
            // headless batchmode renders frames faster than real time, so a
            // frame-count wait can fire before the stars are enabled. Use a
            // real-time deadline instead.
            if (screen != null && screen.ShownStars < session.Stars
                && completeSw != null && completeSw.Elapsed < System.TimeSpan.FromSeconds(5))
                return;
            bool ok = true;
            ok &= Expect(session.Stars == 3, "stars=" + session.Stars + " want 3");
            ok &= Expect(session.Score == 6580, "score=" + session.Score + " want 6580");
            ok &= Expect(screen.completeOverlay != null && screen.completeOverlay.activeSelf,
                "complete overlay not shown");
            if (!ok) { Fail("complete assertions"); return; }
            SaveShot(MenuTest.ShotComplete);

            // Progress JSON: level 1 recorded, level 2 now unlocked.
            var p = ProgressStore.Load();
            ProgressStore.LevelResult r;
            bool has = p.Levels.TryGetValue(1, out r);
            ok &= Expect(has, "level 1 not in progress JSON");
            if (has) ok &= Expect(r.Stars == 3, "saved stars=" + r.Stars + " want 3");
            ok &= Expect(p.IsUnlocked(2), "level 2 not unlocked after win");
            if (!ok) { Fail("progress JSON assertions"); return; }

            // NEXT advances to level 2 without re-entering select.
            flow.HandleNextLevel();
            stage = Stage.Next;
            waitFrames = 0;
            return;
        }

        if (stage == Stage.Next)
        {
            if (waitFrames < 3) return;
            bool ok = true;
            ok &= Expect(flow.Current == MenuFlow.Screen.InGame, "Next left InGame");
            ok &= Expect(session.LevelIndex == 2, "LevelIndex=" + session.LevelIndex + " want 2 (advanced)");
            if (!ok) { Fail("next-level assertions"); return; }

            // Now force a FAIL on level 1 to capture the failed overlay.
            flow.StartLevel(1);
            if (!session.PlacePiece('B', 1, 0, JoyveyorBridge.DirE, 4))
            { Fail("fail-run belt rejected: " + session.Message); return; }
            session.ClearMessage();
            screen.StartRun();
            forcedFail = true;
            stage = Stage.Failed;
            waitFrames = 0;
            return;
        }

        if (stage == Stage.Failed)
        {
            if (forcedFail && session.phase == GameSession.Phase.Run)
            {
                // Jump the timer to the limit; the next sim tick fails.
                session.Ticks = session.Level.TimeLimit - 1;
                forcedFail = false;
            }
            if (session.phase == GameSession.Phase.Failed)
            {
                bool ok = true;
                ok &= Expect(screen.failedOverlay != null && screen.failedOverlay.activeSelf,
                    "failed overlay not shown");
                if (!ok) { Fail("failed assertions"); return; }
                SaveShot(MenuTest.ShotFailed);
                Finish(true);
                return;
            }
            if (waitFrames > 120) Fail("watchdog: never reached Failed");
            return;
        }
    }

    void Finish(bool ok)
    {
        stage = Stage.Done;
        File.Delete(MenuTest.Marker);
        Debug.Log("[MenuTest] " + (ok ? "PASS" : "FAIL")
            + "  stars=" + session.Stars + " score=" + session.Score
            + " levelIndex=" + session.LevelIndex
            + "  shots: " + MenuTest.ShotMenu + " " + MenuTest.ShotSelect + " "
            + MenuTest.ShotBuild + " " + MenuTest.ShotComplete + " " + MenuTest.ShotFailed);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Expect(bool cond, string what)
    {
        if (!cond) Debug.LogError("[MenuTest] " + what);
        return cond;
    }

    // Sum sink storage across locked + player K pieces (mirrors HudTest).
    void SumSinks(out int delivered, out int total)
    {
        delivered = 0; total = 0;
        var sinks = new System.Collections.Generic.List<Vector2Int>();
        if (session.Level != null)
        {
            foreach (var p in session.Level.Locked) if (p.Tag == 'K') sinks.Add(new Vector2Int(p.A, p.B));
            foreach (var p in session.PlayerPieces) if (p.Tag == 'K') sinks.Add(new Vector2Int(p.A, p.B));
        }
        foreach (var s in sinks)
        {
            uint id = JoyveyorBridge.jv_node_at_cell(runner.World, s.x, s.y);
            if (id == JoyveyorBridge.InvalidId) continue;
            ushort count, cap;
            JoyveyorBridge.jv_sink_storage(runner.World, id, out count, out cap);
            delivered += count; total += cap;
        }
    }

    void Fail(string what)
    {
        Debug.LogError("[MenuTest] FAIL: " + what);
        try { File.Delete(MenuTest.Marker); } catch (System.Exception) { }
        EditorApplication.Exit(1);
    }

    void SaveShot(string path)
    {
        var cam = Camera.main;
        if (cam == null) { var g = new GameObject("Main Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); }
        cam.orthographic = true;
        // Frame level 1's content (source x=0, sink x=5, y=0 -> world (0..5,0))
        // so the in-game shots show the level, not just the HUD. Menu shots are
        // full-screen UI, so the framing is irrelevant there.
        cam.orthographicSize = 5.5f;
        // This Unity build's camera looks along +forward (identity rotation),
        // so it must sit at NEGATIVE z to look toward the grid at z=0.
        cam.transform.position = new Vector3(2.5f, -2.5f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.backgroundColor = new Color(0.07f, 0.065f, 0.06f);

        var rt = RenderTexture.GetTemporary(1600, 900, 24);
        var prev = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[MenuTest] saved " + path);
    }
}
