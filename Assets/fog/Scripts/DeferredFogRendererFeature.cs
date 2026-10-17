using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 深度テクスチャから各ピクセルのワールド座標を復元し、距離・高さに応じてフォグ色を合成する全画面パス。
// オブジェクト側のシェーダーに依存しないので、FBXの建物や敵などマテリアルが混在していても一様に効く。
// PC_Renderer / Mobile_Rendererに登録し、パラメータはVolumeのDeferredFogで制御する。
public class DeferredFogRendererFeature : ScriptableRendererFeature
{
    public const string ShaderName = "Hidden/Fog/DeferredFog";

    [SerializeField] Shader shader;

    [Tooltip("不透明物とスカイボックスの後、半透明物の前に掛けるのが標準。半透明物にも掛けたい場合はAfterRenderingTransparents")]
    [SerializeField] RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

    Material material;
    DeferredFogPass pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find(ShaderName);

        if (shader != null && material == null)
            material = CoreUtils.CreateEngineMaterial(shader);

        pass = new DeferredFogPass { renderPassEvent = renderPassEvent };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        var fog = VolumeManager.instance.stack.GetComponent<DeferredFog>();
        if (fog == null || !fog.IsActive())
            return;

        pass.Setup(material, fog);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        material = null;
    }

    class DeferredFogPass : ScriptableRenderPass
    {
        static readonly int FogColorId = Shader.PropertyToID("_FogColor");
        static readonly int FogParamsId = Shader.PropertyToID("_FogParams");
        static readonly int FogParams2Id = Shader.PropertyToID("_FogParams2");

        Material material;

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public DeferredFogPass()
        {
            profilingSampler = new ProfilingSampler("Deferred Fog");
            // バックバッファへ直接描いている場合はカラーを読めないので中間テクスチャを要求する
            requiresIntermediateTexture = true;
            // Mobile_RPAssetはDepth Textureが無効なので、このパスが必要とする時だけ生成させる
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(Material mat, DeferredFog fog)
        {
            material = mat;
            material.SetColor(FogColorId, fog.color.value);
            material.SetVector(FogParamsId, new Vector4(
                fog.density.value,
                fog.startDistance.value,
                fog.baseHeight.value,
                fog.heightFalloff.value));
            material.SetVector(FogParams2Id, new Vector4(
                fog.intensity.value,
                fog.skyboxFog.value,
                fog.skyboxFogHeight.value,
                0f));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle depth = resourceData.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid())
                return;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_DeferredFogColor";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Deferred Fog", out var passData, profilingSampler))
            {
                passData.source = source;
                passData.material = material;

                builder.UseTexture(source, AccessFlags.Read);
                // _CameraDepthTextureはグローバルとして参照するので、読むことをRender Graphに宣言しておく
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            // 以降のパス（半透明・ポストプロセス）はフォグ適用後のテクスチャを使う
            resourceData.cameraColor = destination;
        }
    }
}
