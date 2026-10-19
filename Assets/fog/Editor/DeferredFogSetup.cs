using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Deferred Fogの導入作業をメニュー1回で行うためのセットアップ。
// .metaやアセット内の参照GUIDを手書きせず、Unity側にアセットを生成させるためにエディタースクリプトにしている。
//   1. EnemySpawnTestシーンをAssets/fog/FogTest.unityに複製
//   2. 複製シーンのGlobal Volumeが使っているProfileを複製し、DeferredFogを追加して差し替え
//      （元のProfileは他シーンと共有しているので触らない）
//   3. プロジェクト内の全UniversalRendererDataにDeferredFogRendererFeatureを登録
public static class DeferredFogSetup
{
    const string SourceScenePath = "Assets/GenerateEnemySystem/Scenes/EnemySpawnTest.unity";
    const string FogFolder = "Assets/fog";
    const string FogScenePath = FogFolder + "/FogTest.unity";
    const string FogProfilePath = FogFolder + "/FogVolumeProfile.asset";

    [MenuItem("Tools/Fog/Deferred Fog セットアップ（FogTestシーン作成）")]
    static void Setup()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        AddFeatureToAllRenderers();

        if (!File.Exists(FogScenePath))
        {
            if (!AssetDatabase.CopyAsset(SourceScenePath, FogScenePath))
            {
                Debug.LogError($"[DeferredFog] シーンの複製に失敗しました: {SourceScenePath}");
                return;
            }
        }

        var scene = EditorSceneManager.OpenScene(FogScenePath, OpenSceneMode.Single);

        var volume = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).FirstOrDefault(v => v.isGlobal);
        if (volume == null)
        {
            var go = new GameObject("Global Volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }

        volume.sharedProfile = GetOrCreateFogProfile(volume.sharedProfile);
        EditorUtility.SetDirty(volume);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Selection.activeObject = volume.sharedProfile;
        Debug.Log($"[DeferredFog] セットアップ完了: {FogScenePath} / {FogProfilePath}");
    }

    static VolumeProfile GetOrCreateFogProfile(VolumeProfile baseProfile)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(FogProfilePath);
        if (profile == null)
        {
            var basePath = baseProfile != null ? AssetDatabase.GetAssetPath(baseProfile) : null;
            if (!string.IsNullOrEmpty(basePath) && AssetDatabase.CopyAsset(basePath, FogProfilePath))
            {
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(FogProfilePath);
            }
            else
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, FogProfilePath);
            }
        }

        if (!profile.TryGet<DeferredFog>(out var fog))
        {
            fog = profile.Add<DeferredFog>(false);
            fog.name = nameof(DeferredFog);
            AssetDatabase.AddObjectToAsset(fog, profile);

            // 画像の街並み（far clip 1000m、道路幅の直線で数百m先まで見通せる）に合わせた初期値
            fog.intensity.Override(1f);
            fog.color.Override(new Color(0.72f, 0.80f, 0.90f));
            fog.density.Override(0.006f);
            fog.startDistance.Override(30f);
            fog.baseHeight.Override(0f);
            fog.heightFalloff.Override(0.015f);
            fog.skyboxFog.Override(1f);
            fog.skyboxFogHeight.Override(0.3f);

            EditorUtility.SetDirty(fog);
            EditorUtility.SetDirty(profile);
        }

        return profile;
    }

    static void AddFeatureToAllRenderers()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (rendererData == null || !path.StartsWith("Assets/"))
                continue;
            if (rendererData.rendererFeatures.Any(f => f is DeferredFogRendererFeature))
                continue;

            var feature = ScriptableObject.CreateInstance<DeferredFogRendererFeature>();
            feature.name = "DeferredFog";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            // URPのRenderer Data Inspectorで「Add Renderer Feature」した時と同じく、
            // 参照リストとlocalIdのマップを両方伸ばす（片方だけだと起動時の検証で外される）
            var so = new SerializedObject(rendererData);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            Debug.Log($"[DeferredFog] Renderer Featureを追加: {path}");
        }
        AssetDatabase.SaveAssets();
    }
}
