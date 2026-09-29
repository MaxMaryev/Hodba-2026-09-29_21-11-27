using System.Collections.Generic;
using Hodba.Core;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Бесконечное поле: чанки земли вокруг человека, три уровня детальности.
    /// Край видимой земли (~3 км) целиком тонет в дымке — горизонт растворяется без шва.
    /// </summary>
    public sealed class TerrainStreamer
    {
        sealed class Chunk
        {
            public long Cx, Cz;
            public int Resolution;
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
        }

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Transform _root;
        readonly Material _material;

        readonly Dictionary<(long, long), Chunk> _chunks = new Dictionary<(long, long), Chunk>();
        readonly Stack<Chunk> _pool = new Stack<Chunk>();
        readonly List<(long cx, long cz, int res, float dist)> _pending = new List<(long, long, int, float)>();
        readonly HashSet<(long, long)> _wanted = new HashSet<(long, long)>();

        long _lastCx = long.MinValue, _lastCz = long.MinValue;
        long ChunkMm => _config.chunkSize * 1000L;

        public TerrainStreamer(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;
            _root = new GameObject("Terrain").transform;
            _origin.Shifted += d => _root.position += d;
        }

        /// <summary>Построить всё сразу (старт, возвращение после долгого пути).</summary>
        public void BuildAll(WorldPos focus)
        {
            Plan(focus);
            while (_pending.Count > 0) BuildNext();
        }

        public void Tick(WorldPos focus)
        {
            long cx = WorldPos.FloorDiv(focus.X, ChunkMm);
            long cz = WorldPos.FloorDiv(focus.Z, ChunkMm);
            if (cx != _lastCx || cz != _lastCz) Plan(focus);

            int budget = Mathf.Max(1, _config.chunksPerFrame);
            while (budget-- > 0 && _pending.Count > 0) BuildNext();
        }

        void Plan(WorldPos focus)
        {
            long chunkMm = ChunkMm;
            _lastCx = WorldPos.FloorDiv(focus.X, chunkMm);
            _lastCz = WorldPos.FloorDiv(focus.Z, chunkMm);
            _pending.Clear();
            _wanted.Clear();

            int r = _config.viewChunks;
            float size = _config.chunkSize;
            for (long dz = -r; dz <= r; dz++)
            for (long dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dz * dz > (r + 0.5f) * (r + 0.5f)) continue;
                long cx = _lastCx + dx, cz = _lastCz + dz;

                double centerX = (cx + 0.5) * size - focus.XMeters;
                double centerZ = (cz + 0.5) * size - focus.ZMeters;
                float dist = (float)System.Math.Sqrt(centerX * centerX + centerZ * centerZ);
                int res = dist < _config.lod0Distance ? _config.lod0Resolution
                        : dist < _config.lod1Distance ? _config.lod1Resolution
                        : _config.lod2Resolution;

                _wanted.Add((cx, cz));
                if (_chunks.TryGetValue((cx, cz), out var existing) && existing.Resolution == res) continue;
                _pending.Add((cx, cz, res, dist));
            }

            // Лишние — в пул.
            var remove = new List<(long, long)>();
            foreach (var key in _chunks.Keys)
                if (!_wanted.Contains(key)) remove.Add(key);
            foreach (var key in remove)
            {
                var c = _chunks[key];
                _chunks.Remove(key);
                c.Go.SetActive(false);
                _pool.Push(c);
            }

            // Ближние — первыми.
            _pending.Sort((a, b) => b.dist.CompareTo(a.dist));
        }

        void BuildNext()
        {
            var last = _pending[_pending.Count - 1];
            _pending.RemoveAt(_pending.Count - 1);
            if (!_wanted.Contains((last.cx, last.cz))) return;

            if (!_chunks.TryGetValue((last.cx, last.cz), out var chunk))
            {
                chunk = _pool.Count > 0 ? _pool.Pop() : CreateChunk();
                chunk.Cx = last.cx;
                chunk.Cz = last.cz;
                _chunks[(last.cx, last.cz)] = chunk;
            }

            chunk.Resolution = last.res;
            ChunkMeshBuilder.Build(chunk.Mesh, _world, last.cx * ChunkMm, last.cz * ChunkMm, ChunkMm, last.res);
            chunk.Go.name = $"Chunk {last.cx},{last.cz} ×{last.res}";
            chunk.Go.transform.position = _origin.ToLocal(last.cx * ChunkMm, last.cz * ChunkMm);
            chunk.Go.SetActive(true);
        }

        Chunk CreateChunk()
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(_root, false);
            var mesh = new Mesh { name = "Ground" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new Chunk { Go = go, Mesh = mesh, Renderer = mr };
        }
    }
}
