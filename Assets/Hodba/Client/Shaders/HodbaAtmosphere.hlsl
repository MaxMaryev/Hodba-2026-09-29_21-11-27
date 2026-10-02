// Воздух и свет пепельного мира — общие для земли, камней, следов, пыли и неба.
// Дымка светлеет и теплеет к солнцу и остывает от него — так же, как небо у горизонта, без шва.
// Туман плотнее в низинах и реже на возвышенностях; тени высоких пыльных облаков ползут по ветру.
// Пепел и камень шершавые: свет по Орен — Найяру, а не по гладкому Ламберту.
// Все глобальные значения ставят SkyController и DustShadows; пока их нет — тумана и теней облаков нет.
#ifndef HODBA_ATMOSPHERE_INCLUDED
#define HODBA_ATMOSPHERE_INCLUDED
#define HODBA_WALL_LIGHTING

float4 _HodbaOriginMod;
float4 _HodbaSunDir;
float _HodbaSunOcclusion;   // 0 by default; 1 when the sun is hidden from the eye
float4 _HodbaWall;          // local centre X, half thickness, panel top, enabled
float4 _HodbaWallDetails;   // pier depth, half width, bay length, pier top
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

bool HodbaWallRayAxis(float p, float d, float lo, float hi, inout float nearT, inout float farT)
{
    if (abs(d) < 1e-6) return p >= lo && p <= hi;
    float a = (lo-p)/d, b = (hi-p)/d;
    nearT=max(nearT,min(a,b)); farT=min(farT,max(a,b));
    return nearT <= farT;
}

bool HodbaWallRayInterval(float3 p, float3 L, float halfWidth, float top, out float nearT, out float farT)
{
    nearT=0; farT=1e20;
    return HodbaWallRayAxis(p.x-_HodbaWall.x,L.x,-halfWidth,halfWidth,nearT,farT)
        && HodbaWallRayAxis(p.y,L.y,-100,top,nearT,farT) && farT > 0.001;
}

// Analytic solid-wall shadow: works beyond URP's short cascades and on airborne dust.
half HodbaWallLightVisibility(float3 p, float3 L)
{
    if (_HodbaWall.w < 0.5 || dot(L,L) < 1e-8) return 1;
    float nearT, farT;
    if (HodbaWallRayInterval(p,L,_HodbaWall.y,_HodbaWall.z,nearT,farT)) return 0;
    if (!HodbaWallRayInterval(p,L,_HodbaWall.y+_HodbaWallDetails.x,_HodbaWallDetails.w,nearT,farT)) return 1;
    float z=p.z+_HodbaOriginMod.y, period=max(_HodbaWallDetails.z,1), halfWidth=_HodbaWallDetails.y;
    if (abs(L.z) < 1e-6) return abs(z-round(z/period)*period) <= halfWidth ? 0 : 1;
    if (farT > 1e19) return 0;
    float a=z+L.z*nearT, b=z+L.z*farT;
    return ceil((min(a,b)-halfWidth)/period)*period <= max(a,b)+halfWidth ? 0 : 1;
}

// Infinite vertical plane's angular obstruction; outward-facing walls retain open sky.
half HodbaWallAmbientOcclusion(float3 p, float3 n)
{
    if (_HodbaWall.w < 0.5) return 1;
    float height=max(_HodbaWallDetails.w-p.y,0);
    float distance=max(abs(p.x-_HodbaWall.x)-_HodbaWall.y,0.1);
    float blocked=atan2(height,distance)/PI;
    float toward=sign(_HodbaWall.x-p.x);
    float facing=saturate(0.55+0.45*n.x*toward);
    return saturate(1-blocked*facing*1.65);
}

half3 HodbaWallAmbientLight(half3 ambient, float3 p, float3 n, float3 L)
{
    half lit=HodbaWallLightVisibility(p+n*0.2,L);
    // Shaded sand contributes less warm bounce; diffuse blue sky light remains.
    half3 bounce=lerp(half3(0.58,0.70,0.85),half3(1,1,1),lit);
    return ambient*bounce*HodbaWallAmbientOcclusion(p,n);
}

half HodbaFogLightVisibility(float3 eye, float3 endpoint)
{
    if (_HodbaWall.w < 0.5) return 1;
    float total=0, illuminated=0;
    float3 L=HodbaSunDirection();
    [unroll] for (int k=0;k<4;k++)
    {
        float3 p=lerp(eye,endpoint,(k+0.5)*0.25);
        float height=max((p.y-_HodbaFog.w)*_HodbaFog.y,-log(max(_HodbaHaze.z,1.0)));
        float density=lerp(exp(-height),1.0,saturate(_HodbaFog.z));
        illuminated+=density*HodbaWallLightVisibility(p,L);
        total+=density;
    }
    return illuminated/max(total,1e-4);
}

// Ореол вокруг солнца — тот же, что в Hodba/Sky.
half HodbaSunGlow(float cosA)
{
    float c = saturate(cosA);
    return (pow(c, 8.0) * 0.18 + pow(c, 64.0) * 0.5 + pow(c, 900.0) * 1.5) * (1.0 - saturate(_HodbaSunOcclusion));
}

// Цвет дымки по направлению взгляда, без ореола: к солнцу светлее и теплее, от солнца — темнее.
half3 HodbaHazeColor(half3 fog, float3 dir)
{
    float c = dot(dir, HodbaSunDirection());
    half away = _HodbaHaze.y * saturate(-c);
    half forward = _HodbaHaze.x * pow(saturate(c), 3.0) * (1.0 - saturate(_HodbaSunOcclusion));
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
    if (visibility > 0.999h) return color;
    half sunlit=HodbaFogLightVisibility(GetCameraPositionWS(),positionWS);
    // Extinction stays the same; only light scattered into the view is reduced in shadow.
    half3 scattered=unity_FogColor.rgb*lerp(half3(0.30,0.39,0.53),half3(1,1,1),sunlit);
    half3 fog = HodbaHazeColor(scattered, dir) + _HodbaGlowColor.rgb
        * HodbaSunGlow(dot(dir, HodbaSunDirection())) * sunlit;
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
