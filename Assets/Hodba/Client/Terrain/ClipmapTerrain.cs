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

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Material _material;
        readonly Mesh _mesh;
        readonly ClipmapLevel[] _levels;
        readonly Texture2D[] _heights, _surfaces;
        readonly MaterialPropertyBlock[] _props;
        readonly long[] _ox, _oz;
        readonly long _spacing0;

        public int LevelCount => _levels.Length;

        public ClipmapTerrain(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;
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
        public void Tick(WorldPos focus)
        {
            Update(focus);
            Draw();
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

        void Draw()
        {
            if (_material == null) return;
            int shadowLevels = Mathf.Clamp(_config.clipShadowLevels, 0, _levels.Length);
            for (int l = 0; l < _levels.Length; l++)
            {
                var level = _levels[l];
                bool hasNext = l + 1 < _levels.Length;
                int next = hasNext ? l + 1 : l;
                var p = _props[l];
                p.SetTexture(HeightId, _heights[l]);
                p.SetTexture(SurfaceId, _surfaces[l]);
                p.SetTexture(HeightNextId, _heights[next]);
                p.SetTexture(SurfaceNextId, _surfaces[next]);
                p.SetVector(OriginId, Corner(level));
                p.SetVector(OriginNextId, Corner(_levels[next]));
                p.SetVector(ParamsId, new Vector4(level.SpacingMm / 1000f, ClipmapLevel.Grid,
                    Mathf.Clamp(_config.clipMorph, 1f, ClipmapLevel.HoleStart - 2f), hasNext ? 1f : 0f));

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
            }
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
