// Небо пепельного мира. Выцветший зенит, пыльная дымка у горизонта того же цвета, что туман над землёй,
// диск солнца с ореолом. Ночью — звёзды чужого неба.
// Дымка и ореол — те же функции, что у тумана над землёй (HodbaAtmosphere.hlsl): горизонт без шва.
Shader "Hodba/Sky"
{
    Properties
    {
        _ZenithColor ("Зенит", Color) = (0.54, 0.62, 0.70, 1)
        _HorizonColor ("Горизонт", Color) = (0.85, 0.83, 0.79, 1)
        _HazeColor ("Дымка (= туман)", Color) = (0.81, 0.78, 0.74, 1)
        _SunColor ("Солнце", Color) = (1, 0.95, 0.88, 1)
        _SunDir ("Направление на солнце", Vector) = (0, 0.5, 0.87, 0)
        _SunSize ("Диск солнца, °", Float) = 0.6
        _SunGlow ("Ореол", Float) = 1
        _HazeHeight ("Высота дымки", Range(0.01, 0.5)) = 0.12
        _HorizonCurve ("Переход к зениту", Range(0.1, 2)) = 0.45
        _StarIntensity ("Звёзды", Float) = 0
        _StarDensity ("Редкость звёзд", Range(0.99, 0.9999)) = 0.995
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HodbaAtmosphere.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _HazeColor;
                half4 _SunColor;
                float4 _SunDir;
                float _SunSize;
                half _SunGlow;
                half _HazeHeight;
                half _HorizonCurve;
                half _StarIntensity;
                float _StarDensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(y), _HorizonCurve));
                half haze = exp(-max(y, 0.0) / _HazeHeight);
                sky = lerp(sky, HodbaHazeColor(_HazeColor.rgb, d), haze);

                // Солнце: диск и ореол. В дымке у горизонта диск тускнеет и краснеет вместе с цветом солнца.
                float3 sunDir = normalize(_SunDir.xyz);
                float cosA = dot(d, sunDir);
                float sunCos = cos(radians(_SunSize));
                half disc = saturate((cosA - sunCos) / max(1e-5, (1.0 - sunCos) * 0.25));
                half glow = HodbaSunGlow(cosA);
                half aboveHorizon = saturate(y * 40.0 + 0.5);
                sky += _SunColor.rgb * (disc * 30.0 * aboveHorizon * (1.0 - haze * 0.6) + glow * _SunGlow);

                // Звёзды. Сетка на гранях куба: у каждой звезды своя клетка, и звезда целиком внутри неё.
                // Звезда не тоньше пикселя, иначе она проваливается между пикселями и неба не видно.
                if (_StarIntensity > 0.001)
                {
                    const float grid = 200.0; // клеток на полграни, клетка ≈ 0.29°
                    float3 a = abs(d);
                    float major = max(a.x, max(a.y, a.z));
                    float pix = length(fwidth(d)) * grid / (major * major); // пиксель в клетках; считаем до ветвления

                    float2 uv; float face;
                    if (a.x >= a.y && a.x >= a.z) { uv = d.yz / a.x; face = d.x > 0.0 ? 0.0 : 1.0; }
                    else if (a.y >= a.z)          { uv = d.xz / a.y; face = d.y > 0.0 ? 2.0 : 3.0; }
                    else                          { uv = d.xy / a.z; face = d.z > 0.0 ? 4.0 : 5.0; }

                    float2 g = uv * grid;
                    float2 cell = floor(g);
                    float h = Hash31(float3(cell, face * 1000.0 + 17.0));
                    if (h > _StarDensity)
                    {
                        float u = (h - _StarDensity) / (1.0 - _StarDensity);
                        float2 jitter = float2(Hash31(float3(cell, face + 3.1)), Hash31(float3(cell.yx, face + 7.7))) - 0.5;
                        float2 center = cell + 0.5 + jitter * 0.3;
                        float s = max(0.06, pix);
                        float2 off = g - center;
                        half core = exp(-2.0 * dot(off, off) / (s * s));
                        half bright = 0.15 + 2.2 * u * u * u * u; // почти все тусклые, яркие редки
                        half twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + h * 7.0) + h * 91.0);
                        sky += core * bright * twinkle * _StarIntensity * saturate(y * 5.0) * (1.0 - haze);
                    }
                }

                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
