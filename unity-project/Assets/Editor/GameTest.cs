using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless game-loop test (v1.0 S3). Same pattern as PlayTest: marker file
// survives the play-mode domain reload; the probe added on entering play
// mode drives a real GameSession through level 1 with its reference
// solution and asserts the full contract:
//   * Build -> Run -> Complete state machine
//   * completion tick == 257 (deterministic), stars == 3, score == 6580
//   * budget rejection + locked-piece protection
//   * progress JSON written with the right values (unlock level 2)
// Exits the editor with 0 on PASS, 1 on FAIL.
[InitializeOnLoad]
public static class GameTest
{
    public const string Marker = "/tmp/jv_gametest_active";

    [MenuItem("Joyveyor/Headless Game Test")]
    static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Single);
        File.WriteAllText(Marker, "");
        frames = 0;
        started = System.Diagnostics.Stopwatch.StartNew();
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static int frames;
    static System.Diagnostics.Stopwatch started;

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        if (!File.Exists(Marker)) return;
        if (started == null) started = System.Diagnostics.Stopwatch.StartNew();  // reload wipes statics
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Marker)) return;  // not a GameTest run (e.g. PlayTest)
        frames++;
        if (frames == 10 && !EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
            return;
        }
        if (EditorApplication.isPlaying)
        {
            var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
            if (sim != null && sim.GetComponent<GameTestProbe>() == null)
                sim.gameObject.AddComponent<GameTestProbe>();
        }
        // Watchdog: probe needs ~260 sim ticks (batchmode runs fast); 120s
        // wall clock is a generous cap.
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(120))
        {
            Debug.LogError("[GameTest] watchdog: probe never finished");
            File.Delete(Marker);
            EditorApplication.Exit(2);
        }
    }
}

public class GameTestProbe : MonoBehaviour
{
    const uint ExpectedCompletionTicks = 257;  // S2 reference result for level 1
    const int ExpectedStars = 3;
    const uint ExpectedScore = 6580;           // (900-257)*10 + (4-1)*50

    JoyveyorRunner runner;
    GameSession session;
    bool started;
    int watchdog;

    void Awake()
    {
        // The sandbox's GridEditor loads its own demo level in Start() and
        // would clobber the level under test. Awake runs before Start on the
        // same object, so disabling it here stops it before it can run.
        var ge = GetComponent<GridEditor>();
        if (ge != null) ge.enabled = false;
    }

    void Start()
    {
        runner = GetComponent<JoyveyorRunner>();
        if (runner == null) { Fail("no JoyveyorRunner"); return; }
        session = runner.GetComponent<GameSession>();
        if (session == null) session = runner.gameObject.AddComponent<GameSession>();

        // Clean slate so the assertions are about THIS run's win.
        try { if (File.Exists(ProgressStore.SavePath)) File.Delete(ProgressStore.SavePath); }
        catch (System.Exception) { }

        // 1) Load level 1 (build phase).
        if (!session.LoadLevel(1)) { Fail("LoadLevel(1) failed"); return; }
        if (session.phase != GameSession.Phase.Build) { Fail("not in Build after load"); return; }
        if (session.Level == null || session.Level.TimeLimit != 900 || session.Level.Budget != 4)
        { Fail("level header wrong"); return; }

        // 2) Locked-piece protection: the locked source at (0,0) can't be
        //    built over or removed.
        if (session.PlacePiece('B', 0, 0, JoyveyorBridge.DirE, 2))
            { Fail("placement on locked cell (0,0) accepted"); return; }
        if (session.Message.IndexOf("locked") < 0) { Fail("locked-cell message missing: " + session.Message); return; }
        session.ClearMessage();
        if (session.RemovePiece('S', 0, 0)) { Fail("locked source removable"); return; }
        session.ClearMessage();

        // 3) Apply the reference solution through the game layer (budget path).
        string sol = File.ReadAllText("Assets/Levels/reference/level01.sol");
        int placed = 0;
        foreach (var line in sol.Replace("\r", "").Split('\n'))
        {
            string t = line.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            if (!LevelDef.ParsePlacementLine(t, out LevelDef.LockedPiece op)) { Fail("bad sol line: " + t); return; }
            if (!session.PlacePiece(op.Tag, op.A, op.B, op.C, op.D, op.E))
            { Fail("solution piece rejected: " + t + " (" + session.Message + ")"); return; }
            ++placed;
        }
        if (placed != 1) { Fail("expected 1 solution piece, placed " + placed); return; }

        // 4) Budget: fill to the limit (4) with dummy belts, then the 5th
        //    must be rejected with a budget message.
        int[] dummyY = { 10, 11, 12 };
        foreach (var y in dummyY)
            if (!session.PlacePiece('B', 10, y, JoyveyorBridge.DirE, 2))
                { Fail("dummy belt rejected: " + session.Message); return; }
        if (session.PiecesUsed != 4) { Fail("PiecesUsed=" + session.PiecesUsed + " want 4"); return; }
        if (session.PlacePiece('B', 10, 13, JoyveyorBridge.DirE, 2))
            { Fail("placement over budget accepted"); return; }
        if (session.Message.IndexOf("budget") < 0) { Fail("budget message missing: " + session.Message); return; }
        session.ClearMessage();
        foreach (var y in dummyY)
            if (!session.RemovePiece('B', 10, y)) { Fail("dummy belt removal failed"); return; }
        if (session.PiecesUsed != 1) { Fail("PiecesUsed after cleanup=" + session.PiecesUsed); return; }

        // 5) Run to completion (GameSession.Tick drives the sim, 1 tick / fixed update).
        session.StartRun();
        if (session.phase != GameSession.Phase.Run) { Fail("StartRun did not enter Run"); return; }
        started = true;
        Debug.Log("[GameTest] build OK — running level 1 (limit " + session.Level.TimeLimit + " ticks)");
    }

    void FixedUpdate()
    {
        if (!started) return;
        if (session.phase == GameSession.Phase.Complete)
        {
            Verify(session);
            return;
        }
        if (session.phase == GameSession.Phase.Failed)
        { Fail("level FAILED at tick " + session.Ticks); return; }
        if (++watchdog > 3000)  // 3000 fixed updates = 100s of sim >> 900-tick limit
            Fail("watchdog: never reached Complete (tick " + session.Ticks + ")");
    }

    void Verify(GameSession s)
    {
        bool ok = true;
        ok &= Expect(s.CompletionTicks == ExpectedCompletionTicks,
            "completionTicks=" + s.CompletionTicks + " want " + ExpectedCompletionTicks);
        ok &= Expect(s.Stars == ExpectedStars, "stars=" + s.Stars + " want " + ExpectedStars);
        ok &= Expect(s.Score == ExpectedScore, "score=" + s.Score + " want " + ExpectedScore);
        ok &= Expect(s.PiecesUsed == 1, "piecesUsed=" + s.PiecesUsed + " want 1");

        // Progress JSON: written with the right values.
        bool fileOk = File.Exists(ProgressStore.SavePath);
        ok &= Expect(fileOk, "progress file missing at " + ProgressStore.SavePath);
        if (fileOk)
        {
            var p = ProgressStore.Load();
            ProgressStore.LevelResult r;
            bool has = p.Levels.TryGetValue(1, out r);
            ok &= Expect(has, "level 1 not in progress");
            if (has)
            {
                ok &= Expect(r.Stars == ExpectedStars, "saved stars=" + r.Stars);
                ok &= Expect(r.Time == ExpectedCompletionTicks, "saved time=" + r.Time);
                ok &= Expect(r.Pieces == 1, "saved pieces=" + r.Pieces);
                ok &= Expect(r.Score == ExpectedScore, "saved score=" + r.Score);
            }
            ok &= Expect(p.IsUnlocked(1), "level 1 not unlocked");
            ok &= Expect(p.IsUnlocked(2), "level 2 not unlocked after win");
            ok &= Expect(!p.IsUnlocked(3), "level 3 unlocked without winning 2");
            ok &= Expect(p.TotalStars() == ExpectedStars, "totalStars=" + p.TotalStars());
        }
        ok &= Expect(JoyveyorBridge.jv_check_invariants(runner.World) == 1, "invariants broken");

        File.Delete(GameTest.Marker);
        Debug.Log("[GameTest] " + (ok ? "PASS" : "FAIL")
            + "  ticks=" + s.CompletionTicks + " stars=" + s.Stars + " score=" + s.Score
            + " pieces=" + s.PiecesUsed);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Expect(bool cond, string what)
    {
        if (!cond) Debug.LogError("[GameTest] " + what);
        return cond;
    }

    void Fail(string what)
    {
        Debug.LogError("[GameTest] FAIL: " + what);
        try { File.Delete(GameTest.Marker); } catch (System.Exception) { }
        EditorApplication.Exit(1);
    }
}
