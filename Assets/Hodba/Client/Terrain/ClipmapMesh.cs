using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Одна сетка на все кольца: вершины — целые координаты (0..Grid−1), высоту и место в мире даёт шейдер.
    /// Подсетка 0 — сплошной квадрат (самое мелкое кольцо), 1..4 — квадрат с дыркой под внутреннее кольцо
    /// в каждом из четырёх положений (сдвиг дырки <see cref="ClipmapLevel.HoleStart"/> или на клетку дальше по x и z).
    /// </summary>
    public static class ClipmapMesh
    {
        public static Mesh Build()
        {
            const int n = ClipmapLevel.Grid;
            var vertices = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                vertices[z * n + x] = new Vector3(x, 0f, z);

            var mesh = new Mesh { name = "Ground Clipmap", indexFormat = IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 5;
            mesh.SetIndices(Indices(-1, -1), MeshTopology.Triangles, 0, false);
            for (int k = 0; k < 4; k++)
                mesh.SetIndices(Indices(ClipmapLevel.HoleStart + (k & 1), ClipmapLevel.HoleStart + (k >> 1)), MeshTopology.Triangles, k + 1, false);
            // Место и высоту задаёт шейдер — границы нужны только чтобы сетку не отбросили.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        /// <summary>Подсетка для дырки в (hx, hz); −1 — без дырки.</summary>
        public static int Submesh(int holeX, int holeZ) =>
            holeX < 0 ? 0 : 1 + (holeX - ClipmapLevel.HoleStart) + 2 * (holeZ - ClipmapLevel.HoleStart);

        static int[] Indices(int hx, int hz)
        {
            const int n = ClipmapLevel.Grid;
            var list = new List<int>((n - 1) * (n - 1) * 6);
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                bool hole = hx >= 0 && x >= hx && x < hx + ClipmapLevel.Inner && z >= hz && z < hz + ClipmapLevel.Inner;
                if (hole) continue;
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                list.Add(a); list.Add(c); list.Add(d);
                list.Add(a); list.Add(d); list.Add(b);
            }
            return list.ToArray();
        }
    }
}
