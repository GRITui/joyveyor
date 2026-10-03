using UnityEngine;

// Player boot: the shipped Sandbox scene only holds the sim + editor, so
// without this the player shows an empty window (no GameScreen/MenuFlow).
// Editor probes add the components themselves, so skip when they exist.
// Any failure shows an on-screen message instead of a blank window.
//
// Verification hooks (env vars, unset = no effect for normal players):
//   JV_SHOT=<png>     save a real player frame at frame 120, quit at 150.
//   JV_SHOT_LEVEL=<n> skip the boot/menu screens and go straight into level n's
//                     build phase, so JV_SHOT captures the HUD (hotbar + RUN)
//                     instead of the 3-second boot splash.
public class GameBoot : MonoBehaviour
{
    string error;
    GameScreen screen;
    MenuFlow flow;
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
            var mf = rg.GetComponent<MenuFlow>();
            if (mf == null) mf = rg.AddComponent<MenuFlow>();
            flow = mf;
        }
        catch (System.Exception e)
        {
            error = "Startup failed: " + e.Message;
            Debug.LogError("[GameBoot] " + e);
        }
    }

    // Verification hook: JV_SHOT_LEVEL=<n> jumps straight into level n's build
    // phase so a JV_SHOT capture shows the HUD rather than the boot splash.
    void MaybeJumpToLevel()
    {
        string lv = System.Environment.GetEnvironmentVariable("JV_SHOT_LEVEL");
        if (string.IsNullOrEmpty(lv)) return;
        int level;
        if (!int.TryParse(lv, out level) || level < 1) return;
        // GameScreen.Start already loads level 1 behind the menu chrome, so
        // "session is in Build" is NOT proof the player can see the HUD —
        // MenuFlow.Screen.InGame is. Without this check a menu-only player
        // would skip the jump and capture the menu.
        if (flow != null && flow.Current == MenuFlow.Screen.InGame)
        {
            Debug.Log("[GameBoot] JV_SHOT_LEVEL=" + level + " (already in-game)");
            return;
        }
        try
        {
            Debug.Log("[GameBoot] JV_SHOT_LEVEL=" + level + " -> jumping into build phase");
            if (flow != null) flow.StartLevel(level);
            else if (screen != null) screen.StartLevel(level);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameBoot] JV_SHOT_LEVEL jump failed: " + e.Message);
        }
    }

    void Update()
    {
        // Verification hook: JV_SHOT=<png> saves the real player frame, then quits.
        if (frames == 0 && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("JV_SHOT")))
            Application.runInBackground = true;
        // Let MenuFlow + GameScreen finish Start before steering, then jump
        // into the build phase so the captured frame shows the HUD.
        if (frames == 20 && error == null) MaybeJumpToLevel();
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
