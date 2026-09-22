using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot: builds the sandbox scene (camera + sim object), saves it, quits.
public static class BuildSandboxScene
{
    [MenuItem("Joyveyor/Build Sandbox Scene")]
    static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 9f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.09f, 0.11f);
        cam.transform.position = new Vector3(12f, -8f, -10f);
        cam.transform.rotation = Quaternion.identity;

        var sim = new GameObject("Sim");
        sim.AddComponent<JoyveyorRunner>();
        sim.AddComponent<ItemRenderer>();
        var ed = sim.AddComponent<GridEditor>();
        ed.cellSize = 1f;
        ed.sinkCapacity = 50;

        // Sprint 8 audio: runtime player over the baked WAVs.
        var au = sim.AddComponent<JVAudio>();
        au.place = LoadClip("place");
        au.deleteSfx = LoadClip("delete");
        au.invalid = LoadClip("invalid");
        au.beltHum = LoadClip("belt_hum");
        au.delivery = LoadClip("delivery");
        au.sinkFull = LoadClip("sink_full");
        au.deadlockAlarm = LoadClip("deadlock_alarm");
        au.uiClick = LoadClip("ui_click");
        au.levelComplete = LoadClip("level_complete");
        au.levelFail = LoadClip("level_fail");
        au.countdown = LoadClip("countdown");
        au.countdownFinal = LoadClip("countdown_final");
        au.ambient = LoadClip("ambient");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Sandbox.unity");

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Sandbox.unity", true) };

        Debug.Log("[SandboxScene] saved");
        EditorApplication.Exit(0);
    }

    static AudioClip LoadClip(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/" + name + ".wav");
        if (clip == null)
            Debug.LogError("[SandboxScene] missing baked clip: " + name + ".wav (run Joyveyor/Bake Audio)");
        return clip;
    }
}
