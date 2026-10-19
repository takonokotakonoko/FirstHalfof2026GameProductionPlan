using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Volumeから設定するDeferred Fogのパラメータ。
// 描画自体はDeferredFogRendererFeatureが行い、ここは「どれくらい掛けるか」だけを持つ。
// 強さ(intensity)が0のままなら描画パスは積まれないので、Renderer Featureを全シーン共通で
// 登録しておいても、Volumeでこの項目を有効にしたシーンでだけフォグが掛かる。
[Serializable, VolumeComponentMenu("Fog/Deferred Fog")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class DeferredFog : VolumeComponent
{
    [DisplayInfo(name = "強さ（最大不透明度）")]
    [Tooltip("0でフォグ無効。1で遠景が完全にフォグ色になる")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [DisplayInfo(name = "フォグ色")]
    [Tooltip("空の地平線付近の色に寄せると奥のビル群が空に溶け込む")]
    public ColorParameter color = new ColorParameter(new Color(0.72f, 0.80f, 0.90f), false, false, true);

    [DisplayInfo(name = "濃度")]
    [Tooltip("1mあたりの減衰率。大きいほど近くから霞む")]
    public MinFloatParameter density = new MinFloatParameter(0.01f, 0f);

    [DisplayInfo(name = "開始距離 (m)")]
    [Tooltip("カメラからこの距離まではフォグを掛けない")]
    public MinFloatParameter startDistance = new MinFloatParameter(20f, 0f);

    [DisplayInfo(name = "基準高さ (m)")]
    [Tooltip("この高さで濃度が「濃度」の値になる。上に行くほど薄くなる")]
    public FloatParameter baseHeight = new FloatParameter(0f);

    [DisplayInfo(name = "高さ減衰")]
    [Tooltip("高さ方向の薄まり方。0で高さに関係なく均一")]
    public MinFloatParameter heightFalloff = new MinFloatParameter(0.02f, 0f);

    [DisplayInfo(name = "スカイボックスへの適用率")]
    [Tooltip("地平線での空のフォグの濃さ。1で地平線が完全にフォグ色になる")]
    public ClampedFloatParameter skyboxFog = new ClampedFloatParameter(1f, 0f, 1f);

    [DisplayInfo(name = "スカイボックスのフォグ上端")]
    [Tooltip("空のフォグが消える高さ（視線の仰角のsin）。0.3で約17°、1で真上まで霞む")]
    public ClampedFloatParameter skyboxFogHeight = new ClampedFloatParameter(0.3f, 0.01f, 1f);

    public bool IsActive() => intensity.value > 0f && density.value > 0f;
}
