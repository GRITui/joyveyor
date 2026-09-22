using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batchmode hero capture. Reuses the PlayTest marker + domain-reload pattern.
// Enters play mode, lets the demo level's items flow, frames the camera on the
// level, renders to a 1600x900 RenderTexture (resolution-independent of screen),
// saves a PNG, exits.
[InitializeOnLoad]
public static class CaptureHero
{
    public const string Marker  = "/tmp/jv_capture_active";
    public const string OutPath = "/tmp/joyveyor_hero.png";
    static int frames;
    static System.Diagnostics.Stopwatch started;

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        if (!File.Exists(Marker)) return;
        if (started == null) started = System.Diagnostics.Stopwatch.StartNew(); // reload wipes statics
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    [MenuItem("Joyveyor/Capture Hero")]
    static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Single);
        File.WriteAllText(Marker, "");
        frames = 0;
        started = System.Diagnostics.Stopwatch.StartNew();
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        frames++;
        // flip into play mode a few frames in (not synchronously in executeMethod)
        if (frames == 10 && !EditorApplication.isPlaying)
        { EditorApplication.isPlaying = true; return; }
        if (EditorApplication.isPlaying)
        {
            var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
            if (sim != null && sim.GetComponent<CaptureProbe>() == null)
            { sim.gameObject.AddComponent<CaptureProbe>(); Debug.Log("[CaptureHero] probe added"); }
        }
        // wall-clock watchdog: real-GPU editor startup + shader compile is slow; allow 300s
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(300))
        { File.Delete(Marker); EditorApplication.Exit(2); }
    }
}

public class CaptureProbe : MonoBehaviour
{
    JoyveyorRunner runner;
    int ticks;
    bool framed;
    bool capturing;

    void Start() { runner = GetComponent<JoyveyorRunner>(); ticks = 0; Debug.Log("[CaptureHero] probe Start runner=" + (runner != null ? "OK" : "NULL")); }

    void FixedUpdate()
    {
        ticks++;
        if (ticks % 50 == 1) Debug.Log("[CaptureHero] probe tick=" + ticks + " delivered=" + (runner ? runner.delivered : -1));
        // frame the camera once a few items are in flight
        if (!framed && ticks >= 40) { FrameCamera(); framed = true; }
        // shoot once the sink has a delivery (fill bar visible) or at a max tick
        bool deliver = runner != null && runner.delivered >= 1;
        if (framed && (deliver || ticks >= 595) && !capturing)
        {
            capturing = true;
            SaveShot();
        }
        if (ticks > 620 && !capturing) { File.Delete(CaptureHero.Marker); EditorApplication.Exit(3); }
    }

    void FrameCamera()
    {
        var cam = Camera.main;
        if (cam == null) { var g = new GameObject("Main Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); }
        cam.orthographic = true;
        cam.orthographicSize = 4.2f;
        // demo level spans grid x 0..11, y 0..5 -> world (x, -y)
        cam.transform.position = new Vector3(5.5f, -2.5f, 10f);
        cam.transform.rotation = Quaternion.identity;
        cam.backgroundColor = new Color(0.07f, 0.065f, 0.06f);
    }

    // Render to a RenderTexture and read the pixels. Non-threaded Metal
    // (no -nographics) means cam.Render() completes synchronously, so the RT
    // is fully rendered before ReadPixels. Exit at the end.
    void SaveShot()
    {
        var cam = Camera.main;
        var rt = RenderTexture.GetTemporary(1600, 900, 24);
        var prev = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        var cCenter = tex.GetPixel(800, 450);
        Debug.Log("[CaptureHero] DIAG rt center=" + cCenter.r.ToString("F2") + "," + cCenter.g.ToString("F2") + "," + cCenter.b.ToString("F2"));
        DumpScene(cam, tex);
        File.WriteAllBytes(CaptureHero.OutPath, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[CaptureHero] saved " + CaptureHero.OutPath +
                  " items=" + (runner ? runner.itemCount : 0) +
                  " spawned=" + (runner ? runner.spawned : 0) +
                  " delivered=" + (runner ? runner.delivered : 0));
        File.Delete(CaptureHero.Marker);
        EditorApplication.Exit(0);
    }

    // Diagnostic (temporary): dump camera state + sprite count + pixel samples
    // so a black frame can be diagnosed headlessly.
    void DumpScene(Camera cam, Texture2D tex)
    {
        Debug.Log("[CaptureHero] DIAG cam pos=" + cam.transform.position
            + " ortho=" + cam.orthographic + " size=" + cam.orthographicSize
            + " near=" + cam.nearClipPlane + " far=" + cam.farClipPlane
            + " cullingMask=" + cam.cullingMask
            + " bg=" + cam.backgroundColor
            + " atlas=" + (JVArt.Atlas != null ? "LOADED" : "NULL"));
        var srs = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        int nonNull = 0;
        foreach (var sr in srs) if (sr.sprite != null) nonNull++;
        Debug.Log("[CaptureHero] DIAG SpriteRenderers=" + srs.Length + " withSprite=" + nonNull);
        // sample known sprite locations (screen coords for 1600x900):
        // center (5.5,-2.5)->(800,450), source (0,0)->(210,182), sink (11,-5)->(1389,717)
        var c0 = tex.GetPixel(800, 450);
        var c1 = tex.GetPixel(210, 182);
        var c2 = tex.GetPixel(1389, 717);
        Debug.Log("[CaptureHero] DIAG center=" + c0.r.ToString("F2") + "," + c0.g.ToString("F2") + "," + c0.b.ToString("F2")
            + " source=" + c1.r.ToString("F2") + "," + c1.g.ToString("F2") + "," + c1.b.ToString("F2")
            + " sink=" + c2.r.ToString("F2") + "," + c2.g.ToString("F2") + "," + c2.b.ToString("F2"));
    }
}
