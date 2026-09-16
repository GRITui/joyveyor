using UnityEditor;
using UnityEngine;

// Headless play test. Scene is loaded via the -scene CLI flag. We poll the
// editor update loop, flip into play mode a few frames in (NOT synchronously
// in executeMethod, which deadlocks batchmode), and a watchdog force-exits
// if the game loop never runs. The PlayTestProbe component (added on
// entering play mode) exits the editor after 300 sim ticks.
public static class PlayTest
{
    static int frames;
    static bool entered;

    [MenuItem("Joyveyor/Headless Play Test")]
    static void Run()
    {
        frames = 0;
        entered = false;
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
            entered = true;
            var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
            if (sim == null)
            {
                Debug.LogError("[PlayTest] no JoyveyorRunner in scene");
                EditorApplication.Exit(1);
                return;
            }
            if (sim.GetComponent<PlayTestProbe>() == null)
                sim.gameObject.AddComponent<PlayTestProbe>();
        }
        // Watchdog: 1200 update frames (~20s at 60fps) with no probe exit.
        if (frames > 1200)
        {
            Debug.LogError("[PlayTest] watchdog: game loop did not reach 300 ticks");
            EditorApplication.Exit(2);
        }
    }
}

// Added to the Sim object when play mode starts. Exits the editor after
// 300 sim ticks (~10s) with a PASS/FAIL verdict.
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
        if (ticks >= 300)  // 300 ticks @30Hz = 10s of sim
        {
            bool ok = runner != null && runner.delivered > 0
                && JoyveyorBridge.jv_check_invariants(runner.World) == 1;
            Debug.Log("[PlayTest] ticks=" + ticks
                + (runner != null ? " spawned=" + runner.spawned
                + " delivered=" + runner.delivered
                + " inFlight=" + runner.itemCount : "")
                + " invariants=" + (ok ? "OK" : "FAIL"));
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
