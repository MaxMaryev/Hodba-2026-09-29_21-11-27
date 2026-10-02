// Пустыня отвечает ветру (SandDrift.cs): позёмка по земле и взвесь над ней. Ветер о песке ничего не знает —
// фронты порывов берутся из поля ветра (HodbaWind.hlsl), слои песка бегут в его осях со своими сдвигами.
#ifndef HODBA_SAND_INCLUDED
#define HODBA_SAND_INCLUDED

#include "HodbaWind.hlsl"

float4 _SandDrift;          // x — взвесь 0..1, y — позёмка (0 — песок лежит), z — дальность нитей, м; w — насколько струя закрывает землю
float4 _SandDriftOffset;    // xy — сдвиг мелких струй, zw — крупных, м
float4 _SandDriftTiles;     // x, y — 1/тайл мелких и крупных струй; z — 1/тайл языков взвеси; w — плотность пелены
half4 _SandDriftColor;
float4 _SandDriftLayers[3]; // xy — сдвиг языков слоя, м; z — высота, м; w — доля скорости ветра у глаз
TEXTURE2D(_SandDriftTex); SAMPLER(sampler_SandDriftTex);

// Сколько песка бежит в этой точке, 0..1. dxz/dyz — производные мировых xz по экрану (взяты до любых веток).
// looseness — рыхлость из мира: по корке песок не бежит. normalWS — склон: наветренный в струях, подветренный в тени.
// Мелкая прерывистая позёмка гаснет с расстоянием, без сплошного осветления грунта фронтом порыва.
half HodbaSaltation(float3 positionWS, float dist, float2 dxz, float2 dyz, half looseness, float3 normalWS)
{
    half amount = 0.0h;
    float far = _SandDrift.z * 2.5;
    UNITY_BRANCH
    if (_SandDrift.y > 0.0 && dist < far && looseness > 0.3h)
    {
        float2 q = HodbaWindCoords(positionWS);
        half gust = HodbaWindGust(q);
        half loose = smoothstep(0.35h, 0.7h, looseness);
        half facing = dot(normalWS.xz, _HodbaWind.xy);
        half exposure = saturate(1.0h - 3.0h * max(facing, 0.0h)) * (1.0h + 1.5h * max(-facing, 0.0h));

        half threads = 0.0h;
        float near = 1.0 - smoothstep(_SandDrift.z * 0.5, _SandDrift.z, dist);
        UNITY_BRANCH
        if (near > 0.0)
        {
            float2 qdx = HodbaWindRotate(dxz), qdy = HodbaWindRotate(dyz);
            float2 t = _SandDriftTiles.xy;
            half2 fine = SAMPLE_TEXTURE2D_GRAD(_SandDriftTex, sampler_SandDriftTex,
                (q + _SandDriftOffset.xy) * t.x, qdx * t.x, qdy * t.x).rg;
            half2 coarse = SAMPLE_TEXTURE2D_GRAD(_SandDriftTex, sampler_SandDriftTex,
                (q + _SandDriftOffset.zw) * t.y, qdx * t.y, qdy * t.y).rg;
            half streams = saturate(fine.x * fine.y + coarse.x * coarse.y * 0.35h);
            threads = streams * lerp(0.25h, 1.0h, gust) * (half)near;
        }
        amount = saturate((half)_SandDrift.y * threads * loose * exposure);
    }
    return amount;
}

#endif
