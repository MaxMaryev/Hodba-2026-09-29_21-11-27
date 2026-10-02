using System;
using System.Collections.Generic;
using Hodba.Core;
using Hodba.World;
using Hodba.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>A bounded pool of massive bays follows the view, never the world landmark.</summary>
    public sealed class GreatWall : IDisposable
    {
        public const float ViewDistance = 24000f;
        const int Radius = 96;
        readonly FloatingOrigin _origin;
        readonly GameObject _root;
        readonly GameObject[] _bays = new GameObject[Radius * 2 + 1];
        readonly Mesh _mesh;
        long _center = long.MinValue;

        public GreatWall(IWorldQuery world, FloatingOrigin origin, Material material)
        {
            _origin = origin;
            _mesh = CreateBayMesh();
            _root = new GameObject("Great Wall — unknown builders, " + GreatWallWorld.HeightMeters + " m");
            for (int i = 0; i < _bays.Length; i++)
            {
                var bay = new GameObject("Wall bay");
                bay.transform.SetParent(_root.transform, false);
                bay.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = bay.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                _bays[i] = bay;
            }
            _origin.Shifted += Shift;
        }

        public void Tick(WorldPos focus)
        {
            PushLighting();
            bool visible = Math.Abs(focus.X - GreatWallWorld.CenterXMm) / 1000.0 < ViewDistance;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (!visible) return;
            long center = WorldPos.FloorDiv(focus.Z, GreatWallWorld.BayLengthMm);
            if (center == _center) return;
            _center = center;
            for (int i = 0; i < _bays.Length; i++)
            {
                long bay = center + i - Radius;
                long z = bay * GreatWallWorld.BayLengthMm;
                // A shared top datum keeps neighboring bays seamless; footings extend below dunes.
                _bays[i].transform.position = _origin.ToLocal(GreatWallWorld.CenterXMm, z);
                _bays[i].name = "Wall bay " + bay;
            }
        }

        void Shift(Vector3 delta)
        {
            _root.transform.position += delta;
            PushLighting();
        }

        void PushLighting()
        {
            float x = _origin.ToLocal(GreatWallWorld.CenterXMm, 0).x;
            Shader.SetGlobalVector("_HodbaWall", new Vector4(x, GreatWallWorld.HalfThicknessMm / 1000f,
                GreatWallWorld.HeightMeters - 18f, 1f));
            Shader.SetGlobalVector("_HodbaWallDetails", new Vector4(GreatWallWorld.ButtressDepthMm / 1000f,
                GreatWallWorld.ButtressHalfWidthMm / 1000f, GreatWallWorld.BayLengthMm / 1000f, GreatWallWorld.HeightMeters));
        }

        public static Mesh CreateBayMesh()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            float length = GreatWallWorld.BayLengthMm / 1000f;
            float half = GreatWallWorld.HalfThicknessMm / 1000f;
            float depth = GreatWallWorld.ButtressDepthMm / 1000f;
            float width = GreatWallWorld.ButtressHalfWidthMm / 1000f;
            float height = GreatWallWorld.HeightMeters;
            // Buried foundations, tall faces, wider piers and a narrow monumental crown.
            Box(new Vector3(-half, -100, 0), new Vector3(half, height - 18, length));
            Box(new Vector3(-half - depth, -100, -width), new Vector3(half + depth, height, width));
            Box(new Vector3(-half - 4, height - 36, width), new Vector3(half + 4, height - 18, length - width));
            var mesh = new Mesh { name = "Great Wall / 256 m masonry bay" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;

            void Box(Vector3 a, Vector3 b)
            {
                Face(new Vector3(a.x,a.y,a.z),new Vector3(a.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(a.x,b.y,a.z),Vector3.left);
                Face(new Vector3(b.x,a.y,b.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(b.x,b.y,b.z),Vector3.right);
                Face(new Vector3(b.x,a.y,a.z),new Vector3(a.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(b.x,b.y,a.z),Vector3.back);
                Face(new Vector3(a.x,a.y,b.z),new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(a.x,b.y,b.z),Vector3.forward);
                Face(new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,b.y,a.z),new Vector3(a.x,b.y,a.z),Vector3.up);
                Face(new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,a.y,b.z),new Vector3(a.x,a.y,b.z),Vector3.down);
            }
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(n);
                // UVs are metres: block courses stay consistent on every face and bay.
                bool horizontal = Mathf.Abs(n.y) > 0.5f;
                bool alongZ = Mathf.Abs(n.x) > 0.5f;
                foreach (var p in new[] {a,b,c,d}) uv.Add(horizontal ? new Vector2(p.x,p.z) : new Vector2(alongZ ? p.z : p.x,p.y));
                triangles.Add(index); triangles.Add(index+1); triangles.Add(index+2);
                triangles.Add(index); triangles.Add(index+2); triangles.Add(index+3);
            }
        }

        public void Dispose()
        {
            _origin.Shifted -= Shift;
            Shader.SetGlobalVector("_HodbaWall", Vector4.zero);
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_mesh);
        }
    }
}
