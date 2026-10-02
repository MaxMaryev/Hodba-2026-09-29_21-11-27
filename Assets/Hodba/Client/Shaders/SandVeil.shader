// Взвесь: песок и пыль в воздухе у земли, 0–1 м. Те же кольца, что у земли (HodbaClipmap.hlsl), рисуются ещё раз
// прозрачными оболочками на высотах слоёв (SandDrift.cs). Языки вытянуты по ветру и бегут со скоростью своего слоя;
// фронт порыва из поля ветра сгущает их в пелену. Гребни курятся: выпуклость поднимает и уплотняет верхние слои.
// Свет как у пыли: против низкого солнца пелена светится; в тени камня и путника — темнеет.
Shader "Hodba/SandVeil"
{
    Properties
    {
        _Scatter ("Свечение против солнца", Float) = 2
        _Drift ("Снос верхних слоёв по ветру, м на метр высоты", Float) = 0.6
        _CrestLift ("Насколько гребень поднимает слои", Float) = 1.5
        _Fade ("Гаснет: x — от, м; y — до, м; z — у глаза до, м", Vector) = (35, 55, 2, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SandVeil"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "HodbaClipmap.hlsl"
            #include "HodbaAtmosphere.hlsl"
            #include "HodbaSand.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Scatter;
                float _Drift;
                float _CrestLift;
                float4 _Fade;
            CBUFFER_END

            float4 _VeilLayer; // x — номер слоя (MaterialPropertyBlock)
            half4 _HodbaAmbient;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half2 shape : TEXCOORD2; // x — рыхлость, y — гребень 0..1
            };

            Varyings vert(Attributes v)
            {
                GroundVertex g = ClipVertex(v.positionOS.xyz);
                float4 layer = _SandDriftLayers[(int)_VeilLayer.x];
                float h = layer.z;
                float crest = saturate(-g.curvature * 4.0);
                float upper = saturate(h / 0.3);
                float3 p = g.positionWS + g.normalWS * (h * (1.0 + _CrestLift * crest * upper));
                // Слои наклонены по ветру: чем выше, тем дальше снесён рисунок — параллакс, а не стопка пластин.
                p.xz += _HodbaWind.xy * (h * _Drift * _SandDrift.x);

                Varyings o;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = g.normalWS;
                o.shape = half2(g.surface.r, crest);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 layer = _SandDriftLayers[(int)_VeilLayer.x];
                float h = layer.z;
                float3 toPoint = i.positionWS - GetCameraPositionWS();
                float dist = length(toPoint);
                float3 view = toPoint / max(dist, 1e-4);
                half fade = (half)((1.0 - smoothstep(_Fade.x, _Fade.y, dist)) * smoothstep(0.3, max(_Fade.z, 0.31), dist));

                float2 q = HodbaWindCoords(i.positionWS);
                half gust = HodbaWindGust(q);
                float tile = _SandDriftTiles.z;
                float2 uv = (q + layer.xy) * tile;
                // Языки не идут по линейке: второй, крупный шум изгибает их поперёк ветра.
                half bend = SAMPLE_TEXTURE2D(_SandDriftTex, sampler_SandDriftTex, (q + layer.xy) * (tile / 3.0) + float2(0.37, 0.61)).a;
                uv.y += (bend - 0.5h) * 0.6;
                half tongue = SAMPLE_TEXTURE2D(_SandDriftTex, sampler_SandDriftTex, uv).a;

                // Лёгкий ветер — только вершины языков; сильный и порыв — сплошная текучая пелена.
                half amount = (half)_SandDrift.x;
                half cover = lerp(0.8h, -0.1h, saturate(amount * (0.55h + 0.45h * gust)));
                half tongues = smoothstep(cover - 0.15h, cover + 0.3h, tongue);

                half crest = i.shape.y * (half)saturate(h / 0.3);
                half density = tongues * lerp(0.3h, 1.0h, gust) * amount
                    * lerp(0.4h, 1.0h, saturate(i.shape.x))
                    * (half)exp(-h / 0.3) * (1.0h + 1.5h * crest)
                    * fade * (half)_SandDriftTiles.w;
                // Тонкий слой вскользь — длинный путь сквозь него: у горизонта пелена плотнее, чем под ногами.
                half facing = (half)max(abs(dot(view, normalize(i.normalWS))), 0.12);
                half alpha = saturate(1.0h - pow(saturate(1.0h - density), 1.0h / facing));

                float3 L = HodbaSunDirection();
                half forward = pow(saturate(dot(view, L)), 6.0) * (1.0 - saturate(_HodbaSunOcclusion));
                half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.positionWS));
                half sunlight=HodbaWallLightVisibility(i.positionWS,L);
                half3 light = _HodbaAmbient.rgb * lerp(0.65h,1.0h,sunlight)
                    + _HodbaSunColor.rgb * ((0.4h + _Scatter * forward) * HodbaDustShadow(i.positionWS) * shadow * sunlight);
                half3 col = HodbaApplyFog(_SandDriftColor.rgb * light, i.positionWS);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
