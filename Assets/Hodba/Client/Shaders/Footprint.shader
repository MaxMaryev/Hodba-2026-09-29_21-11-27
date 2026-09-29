// След в пепле. Умножение ×2: серый 0.5 ничего не меняет, темнее — вмятина, светлее — выдавленный край.
// След не рисуется поверх пепла, а затеняет его — поэтому подходит к любому свету и времени суток.
Shader "Hodba/Footprint"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("След (0.5 — нейтраль, без sRGB)", 2D) = "gray" {}
        _Strength ("Сила", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Footprint"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fogFactor : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half v = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                half c = lerp(0.5h, v, i.color.a * _Strength);
                half3 col = MixFogColor(half3(c, c, c), half3(0.5h, 0.5h, 0.5h), i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
