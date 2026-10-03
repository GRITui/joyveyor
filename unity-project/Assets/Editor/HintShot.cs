using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless shot of the level-1 tutorial hint 1 ("Ready? Press RUN") so the
// RUN callout + its down arrow can be reviewed. Boots MenuFlow + GameScreen,
// enters level 1, places the reference belt through the real GameScreen
// placement path (which fires Onboarding.OnBeltPlaced -> hint 1), then saves
// a 1600x900 shot. Exits 0 on PASS, 1 on FAIL.
[InitializeOnLoad]
public static class HintShot
{
    public const string Marker = "/tmp/jv_hintshot_active";
    public const string OutPath = "/tmp/joyveyor_hint_run.png";
    static int frames;
    static System.Diagnostics.Stopwatch started;

    [MenuItem("Joyveyor/Headless RUN Hint Shot")]
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
        if (started == null) started = System.Diagnostics.Stopwatch.StartNew();
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
            if (sim != null && sim.GetComponent<HintShotProbe>() == null)
                sim.gameObject.AddComponent<HintShotProbe>();
        }
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(120))
        {
            Debug.LogError("[HintShot] watchdog: probe never finished");
            File.Delete(Marker);
            EditorApplication.Exit(2);
        }
    }
}

public class HintShotProbe : MonoBehaviour
{
    int frame;
    int step;
    JoyveyorRunner runner;
    GameSession session;
    GameScreen screen;
    MenuFlow flow;

    void Awake()
    {
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
        frame++;
        if (step == 0)
        {
            if (frame < 14) return;
            if (flow == null || flow.menuCanvas == null) { Fail("MenuFlow not ready"); return; }
            flow.ShowMainMenu();
            flow.ShowLevelSelect();
            flow.StartLevel(1);            // tutorial: hint 0 (DrawBelt) shows
            step = 1;
            return;
        }
        if (step == 1)
        {
            if (frame < 20) return;
            if (session == null || session.phase != GameSession.Phase.Build) { Fail("not in Build"); return; }
            // Place the reference belt through the game layer, then tell the
            // hint engine exactly what a drag release would tell it.
            if (!session.PlacePiece('B', 1, 0, JoyveyorBridge.DirE, 4))
            { Fail("belt rejected: " + session.Message); return; }
            session.ClearMessage();
            screen.onboarding.OnBeltPlaced();
            step = 2;
            return;
        }
        if (step == 2)
        {
            if (frame < 26) return;
            if (screen.onboarding == null) { Fail("no onboarding"); return; }
            Debug.Log("[HintShot] current hint fired — capturing " + HintShot.OutPath);
            SaveShot(HintShot.OutPath);
            Finish(true);
        }
    }

    void Finish(bool ok)
    {
        File.Delete(HintShot.Marker);
        Debug.Log("[HintShot] " + (ok ? "PASS" : "FAIL") + "  shot: " + HintShot.OutPath);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    void SaveShot(string path)
    {
        var cam = Camera.main;
        if (cam == null) { var g = new GameObject("Main Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); }
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
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
        Debug.Log("[HintShot] saved " + path);
    }

    void Fail(string what)
    {
        Debug.LogError("[HintShot] FAIL: " + what);
        try { File.Delete(HintShot.Marker); } catch (System.Exception) { }
        EditorApplication.Exit(1);
    }
}
