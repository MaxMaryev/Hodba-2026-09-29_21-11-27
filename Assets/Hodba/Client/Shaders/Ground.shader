// Пепел. Земля — кольца вокруг путника (геометрические клипмапы): вершины сетки — целые координаты,
// высоту и поверхность каждой вершины шейдер берёт из текстур кольца (кольцевая адресация, см. ClipmapTerrain).
// У внешнего края кольцо плавно перетекает в следующее, более крупное — без щелей.
// Где пепел, а где слежавшаяся корка, решает мир (r — рыхлость, g — рябь, b — сдвиг гребней, a — амплитуда),
// чтобы земля выглядела так, как её чувствуют ноги. Крупная рябь рисуется светом по тем же гребням.
// Мелкая рябь — одна выборка шума (HodbaRipple.hlsl). Все тайлы делят 4096 м — иначе при переносе плавающего центра рисунок прыгнет.
// Цвет и рельеф — арт Т1 (пепел) и Т3 (корка); варианты тайла сдвигают его, Т1 без крупных пятен — повтор не виден вдали.
// Свет шершавый (Орен — Найяр), в зерне и трещинах — микротени из альфы альбедо (запекает генератор текстур);
// дымка и тени пыльных облаков — общие с небом и камнями (HodbaAtmosphere.hlsl); позёмка — песок, бегущий по ветру (HodbaSand.hlsl).
Shader "Hodba/Ground"
{
    Properties
    {
        [NoScaleOffset] _AshAlbedo ("Пепел (Т1): цвет, в альфе микротени", 2D) = "gray" {}
        _AshTile ("Пепел: метров на тайл", Float) = 2
        [NoScaleOffset][Normal] _AshNormal ("Пепел: нормали", 2D) = "bump" {}
        _AshNormalStrength ("Сила нормалей пепла", Range(0, 2)) = 0.6
        [NoScaleOffset] _PackedAlbedo ("Корка (Т3): цвет, в альфе микротени", 2D) = "gray" {}
        _PackedTile ("Корка: метров на тайл", Float) = 4
        [NoScaleOffset][Normal] _PackedNormal ("Корка: нормали (трещины)", 2D) = "bump" {}
        _PackedNormalStrength ("Сила нормалей корки", Range(0, 2)) = 1
        [NoScaleOffset] _RippleNoise ("Мелкая рябь: изгиб, его наклон, маска гребней (64 м)", 2D) = "gray" {}
        _RippleStrength ("Сила мелкой ряби", Range(0, 2)) = 0.9
        _MegaRippleStrength ("Сила крупной ряби", Range(0, 2)) = 1
        _MegaRippleFadeDistance ("Крупная рябь видна до, м", Float) = 40
        _RippleFadeDistance ("Мелкая рябь видна до, м", Float) = 70
        [NoScaleOffset] _MacroTex ("Крупные пятна", 2D) = "gray" {}
        _MacroTile ("Пятна: метров на тайл", Float) = 512
        _MacroContrast ("Контраст пятен", Range(0, 4)) = 1.8
        _MacroTint ("Пятнистость внутри одного вида", Range(0, 1)) = 0.25
        [NoScaleOffset] _VariationTex ("Варианты / оттенок / детали (128 м)", 2D) = "gray" {}
        _VariantBlend ("Ширина перехода вариантов", Range(0.05, 1)) = 0.3
        _MidTint ("Яркость средних пятен", Range(0, 0.3)) = 0.08
        _MidHue ("Тёплые / холодные пятна", Range(0, 0.1)) = 0.03
        _DetailVar ("Вариация силы деталей", Range(0, 1)) = 0.35
        _FadePxStart ("Полные детали: пикселей на тайл", Float) = 64
        _FadePxEnd ("Средний цвет: пикселей на тайл", Float) = 8
        _Wrap ("Мягкость света", Range(0, 1)) = 0.1
        _RoughNear ("Шероховатость вблизи", Range(0, 1)) = 0.6
        _RoughFar ("Шероховатость вдали (зерна уже не видно)", Range(0, 1)) = 0.9
        _AOStrength ("Микротени (альфа альбедо)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _AshTile;
            float _PackedTile;
            half _AshNormalStrength;
            half _PackedNormalStrength;
            half _RippleStrength;
            half _MegaRippleStrength;
            float _MegaRippleFadeDistance;
            float _RippleFadeDistance;
            float _MacroTile;
            half _MacroContrast;
            half _MacroTint;
            half _VariantBlend;
            half _MidTint;
            half _MidHue;
            half _DetailVar;
            float _FadePxStart;
            float _FadePxEnd;
            half _Wrap;
            half _RoughNear;
            half _RoughFar;
            half _AOStrength;
        CBUFFER_END

        #include "HodbaClipmap.hlsl"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HodbaAtmosphere.hlsl"
            #include "HodbaSand.hlsl"
            #include "HodbaRipple.hlsl"

            TEXTURE2D(_AshAlbedo);     SAMPLER(sampler_AshAlbedo);
            TEXTURE2D(_PackedAlbedo);  SAMPLER(sampler_PackedAlbedo);
            TEXTURE2D(_AshNormal);     SAMPLER(sampler_AshNormal);
            TEXTURE2D(_PackedNormal);  SAMPLER(sampler_PackedNormal);
            TEXTURE2D(_RippleNoise);   SAMPLER(sampler_RippleNoise);
            TEXTURE2D(_MacroTex);      SAMPLER(sampler_MacroTex);
            TEXTURE2D(_VariationTex);  SAMPLER(sampler_VariationTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                // float: наклон сдвига гребней берётся из экранных производных, half их бы раскрошил.
                float4 surface : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                GroundVertex g = ClipVertex(v.positionOS.xyz);
                Varyings o;
                o.positionWS = g.positionWS;
                o.positionCS = TransformWorldToHClip(g.positionWS);
                o.normalWS = g.normalWS;
                o.surface = g.surface;
                return o;
            }

            float2 VariantOffset(float index)
            {
                return frac(sin(float2(3.0, 7.0) * index));
            }

            // Производные исходного UV вычисляются до всех веток и разрывов индекса варианта.
            // rgb — цвет, a — микротени (AO): варианты смешиваются вместе.
            half4 SampleNoTile(TEXTURE2D_PARAM(tex, smp), float2 uv, float v,
                float2 dx, float2 dy, half4 mean, out half weight)
            {
                float index = floor(v);
                float width = max((float)_VariantBlend, 0.05);
                float t = saturate((frac(v) - 0.5) / width + 0.5);
                weight = smoothstep(0.0, 1.0, t);
                half4 sampled = mean;
                UNITY_BRANCH
                if (t <= 0.0)
                    sampled = SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index), dx, dy);
                else if (t >= 1.0)
                    sampled = SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index + 1.0), dx, dy);
                else
                {
                    half4 a = SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index), dx, dy);
                    half4 b = SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index + 1.0), dx, dy);
                    // Смещение по яркости затухает на краях окна: ветки остаются непрерывными.
                    float brightnessDifference = dot(a.rgb - b.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                    weight = smoothstep(0.0, 1.0, saturate(t - 0.1 * brightnessDifference * (4.0 * t * (1.0 - t)) / width));
                    half variance = weight * weight + (1.0h - weight) * (1.0h - weight);
                    sampled = mean + (lerp(a, b, weight) - mean) * rsqrt(variance);
                }
                return sampled;
            }

            // Нормали — с теми же вариантами и весом, что у альбедо: рельеф совпадает с цветом.
            half3 SampleNormalNoTile(TEXTURE2D_PARAM(tex, smp), float2 uv, float v, half weight, float2 dx, float2 dy, half strength)
            {
                float index = floor(v);
                half3 sampled = half3(0.0h, 0.0h, 1.0h);
                UNITY_BRANCH
                if (weight <= 0.0h)
                    sampled = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index), dx, dy), strength);
                else if (weight >= 1.0h)
                    sampled = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index + 1.0), dx, dy), strength);
                else
                {
                    half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index), dx, dy), strength);
                    half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, smp, uv + VariantOffset(index + 1.0), dx, dy), strength);
                    sampled = lerp(a, b, weight);
                }
                return sampled;
            }

            half DetailVisibility(float tile, float footprint)
            {
                float end = max(_FadePxEnd, 0.0);
                float start = max(_FadePxStart, end + 0.001);
                return smoothstep(end, start, tile / max(footprint, 0.000001));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 wp = i.positionWS.xz + _HodbaOriginMod.xy;
                float dist = distance(i.positionWS, GetCameraPositionWS());
                // Производные — от локальных координат: wp доходит до 4096 м, и у ног его разности тонут в округлении.
                float2 dx = ddx(i.positionWS.xz), dy = ddy(i.positionWS.xz);
                float fw = max(length(dx), length(dy));
                float ashTile = max(_AshTile, 0.001), packedTile = max(_PackedTile, 0.001);
                half3 variation = SAMPLE_TEXTURE2D_GRAD(_VariationTex, sampler_VariationTex, wp / 128.0, dx / 128.0, dy / 128.0).rgb;
                float variant = variation.r * 8.0;
                half detailScale = lerp(1.0h - _DetailVar, 1.0h + _DetailVar, variation.b);
                // Вес второго варианта; вблизи его уточняет выборка альбедо (с поправкой на яркость).
                half ashWeight = smoothstep(0.0, 1.0, saturate((frac(variant) - 0.5) / max((float)_VariantBlend, 0.05) + 0.5));
                half packedWeight = ashWeight;

                // Пепел или корка — как решил мир: рыхлость (корка ~0,3, пепел ~0,7).
                half ashness = smoothstep(0.35h, 0.65h, i.surface.r);

                // Крупные пятна остаются только лёгким оттенком внутри одного вида.
                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, wp / _MacroTile).r;
                macro = saturate((macro - 0.5h) * _MacroContrast + 0.5h);

                // Микротени — относительно средней затенённости тайла: общая яркость та же, что без них,
                // вдали их нет, и следы (без AO) не выделяются пятном.
                half3 ash = 0.0h, packed = 0.0h;
                half ashAO = 1.0h, packedAO = 1.0h;
                UNITY_BRANCH
                if (ashness > 0.0h)
                {
                    half4 mean = SAMPLE_TEXTURE2D_LOD(_AshAlbedo, sampler_AshAlbedo, float2(0.5, 0.5), 16.0);
                    half visibility = DetailVisibility(ashTile, fw);
                    half4 detail = mean;
                    UNITY_BRANCH
                    if (visibility > 0.0h)
                        detail = SampleNoTile(TEXTURE2D_ARGS(_AshAlbedo, sampler_AshAlbedo), wp / ashTile, variant,
                            dx / ashTile, dy / ashTile, mean, ashWeight);
                    ash = max(0.0h, mean.rgb + (detail.rgb - mean.rgb) * (detailScale * visibility));
                    ashAO = 1.0h + (detail.a - mean.a) * visibility / max(mean.a, 0.5h);
                }
                UNITY_BRANCH
                if (ashness < 1.0h)
                {
                    half4 mean = SAMPLE_TEXTURE2D_LOD(_PackedAlbedo, sampler_PackedAlbedo, float2(0.5, 0.5), 16.0);
                    half visibility = DetailVisibility(packedTile, fw);
                    half4 detail = mean;
                    UNITY_BRANCH
                    if (visibility > 0.0h)
                        detail = SampleNoTile(TEXTURE2D_ARGS(_PackedAlbedo, sampler_PackedAlbedo), wp / packedTile, variant,
                            dx / packedTile, dy / packedTile, mean, packedWeight);
                    packed = max(0.0h, mean.rgb + (detail.rgb - mean.rgb) * (detailScale * visibility));
                    packedAO = 1.0h + (detail.a - mean.a) * visibility / max(mean.a, 0.5h);
                }
                half mid = variation.g * 2.0h - 1.0h;
                half3 midTint = (1.0h + mid * _MidTint) * (1.0h + half3(1.0h, 0.0h, -1.0h) * (mid * _MidHue));
                half3 albedo = lerp(packed, ash, ashness) * midTint * lerp(1.0h, 0.85h + 0.3h * macro, _MacroTint);

                // Позёмка: бегущий песок светлее и накрывает рябь и зерно.
                half salt = HodbaSaltation(i.positionWS, dist, dx, dy, i.surface.r, normalize(i.normalWS));
                albedo = lerp(albedo, _SandDriftColor.rgb, salt * _SandDrift.w);
                half buried = 1.0h - 0.8h * salt;

                // Крупная рябь — та же волна, что под ногами. Мелкая гаснет к 70 м; обе — пока волна шире 2–6 пикселей.
                float rippleX = i.positionWS.x;
                float footprintX = max(abs(dx.x), abs(dy.x));
                float shift = HodbaRippleShift(i.surface.b);
                float2 shiftSlope = HodbaWorldGrad(shift, dx, dy);
                float megaAmp = i.surface.a * 0.030 * _MegaRippleStrength * buried
                    * (1.0 - smoothstep(0.4 * _MegaRippleFadeDistance, _MegaRippleFadeDistance, dist))
                    * HodbaRippleVisible(HodbaMegaRippleLength, footprintX);
                float2 rippleSlope = HodbaMegaRipple(rippleX, i.positionWS.z, shift, shiftSlope, megaAmp);
                half rippleAmount = _RippleStrength * saturate(1.0 - dist / _RippleFadeDistance) * i.surface.g * buried
                    * HodbaRippleVisible(HodbaFineRippleLength, footprintX);
                UNITY_BRANCH
                if (rippleAmount > 0.0h)
                {
                    half4 noise = SAMPLE_TEXTURE2D_GRAD(_RippleNoise, sampler_RippleNoise, wp / 64.0, dx / 64.0, dy / 64.0);
                    rippleSlope += HodbaFineRipple(rippleX, i.positionWS.z, shift, shiftSlope, noise, rippleAmount * 0.008);
                }
                // Зерно пепла и трещины корки — вблизи, дальше они тоньше пикселя.
                half grainFade = saturate(1.0 - dist / 30.0) * buried;
                half3 rn = half3(rippleSlope, 1.0h), gn = half3(0.0h, 0.0h, 1.0h);
                UNITY_BRANCH
                if (grainFade > 0.0h)
                {
                    half3 ashN = half3(0.0h, 0.0h, 1.0h), packedN = half3(0.0h, 0.0h, 1.0h);
                    UNITY_BRANCH
                    if (ashness > 0.0h)
                        ashN = SampleNormalNoTile(TEXTURE2D_ARGS(_AshNormal, sampler_AshNormal), wp / ashTile, variant, ashWeight,
                            dx / ashTile, dy / ashTile, _AshNormalStrength * grainFade);
                    UNITY_BRANCH
                    if (ashness < 1.0h)
                        packedN = SampleNormalNoTile(TEXTURE2D_ARGS(_PackedNormal, sampler_PackedNormal), wp / packedTile, variant, packedWeight,
                            dx / packedTile, dy / packedTile, _PackedNormalStrength * grainFade);
                    gn = lerp(packedN, ashN, ashness);
                }

                float3 N = normalize(i.normalWS);
                float3 T = normalize(float3(1, 0, 0) - N * N.x);
                float3 B = cross(T, N);
                float3 n = normalize(N + T * (rn.x + gn.x) + B * (rn.y + gn.y));

                half ao = lerp(1.0h, lerp(packedAO, ashAO, ashness), _AOStrength);

                // Где нормали зерна уже погасли, поверхность не гладкая, а шершавая на масштабе пикселя.
                half rough = lerp(_RoughFar, _RoughNear, saturate(1.0 - dist / 30.0));
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = HodbaRoughDiffuse(n, light.direction, V, rough, _Wrap);
                half shade = light.shadowAttenuation * light.distanceAttenuation
                    * HodbaMicroShadow(ao, dot(n, light.direction)) * HodbaDustShadow(i.positionWS)
                    * HodbaWallLightVisibility(i.positionWS+n*0.2,light.direction);
                half3 direct = light.color * (diffuse * shade);
                half3 ambient = HodbaWallAmbientLight(SampleSH(n),i.positionWS,n,light.direction) * ao;

                half3 color = albedo * (direct + ambient);
                color = HodbaApplyFog(color, i.positionWS);
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

        // Тени отбрасывают только ближние кольца (ClipmapTerrain): бугры у ног на рассвете.
        // Гребней ряби в меше нет — их затеняет нормаль.
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
