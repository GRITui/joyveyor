using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless play test. Scene is loaded via the -scene CLI flag. We poll the
// editor update loop, flip into play mode a few frames in (NOT synchronously
// in executeMethod, which deadlocks batchmode), and a watchdog force-exits
// if the game loop never runs. The PlayTestProbe component (added on
// entering play mode) exits the editor after 300 sim ticks.
//
// The play-mode transition does a domain reload, which wipes static state
// AND unregisters EditorApplication.update callbacks. So Run() drops a
// marker file and [InitializeOnLoadMethod] re-registers Poll after the
// reload; the probe deletes the marker on exit.
[InitializeOnLoad]
public static class PlayTest
{
    public const string Marker = "/tmp/jv_playtest_active";
    static int frames;
    static bool entered;
    static System.Diagnostics.Stopwatch started;

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        if (!File.Exists(Marker)) return;
        if (started == null) started = System.Diagnostics.Stopwatch.StartNew();  // reload wipes statics
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    [MenuItem("Joyveyor/Headless Play Test")]
    static void Run()
    {
        // -scene is not a real batchmode flag; open the sandbox scene ourselves.
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Single);
        File.WriteAllText(Marker, "");
        frames = 0;
        entered = false;
        started = System.Diagnostics.Stopwatch.StartNew();
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        frames++;
        if (frames == 10 && !EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = true;
            return;
        }
        if (EditorApplication.isPlaying && !entered)
        {
            // The play-mode domain reload can fire the first Poll before the
            // scene's objects are instantiated, so retry across frames instead
            // of failing on the first miss.
            var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
            if (sim != null)
            {
                entered = true;
                if (sim.GetComponent<PlayTestProbe>() == null)
                    sim.gameObject.AddComponent<PlayTestProbe>();
            }
            else if (frames > 40)
            {
                Debug.LogError("[PlayTest] no JoyveyorRunner in scene after " + frames + " frames");
                File.Delete(Marker);
                EditorApplication.Exit(1);
            }
        }
        // Watchdog: 90s wall-clock with no probe exit (probe needs ~15s of
        // sim time; batchmode frame rate is unbounded, so wall-clock is the
        // only reliable cap).
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(90))
        {
            Debug.LogError("[PlayTest] watchdog: game loop did not reach 300 ticks");
            File.Delete(Marker);
            EditorApplication.Exit(2);
        }
    }
}

// Added to the Sim object when play mode starts. Exits the editor after
// 600 sim ticks (~12s of sim at the 0.02s fixed timestep) with a PASS/FAIL
// verdict. The demo level's first delivery lands at ~9.1s (tick ~456), so the
// window must clear that to observe delivered>0.
public class PlayTestProbe : MonoBehaviour
{
    JoyveyorRunner runner;
    int ticks;

    void Start()
    {
        runner = GetComponent<JoyveyorRunner>();
        ticks = 0;
    }

    void FixedUpdate()
    {
        ticks++;
        if (ticks >= 600)  // 600 fixed updates = 12s of sim
        {
            bool ok = runner != null && runner.delivered > 0
                && JoyveyorBridge.jv_check_invariants(runner.World) == 1;
            Debug.Log("[PlayTest] ticks=" + ticks
                + (runner != null ? " spawned=" + runner.spawned
                + " delivered=" + runner.delivered
                + " inFlight=" + runner.itemCount : "")
                + " invariants=" + (ok ? "OK" : "FAIL"));
            File.Delete(PlayTest.Marker);
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
