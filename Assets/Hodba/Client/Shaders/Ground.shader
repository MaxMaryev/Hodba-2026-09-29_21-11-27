// Пепел. Земля — кольца вокруг путника (геометрические клипмапы): вершины сетки — целые координаты,
// высоту и поверхность каждой вершины шейдер берёт из текстур кольца (кольцевая адресация, см. ClipmapTerrain).
// У внешнего края кольцо плавно перетекает в следующее, более крупное — без щелей.
// Где пепел, а где слежавшаяся корка, решает мир (r — рыхлость, g — рябь, b — неровность),
// чтобы земля выглядела так, как её чувствуют ноги. Рябь-нормаль — там, где рябь есть в мире.
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
        _MacroTint ("Пятнистость внутри одного вида", Range(0, 1)) = 0.25
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
            half _MacroTint;
            half _Wrap;
        CBUFFER_END

        // Кольцо — на каждый вызов отрисовки свои (MaterialPropertyBlock).
        TEXTURE2D(_ClipHeight);
        TEXTURE2D(_ClipSurface);
        TEXTURE2D(_ClipHeightNext);
        TEXTURE2D(_ClipSurfaceNext);
        float4 _ClipOrigin;     // xy — угол кольца (локальные xz), zw — его кольцевой адрес в текстуре
        float4 _ClipOriginNext; // то же для следующего, более крупного кольца
        float4 _ClipParams;     // x — шаг, м; y — вершин по стороне; z — ширина перетекания, клеток; w — есть ли следующее

        #define CLIP_SIZE 128

        int2 ClipWrap(int2 t) { return t & (CLIP_SIZE - 1); }

        struct GroundVertex
        {
            float3 positionWS;
            float3 normalWS;
            half3 surface;
        };

        GroundVertex ClipVertex(float3 positionOS)
        {
            float s = _ClipParams.x;
            int2 g = int2(round(positionOS.xz));
            int2 t = g + int2(_ClipOrigin.zw);

            float h = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t)).r;
            half3 surface = LOAD_TEXTURE2D(_ClipSurface, ClipWrap(t)).rgb;
            float hl = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(-1, 0))).r;
            float hr = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(1, 0))).r;
            float hd = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(0, -1))).r;
            float hu = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(0, 1))).r;
            float3 normal = float3(hl - hr, 2.0 * s, hd - hu);

            float2 xz = _ClipOrigin.xy + float2(g) * s;

            // У внешнего края — плавно в следующее кольцо: там вершины ложатся ровно на его треугольники.
            float c = (_ClipParams.y - 1.0) * 0.5;
            float d = max(abs(g.x - c), abs(g.y - c));
            float a = saturate((d - (c - _ClipParams.z)) / _ClipParams.z) * _ClipParams.w;
            UNITY_BRANCH
            if (a > 0.0)
            {
                float s2 = 2.0 * s;
                float2 f = (xz - _ClipOriginNext.xy) / s2;
                int2 i0 = int2(floor(f));
                float2 fr = f - float2(i0);
                int2 tn = i0 + int2(_ClipOriginNext.zw);
                float h00 = LOAD_TEXTURE2D(_ClipHeightNext, ClipWrap(tn)).r;
                float h10 = LOAD_TEXTURE2D(_ClipHeightNext, ClipWrap(tn + int2(1, 0))).r;
                float h01 = LOAD_TEXTURE2D(_ClipHeightNext, ClipWrap(tn + int2(0, 1))).r;
                float h11 = LOAD_TEXTURE2D(_ClipHeightNext, ClipWrap(tn + int2(1, 1))).r;
                half3 s00 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn)).rgb;
                half3 s10 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(1, 0))).rgb;
                half3 s01 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(0, 1))).rgb;
                half3 s11 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(1, 1))).rgb;

                float hc = lerp(lerp(h00, h10, fr.x), lerp(h01, h11, fr.x), fr.y);
                half3 sc = lerp(lerp(s00, s10, fr.x), lerp(s01, s11, fr.x), fr.y);
                float3 nc = float3(-(h10 - h00 + h11 - h01) * 0.5, s2, -(h01 - h00 + h11 - h10) * 0.5);

                h = lerp(h, hc, a);
                surface = lerp(surface, sc, a);
                normal = lerp(normalize(normal), normalize(nc), a);
            }

            GroundVertex o;
            o.positionWS = float3(xz.x, h, xz.y);
            o.normalWS = normalize(normal);
            o.surface = surface;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half3 surface : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                GroundVertex g = ClipVertex(v.positionOS.xyz);
                Varyings o;
                o.positionWS = g.positionWS;
                o.positionCS = TransformWorldToHClip(g.positionWS);
                o.normalWS = g.normalWS;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.surface = g.surface;
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

                // Пепел или корка — как решил мир: рыхлость (корка ~0,3, пепел ~0,7).
                half ashness = smoothstep(0.35h, 0.65h, i.surface.r);

                // Крупные пятна остаются только лёгким оттенком внутри одного вида.
                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroTile).r;
                macro = saturate((macro - 0.5h) * _MacroContrast + 0.5h);

                half3 ash = Detail(SAMPLE_TEXTURE2D(_AshAlbedo, sampler_AshAlbedo, wp / _AshTile).rgb, _AshColor.rgb);
                half3 packed = Detail(SAMPLE_TEXTURE2D(_PackedAlbedo, sampler_PackedAlbedo, wp / _PackedTile).rgb, _PackedColor.rgb);
                half3 albedo = lerp(packed, ash, ashness) * lerp(1.0h, 0.85h + 0.3h * macro, _MacroTint);

                // Рябь — там, где она есть в мире, и вблизи; вдали только мерцала бы.
                half rippleAmount = _RippleStrength * saturate(1.0 - dist / _RippleFadeDistance) * i.surface.g;
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformWorldToHClip(ClipVertex(positionOS.xyz).positionWS);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // Тени отбрасывают только ближние кольца (ClipmapTerrain): рябь и бугры у ног на рассвете.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                GroundVertex g = ClipVertex(positionOS.xyz);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(g.positionWS, g.normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
