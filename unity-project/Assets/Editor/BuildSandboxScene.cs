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

        EditorSceneManager.SaveScene(scene, Application.dataPath + "/Scenes/Sandbox.unity");

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Application.dataPath + "/Scenes/Sandbox.unity", true) };

        Debug.Log("[SandboxScene] saved");
        EditorApplication.Exit(0);
    }
}
