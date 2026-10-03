using UnityEngine;

// Player boot: the shipped Sandbox scene only holds the sim + editor, so
// without this the player shows an empty window (no GameScreen/MenuFlow).
// Editor probes add the components themselves, so skip when they exist.
// Any failure shows an on-screen message instead of a blank window.
public class GameBoot : MonoBehaviour
{
    string error;
    GameScreen screen;
    int frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Application.isEditor) return;
        var go = new GameObject("GameBoot");
        DontDestroyOnLoad(go);
        go.AddComponent<GameBoot>().Run();
    }

    void Run()
    {
        try
        {
            LevelDef probe;
            if (!LevelDef.Load(1, out probe))
            { error = "Level files missing or unreadable:\n" + LevelDef.LevelsDir(); return; }
            var runner = FindObjectOfType<JoyveyorRunner>();
            if (runner == null) { error = "First scene failed to load: no JoyveyorRunner in scene."; return; }
            var rg = runner.gameObject;
            if (rg.GetComponent<GameSession>() == null) rg.AddComponent<GameSession>();
            screen = rg.GetComponent<GameScreen>() ?? rg.AddComponent<GameScreen>();
            if (rg.GetComponent<MenuFlow>() == null) rg.AddComponent<MenuFlow>();
        }
        catch (System.Exception e)
        {
            error = "Startup failed: " + e.Message;
            Debug.LogError("[GameBoot] " + e);
        }
    }

    void Update()
    {
        // Verification hook: JV_SHOT=<png> saves the real player frame, then quits.
        if (frames == 0 && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("JV_SHOT")))
            Application.runInBackground = true;
        if (frames == 120)
        {
            var shot = System.Environment.GetEnvironmentVariable("JV_SHOT");
            if (!string.IsNullOrEmpty(shot)) { ScreenCapture.CaptureScreenshot(shot); }
        }
        if (frames == 150 && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("JV_SHOT")))
            Application.Quit();
        if (frames <= 150 && error != null) frames++;
        if (error != null || ++frames != 5) return;
        if (screen == null || screen.hudCanvas == null || screen.session == null)
            error = "Game failed to initialise (HUD/session missing). See Player.log.";
    }

    void OnGUI()
    {
        if (error == null) return;
        var bg = GUI.color; GUI.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = bg;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        st.normal.textColor = new Color(1f, 0.45f, 0.4f);
        GUI.Label(new Rect(40, 0, Screen.width - 80, Screen.height),
            "JoyVeyor couldn't start\n\n" + error + "\n\nLog: ~/Library/Logs/GRITui/JoyVeyor/Player.log", st);
    }
}
