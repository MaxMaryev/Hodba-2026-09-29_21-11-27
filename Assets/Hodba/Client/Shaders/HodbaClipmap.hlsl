// Кольца земли вокруг путника (ClipmapTerrain): вершины сетки — целые координаты, высоту и поверхность вершина
// берёт из текстур кольца с кольцевой адресацией. У внешнего края кольцо плавно перетекает в следующее.
// Общие для земли и взвеси над ней: оболочки взвеси лежат на тех же вершинах.
#ifndef HODBA_CLIPMAP_INCLUDED
#define HODBA_CLIPMAP_INCLUDED

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
    half4 surface;
    // Лапласиан высоты на шаг кольца: меньше нуля — выпуклость (гребень), больше — ложбина. В перетекании гаснет.
    float curvature;
};

GroundVertex ClipVertex(float3 positionOS)
{
    float s = _ClipParams.x;
    int2 g = int2(round(positionOS.xz));
    int2 t = g + int2(_ClipOrigin.zw);

    float h = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t)).r;
    half4 surface = LOAD_TEXTURE2D(_ClipSurface, ClipWrap(t));
    float hl = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(-1, 0))).r;
    float hr = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(1, 0))).r;
    float hd = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(0, -1))).r;
    float hu = LOAD_TEXTURE2D(_ClipHeight, ClipWrap(t + int2(0, 1))).r;
    float3 normal = float3(hl - hr, 2.0 * s, hd - hu);
    float curvature = (hl + hr + hd + hu - 4.0 * h) / s;

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
        half4 s00 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn));
        half4 s10 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(1, 0)));
        half4 s01 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(0, 1)));
        half4 s11 = LOAD_TEXTURE2D(_ClipSurfaceNext, ClipWrap(tn + int2(1, 1)));

        float hc = lerp(lerp(h00, h10, fr.x), lerp(h01, h11, fr.x), fr.y);
        half4 sc = lerp(lerp(s00, s10, fr.x), lerp(s01, s11, fr.x), fr.y);
        float3 nc = float3(-(h10 - h00 + h11 - h01) * 0.5, s2, -(h01 - h00 + h11 - h10) * 0.5);

        h = lerp(h, hc, a);
        surface = lerp(surface, sc, a);
        normal = lerp(normalize(normal), normalize(nc), a);
        curvature *= 1.0 - a;
    }

    GroundVertex o;
    o.positionWS = float3(xz.x, h, xz.y);
    o.normalWS = normalize(normal);
    o.surface = surface;
    o.curvature = curvature;
    return o;
}

#endif
