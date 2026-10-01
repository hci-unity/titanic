using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds standalone PCVR variants of the Titanic scene that are LOCKED to a single
// mirror-avatar character (no in-experience character switching).
//
// HOW IT WORKS (non-destructive): for each variant we duplicate GrandStaircase.unity to a
// temporary scene asset, configure that copy so the MirrorCharacterSwitcher holds ONLY the
// target character (which makes the A-button a no-op -> character locked), deactivate the
// other avatars, build from the temp scene, then delete it and re-open the real scene.
// The real GrandStaircase.unity is never modified, so local dev keeps BOTH characters +
// the A-button cycle exactly as before.
//
// Menu: Build > Single Character > Rose Only / Jack Only / Both
public static class SingleCharacterBuilder
{
    const string SourceScene = "Assets/Scenes/GrandStaircase.unity";
    const BuildTarget Target = BuildTarget.StandaloneWindows64;

    // avatar GameObject name in the scene  ->  output .exe path (relative to project root)
    static readonly (string avatar, string output)[] Variants =
    {
        ("RoseV2", "Builds/Rose/Titanic-Rose.exe"),
        ("JackV2", "Builds/Jack/Titanic-Jack.exe"),
    };

    [MenuItem("Build/Single Character/Rose Only")]
    public static void BuildRose() => BuildVariant(Variants[0].avatar, Variants[0].output);

    [MenuItem("Build/Single Character/Jack Only")]
    public static void BuildJack() => BuildVariant(Variants[1].avatar, Variants[1].output);

    [MenuItem("Build/Single Character/Both (Rose + Jack)")]
    public static void BuildBoth()
    {
        var summaries = new List<string>();
        foreach (var v in Variants)
            summaries.Add(BuildVariant(v.avatar, v.output));
        Debug.Log("[SingleCharacterBuilder] DONE:\n" + string.Join("\n", summaries));
    }

    // Builds one single-character variant and returns a one-line summary. Safe to call from
    // a Unity-MCP RunCommand. Always restores the original scene afterward.
    public static string BuildVariant(string avatarName, string outputExe)
    {
        // Capture latest edits so the duplicate reflects what the user sees on disk.
        var open = SceneManager.GetActiveScene();
        if (open.path == SourceScene && open.isDirty)
            EditorSceneManager.SaveScene(open);

        string tempPath = $"Assets/Scenes/_SingleCharBuild_{avatarName}.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(tempPath) != null)
            AssetDatabase.DeleteAsset(tempPath);

        if (!AssetDatabase.CopyAsset(SourceScene, tempPath))
            return $"FAILED {avatarName}: could not copy {SourceScene} -> {tempPath}";

        BuildReport report;
        try
        {
            Scene scene = EditorSceneManager.OpenScene(tempPath, OpenSceneMode.Single);

            var switcher = Object.FindAnyObjectByType<MirrorCharacterSwitcher>(FindObjectsInactive.Include);
            if (switcher == null)
                return $"FAILED {avatarName}: no MirrorCharacterSwitcher in scene";

            // All sibling avatars live as children of the MirrorAvatars object (where the switcher
            // sits) and each carries an AvatarRigDriver. Find them so we can deactivate the others.
            var avatarRoots = switcher.GetComponentsInChildren<AvatarRigDriver>(true)
                                      .Select(d => d.gameObject)
                                      .Distinct()
                                      .ToList();

            GameObject target = avatarRoots.FirstOrDefault(g => g.name == avatarName);
            if (target == null)
                return $"FAILED {avatarName}: avatar not found among [{string.Join(", ", avatarRoots.Select(g => g.name))}]";

            // Lock to the single character: only this one in the switcher list, only this one active.
            switcher.characters = new List<GameObject> { target };
            foreach (var g in avatarRoots)
                g.SetActive(g == target);

            EditorUtility.SetDirty(switcher);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { tempPath },
                locationPathName = outputExe,
                target = Target,
                options = BuildOptions.None,
            });
        }
        finally
        {
            // Always restore the real dev scene (both characters + A-button cycle) and remove the
            // temp copy, even if a step above failed or threw.
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            CleanupTemp(tempPath);
        }

        var s = report.summary;
        string line = $"{avatarName}: {s.result}  errors={s.totalErrors}  " +
                      $"size={s.totalSize / (1024 * 1024)}MB  -> {s.outputPath}";
        Debug.Log("[SingleCharacterBuilder] " + line);
        return line;
    }

    static void CleanupTemp(string tempPath)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(tempPath) != null)
            AssetDatabase.DeleteAsset(tempPath);
    }
}
