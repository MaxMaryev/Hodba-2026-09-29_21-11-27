// Заставка: пепельный горизонт в сумерках, из дымки проступает «ХОДЬБА» и в конце разлетается по ветру.
// Рисуется одним прямоугольником на весь экран поверх мира (Canvas, Screen Space Overlay), без текстур, кроме поля расстояний надписи.
// Всё считается здесь: небо, тусклое солнце в пепле, дюны, дымка, пылинки, зерно.
// _Params: x — проступание надписи 0..1, y — распад по ветру 0..1, z — непрозрачность фона (1 → 0 открывает мир), w — время, с.
// _Sdf: x — расстояние, которое хранит текстура, в долях ширины; y — полутолщина штриха; z — ширина ореола.
Shader "Hodba/Title"
{
    Properties
    {
        _Glyphs ("Glyph distance field", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Glyphs;
            float4 _Params;
            float4 _Sdf;
            float4 _Frame; // x — отношение сторон текстуры надписи (ширина / высота)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float a = 0.5, s = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return s;
            }

            // Штрих надписи в точке g (в долях текстуры): x — линия, y — ореол.
            float2 Glyph(float2 g)
            {
                float aa = max(fwidth(g.x) * 0.75, 1e-5);
                if (g.x < 0 || g.x > 1 || g.y < 0 || g.y > 1) return float2(0, 0);
                float d = tex2Dlod(_Glyphs, float4(g, 0, 0)).r * _Sdf.x;
                float stroke = 1.0 - smoothstep(_Sdf.y - aa, _Sdf.y + aa, d);
                float halo = exp(-d / _Sdf.z) * step(d, _Sdf.x * 0.99);
                return float2(stroke, halo);
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Params.w;
                float appear = _Params.x;
                float blow = _Params.y;
                float fade = _Params.z;
                float asp = _ScreenParams.x / _ScreenParams.y;

                // Высота экрана — единица, начало в центре, y вверх.
                float2 p = float2((i.uv.x - 0.5) * asp, i.uv.y - 0.5);

                float horizon = -0.16;
                float farDune = vnoise(float2(p.x * 2.2 + 3.1, 1.7)) * 0.6 + vnoise(float2(p.x * 7.0, 9.3)) * 0.4;
                float dune = horizon + 0.018 * (farDune - 0.5);

                float intro = smoothstep(0.0, 1.8, t);
                float dusk = 0.55 + 0.45 * smoothstep(0.5, 4.5, t);

                // Небо: выцветший свод, тёплый край, тусклое солнце в пепле.
                float3 zenith = float3(0.035, 0.043, 0.065);
                float3 rim = float3(0.50, 0.38, 0.33);
                float h = saturate((p.y - dune) / 0.75);
                float3 sky = lerp(rim * dusk, zenith, pow(h, 0.42));
                float2 sd = (p - float2(0.0, dune + 0.07)) * float2(1.0, 1.7);
                float sun = length(sd);
                sky += float3(1.0, 0.72, 0.5) * (exp(-sun * 3.4) * 0.30 + exp(-sun * 16.0) * 0.45) * dusk;

                // Земля: тёмная, с лёгкой рябью по перспективе.
                float depth = max(dune - p.y, 0.0);
                float2 gp = float2(p.x / (depth + 0.09), 1.0 / (depth + 0.09));
                float ripple = vnoise(gp * float2(0.9, 0.7) + float2(t * 0.02, 0.0));
                float3 ground = lerp(float3(0.060, 0.050, 0.048), rim * 0.16 * dusk, exp(-depth * 7.0));
                ground *= 0.8 + 0.4 * ripple;

                float3 col = lerp(ground, sky, smoothstep(-0.004, 0.004, p.y - dune));

                // Дымка плывёт вдоль горизонта.
                float env = exp(-abs(p.y - dune - 0.03) * 4.2);
                float fog = fbm(float2(p.x * 1.1 - t * 0.04, p.y * 3.6 + 7.0));
                float3 fogCol = lerp(rim * 0.9 * dusk, float3(0.5, 0.45, 0.42), 0.35);
                col = lerp(col, fogCol, saturate(fog * 1.1 - 0.15) * 0.6 * env);

                // Надпись.
                float hx = min(0.30 * asp, 0.52);
                float hy = hx / _Frame.x;
                float2 c = float2(0.0, 0.07);
                float2 g = (p - c) / (2.0 * float2(hx, hy)) + 0.5;

                float q = g.x * 0.8 + vnoise(g * float2(70.0, 22.0)) * 0.35;
                float reveal = saturate((appear * 1.45 - q) / 0.2);

                float wind = vnoise(g * float2(6.0, 3.0) + 5.0);
                float lift = blow * blow;
                float2 ln = float2(0, 0);
                [unroll] for (int k = 0; k < 3; k++)
                {
                    // Каждый штрих тянется по ветру: ближние отсчёты — от самой буквы, дальние — шлейф.
                    float2 gk = g - float2(lift * (0.10 + 0.9 * wind) * (0.2 + 0.4 * k), 0);
                    gk.y += (hash(floor(gk * float2(90.0, 30.0))) - 0.5) * blow * 0.10;
                    float2 s = Glyph(gk);
                    float w = 1.0 - 0.32 * k;
                    ln = max(ln, s * w);
                }
                float letterGone = 1.0 - smoothstep(0.30, 1.0, blow + wind * 0.35);
                float breath = 0.92 + 0.08 * sin(t * 0.7);
                float vis = (1.0 - saturate(fog * 1.4) * 0.45) * reveal * letterGone * breath;

                float3 bone = float3(0.90, 0.87, 0.80);
                col += float3(0.85, 0.52, 0.34) * ln.y * 0.28 * reveal * letterGone;
                col = lerp(col, bone, saturate(ln.x) * vis);

                // Линия горизонта растёт от центра к краям.
                float draw = smoothstep(0.8, 4.0, t);
                float span = hx * 1.9 * draw;
                float edge = 1.0 - smoothstep(span * 0.5, span, abs(p.x));
                float hair = exp(-abs(p.y - dune) * 260.0) * edge * 0.55;
                col += float3(0.92, 0.75, 0.62) * hair * (1.0 - blow);

                // Пылинки: ветер несёт их слева направо, а к концу крепчает.
                float moteA = 0.0;
                float3 moteCol = float3(0.95, 0.80, 0.65);
                float speed = 0.05 + blow * 0.9;
                [unroll] for (int m = 0; m < 2; m++)
                {
                    float scale = 22.0 + m * 17.0;
                    float2 mp = p * scale + float2(-t * speed * (1.0 + m * 0.7) * scale * 0.1, t * 0.03 * scale * 0.1);
                    float2 cell = floor(mp);
                    float2 f = frac(mp);
                    float on = step(0.80 - blow * 0.15, hash(cell + m * 31.7));
                    float2 at = float2(hash(cell + 7.1), hash(cell + 13.3)) * 0.6 + 0.2;
                    float size = lerp(0.05, 0.13, hash(cell + 3.9)) * (1.0 + m * 0.3);
                    float d = length((f - at) * float2(1.0 + blow * 5.0, 1.0));
                    float a = on * smoothstep(size, 0.0, d) * (0.25 + 0.5 * hash(cell + 21.0));
                    moteA = max(moteA, a * smoothstep(-0.30, 0.0, p.y - dune + 0.30));
                }
                moteA *= 0.55 * intro;

                col *= 1.0 - smoothstep(0.45, 1.15, length(p * float2(0.8, 1.1))) * 0.7;
                col += (hash(i.uv * _ScreenParams.xy + frac(t) * 61.0) - 0.5) * 0.035;
                col = max(col * intro, 0.0);

                float open = saturate(fade);
                float3 rgb = col * open * (1.0 - moteA) + moteCol * moteA * 0.9;
                float alpha = open * (1.0 - moteA) + moteA;
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
