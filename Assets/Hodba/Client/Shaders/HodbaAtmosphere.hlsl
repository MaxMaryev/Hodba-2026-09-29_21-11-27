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

// Circular solar disc, angular radius 0.27 degrees. Signed sine is accurate
// enough at this angle; this is a segment area, not a spatial blur.
float HodbaSunDiscCoverage(float signedSine)
{
    float x=signedSine/0.00471239;
    if (x <= -1) return 0;
    if (x >= 1) return 1;
    return 0.5+(asin(x)+x*sqrt(max(1-x*x,0)))/PI;
}

float HodbaWallEdgeCoverage(float distance, float height, float toward, float sunY)
{
    float lengthToEdge=max(length(float2(distance,height)),1e-5);
    return HodbaSunDiscCoverage((height*toward-distance*sunY)/lengthToEdge);
}

// The infinite panel silhouette, including its buried lower edge.
float HodbaWallPanelCoverage(float2 p, float2 L, float halfWidth, float top)
{
    float coverage=1;
    if (abs(p.x) <= halfWidth)
    {
        if (p.y >= top) coverage=HodbaSunDiscCoverage(-L.y);
        else if (p.y <= -100) coverage=HodbaSunDiscCoverage(L.y);
    }
    else
    {
        float distance=abs(p.x)-halfWidth;
        float toward=-sign(p.x)*L.x;
        float facing=HodbaSunDiscCoverage(toward);
        float upper=HodbaWallEdgeCoverage(distance,top-p.y,toward,L.y);
        float lower=HodbaWallEdgeCoverage(distance,p.y+100,toward,-L.y);
        coverage=facing*upper*lower;
    }
    return coverage;
}

// Project both near and far corners of a pier. Side and top disc segments
// are multiplied: a small, bounded approximation near silhouette corners.
float HodbaWallPierCoverage(float3 p, float3 L, float halfX, float halfZ, float top)
{
    float coverage=0;
    if (abs(p.x) <= halfX && abs(p.z) <= halfZ)
    {
        coverage=HodbaWallPanelCoverage(p.xy,L.xy,halfX,top);
    }
    else
    {
        float a=p.x, b=p.z, la=L.x, lb=L.z, ha=halfX, hb=halfZ;
        if (abs(a) <= ha)
        {
            a=p.z; b=p.x; la=L.z; lb=L.x; ha=halfZ; hb=halfX;
        }
        float distance=max(abs(a)-ha,1e-5), farDistance=abs(a)+ha;
        float toward=-sign(a)*la;
        float vertical=HodbaWallPanelCoverage(float2(a,p.y),float2(la,L.y),ha,top);
        if (vertical > 0)
        {
            float left=-hb-b, right=hb-b;
            float dl=left < 0 ? distance : farDistance;
            float dr=right > 0 ? distance : farDistance;
            float fromLeft=HodbaSunDiscCoverage((dl*lb-left*toward)/max(length(float2(dl,left)),1e-5));
            float fromRight=HodbaSunDiscCoverage((dr*lb-right*toward)/max(length(float2(dr,right)),1e-5));
            coverage=vertical*saturate(fromLeft-fromRight);
        }
    }
    return coverage;
}

// Soft analytic wall shadow beyond URP cascades. All receivers use the same
// finite sun disc; Unity shadow casting stays enabled for standard URP Lit.
half HodbaWallLightVisibility(float3 p, float3 L)
{
    if (_HodbaWall.w < 0.5 || dot(L,L) < 1e-8) return 1;
    L=normalize(L);
    p.x-=_HodbaWall.x;
    p.z+=_HodbaOriginMod.y;
    float panel=HodbaWallPanelCoverage(p.xy,L.xy,_HodbaWall.y,_HodbaWall.z);
    if (panel >= 0.9999) return 0;
    float halfX=_HodbaWall.y+_HodbaWallDetails.x;
    float period=max(_HodbaWallDetails.z,1);
    float toward=-sign(p.x)*L.x;
    float t=max(abs(p.x)-halfX,0)/max(toward,1e-5);
    float centre=round((p.z+L.z*t)/period)*period;
    // When the sun travels along the wall, select nearby Z faces directly.
    if (toward < 1e-4) centre=round(p.z/period)*period;
    float piers=0;
    [unroll] for(int k=-1;k<=1;k++)
        piers+=HodbaWallPierCoverage(float3(p.x,p.y,p.z-centre-k*period),L,
            halfX,_HodbaWallDetails.y,_HodbaWallDetails.w);
    return (1-panel)*(1-saturate(piers));
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

// Clip a view segment against one shadow half-space: f(t) >= 0.
bool HodbaClipShadowPlane(float atEye, float alongRay, inout float lo, inout float hi)
{
    if (abs(alongRay) < 1e-7) return atEye >= 0;
    float t=-atEye/alongRay;
    if (alongRay > 0) lo=max(lo,t); else hi=min(hi,t);
    return lo < hi;
}

float HodbaMeanExponential(float a, float b)
{
    float delta=b-a;
    return abs(delta) < 1e-3 ? exp(-(a+b)*0.5) : (exp(-a)-exp(-b))/delta;
}

// Integral of the exact clamped height density over [lo,hi] on a view ray.
// Splitting at the density floor also handles rays that cross a low basin.
float HodbaFogDensityIntegral(float eyeY, float endY, float lo, float hi)
{
    float span=max(hi-lo,0);
    if (_HodbaFog.y <= 0) return span;
    float a=(lerp(eyeY,endY,lo)-_HodbaFog.w)*_HodbaFog.y;
    float b=(lerp(eyeY,endY,hi)-_HodbaFog.w)*_HodbaFog.y;
    float cap=max(_HodbaHaze.z,1), floorHeight=-log(cap);
    float average;
    if (max(a,b) <= floorHeight) average=cap;
    else if (min(a,b) >= floorHeight) average=HodbaMeanExponential(a,b);
    else
    {
        float crossing=saturate((floorHeight-a)/(b-a));
        average=a < floorHeight
            ? cap*crossing+HodbaMeanExponential(floorHeight,b)*(1-crossing)
            : HodbaMeanExponential(a,floorHeight)*crossing+cap*(1-crossing);
    }
    return span*lerp(average,1.0,saturate(_HodbaFog.z));
}

half HodbaFogLightVisibility(float3 eye, float3 endpoint)
{
    if (_HodbaWall.w < 0.5) return 1;
    float3 L=HodbaSunDirection();
    if (dot(L,L) < 1e-8) return 1;
    float2 p=float2(eye.x-_HodbaWall.x,eye.y);
    float2 d=float2(endpoint.x-eye.x,endpoint.y-eye.y);
    float lo=0,hi=1, halfWidth=_HodbaWall.y, top=_HodbaWall.z;
    // Infinite-Z box extruded away from the sun: convex shadow prism.
    // The lower slanted plane matters only below the buried foundation.
    float sx=L.x >= 0 ? 1 : -1, sy=L.y >= 0 ? 1 : -1;
    if (!HodbaClipShadowPlane(halfWidth-sx*p.x,-sx*d.x,lo,hi)) return 1;
    float yLimit=sy > 0 ? top : -100;
    if (!HodbaClipShadowPlane(sy*(yLimit-p.y),-sy*d.y,lo,hi)) return 1;
    float2 n=float2(-L.y,L.x);
    float support=abs(n.x)*halfWidth+(n.y >= 0 ? n.y*top : n.y*(-100));
    if (!HodbaClipShadowPlane(support-dot(n,p),-dot(n,d),lo,hi)) return 1;
    n=-n;
    support=abs(n.x)*halfWidth+(n.y >= 0 ? n.y*top : n.y*(-100));
    if (!HodbaClipShadowPlane(support-dot(n,p),-dot(n,d),lo,hi)) return 1;
    // HLSL boolean operators need not short-circuit: keep inout clipping
    // inside its branch, or a non-parallel sun incorrectly clips to the box.
    if (abs(L.x) < 1e-6)
    {
        if (!HodbaClipShadowPlane(halfWidth+sx*p.x,sx*d.x,lo,hi)) return 1;
    }
    if (abs(L.y) < 1e-6)
    {
        if (!HodbaClipShadowPlane(sy*(p.y-yLimit)+(top+100),sy*d.y,lo,hi)) return 1;
    }
    float total=HodbaFogDensityIntegral(eye.y,endpoint.y,0,1);
    float shaded=HodbaFogDensityIntegral(eye.y,endpoint.y,lo,hi);
    // Thin pier shadows in the air are intentionally omitted, not sampled.
    return saturate(1-shaded/max(total,1e-8));
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
    float layer=HodbaFogDensityIntegral(GetCameraPositionWS().y,positionWS.y,0,1);
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
