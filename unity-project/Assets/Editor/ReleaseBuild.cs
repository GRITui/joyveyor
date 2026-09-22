using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// JoyVeyor v1.0 — S9 release packaging: Player Settings + macOS Standalone build.
// Invoked headless: -executeMethod ReleaseBuild.Run
public static class ReleaseBuild
{
    [MenuItem("Joyveyor/Release Build (macOS)")]
    public static void Run()
    {
        // ---- Player Settings (card: product name, company, icon, bundle id) ----
        PlayerSettings.productName = "JoyVeyor";
        PlayerSettings.companyName = "GRITui";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.gritui.joyveyor");

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/icon_placeholder.png");
        if (icon != null)
        {
            PlayerSettings.SetIconsForTargetGroup(
                BuildTargetGroup.Standalone, new[] { icon }, UnityEditor.IconKind.Application);
            Debug.Log("[ReleaseBuild] icon set: Assets/Art/icon_placeholder.png");
        }
        else
        {
            Debug.LogError("[ReleaseBuild] icon not found: Assets/Art/icon_placeholder.png");
        }

        // ---- Build ----
        // dataPath = <repo>/unity-project/Assets  ->  repo root is two levels up.
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string outDir = Path.Combine(repoRoot, "dist");
        string outPath = Path.Combine(outDir, "JoyVeyor.app");
        Directory.CreateDirectory(outDir);

        var scenePaths = new[] { "Assets/Scenes/Sandbox.unity" };
        EditorBuildSettings.scenes = scenePaths
            .Select(p => new EditorBuildSettingsScene(p, true))
            .ToArray();

        var opts = new BuildPlayerOptions
        {
            scenes = scenePaths,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
            locationPathName = outPath,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log("[ReleaseBuild] result=" + s.result
            + " size=" + s.totalSize.ToString("N0") + "B"
            + " errors=" + s.totalErrors);
        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[ReleaseBuild] BUILD FAILED: " + s.result
                + " totalErrors=" + s.totalErrors
                + " totalWarnings=" + s.totalWarnings);
            EditorApplication.Exit(1);
        }
        Debug.Log("[ReleaseBuild] BUILD OK -> " + outPath);
        EditorApplication.Exit(0);
    }
}
