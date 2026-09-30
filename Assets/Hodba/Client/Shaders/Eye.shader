// Глаз поверх готовой картинки: веки, тень ресниц, свет сквозь сомкнутые веки, лучи ресниц в щели прищура.
// Веко в двух сантиметрах от глаза — оно всегда не в фокусе, поэтому край мягкий.
Shader "Hidden/Hodba/Eye"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Eye"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x — край верхнего века в центре экрана (0 — низ, 1 — верх), y — нижнего,
            // z — изгиб верхнего (углы закрываются раньше центра), w — изгиб нижнего.
            float4 _Lids;
            // x — мягкость края, y — тень ресниц, z — лучи ресниц, w — свет сквозь веки.
            float4 _LidLook;
            float4 _GlowColor;

            half Luma(half3 c) { return dot(c, half3(0.299h, 0.587h, 0.114h)); }

            half3 Scene(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = Scene(uv);

                float x = uv.x * 2.0 - 1.0;
                float upper = _Lids.x - _Lids.z * x * x;
                float lower = _Lids.y + _Lids.w * x * x;
                float soft = _LidLook.x;
                float open = smoothstep(0.0, 1.0, saturate((upper - uv.y) / soft))
                           * smoothstep(0.0, 1.0, saturate((uv.y - lower) / soft));

                // Лучи от ресниц: в щели прищура яркое тянется вертикальными штрихами.
                UNITY_BRANCH
                if (_LidLook.z > 0.001)
                {
                    half3 streak = 0;
                    UNITY_UNROLL
                    for (int k = 1; k <= 6; k++)
                    {
                        float o = k * 0.022;
                        half w = 1.0h - k / 7.0h;
                        streak += (max(0.0h, Luma(Scene(uv + float2(0, o))) - 0.8h)
                                 + max(0.0h, Luma(Scene(uv - float2(0, o))) - 0.8h)) * w;
                    }
                    col += streak * _LidLook.z * half3(1.0h, 0.95h, 0.85h);
                }

                // Тень ресниц — тёмная кромка под краем верхнего века.
                float below = max(0.0, upper - uv.y);
                col *= 1.0 - _LidLook.y * exp(-below / (soft * 0.7));

                // Изнутри веко почти чёрное; против солнца светится тёплым красным, к краям — темнее.
                float2 c = uv * 2.0 - 1.0;
                half depth = 1.0h - 0.45h * saturate(dot(c, c) * 0.5);
                half3 lid = _GlowColor.rgb * _LidLook.w * depth + col * 0.03h;
                return half4(lerp(lid, col, open), 1.0h);
            }
            ENDHLSL
        }
    }
}
