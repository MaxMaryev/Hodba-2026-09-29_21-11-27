// Рябь пепла светом: крупная волна 640 мм — та же, по которой идут ноги, мелкая 100 мм — только картинка.
// Длины постоянны: фаза — координата, делённая на длину; переменная длина умножилась бы на 4096 м и рвала гребни.
// Гребни петляют сдвигом мира (вершины кольца, его наклон — экранные производные: сдвиг мал и плавен) и,
// у мелкой, изгибом из шума (TextureGen.RippleNoise, наклон в нём готовый — изломов на текселях нет).
// Каждый гребень живёт сам по себе: высота и обрывы — хэш его номера и шум вдоль него (MicroRelief.CrestMask).
// Гребни сменяют друг друга во впадине, где волна — ноль: маска скачет без следа.
// Фаза считается от локальной x: центр мира переносится шагами по 512 м — кратно длинам и клеткам (FloatingOrigin.SnapMm),
// поэтому гребни не прыгают при переносе, а числа у ног малые и точные; номера у центра мира — в _RippleIndex.
#ifndef HODBA_RIPPLE_INCLUDED
#define HODBA_RIPPLE_INCLUDED

static const float HodbaMegaRippleLength = 0.64; // SurfaceSample.RippleLengthMm
static const float HodbaMegaCrestCell = 3.2;     // MicroRelief.CrestCellMm
static const float HodbaRippleShiftMax = 0.7;    // SurfaceSample.RippleShiftMaxMm
static const float HodbaCrestMean = 0.22;        // MicroRelief.CrestMeanQ
static const float HodbaFineRippleLength = 0.1;  // ClipmapTerrain.FineRippleLengthMm
static const float HodbaFineCrestCell = 0.8;     // ClipmapTerrain.FineCrestCellMm
static const float HodbaRippleWarp = 0.12;       // ± м, канал R шума
static const float HodbaRippleWarpSlope = 0.6;   // ± м/м, каналы G и B шума

// Кольцо — на каждый вызов отрисовки (ClipmapTerrain).
float4 _RippleIndex; // номер гребня и клетки вдоль него у центра мира: xy — крупная, zw — мелкая
float4 _RippleSeed;  // начало хэша (Hash.Cell) по 16 бит: xy — крупная (как в мире), zw — мелкая

// Сдвиг гребней из байта кольца (ClipmapLevel.ShiftByte).
float HodbaRippleShift(float encoded)
{
    return (encoded * 2.0 - 1.0) * HodbaRippleShiftMax;
}

// Градиент плавной величины по мировым XZ из экранных производных; dx, dy — ddx/ddy локальных XZ.
float2 HodbaWorldGrad(float value, float2 dx, float2 dy)
{
    float dvx = ddx(value), dvy = ddy(value);
    float det = dx.x * dy.y - dx.y * dy.x;
    if (abs(det) < 1e-12) return 0.0;
    return float2(dvx * dy.y - dvy * dx.y, dvy * dx.x - dvx * dy.x) / det;
}

// Волна видна, пока на неё приходится больше 2–6 пикселей; дальше она дала бы муар.
half HodbaRippleVisible(float length, float footprint)
{
    return smoothstep(2.0, 6.0, length / max(footprint, 1e-6));
}

// Hash.Mix / Hash.Cell — побитно как в мире.
uint HodbaMix(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float HodbaCellUnit(uint x, uint z, uint start)
{
    uint h = HodbaMix(start ^ x);
    h = HodbaMix(h ^ z * 0xC2B2AE35u);
    return (h >> 16) / 65536.0;
}

// Маска гребня номер crest (локальный) в точке z: x — 0..1, y — её наклон вдоль z, на метр.
float2 HodbaCrestMask(float crest, float z, float cell, float2 index, float2 seed)
{
    uint start = ((uint)seed.y << 16) | (uint)seed.x;
    float zc = z / cell;
    float czLocal = floor(zc);
    float t = zc - czLocal;
    uint k = (uint)((int)crest + (int)index.x) & 0xFFFFFu;
    uint cz = (uint)((int)czLocal + (int)index.y) & 0xFFFFFu;
    float a = HodbaCellUnit(k, cz, start);
    float b = HodbaCellUnit(k, (cz + 1u) & 0xFFFFFu, start);
    float n = lerp(a, b, t * t * (3.0 - 2.0 * t));
    float dn = (b - a) * 6.0 * t * (1.0 - t) / cell;
    float u = saturate((n - 0.28) / 0.17);
    float on = u * u * (3.0 - 2.0 * u);
    float don = 6.0 * u * (1.0 - u) / 0.17;
    return float2(on * (0.5 + 0.5 * n), (don * (0.5 + 0.5 * n) + on * 0.5) * dn);
}

// Профиль 70/30: x — высота 0..1, y — производная по фазе. Подъём пологий, спад крутой, на стыке ноль.
float2 HodbaRippleProfile(float phase)
{
    float p = frac(phase);
    float t = p < 0.7 ? p / 0.7 : (1.0 - p) / 0.3;
    float s = t * t * (3.0 - 2.0 * t);
    float ds = 6.0 * t * (1.0 - t);
    return float2(s, p < 0.7 ? ds / 0.7 : -ds / 0.3);
}

// Возмущение нормали (−∇h по мировым XZ) от гребней со своими масками. Амплитуда — от впадины до гребня, м.
float2 HodbaCrests(float phase, float2 phaseGrad, float z, float cell, float2 index, float2 seed, float amplitude)
{
    float2 profile = HodbaRippleProfile(phase);
    float2 mask = HodbaCrestMask(floor(phase), z, cell, index, seed);
    float2 grad = mask.x * profile.y * phaseGrad + float2(0.0, profile.x * mask.y);
    return -grad * amplitude;
}

float2 HodbaMegaRipple(float rippleX, float z, float shift, float2 shiftSlope, float amplitude)
{
    float phase = (rippleX + shift) / HodbaMegaRippleLength;
    float2 grad = float2(1.0 + shiftSlope.x, shiftSlope.y) / HodbaMegaRippleLength;
    return HodbaCrests(phase, grad, z, HodbaMegaCrestCell, _RippleIndex.xy, _RippleSeed.xy, amplitude);
}

// noise: r — изгиб, gb — его наклон по x и z, a — сила ряби по месту.
float2 HodbaFineRipple(float rippleX, float z, float shift, float2 shiftSlope, half4 noise, float amplitude)
{
    float warp = (noise.r - 0.5) * (2.0 * HodbaRippleWarp);
    float2 slope = shiftSlope + (noise.gb - 0.5) * (2.0 * HodbaRippleWarpSlope);
    float phase = (rippleX + shift + warp) / HodbaFineRippleLength;
    float2 grad = float2(1.0 + slope.x, slope.y) / HodbaFineRippleLength;
    return HodbaCrests(phase, grad, z, HodbaFineCrestCell, _RippleIndex.zw, _RippleSeed.zw, amplitude * noise.a);
}

#endif
