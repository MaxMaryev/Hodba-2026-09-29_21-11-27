// Пепел. Крупные пятна слежавшегося пепла, мелкая зернистость, рябь от ветра поперёк преобладающего ветра.
// Все тайлы делят 4096 м — иначе при переносе плавающего центра рисунок прыгнет.
Shader "Hodba/Ground"
{
    Properties
    {
        _AshColor ("Пепел", Color) = (0.72, 0.69, 0.64, 1)
        _PackedColor ("Слежавшийся пепел", Color) = (0.54, 0.51, 0.48, 1)
        [Toggle] _AlbedoMode ("Текстуры — готовый цвет (Т1/Т3), а не серый рельеф", Float) = 0
        [NoScaleOffset] _AshAlbedo ("Пепел (Т1)", 2D) = "gray" {}
        _AshTile ("Пепел: метров на тайл", Float) = 2
        [NoScaleOffset] _PackedAlbedo ("Слежавшийся (Т3)", 2D) = "gray" {}
        _PackedTile ("Слежавшийся: метров на тайл", Float) = 4
        _DetailStrength ("Сила зерна", Range(0, 1)) = 0.6
        [NoScaleOffset][Normal] _AshNormal ("Пепел: нормали", 2D) = "bump" {}
        _AshNormalStrength ("Сила нормалей пепла", Range(0, 2)) = 0.6
        [NoScaleOffset][Normal] _RippleNormal ("Рябь (Т2)", 2D) = "bump" {}
        _RippleTile ("Рябь: метров на тайл", Float) = 4
        _RippleStrength ("Сила ряби", Range(0, 2)) = 0.9
        _RippleFadeDistance ("Рябь видна до, м", Float) = 70
        [NoScaleOffset] _MacroTex ("Крупные пятна", 2D) = "gray" {}
        _MacroTile ("Пятна: метров на тайл", Float) = 512
        _MacroContrast ("Контраст пятен", Range(0, 4)) = 1.8
        _Wrap ("Мягкость света", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _AshColor;
            half4 _PackedColor;
            half _AlbedoMode;
            float _AshTile;
            float _PackedTile;
            half _DetailStrength;
            half _AshNormalStrength;
            float _RippleTile;
            half _RippleStrength;
            float _RippleFadeDistance;
            float _MacroTile;
            half _MacroContrast;
            half _Wrap;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_AshAlbedo);     SAMPLER(sampler_AshAlbedo);
            TEXTURE2D(_PackedAlbedo);  SAMPLER(sampler_PackedAlbedo);
            TEXTURE2D(_AshNormal);     SAMPLER(sampler_AshNormal);
            TEXTURE2D(_RippleNormal);  SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_MacroTex);      SAMPLER(sampler_MacroTex);

            float4 _HodbaOriginMod;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Detail(half3 tex, half3 tint)
            {
                return _AlbedoMode > 0.5 ? tint * tex : tint * lerp(1.0h, tex * 2.0h, _DetailStrength);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 wp = i.positionWS.xz + _HodbaOriginMod.xy;
                float dist = distance(i.positionWS, GetCameraPositionWS());

                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroTile).r;
                macro = saturate((macro - 0.5h) * _MacroContrast + 0.5h);

                half3 ash = Detail(SAMPLE_TEXTURE2D(_AshAlbedo, sampler_AshAlbedo, wp / _AshTile).rgb, _AshColor.rgb);
                half3 packed = Detail(SAMPLE_TEXTURE2D(_PackedAlbedo, sampler_PackedAlbedo, wp / _PackedTile).rgb, _PackedColor.rgb);
                half3 albedo = lerp(packed, ash, macro);

                // Рябь живёт на рыхлом пепле, вблизи; вдали только мерцала бы.
                half rippleAmount = _RippleStrength * saturate(1.0 - dist / _RippleFadeDistance) * macro;
                half grainAmount = _AshNormalStrength * saturate(1.0 - dist / 30.0);
                half3 rn = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, wp / _RippleTile), rippleAmount);
                half3 an = UnpackNormalScale(SAMPLE_TEXTURE2D(_AshNormal, sampler_AshNormal, wp / _AshTile), grainAmount);

                float3 N = normalize(i.normalWS);
                float3 T = normalize(float3(1, 0, 0) - N * N.x);
                float3 B = cross(T, N);
                float3 n = normalize(N + T * (rn.x + an.x) + B * (rn.y + an.y));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate((dot(n, light.direction) + _Wrap) / (1.0h + _Wrap));
                half3 direct = light.color * (ndl * light.shadowAttenuation * light.distanceAttenuation);
                half3 ambient = SampleSH(n);

                half3 color = albedo * (direct + ambient);
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
