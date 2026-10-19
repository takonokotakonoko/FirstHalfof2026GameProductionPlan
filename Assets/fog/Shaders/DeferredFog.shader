// DeferredFogRendererFeatureから全画面Blitで使うシェーダー。
// 深度テクスチャからワールド座標を復元し、「距離フォグ × 高さによる減衰」の指数フォグを合成する。
Shader "Hidden/Fog/DeferredFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "DeferredFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _FogColor;
            float4 _FogParams;   // x: 濃度, y: 開始距離, z: 基準高さ, w: 高さ減衰
            float4 _FogParams2;  // x: 強さ(最大不透明度), y: スカイボックス適用率, z: スカイボックスのフォグ上端

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
                bool isSky = depth < 1e-6;
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                bool isSky = depth > 1.0 - 1e-6;
            #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 ray = worldPos - _WorldSpaceCameraPos;
                float rayLength = length(ray);
                float fogDistance = max(rayLength - _FogParams.y, 0.0);

                // 高さ方向に指数で薄くなるフォグを視線に沿って積分した係数。
                // 空を見上げる視線ほど薄い層を通るので、地平線付近だけが濃く霞む。
                float falloff = max(_FogParams.w, 1e-5);
                float cameraHeight = _WorldSpaceCameraPos.y - _FogParams.z;
                // カメラが基準高さより大きく下にある場合のオーバーフロー防止
                float heightDensity = min(exp(-falloff * cameraHeight), 1e4);
                float k = falloff * ray.y;
                float heightIntegral = abs(k) > 1e-4 ? (1.0 - exp(-k)) / k : 1.0;

                float fog;
                if (isSky)
                {
                    // 空は距離が定義できない（far clip面の位置になる）ので、視線の仰角で決める。
                    // 地平線で「適用率」、上端の仰角で0になるグラデーション。地平線より下は最大。
                    float elevation = ray.y / max(rayLength, 1e-5);
                    float t = saturate(elevation / _FogParams2.z);
                    fog = _FogParams2.y * (1.0 - smoothstep(0.0, 1.0, t));
                }
                else
                {
                    float opticalDepth = _FogParams.x * fogDistance * heightDensity * heightIntegral;
                    fog = saturate(1.0 - exp(-opticalDepth));
                }
                fog *= _FogParams2.x;

                return half4(lerp(sceneColor.rgb, _FogColor.rgb, fog), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
