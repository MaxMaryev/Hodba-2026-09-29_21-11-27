// Воздух и свет пепельного мира — общие для земли, камней, следов, пыли и неба.
// Дымка светлеет и теплеет к солнцу и остывает от него — так же, как небо у горизонта, без шва.
// Туман плотнее в низинах и реже на возвышенностях; тени высоких пыльных облаков ползут по ветру.
// Пепел и камень шершавые: свет по Орен — Найяру, а не по гладкому Ламберту.
// Все глобальные значения ставят SkyController и DustShadows; пока их нет — тумана и теней облаков нет.
#ifndef HODBA_ATMOSPHERE_INCLUDED
#define HODBA_ATMOSPHERE_INCLUDED

float4 _HodbaOriginMod;
float4 _HodbaSunDir;
half4 _HodbaSunColor;
half4 _HodbaGlowColor;      // ореол солнца, как в небе: цвет солнца × _SunGlow
float4 _HodbaFog;           // x — плотность (exp²), y — 1/высота слоя, м; z — доля тумана без высоты; w — уровень слоя, м
float4 _HodbaHaze;          // x — светлее к солнцу, y — темнее от солнца, z — во сколько раз гуще в низине
float4 _HodbaDustShadow;    // xy — сдвиг по ветру, м; z — 1/тайл, м; w — сила
float4 _HodbaDustShadowShape; // x — высота облаков, м; y — покрытие; z — мягкость края
TEXTURE2D(_HodbaDustShadowTex); SAMPLER(sampler_HodbaDustShadowTex);

// Направление на солнце; пока его не поставили (сцена в редакторе без игры) — ноль, а не NaN.
float3 HodbaSunDirection()
{
    float3 s = _HodbaSunDir.xyz;
    return s * rsqrt(max(dot(s, s), 1e-8));
}

// Ореол вокруг солнца — тот же, что в Hodba/Sky.
half HodbaSunGlow(float cosA)
{
    float c = saturate(cosA);
    return pow(c, 8.0) * 0.18 + pow(c, 64.0) * 0.5 + pow(c, 900.0) * 1.5;
}

// Цвет дымки по направлению взгляда, без ореола: к солнцу светлее и теплее, от солнца — темнее.
half3 HodbaHazeColor(half3 fog, float3 dir)
{
    float c = dot(dir, HodbaSunDirection());
    half away = _HodbaHaze.y * saturate(-c);
    half forward = _HodbaHaze.x * pow(saturate(c), 3.0);
    return fog * (1.0h - away) + _HodbaGlowColor.rgb * forward;
}

// Сколько дымки на луче от глаза до точки. Плотность ~ b + (1 − b)·exp(−(y − уровень)/H), на уровне слоя — как раньше.
half HodbaFogVisibility(float3 positionWS, float dist)
{
    float density = _HodbaFog.x;
    if (density <= 0.0) return 1.0h;
    float k = _HodbaFog.y;
    float layer = 1.0;
    if (k > 0.0)
    {
        // В низине гуще, но не больше чем в _HodbaHaze.z раз.
        float lowest = -log(max(_HodbaHaze.z, 1.0)) / k;
        float y0 = max(GetCameraPositionWS().y - _HodbaFog.w, lowest) * k;
        float y1 = max(positionWS.y - _HodbaFog.w, lowest) * k;
        float e0 = exp(-y0), e1 = exp(-y1);
        float dy = y1 - y0;
        float average = abs(dy) > 1e-3 ? (e0 - e1) / dy : e0;
        layer = lerp(average, 1.0, saturate(_HodbaFog.z));
    }
    float tau = density * layer * dist;
    return (half)exp(-tau * tau);
}

half3 HodbaApplyFog(half3 color, float3 positionWS)
{
    float3 toPoint = positionWS - GetCameraPositionWS();
    float dist = length(toPoint);
    half visibility = HodbaFogVisibility(positionWS, dist);
    float3 dir = toPoint / max(dist, 1e-4);
    half3 fog = HodbaHazeColor(unity_FogColor.rgb, dir) + _HodbaGlowColor.rgb * HodbaSunGlow(dot(dir, HodbaSunDirection()));
    return lerp(fog, color, visibility);
}

// Тень высоких пыльных облаков: поле шума над землёй, спроецированное вдоль солнца и сдвинутое ветром.
// Тайл делит 4096 м — при переносе плавающего центра тени не прыгают.
half HodbaDustShadow(float3 positionWS)
{
    if (_HodbaDustShadow.w <= 0.0) return 1.0h;
    float3 L = HodbaSunDirection();
    float2 p = positionWS.xz + _HodbaOriginMod.xy + L.xz / max(L.y, 0.2) * _HodbaDustShadowShape.x;
    half n = SAMPLE_TEXTURE2D(_HodbaDustShadowTex, sampler_HodbaDustShadowTex, (p + _HodbaDustShadow.xy) * _HodbaDustShadow.z).r;
    half soft = max(_HodbaDustShadowShape.z, 0.01);
    half edge = 1.0h - _HodbaDustShadowShape.y;
    half cover = smoothstep(edge - soft, edge + soft, n);
    return 1.0h - _HodbaDustShadow.w * cover;
}

// Позёмка: песок бежит по земле струями вдоль ветра (Saltation.cs). Координаты ветра вокруг путника:
// x — по ветру, y — поперёк; сдвиги копит CPU, так что рисунок стоит в мире, пока путник идёт.
float4 _HodbaSaltation;        // x — сила (0 — штиль), y — порыв 0..1, z — дальность, м; w — насколько струя закрывает землю
float4 _HodbaSaltationFrame;   // xy — куда дует (xz), zw — путник (локальные xz)
float4 _HodbaSaltationOffset;  // xy — сдвиг мелких струй, zw — крупных, м
float4 _HodbaSaltationFront;   // xy — сдвиг фронтов порыва, м
float4 _HodbaSaltationTiles;   // x, y, z — 1/тайл мелких струй, крупных, фронтов
half4 _HodbaSaltationColor;
TEXTURE2D(_HodbaSaltationTex); SAMPLER(sampler_HodbaSaltationTex);

// Сколько песка бежит в этой точке, 0..1. dxz/dyz — производные мировых xz по экрану (взяты до любых веток).
// looseness — рыхлость из мира: по корке песок почти не бежит. normalWS — склон: наветренный в струях, подветренный в тени.
half HodbaSaltation(float3 positionWS, float dist, float2 dxz, float2 dyz, half looseness, float3 normalWS)
{
    half amount = 0.0h;
    float fade = 1.0 - smoothstep(_HodbaSaltation.z * 0.5, _HodbaSaltation.z, dist);
    UNITY_BRANCH
    if (_HodbaSaltation.x > 0.0 && fade > 0.0 && looseness > 0.3h)
    {
        float2 d = _HodbaSaltationFrame.xy;
        float2 across = float2(-d.y, d.x);
        float2 rel = positionWS.xz - _HodbaSaltationFrame.zw;
        float2 q = float2(dot(rel, d), dot(rel, across));
        float2 qdx = float2(dot(dxz, d), dot(dxz, across));
        float2 qdy = float2(dot(dyz, d), dot(dyz, across));

        float2 t = _HodbaSaltationTiles.xy;
        half2 fine = SAMPLE_TEXTURE2D_GRAD(_HodbaSaltationTex, sampler_HodbaSaltationTex,
            (q + _HodbaSaltationOffset.xy) * t.x, qdx * t.x, qdy * t.x).rg;
        half2 coarse = SAMPLE_TEXTURE2D_GRAD(_HodbaSaltationTex, sampler_HodbaSaltationTex,
            (q + _HodbaSaltationOffset.zw) * t.y, qdx * t.y, qdy * t.y).rg;
        half front = SAMPLE_TEXTURE2D_GRAD(_HodbaSaltationTex, sampler_HodbaSaltationTex,
            (q + _HodbaSaltationFront.xy) * _HodbaSaltationTiles.z, qdx * _HodbaSaltationTiles.z, qdy * _HodbaSaltationTiles.z).b;

        // Нити — только там, где их не рвёт; две сетки на разных скоростях сплетаются.
        half streams = saturate(fine.x * fine.y * 1.7h + coarse.x * coarse.y * 0.9h);
        // Чем сильнее порыв, тем шире фронты; между ними песок едва шевелится.
        half edge = lerp(0.75h, 0.35h, (half)_HodbaSaltation.y);
        half gust = lerp(0.2h, 1.0h, smoothstep(edge - 0.15h, edge + 0.15h, front));

        half loose = smoothstep(0.35h, 0.7h, looseness);
        // Склон к ветру в струях, за гребнем — затишье.
        half facing = dot(normalWS.xz, d);
        half exposure = saturate(1.0h - 3.0h * max(facing, 0.0h)) * (1.0h + 1.5h * max(-facing, 0.0h));

        amount = saturate(_HodbaSaltation.x * gust * streams * loose * exposure * fade);
    }
    return amount;
}

// Шершавая поверхность (Орен — Найяр в приближении Fujii): плоский порошковый свет,
// ярче, когда солнце за спиной. wrap — прежняя мягкость терминатора, 0 — честный край.
half HodbaRoughDiffuse(float3 n, float3 l, float3 v, half sigma, half wrap)
{
    half nlRaw = dot(n, l);
    half nl = saturate(nlRaw);
    half nv = saturate(dot(n, v));
    half s = dot(l, v) - nl * nv;
    half t = s > 0.0h ? max(max(nl, nv), 1e-3h) : 1.0h;
    half s2 = sigma * sigma;
    half A = 1.0h - 0.5h * s2 / (s2 + 0.33h);
    half B = 0.45h * s2 / (s2 + 0.09h);
    // Обратное рассеяние — только с настоящим nl: s/t растёт к скользящим углам, и nl его гасит.
    // Мягкость терминатора (wrap) добавляется отдельно, без него, иначе у горизонта вспыхнул бы ореол.
    half wrapped = saturate((nlRaw + wrap) / (1.0h + wrap));
    return nl * max(0.0h, A + B * s / t) + max(0.0h, wrapped - nl) * A;
}

// Микротени: в углублениях зерна и трещин прямой свет гаснет раньше, чем на гладком (Chan, CoD).
half HodbaMicroShadow(half ao, half nl)
{
    return saturate(abs(nl) + 2.0h * ao * ao - 1.0h);
}

#endif
