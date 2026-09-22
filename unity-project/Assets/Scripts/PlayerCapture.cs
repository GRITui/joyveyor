using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// Player-side hero capture. Runs only in a standalone player build (never in
// the editor). The player renders the game to a real window on the Metal GPU,
// so this is the only environment where geometry actually renders.
//
// macOS App Nap throttles the display link that drives the game loop when the
// window is backgrounded/occluded, so we disable App Nap and force the app
// frontmost via the native JVForceActive helper.
//
// Strategy:
//  - Force the app active (native) so the display link runs.
//  - Wait for the demo to run (items in flight + sink deliveries so the fill
//    bar is visible), frame the camera on the demo.
//  - Point the main camera at a 1600x900 RenderTexture and let the NORMAL
//    render loop fill it for several frames (avoids the threaded-Metal
//    cam.Render() async race that left a clear-only frame).
//  - WaitForEndOfFrame so the GPU has finished, then ReadPixels -> PNG.
//  - Also call ScreenCapture.CaptureScreenshot as a backup.
//  - Write a state file every second so the run can be monitored from the
//    terminal without relying on log flushing.
public class PlayerCapture : MonoBehaviour
{
    [DllImport("JVForceActive")]
    static extern void JV_ForceActive();
    public const string OutPath   = "/tmp/joyveyor_hero.png";
    public const string ScPath    = "/tmp/joyveyor_hero_sc.png";
    public const string StatePath = "/tmp/jv_player_state.txt";

    JoyveyorRunner runner;
    bool framed;
    bool started;
    bool done;
    float t0;
    float lastState;
    float lastForce;

    void Awake()
    {
        if (Application.isEditor) { enabled = false; return; }
        t0 = Time.time;
        lastState = 0f;
        // Keep the game loop running even when the window loses focus (the
        // default pauses Update/FixedUpdate when another app is frontmost).
        Application.runInBackground = true;
        try { JV_ForceActive(); Debug.Log("[PlayerCapture] JV_ForceActive OK"); }
        catch (System.Exception e) { Debug.Log("[PlayerCapture] JV_ForceActive err: " + e.Message); }
        Debug.Log("[PlayerCapture] start atlas=" + (JVArt.Atlas != null ? "LOADED" : "NULL")
            + " mainCam=" + (Camera.main != null)
            + " runInBackground=" + Application.runInBackground);
    }

    void Update()
    {
        if (Application.isEditor) return;
        if (!done && Time.time - lastForce > 2f)
        {
            lastForce = Time.time;
            try { JV_ForceActive(); } catch (System.Exception) { }
        }
        if (runner == null) runner = FindAnyObjectByType<JoyveyorRunner>();
        if (!framed && runner != null && runner.itemCount >= 3) { FrameCamera(); framed = true; }
        if (!started && !done && runner != null && framed && (runner.delivered >= 3 || Time.time - t0 > 40f))
        {
            started = true;
            StartCoroutine(Capture());
        }
        if (Time.time - lastState > 1f) { lastState = Time.time; WriteState(); }
    }

    void WriteState()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("t=").Append((Time.time - t0).ToString("F1"));
            sb.Append(" runner=").Append(runner != null ? "OK" : "NULL");
            if (runner != null)
            {
                sb.Append(" items=").Append(runner.itemCount);
                sb.Append(" spawned=").Append(runner.spawned);
                sb.Append(" delivered=").Append(runner.delivered);
                sb.Append(" tick=").Append(runner.tickCount);
            }
            sb.Append(" framed=").Append(framed);
            sb.Append(" started=").Append(started);
            sb.Append(" done=").Append(done);
            sb.Append(" mainCam=").Append(Camera.main != null);
            sb.Append(" frame=").Append(Time.frameCount);
            sb.Append(" realT=").Append(Time.realtimeSinceStartup.ToString("F1"));
            File.WriteAllText(StatePath, sb.ToString());
        }
        catch (System.Exception e) { Debug.Log("[PlayerCapture] state write err: " + e.Message); }
    }

    System.Collections.IEnumerator Capture()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.Log("[PlayerCapture] no main cam, quit"); Application.Quit(); yield break; }
        Debug.Log("[PlayerCapture] capture: pointing camera at RT");

        // --- Diagnostic 1: does the atlas texture actually contain the baked
        // pixel art at runtime? Sample known atlas-pixel locations. ---
        DumpAtlas();

        // --- Diagnostic 2: known-good red quad at frame center. If it renders,
        // the render pass works and the atlas sprites are the problem. If it
        // does NOT render, the geometry pass itself is being skipped. ---
        var redTex = new Texture2D(2, 2);
        var rp = new Color[4];
        for (int i = 0; i < 4; i++) rp[i] = Color.red;
        redTex.SetPixels(rp);
        redTex.Apply();
        var redSpr = Sprite.Create(redTex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 1f); // 1 ppu -> 2x2 UNITS
        var quad = new GameObject("redquad_diag");
        var rsr = quad.AddComponent<SpriteRenderer>();
        rsr.sprite = redSpr;
        rsr.transform.position = new Vector3(5.5f, -2.5f, 0.5f); // frame center, in front of cam
        Debug.Log("[PlayerCapture] DIAG cam near=" + cam.nearClipPlane + " far=" + cam.farClipPlane
            + " redQuad at (5.5,-2.5,0.5) size=2x2 units");

        // --- Diagnostic 3: FULLY ISOLATED fresh camera + fresh red quad.
        // Independent of the scene camera and scene sprites. If THIS renders,
        // the scene camera is the problem. If it does NOT, the geometry pass
        // is globally broken in this environment. ---
        var camGO = new GameObject("diag_cam");
        var dcam = camGO.AddComponent<Camera>();
        dcam.orthographic = true;
        dcam.orthographicSize = 5f;
        dcam.nearClipPlane = 0.3f;
        dcam.farClipPlane = 100f;
        dcam.cullingMask = ~0;
        dcam.clearFlags = CameraClearFlags.SolidColor;
        dcam.backgroundColor = new Color(0f, 0f, 0f);
        dcam.transform.position = new Vector3(0f, 0f, 10f);
        dcam.transform.rotation = Quaternion.identity;
        dcam.depth = 99;
        var qGO = new GameObject("diag_quad");
        var qsr = qGO.AddComponent<SpriteRenderer>();
        qsr.sprite = redSpr;
        qsr.transform.position = new Vector3(0f, 0f, 0f);
        var rt2 = RenderTexture.GetTemporary(800, 450, 24);
        dcam.targetTexture = rt2;
        for (int i = 0; i < 8; i++) yield return null;
        yield return new WaitForEndOfFrame();
        RenderTexture.active = rt2;
        var tex2 = new Texture2D(800, 450, TextureFormat.RGB24, false);
        tex2.ReadPixels(new Rect(0, 0, 800, 450), 0, 0);
        tex2.Apply();
        var cc = tex2.GetPixel(400, 225);
        Debug.Log("[PlayerCapture] DIAG FRESHCAM center=" + cc.r.ToString("F2") + "," + cc.g.ToString("F2") + "," + cc.b.ToString("F2")
            + " (red=1,0,0 if geometry pass works) gfxDevice=" + SystemInfo.graphicsDeviceType
            + " sceneCamEnabled=" + cam.enabled + " camCount=" + FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
        Object.Destroy(camGO);
        Object.Destroy(qGO);
        dcam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt2);

        var rt = RenderTexture.GetTemporary(1600, 900, 24);
        cam.targetTexture = rt;
        // let the normal render loop fill the RT for several frames
        for (int i = 0; i < 8; i++) yield return null;
        // ensure the GPU has finished the last frame
        yield return new WaitForEndOfFrame();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        var c = tex.GetPixel(800, 450);
        Debug.Log("[PlayerCapture] DIAG center=" + c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2")
            + " (red quad expected ~1,0,0 if render pass works)");
        DumpScene(tex);
        File.WriteAllBytes(OutPath, tex.EncodeToPNG());
        Debug.Log("[PlayerCapture] saved " + OutPath + " exists=" + File.Exists(OutPath)
            + " items=" + runner.itemCount + " delivered=" + runner.delivered);
        Object.Destroy(quad);
        cam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        try { ScreenCapture.CaptureScreenshot(ScPath); Debug.Log("[PlayerCapture] ScreenCapture called -> " + ScPath); }
        catch (System.Exception e) { Debug.Log("[PlayerCapture] ScreenCapture err: " + e.Message); }
        done = true;
        WriteState();
        yield return new WaitForSeconds(2f); // let logs flush before quit
        Application.Quit();
    }

    void DumpAtlas()
    {
        var t = JVArt.Atlas;
        if (t == null) { Debug.Log("[PlayerCapture] DIAG atlas=NULL"); return; }
        Debug.Log("[PlayerCapture] DIAG atlas size=" + t.width + "x" + t.height
            + " filter=" + t.filterMode + " mip=" + t.mipmapCount
            + " isReadable=" + t.isReadable + " format=" + t.format);
        // Sample known atlas-pixel locations (atlas is 256x256):
        //  belt strip center  ~ (16,16)   should be belt color (gray/blue)
        //  source idle center ~ (144,16)  should be green
        //  sink center        ~ (208,16)  should be blue
        //  crate center       ~ (40,40)   should be yellow/brown
        //  top-left corner    ~ (0,0)     belt start
        var samples = new System.Collections.Generic.List<string>();
        samples.Add("belt(16,16)=" + Pix(t, 16, 16));
        samples.Add("src(144,16)=" + Pix(t, 144, 16));
        samples.Add("sink(208,16)=" + Pix(t, 208, 16));
        samples.Add("crate(40,40)=" + Pix(t, 40, 40));
        samples.Add("corner(0,0)=" + Pix(t, 0, 0));
        samples.Add("mid(128,128)=" + Pix(t, 128, 128));
        Debug.Log("[PlayerCapture] DIAG atlaspx " + string.Join(" ", samples.ToArray()));
    }

    string Pix(Texture2D t, int x, int y)
    {
        if (!t.isReadable) return "UNREADABLE";
        try { var c = t.GetPixel(x, y); return c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2") + "a" + c.a.ToString("F2"); }
        catch (System.Exception e) { return "ERR" + e.Message; }
    }

    void FrameCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = 4.2f;
        cam.transform.position = new Vector3(5.5f, -2.5f, 10f);
        cam.transform.rotation = Quaternion.identity;
        cam.backgroundColor = new Color(0.07f, 0.065f, 0.06f);
    }

    void DumpScene(Texture2D tex)
    {
        var cam = Camera.main;
        var srs = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        int nonNull = 0;
        foreach (var sr in srs) if (sr.sprite != null) nonNull++;
        Debug.Log("[PlayerCapture] DIAG atlas=" + (JVArt.Atlas != null ? "LOADED" : "NULL")
            + " SpriteRenderers=" + srs.Length + " withSprite=" + nonNull
            + " camCull=" + cam.cullingMask
            + " camPos=" + cam.transform.position
            + " ortho=" + cam.orthographic + " size=" + cam.orthographicSize
            + " camZ=" + cam.transform.position.z);
        // log first 6 sprite world positions + layers to verify frustum
        int logged = 0;
        foreach (var sr in srs)
        {
            if (sr.sprite == null) continue;
            var wp = sr.transform.position;
            bool inFront = wp.z < cam.transform.position.z;
            Debug.Log("[PlayerCapture] DIAG spr " + logged + " name=" + sr.name
                + " pos=(" + wp.x.ToString("F2") + "," + wp.y.ToString("F2") + "," + wp.z.ToString("F2") + ")"
                + " layer=" + sr.gameObject.layer
                + " inFront=" + inFront
                + " enabled=" + sr.enabled);
            logged++;
            if (logged >= 6) break;
        }
        var c0 = tex.GetPixel(800, 450);
        var c1 = tex.GetPixel(210, 182);
        var c2 = tex.GetPixel(1389, 717);
        Debug.Log("[PlayerCapture] DIAG center=" + c0.r.ToString("F2") + "," + c0.g.ToString("F2") + "," + c0.b.ToString("F2")
            + " source=" + c1.r.ToString("F2") + "," + c1.g.ToString("F2") + "," + c1.b.ToString("F2")
            + " sink=" + c2.r.ToString("F2") + "," + c2.g.ToString("F2") + "," + c2.b.ToString("F2"));
    }
}
