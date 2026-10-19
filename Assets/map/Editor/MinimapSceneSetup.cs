using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EnemySpawnTestシーンにミニマップ（MinimapController）を配置するエディタメニュー。
// シーンファイルはスクリプトの.metaに記録されたGUIDでコンポーネントを参照するため、
// .metaをUnityに生成させたうえで、Unity自身にシーンへ追加・保存させる目的で用意している。
public static class MinimapSceneSetup
{
    private const string TargetScenePath = "Assets/GenerateEnemySystem/Scenes/EnemySpawnTest.unity";
    private const string MinimapObjectName = "Minimap";

    [MenuItem("Tools/Map/EnemySpawnTestにミニマップを配置")]
    private static void AddMinimapToEnemySpawnTest()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

        if (Object.FindFirstObjectByType<MinimapController>() != null)
        {
            EditorUtility.DisplayDialog("ミニマップ", "このシーンには既にMinimapControllerが配置されています。", "OK");
            return;
        }

        GameObject minimapObject = new GameObject(MinimapObjectName);
        minimapObject.AddComponent<MinimapController>();
        Undo.RegisterCreatedObjectUndo(minimapObject, "ミニマップを配置");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = minimapObject;

        Debug.Log($"MinimapSceneSetup: {TargetScenePath} に \"{MinimapObjectName}\" を配置して保存しました。");
    }
}
