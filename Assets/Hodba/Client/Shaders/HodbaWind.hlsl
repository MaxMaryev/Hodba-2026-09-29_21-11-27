// Ветер — поле над пустыней (Wind.cs). Кадр ветра вокруг путника: x — по ветру, y — поперёк.
// Фронты порывов — та же текстура, что CPU читает у путника: видимый фронт приходит вместе со звуком и прищуром.
#ifndef HODBA_WIND_INCLUDED
#define HODBA_WIND_INCLUDED

float4 _HodbaWind;       // xy — куда дует (xz), z — скорость у глаз, м/с; w — сила 0..1
float4 _HodbaWindFrame;  // xy — путник (локальные xz)
float4 _HodbaWindFront;  // xy — сдвиг фронтов, м; z — 1/тайл; w — доля порыва в силе ветра
TEXTURE2D(_HodbaWindFrontTex); SAMPLER(sampler_HodbaWindFrontTex);

// Вектор из мировых xz в оси ветра.
float2 HodbaWindRotate(float2 v)
{
    float2 d = _HodbaWind.xy;
    return float2(dot(v, d), dot(v, float2(-d.y, d.x)));
}

// Точка в осях ветра относительно путника, без сдвигов: каждый слой прибавляет свой.
float2 HodbaWindCoords(float3 positionWS)
{
    return HodbaWindRotate(positionWS.xz - _HodbaWindFrame.xy);
}

// Порыв 0..1 в точке q (HodbaWindCoords). Без мипов: тексель — метры, мерцать нечему; годится и в вершине.
half HodbaWindGust(float2 q)
{
    half n = SAMPLE_TEXTURE2D_LOD(_HodbaWindFrontTex, sampler_HodbaWindFrontTex, (q + _HodbaWindFront.xy) * _HodbaWindFront.z, 0).r;
    return smoothstep(0.35h, 0.85h, n);
}

#endif
