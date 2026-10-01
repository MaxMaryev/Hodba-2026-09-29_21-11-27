using System.Collections.Generic;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Редкие камни и совсем редкие валуны. Нужны не для «контента», а чтобы глаз чувствовал,
    /// что ты идёшь: на ровном пепле без деталей движения не видно. Где лежит камень, решает
    /// <see cref="StoneField"/> — та же раскладка, через которую переступает тело.
    /// </summary>
    public sealed class StoneScatter
    {
        sealed class Layer
        {
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public bool External;
            public readonly List<List<Matrix4x4>> Matrices = new List<List<Matrix4x4>>();
            public long LastCx = long.MinValue, LastCz = long.MinValue;
        }

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Material _material;
        readonly Material _boulderMaterial;
        readonly Layer _stones = new Layer();
        readonly Layer _boulders = new Layer();
        readonly Matrix4x4[] _batch = new Matrix4x4[1023];

        public StoneScatter(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material, Material boulderMaterial)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;
            _boulderMaterial = boulderMaterial;

            Fill(_stones, config.stoneMeshes, 6, 0.45f);
            Fill(_boulders, config.boulderMeshes, 3, 0.7f);

            _origin.Shifted += _ => { _stones.LastCx = long.MinValue; _boulders.LastCx = long.MinValue; };
        }

        public void Tick(WorldPos focus)
        {
            Rebuild(_stones, focus, _config.StoneLayout, _config.stoneRadius);
            Rebuild(_boulders, focus, _config.BoulderLayout, _config.boulderRadius);
            Draw(_stones, _material);
            Draw(_boulders, _boulderMaterial);
        }

        static void Fill(Layer layer, List<Mesh> external, int procedural, float flatness)
        {
            if (external != null)
                foreach (var m in external)
                    if (m != null) layer.Meshes.Add(m);
            layer.External = layer.Meshes.Count > 0;
            // От мелкого к крупному: мелких камней в мире больше.
            layer.Meshes.Sort((a, b) => Footprint(a).CompareTo(Footprint(b)));

            if (layer.Meshes.Count == 0)
                for (int i = 0; i < procedural; i++)
                    layer.Meshes.Add(RockMeshFactory.Create((uint)(i * 7919 + (int)(flatness * 100)), flatness * Mathf.Lerp(0.6f, 1.4f, i / (float)procedural)));

            foreach (var _ in layer.Meshes) layer.Matrices.Add(new List<Matrix4x4>());
        }

        void Rebuild(Layer layer, WorldPos focus, in StoneLayout layout, float radius)
        {
            bool boulders = layout.Boulders;
            long cellMm = layout.CellMm;
            long cx = WorldPos.FloorDiv(focus.X, cellMm);
            long cz = WorldPos.FloorDiv(focus.Z, cellMm);
            if (cx == layer.LastCx && cz == layer.LastCz) return;
            layer.LastCx = cx;
            layer.LastCz = cz;

            foreach (var list in layer.Matrices) list.Clear();

            int r = Mathf.CeilToInt(radius / layout.CellSize);
            for (long dz = -r; dz <= r; dz++)
            for (long dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                long x = cx + dx, z = cz + dz;
                uint h = StoneField.CellHash(x, z, _world.Info.Seed, layout);
                int count = StoneField.Count(h, layout);

                for (int k = 0; k < count; k++)
                {
                    var stone = StoneField.Place(x, z, h, k, layout);
                    uint s = stone.Hash;
                    long px = stone.X, pz = stone.Z;
                    float skew = stone.Skew;
                    float yaw = Hash.Unit(Hash.Next(s, 5)) * 360f;
                    float tilt = (Hash.Unit(Hash.Next(s, 6)) - 0.5f) * 14f;

                    int variant;
                    float scale;
                    Mesh mesh;
                    if (layer.External && _config.authoredStoneSizes)
                    {
                        // Модель уже своего размера: мелкие варианты выпадают чаще, размер чуть гуляет.
                        variant = Mathf.Min(layer.Meshes.Count - 1, (int)(skew * layer.Meshes.Count));
                        mesh = layer.Meshes[variant];
                        scale = Mathf.Lerp(0.85f, 1.15f, Hash.Unit(Hash.Next(s, 4)));
                    }
                    else
                    {
                        Vector2 range = boulders ? _config.boulderSize : _config.stoneSize;
                        float size = Mathf.Lerp(range.x, range.y, skew);
                        variant = (int)(Hash.Next(s, 4) % (uint)layer.Meshes.Count);
                        mesh = layer.Meshes[variant];
                        scale = size / Mathf.Max(0.001f, Footprint(mesh));
                    }

                    // У валунов М2 нанос пепла смотрит на +Z — разворачиваем его навстречу ветру.
                    if (boulders && layer.External)
                    {
                        yaw = _config.prevailingWind + 180f + (Hash.Unit(Hash.Next(s, 5)) - 0.5f) * 40f;
                        tilt *= 0.3f;
                    }

                    var b = mesh.bounds.size;
                    float sink = b.y * scale * (layer.External ? 0.05f : boulders ? 0.3f : 0.2f);

                    float y = _world.SampleHeightMm(px, pz) / 1000f - sink;
                    var pos = _origin.ToLocal(px, pz, y);
                    var rot = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
                    layer.Matrices[variant].Add(Matrix4x4.TRS(pos, rot, Vector3.one * scale));
                }
            }
        }

        static float Footprint(Mesh m) => Mathf.Max(m.bounds.size.x, m.bounds.size.z);

        void Draw(Layer layer, Material material)
        {
            if (material == null) return;
            var rp = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.On,
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
