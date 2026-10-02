using System;
using Hodba.Core;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Земля — концентрические кольца вокруг путника (геометрические клипмапы). У ног шаг сетки четверть метра:
    /// видно зерно, рябь и бугры, а на рассвете их тени; каждое следующее кольцо вдвое крупнее — до горизонта в дымке.
    /// Кольца привязаны к своему шагу, поэтому рельеф не плывёт; у внешнего края кольцо плавно перетекает
    /// в следующее — ни щелей, ни «юбок», ни заплаток. Мир остаётся источником правды: кольца только рисуют.
    /// </summary>
    public sealed class ClipmapTerrain : IDisposable
    {
        static readonly int HeightId = Shader.PropertyToID("_ClipHeight");
        static readonly int SurfaceId = Shader.PropertyToID("_ClipSurface");
        static readonly int HeightNextId = Shader.PropertyToID("_ClipHeightNext");
        static readonly int SurfaceNextId = Shader.PropertyToID("_ClipSurfaceNext");
        static readonly int OriginId = Shader.PropertyToID("_ClipOrigin");
        static readonly int OriginNextId = Shader.PropertyToID("_ClipOriginNext");
        static readonly int ParamsId = Shader.PropertyToID("_ClipParams");
        static readonly int VeilLayerId = Shader.PropertyToID("_VeilLayer");
        static readonly int RippleIndexId = Shader.PropertyToID("_RippleIndex");
        static readonly int RippleSeedId = Shader.PropertyToID("_RippleSeed");

        /// <summary>Мелкая рябь — только картинка: длина и клетка гребня вдоль него, как в HodbaRipple.hlsl.</summary>
        public const long FineRippleLengthMm = 100, FineCrestCellMm = 800;

        const int MaxVeilLevels = 3;
        const int MaxVeilLayers = 3;

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Material _material;
        readonly Material _veilMaterial;
        readonly Mesh _mesh;
        readonly ClipmapLevel[] _levels;
        readonly Texture2D[] _heights, _surfaces;
        readonly MaterialPropertyBlock[] _props;
        readonly MaterialPropertyBlock[] _veilProps = new MaterialPropertyBlock[MaxVeilLevels * MaxVeilLayers];
        readonly long[] _ox, _oz;
        readonly long _spacing0;

        public int LevelCount => _levels.Length;

        /// <param name="veilMaterial">Взвесь над ближними кольцами (Hodba/SandVeil). Пусто — взвеси нет.</param>
        public ClipmapTerrain(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material, Material veilMaterial = null)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;
            _veilMaterial = veilMaterial;
            for (int i = 0; i < _veilProps.Length; i++) _veilProps[i] = new MaterialPropertyBlock();
            _mesh = ClipmapMesh.Build();

            int count = Mathf.Clamp(config.clipLevels, 1, 16);
            _spacing0 = Math.Max(10, (long)Math.Round(config.clipSpacing * 1000f));
            _levels = new ClipmapLevel[count];
            _heights = new Texture2D[count];
            _surfaces = new Texture2D[count];
            _props = new MaterialPropertyBlock[count];
            _ox = new long[count];
            _oz = new long[count];
            for (int l = 0; l < count; l++)
            {
                _levels[l] = new ClipmapLevel(l, _spacing0 << l);
                _heights[l] = Texture(TextureFormat.RFloat, $"Ground Height {l}");
                _surfaces[l] = Texture(TextureFormat.RGBA32, $"Ground Surface {l}");
                _props[l] = new MaterialPropertyBlock();
            }
        }

        static Texture2D Texture(TextureFormat format, string name) =>
            new Texture2D(ClipmapLevel.Size, ClipmapLevel.Size, format, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };

        /// <summary>Досчитать и нарисовать. Зовётся каждый кадр.</summary>
        /// <param name="veil">Сила взвеси (SandDrift.Veil): в штиль оболочки не рисуются вовсе.</param>
        public void Tick(WorldPos focus, float veil = 0f)
        {
            Update(focus);
            Draw(veil);
        }

        /// <summary>Встать вокруг точки: только досчитать въехавшее (при телепорте — всё).</summary>
        public void Update(WorldPos focus)
        {
            ClipmapLevel.Origins(focus.X, _spacing0, _ox);
            ClipmapLevel.Origins(focus.Z, _spacing0, _oz);
            for (int l = 0; l < _levels.Length; l++)
            {
                if (!_levels[l].MoveTo(_ox[l], _oz[l], _world)) continue;
                _heights[l].SetPixelData(_levels[l].Heights, 0);
                _heights[l].Apply(false);
                _surfaces[l].SetPixelData(_levels[l].Surface, 0);
                _surfaces[l].Apply(false);
            }
        }

        void Draw(float veil)
        {
            if (_material == null) return;
            int shadowLevels = Mathf.Clamp(_config.clipShadowLevels, 0, _levels.Length);
            // Прозрачный фрагмент с нулевой альфой стоит столько же — в штиль взвеси нет ни одного вызова.
            bool veilOn = _veilMaterial != null && veil > 0f && _config.saltationTexture != null;
            int veilLevels = veilOn ? Mathf.Clamp(_config.veilLevels, 0, Mathf.Min(MaxVeilLevels, _levels.Length)) : 0;
            int veilLayers = Mathf.Clamp(_config.veilLayers, 0, MaxVeilLayers);
            for (int l = 0; l < _levels.Length; l++)
            {
                var level = _levels[l];
                bool hasNext = l + 1 < _levels.Length;
                int next = hasNext ? l + 1 : l;
                var p = _props[l];
                Fill(p, l, next, hasNext);

                int submesh = l == 0
                    ? ClipmapMesh.Submesh(-1, -1)
                    : ClipmapMesh.Submesh(ClipmapLevel.Hole(_levels[l - 1].OriginX, level.OriginX),
                        ClipmapLevel.Hole(_levels[l - 1].OriginZ, level.OriginZ));

                var rp = new RenderParams(_material)
                {
                    matProps = p,
                    // Тени — только у ног: там рябь и бугры, дальше их не разглядеть.
                    shadowCastingMode = l < shadowLevels ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                };
                Graphics.RenderMesh(rp, _mesh, submesh, Matrix4x4.identity);

                if (l >= veilLevels) continue;
                for (int k = 0; k < veilLayers; k++)
                {
                    var vp = _veilProps[l * MaxVeilLayers + k];
                    Fill(vp, l, next, hasNext);
                    vp.SetVector(VeilLayerId, new Vector4(k, 0f, 0f, 0f));
                    var veilParams = new RenderParams(_veilMaterial)
                    {
                        matProps = vp,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        worldBounds = rp.worldBounds,
                    };
                    Graphics.RenderMesh(veilParams, _mesh, submesh, Matrix4x4.identity);
                }
            }
        }

        void Fill(MaterialPropertyBlock p, int l, int next, bool hasNext)
        {
            var level = _levels[l];
            p.SetTexture(HeightId, _heights[l]);
            p.SetTexture(SurfaceId, _surfaces[l]);
            p.SetTexture(HeightNextId, _heights[next]);
            p.SetTexture(SurfaceNextId, _surfaces[next]);
            p.SetVector(OriginId, Corner(level));
            p.SetVector(OriginNextId, Corner(_levels[next]));
            p.SetVector(ParamsId, new Vector4(level.SpacingMm / 1000f, ClipmapLevel.Grid,
                Mathf.Clamp(_config.clipMorph, 1f, ClipmapLevel.HoleStart - 2f), hasNext ? 1f : 0f));
            p.SetVector(RippleIndexId, RippleIndex());
            p.SetVector(RippleSeedId, RippleSeed());
        }

        /// <summary>
        /// Номера гребней и клеток вдоль них у центра мира — шейдер прибавляет к ним локальные (HodbaRipple.hlsl).
        /// Центр переносится шагом, кратным всем длинам и клеткам; номера по модулю 2^20 точны во float.
        /// </summary>
        Vector4 RippleIndex()
        {
            var o = _origin.Origin;
            return new Vector4(
                Index(o.X, SurfaceSample.RippleLengthMm), Index(o.Z, World.Gen.MicroRelief.CrestCellMm),
                Index(o.X, FineRippleLengthMm), Index(o.Z, FineCrestCellMm));
        }

        static float Index(long mm, long cell) => WorldPos.FloorDiv(mm, cell) & World.Gen.MicroRelief.CrestIndexMask;

        /// <summary>Начало хэша гребней (Hash.Cell) по 16 бит: крупная — как в мире (MicroRelief.CrestMask), мелкая — своя.</summary>
        Vector4 RippleSeed()
        {
            uint mega = unchecked((_world.Info.Seed + 281u) * 0x9E3779B9u);
            uint fine = unchecked((_world.Info.Seed + 283u) * 0x9E3779B9u);
            return new Vector4(mega & 0xFFFF, mega >> 16, fine & 0xFFFF, fine >> 16);
        }

        /// <summary>Угол кольца в локальных координатах (xy) и его кольцевой адрес в текстуре (zw).</summary>
        Vector4 Corner(ClipmapLevel level)
        {
            var local = _origin.ToLocal(level.OriginX * level.SpacingMm, level.OriginZ * level.SpacingMm);
            return new Vector4(local.x, local.z,
                WorldPos.FloorMod(level.OriginX, ClipmapLevel.Size), WorldPos.FloorMod(level.OriginZ, ClipmapLevel.Size));
        }

        public void Dispose()
        {
            foreach (var t in _heights) Destroy(t);
            foreach (var t in _surfaces) Destroy(t);
            Destroy(_mesh);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
