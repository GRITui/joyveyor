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
                sim.gameObject.AddComponent<CaptureProbe>();
        }
        // wall-clock watchdog: probe needs ~3.2s of sim (160 fixed updates @ 50Hz)
        if (started != null && started.Elapsed > System.TimeSpan.FromSeconds(60))
        { File.Delete(Marker); EditorApplication.Exit(2); }
    }
}

public class CaptureProbe : MonoBehaviour
{
    JoyveyorRunner runner;
    int ticks;
    bool framed;

    void Start() { runner = GetComponent<JoyveyorRunner>(); ticks = 0; }

    void FixedUpdate()
    {
        ticks++;
        // frame the camera once a few items are in flight
        if (!framed && ticks >= 40) { FrameCamera(); framed = true; }
        // let items spread, then shoot
        if (framed && ticks >= 160) { SaveShot(); File.Delete(CaptureHero.Marker); EditorApplication.Exit(0); }
        if (ticks > 500) { File.Delete(CaptureHero.Marker); EditorApplication.Exit(3); }
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
        File.WriteAllBytes(CaptureHero.OutPath, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[CaptureHero] saved " + CaptureHero.OutPath +
                  " items=" + (runner ? runner.itemCount : 0) +
                  " spawned=" + (runner ? runner.spawned : 0) +
                  " delivered=" + (runner ? runner.delivered : 0));
    }
}
