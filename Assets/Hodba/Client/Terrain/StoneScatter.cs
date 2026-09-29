using System.Collections.Generic;
using Hodba.Core;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Редкие камни и совсем редкие валуны. Нужны не для «контента», а чтобы глаз чувствовал,
    /// что ты идёшь: на ровном пепле без деталей движения не видно. Раскладка детерминирована по хэшу.
    /// </summary>
    public sealed class StoneScatter
    {
        sealed class Layer
        {
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public readonly List<List<Matrix4x4>> Matrices = new List<List<Matrix4x4>>();
            public long LastCx = long.MinValue, LastCz = long.MinValue;
        }

        const uint StoneSalt = 500, BoulderSalt = 900;

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Material _material;
        readonly Layer _stones = new Layer();
        readonly Layer _boulders = new Layer();
        readonly Matrix4x4[] _batch = new Matrix4x4[1023];

        public StoneScatter(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;

            Fill(_stones, config.stoneMeshes, 6, 0.45f);
            Fill(_boulders, config.boulderMeshes, 3, 0.7f);

            _origin.Shifted += _ => { _stones.LastCx = long.MinValue; _boulders.LastCx = long.MinValue; };
        }

        public void Tick(WorldPos focus)
        {
            Rebuild(_stones, focus, _config.stoneCellSize, _config.stoneRadius, StoneSalt, false);
            Rebuild(_boulders, focus, _config.boulderCellSize, _config.boulderRadius, BoulderSalt, true);
            Draw(_stones, true);
            Draw(_boulders, true);
        }

        static void Fill(Layer layer, List<Mesh> external, int procedural, float flatness)
        {
            if (external != null)
                foreach (var m in external)
                    if (m != null) layer.Meshes.Add(m);

            if (layer.Meshes.Count == 0)
                for (int i = 0; i < procedural; i++)
                    layer.Meshes.Add(RockMeshFactory.Create((uint)(i * 7919 + (int)(flatness * 100)), flatness * Mathf.Lerp(0.6f, 1.4f, i / (float)procedural)));

            foreach (var _ in layer.Meshes) layer.Matrices.Add(new List<Matrix4x4>());
        }

        void Rebuild(Layer layer, WorldPos focus, float cellSize, float radius, uint salt, bool boulders)
        {
            long cellMm = (long)(cellSize * 1000f);
            long cx = WorldPos.FloorDiv(focus.X, cellMm);
            long cz = WorldPos.FloorDiv(focus.Z, cellMm);
            if (cx == layer.LastCx && cz == layer.LastCz) return;
            layer.LastCx = cx;
            layer.LastCz = cz;

            foreach (var list in layer.Matrices) list.Clear();

            int r = Mathf.CeilToInt(radius / cellSize);
            uint seed = _world.Info.Seed + salt;
            for (long dz = -r; dz <= r; dz++)
            for (long dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                long x = cx + dx, z = cz + dz;
                uint h = Hash.Cell(x, z, seed);

                int count;
                if (boulders) count = Hash.Unit(h) < _config.boulderChance ? 1 : 0;
                else count = Mathf.FloorToInt(Hash.Unit(h) * 2f * _config.stonesPerCell + 0.5f);

                for (int k = 0; k < count; k++)
                {
                    uint s = Hash.Next(h, (uint)k + 1);
                    long px = x * cellMm + (long)(Hash.Unit(Hash.Next(s, 1)) * cellMm);
                    long pz = z * cellMm + (long)(Hash.Unit(Hash.Next(s, 2)) * cellMm);
                    Vector2 range = boulders ? _config.boulderSize : _config.stoneSize;
                    float size = Mathf.Lerp(range.x, range.y, Mathf.Pow(Hash.Unit(Hash.Next(s, 3)), 2.5f));
                    int variant = (int)(Hash.Next(s, 4) % (uint)layer.Meshes.Count);
                    float yaw = Hash.Unit(Hash.Next(s, 5)) * 360f;
                    float tilt = (Hash.Unit(Hash.Next(s, 6)) - 0.5f) * 14f;

                    var mesh = layer.Meshes[variant];
                    var b = mesh.bounds.size;
                    float scale = size / Mathf.Max(0.001f, Mathf.Max(b.x, b.z));
                    float sink = b.y * scale * (boulders ? 0.3f : 0.2f);

                    float y = _world.SampleHeightMm(px, pz) / 1000f - sink;
                    var pos = _origin.ToLocal(px, pz, y);
                    var rot = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
                    layer.Matrices[variant].Add(Matrix4x4.TRS(pos, rot, Vector3.one * scale));
                }
            }
        }

        void Draw(Layer layer, bool castShadows)
        {
            if (_material == null) return;
            var rp = new RenderParams(_material)
            {
                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            };
            for (int m = 0; m < layer.Meshes.Count; m++)
            {
                var list = layer.Matrices[m];
                for (int start = 0; start < list.Count; start += _batch.Length)
                {
                    int n = Mathf.Min(_batch.Length, list.Count - start);
                    list.CopyTo(start, _batch, 0, n);
                    Graphics.RenderMeshInstanced(rp, layer.Meshes[m], 0, _batch, n);
                }
            }
        }
    }
}
