using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Sprint 6: one-shot editor setup. Bakes the pixel-art atlas, then wires the
// Sandbox scene to use it (itemScale 0.2 -> 0.4, add an AtlasHolder referencing
// the baked atlas so the standalone player build includes it).
//
// Run headlessly:
//   Unity -batchmode -nographics -quit -projectPath unity-project \
//     -executeMethod S6Setup.BakeAndSetup -logFile /tmp/jv_s6setup.log
//
// Idempotent: re-running reuses an existing AtlasHolder and only touches
// itemScale when it differs from 0.4.
public static class S6Setup
{
    [MenuItem("Joyveyor/S6 Bake Atlas + Setup Scene")]
    public static void BakeAndSetup()
    {
        // 1. Bake the 256x256 atlas -> Assets/Art/atlas.png (+ .meta).
        SpriteBaker.BakeAll();
        AssetDatabase.Refresh();

        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteBaker.OutPath);
        if (atlas == null)
        {
            Debug.LogError("[S6Setup] atlas.png not found at " + SpriteBaker.OutPath);
            EditorApplication.Exit(1);
            return;
        }

        // 2. Wire the scene.
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Sandbox.unity", OpenSceneMode.Single);
        var sim = Object.FindAnyObjectByType<JoyveyorRunner>();
        if (sim == null)
        {
            Debug.LogError("[S6Setup] no JoyveyorRunner in scene");
            EditorApplication.Exit(1);
            return;
        }

        // itemScale 0.2 -> 0.4 (spec §3: items render at 0.4 cell ≈ 24 px).
        var ir = sim.GetComponent<ItemRenderer>();
        if (ir != null && Mathf.Abs(ir.itemScale - 0.4f) > 1e-4f)
        {
            ir.itemScale = 0.4f;
            Debug.Log("[S6Setup] itemScale -> 0.4");
        }

        // AtlasHolder referencing the baked atlas (scene reference is what
        // makes the build pipeline include the atlas — it is referenced from
        // code only, not from Resources/).
        var holder = sim.GetComponent<AtlasHolder>();
        if (holder == null) holder = sim.gameObject.AddComponent<AtlasHolder>();
        holder.atlas = atlas;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[S6Setup] scene wired: itemScale=" + (ir != null ? ir.itemScale.ToString() : "?")
            + ", AtlasHolder.atlas=" + atlas.name);
    }
}
