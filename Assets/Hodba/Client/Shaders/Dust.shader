// Пылинки и пепельные полосы. Освещены солнцем, против низкого солнца светятся — рассеяние вперёд.
Shader "Hodba/Dust"
{
    Properties
    {
        _MainTex ("Форма", 2D) = "white" {}
        _Tint ("Цвет пепла", Color) = (0.82, 0.79, 0.74, 1)
        _Scatter ("Свечение против солнца", Float) = 3
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Dust"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Scatter;
            CBUFFER_END

            float4 _HodbaSunDir;
            half4 _HodbaSunColor;
            half4 _HodbaAmbient;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a;
                float3 view = normalize(i.positionWS - GetCameraPositionWS());
                half forward = pow(saturate(dot(view, normalize(_HodbaSunDir.xyz))), 6.0);
                half3 light = _HodbaAmbient.rgb + _HodbaSunColor.rgb * (0.35h + _Scatter * forward);
                half3 col = _Tint.rgb * i.color.rgb * light;
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
