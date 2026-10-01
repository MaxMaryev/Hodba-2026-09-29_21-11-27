// След в пепле по-настоящему: цвет с маской и нормали (Т4), освещённые солнцем.
// На рассвете внутри отпечатка лежит своя тень. Правый след — отражённый левый, касательные это учитывают.
// Свет и дымка — те же, что у земли (HodbaAtmosphere.hlsl), иначе след выделялся бы пятном.
Shader "Hodba/FootprintLit"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("След: цвет и маска (Т4 Albedo)", 2D) = "white" {}
        [NoScaleOffset][Normal] _BumpMap ("След: нормали (Т4 Normal)", 2D) = "bump" {}
        _Strength ("Сила", Range(0, 1)) = 1
        _NormalStrength ("Глубина", Range(0, 2)) = 1
        _Wrap ("Мягкость света", Range(0, 1)) = 0.1
        _Rough ("Шероховатость (как у земли вблизи)", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FootprintLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HodbaAtmosphere.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                half _NormalStrength;
                half _Wrap;
                half _Rough;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = n.tangentWS;
                o.bitangentWS = n.bitangentWS;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _NormalStrength);
                float3 n = normalize(i.tangentWS * nt.x + i.bitangentWS * nt.y + i.normalWS * nt.z);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                half diffuse = HodbaRoughDiffuse(n, light.direction, V, _Rough, _Wrap);
                half shade = light.shadowAttenuation * light.distanceAttenuation * HodbaDustShadow(i.positionWS);
                half3 lighting = light.color * (diffuse * shade) + SampleSH(n);

                half3 color = HodbaApplyFog(albedo.rgb * lighting, i.positionWS);
                return half4(color, albedo.a * i.color.a * _Strength);
            }
            ENDHLSL
        }
    }
}
