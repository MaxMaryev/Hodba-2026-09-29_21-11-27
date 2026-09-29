using Hodba.Core;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Следы за спиной. Обернись — нитка следов уходит к горизонту. Ветер понемногу их заметает.
    /// </summary>
    public sealed class Footprints
    {
        struct Print
        {
            public WorldPos Pos;
            public float Course;
            public bool Left;
            public float Age;
        }

        readonly IWorldQuery _world;
        readonly FloatingOrigin _origin;
        readonly FieldConfig _config;
        readonly Mesh _mesh;
        readonly Material _material;

        Print[] _prints;
        int _head, _count;
        Vector3[] _verts;
        Vector3[] _normals;
        Vector4[] _tangents;
        Color32[] _colors;
        Vector2[] _uvs;
        int[] _indices;
        float _refresh;
        bool _dirty;

        public Footprints(IWorldQuery world, FloatingOrigin origin, FieldConfig config, Material material)
        {
            _world = world;
            _origin = origin;
            _config = config;
            _material = material;
            Allocate(Mathf.Max(16, config.footprintMax));

            _mesh = new Mesh { name = "Footprints" };
            _mesh.MarkDynamic();
            _origin.Shifted += _ => _dirty = true;
        }

        void Allocate(int capacity)
        {
            _prints = new Print[capacity];
            _verts = new Vector3[capacity * 4];
            _normals = new Vector3[capacity * 4];
            _tangents = new Vector4[capacity * 4];
            _colors = new Color32[capacity * 4];
            _uvs = new Vector2[capacity * 4];
            _indices = new int[capacity * 6];
            _head = _count = 0;
        }

        public void Clear()
        {
            _head = _count = 0;
            _dirty = true;
        }

        public void Add(bool left, WorldPos pos, float course)
        {
            _prints[_head] = new Print { Pos = pos, Course = course, Left = left, Age = 0f };
            _head = (_head + 1) % _prints.Length;
            _count = Mathf.Min(_count + 1, _prints.Length);
            _dirty = true;
        }

        public void Tick(float dt, float windStrength)
        {
            if (_material == null) return;

            float erosion = 1f + windStrength * 2f;
            for (int i = 0; i < _count; i++) _prints[Index(i)].Age += dt * erosion;

            _refresh -= dt;
            if (_dirty || _refresh <= 0f)
            {
                Rebuild();
                _refresh = 1f;
                _dirty = false;
            }

            if (_count > 0)
                Graphics.RenderMesh(new RenderParams(_material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                }, _mesh, 0, Matrix4x4.identity);
        }

        int Index(int i) => (_head - _count + i + _prints.Length) % _prints.Length;

        void Rebuild()
        {
            float life = Mathf.Max(1f, _config.footprintLifetimeMinutes * 60f);
            Vector2 size = _config.footprintSize;
            int q = 0;
            for (int i = 0; i < _count; i++)
            {
                var p = _prints[Index(i)];
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(life * 0.6f, life, p.Age));
                if (fade <= 0.01f) continue;

                float rad = p.Course * Mathf.Deg2Rad;
                var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                var right = new Vector3(fwd.z, 0f, -fwd.x);
                var hx = right * (size.x * 0.5f);
                var hz = fwd * (size.y * 0.5f);
                var center = _origin.ToLocal(p.Pos);

                int v = q * 4;
                _verts[v + 0] = Ground(center - hx - hz);
                _verts[v + 1] = Ground(center - hx + hz);
                _verts[v + 2] = Ground(center + hx + hz);
                _verts[v + 3] = Ground(center + hx - hz);

                float u0 = p.Left ? 0f : 1f, u1 = p.Left ? 1f : 0f; // правый — отражение левого
                _uvs[v + 0] = new Vector2(u0, 0f);
                _uvs[v + 1] = new Vector2(u0, 1f);
                _uvs[v + 2] = new Vector2(u1, 1f);
                _uvs[v + 3] = new Vector2(u1, 0f);

                var c = new Color32(255, 255, 255, (byte)(fade * 255f));
                _colors[v] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = c;

                // Касательная — вдоль u. У правого следа u отражён, поэтому и она смотрит в другую сторону;
                // w подобран так, чтобы битангенс всегда смотрел вперёд по ходу (вдоль v).
                var tangent = p.Left ? new Vector4(right.x, 0f, right.z, -1f) : new Vector4(-right.x, 0f, -right.z, 1f);
                for (int k = 0; k < 4; k++)
                {
                    _normals[v + k] = Vector3.up;
                    _tangents[v + k] = tangent;
                }

                int t = q * 6;
                _indices[t + 0] = v; _indices[t + 1] = v + 1; _indices[t + 2] = v + 2;
                _indices[t + 3] = v; _indices[t + 4] = v + 2; _indices[t + 5] = v + 3;
                q++;
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts, 0, q * 4);
            _mesh.SetNormals(_normals, 0, q * 4);
            _mesh.SetTangents(_tangents, 0, q * 4);
            _mesh.SetUVs(0, _uvs, 0, q * 4);
            _mesh.SetColors(_colors, 0, q * 4);
            _mesh.SetIndices(_indices, 0, q * 6, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        Vector3 Ground(Vector3 local)
        {
            var w = _origin.ToWorld(local);
            local.y = _world.SampleHeightMm(w) / 1000f + 0.015f;
            return local;
        }
    }
}
