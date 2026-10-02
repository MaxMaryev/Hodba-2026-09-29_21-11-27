// Пылинки и песчинки в полёте. Освещены солнцем, против низкого солнца светятся — рассеяние вперёд.
Shader "Hodba/Dust"
{
    Properties
    {
        _MainTex ("Форма", 2D) = "white" {}
        _Tint ("Цвет пепла", Color) = (0.82, 0.79, 0.74, 1)
        _Scatter ("Свечение против солнца", Float) = 3
        _NearFade ("Гаснет у глаза: x — с, м; y — за, м", Vector) = (0.3, 1.2, 0, 0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HodbaAtmosphere.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Scatter;
                float4 _NearFade;
            CBUFFER_END

            half4 _HodbaAmbient;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toPoint = i.positionWS - GetCameraPositionWS();
                float dist = length(toPoint);
                // Вплотную к глазу частица была бы огромным мягким пятном — гаснет.
                half near = saturate((dist - _NearFade.x) / max(_NearFade.y, 0.01));
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a * near;
                float3 view = toPoint / max(dist, 1e-4);
                half forward = pow(saturate(dot(view, HodbaSunDirection())), 6.0) * (1.0 - saturate(_HodbaSunOcclusion));
                half sunlight=HodbaWallLightVisibility(i.positionWS,HodbaSunDirection());
                half3 light = _HodbaAmbient.rgb * lerp(0.65h,1.0h,sunlight)
                    + _HodbaSunColor.rgb * ((0.35h + _Scatter * forward) * HodbaDustShadow(i.positionWS) * sunlight);
                half3 col = _Tint.rgb * i.color.rgb * light;
                col = HodbaApplyFog(col, i.positionWS);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
