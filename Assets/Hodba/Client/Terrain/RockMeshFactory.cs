using System.Collections.Generic;
using Hodba.Core;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Заглушки камней, пока нет моделей М1/М2: гранёный комок с плоским основанием.
    /// Размер нормирован к 1 м по горизонтали, pivot внизу по центру — как у заказанных моделей.
    /// </summary>
    public static class RockMeshFactory
    {
        public static Mesh Create(uint seed, float flatness)
        {
            var baseVerts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere(baseVerts, tris);

            // Неровный комок.
            for (int i = 0; i < baseVerts.Count; i++)
            {
                uint h = Hash.Cell(i, 0, seed);
                float r = Mathf.Lerp(0.72f, 1.1f, Hash.Unit(h));
                var v = baseVerts[i] * r;
                v.y *= flatness;
                baseVerts[i] = v;
            }

            // Плоское основание: всё ниже нуля кладём на ноль.
            float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f;
            foreach (var v in baseVerts) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            float cut = Mathf.Lerp(minY, maxY, 0.3f);
            for (int i = 0; i < baseVerts.Count; i++)
            {
                var v = baseVerts[i];
                v.y = Mathf.Max(v.y, cut) - cut;
                baseVerts[i] = v;
                maxR = Mathf.Max(maxR, new Vector2(v.x, v.z).magnitude);
            }
            for (int i = 0; i < baseVerts.Count; i++) baseVerts[i] /= 2f * maxR;

            // Грани: у каждого треугольника свои вершины — рубленый низкополигональный вид.
            var verts = new Vector3[tris.Count];
            var idx = new int[tris.Count];
            var center = Vector3.zero;
            foreach (var v in baseVerts) center += v;
            center /= baseVerts.Count;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = baseVerts[tris[i]], b = baseVerts[tris[i + 1]], c = baseVerts[tris[i + 2]];
                // Лицевая сторона в Unity — по часовой, нормаль cross(b−a, c−a) наружу.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f) (b, c) = (c, b);
                verts[i] = a; verts[i + 1] = b; verts[i + 2] = c;
                idx[i] = i; idx[i + 1] = i + 1; idx[i + 2] = i + 2;
            }

            var mesh = new Mesh { name = $"Rock {seed}" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Icosphere(List<Vector3> verts, List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts.AddRange(new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            });
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            // Одно деление — 80 граней, для мелкого камня достаточно.
            var cache = new Dictionary<long, int>();
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(verts, cache, a, b), bc = Mid(verts, cache, b, c), ca = Mid(verts, cache, c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
        }

        static int Mid(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int i)) return i;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }
    }
}
