// Глаз поверх готовой картинки: периферия (мыло, меньше цвета, туннель), веки, тень ресниц,
// свет сквозь сомкнутые веки, лучи ресниц в щели прищура.
// Веко в двух сантиметрах от глаза — оно всегда не в фокусе, поэтому край мягкий.
// Проход 0 — сборка; 1 и 2 — размытие уменьшенной картинки по горизонтали и вертикали.
Shader "Hidden/Hodba/Eye"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // x — край верхнего века в центре экрана (0 — низ, 1 — верх), y — нижнего,
    // z — изгиб верхнего (углы закрываются раньше центра), w — изгиб нижнего.
    float4 _Lids;
    // x — мягкость края, y — тень ресниц, z — лучи ресниц, w — свет сквозь веки.
    float4 _LidLook;
    float4 _GlowColor;
    // x — откуда мыло (0 — центр, 1 — углы), y — сколько мыла, z — сколько цвета уходит, w — затемнение краёв.
    float4 _Periphery;
    // xy — шаг размытия в долях уменьшенной картинки.
    float4 _BlurStep;

    TEXTURE2D_X(_PeripheryBlur);

    half Luma(half3 c) { return dot(c, half3(0.299h, 0.587h, 0.114h)); }

    half3 Scene(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }

    // Девять выборок с весами Гаусса, из них пять — через билинейную фильтрацию.
    half4 Blur(float2 uv, float2 dir)
    {
        const float o1 = 1.3846153846, o2 = 3.2307692308;
        const half w0 = 0.2270270270h, w1 = 0.3162162162h, w2 = 0.0702702703h;
        half3 c = Scene(uv) * w0;
        c += Scene(uv + dir * o1) * w1 + Scene(uv - dir * o1) * w1;
        c += Scene(uv + dir * o2) * w2 + Scene(uv - dir * o2) * w2;
        return half4(c, 1.0h);
    }
    ENDHLSL

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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = Scene(uv);

                // Периферия: к углам мыло, меньше цвета; в туннеле ещё и темнее. Центр всегда чистый.
                float r = length(uv * 2.0 - 1.0) * 0.70710678;
                UNITY_BRANCH
                if (_Periphery.y + _Periphery.z + _Periphery.w > 0.001)
                {
                    half edge = smoothstep(_Periphery.x, 1.0, r);
                    half3 soft = SAMPLE_TEXTURE2D_X(_PeripheryBlur, sampler_LinearClamp, uv).rgb;
                    col = lerp(col, soft, edge * _Periphery.y);
                    col = lerp(col, Luma(col).xxx, edge * _Periphery.z);
                    col *= 1.0h - _Periphery.w * smoothstep(_Periphery.x, 1.1, r);
                }

                float x = uv.x * 2.0 - 1.0;
                float upper = _Lids.x - _Lids.z * x * x;
                float lower = _Lids.y + _Lids.w * x * x;
                float softEdge = _LidLook.x;
                float open = smoothstep(0.0, 1.0, saturate((upper - uv.y) / softEdge))
                           * smoothstep(0.0, 1.0, saturate((uv.y - lower) / softEdge));

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
                col *= 1.0 - _LidLook.y * exp(-below / (softEdge * 0.7));

                // Изнутри веко почти чёрное; против солнца светится тёплым красным, к краям — темнее.
                float2 c = uv * 2.0 - 1.0;
                half depth = 1.0h - 0.45h * saturate(dot(c, c) * 0.5);
                half3 lid = _GlowColor.rgb * _LidLook.w * depth + col * 0.03h;
                return half4(lerp(lid, col, open), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur Horizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return Blur(input.texcoord, float2(_BlurStep.x, 0)); }
            ENDHLSL
        }

        Pass
        {
            Name "Blur Vertical"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return Blur(input.texcoord, float2(0, _BlurStep.y)); }
            ENDHLSL
        }
    }
}
