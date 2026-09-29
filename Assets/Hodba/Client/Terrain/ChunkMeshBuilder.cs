using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>Сетка высот чанка + «юбка» по краям, чтобы между уровнями детальности не было щелей.</summary>
    public static class ChunkMeshBuilder
    {
        static Vector3[] _vertices = new Vector3[0];
        static Vector3[] _normals = new Vector3[0];
        static int[] _indices = new int[0];
        static float[] _heights = new float[0];

        public static void Build(Mesh mesh, IWorldQuery world, long originXMm, long originZMm, long sizeMm, int res)
        {
            int n = res + 1;               // вершин по стороне
            int g = n + 2;                 // с полями для нормалей
            double stepMm = sizeMm / (double)res;
            float stepM = (float)(stepMm / 1000.0);

            Ensure(ref _heights, g * g);
            for (int z = 0; z < g; z++)
            for (int x = 0; x < g; x++)
            {
                long wx = originXMm + (long)((x - 1) * stepMm);
                long wz = originZMm + (long)((z - 1) * stepMm);
                _heights[z * g + x] = world.SampleHeightMm(wx, wz) / 1000f;
            }

            int skirt = 4 * n;
            int vCount = n * n + skirt;
            Ensure(ref _vertices, vCount);
            Ensure(ref _normals, vCount);

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int gi = (z + 1) * g + (x + 1);
                float h = _heights[gi];
                float hl = _heights[gi - 1], hr = _heights[gi + 1];
                float hd = _heights[gi - g], hu = _heights[gi + g];
                int i = z * n + x;
                _vertices[i] = new Vector3(x * stepM, h, z * stepM);
                _normals[i] = new Vector3(hl - hr, 2f * stepM, hd - hu).normalized;
            }

            // Юбка: копии краёв, опущенные вниз.
            float drop = Mathf.Max(2f, stepM * 0.5f);
            int s = n * n;
            for (int k = 0; k < n; k++)
            {
                CopyDown(s + k, k, drop);                         // z = 0
                CopyDown(s + n + k, (n - 1) * n + k, drop);       // z = max
                CopyDown(s + 2 * n + k, k * n, drop);             // x = 0
                CopyDown(s + 3 * n + k, k * n + (n - 1), drop);   // x = max
            }

            int quads = res * res + 4 * res;
            Ensure(ref _indices, quads * 6);
            int t = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                t = Quad(t, a, c, d, b);
            }
            for (int k = 0; k < res; k++)
            {
                t = Quad(t, s + k, k, k + 1, s + k + 1);                                               // z = 0, смотрит на −z
                t = Quad(t, (n - 1) * n + k, s + n + k, s + n + k + 1, (n - 1) * n + k + 1);         // z = max
                t = Quad(t, k * n, s + 2 * n + k, s + 2 * n + k + 1, (k + 1) * n);                   // x = 0
                t = Quad(t, s + 3 * n + k, k * n + (n - 1), (k + 1) * n + (n - 1), s + 3 * n + k + 1); // x = max
            }

            mesh.Clear();
            mesh.indexFormat = vCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices, 0, vCount);
            mesh.SetNormals(_normals, 0, vCount);
            mesh.SetIndices(_indices, 0, t, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
        }

        static void CopyDown(int dst, int src, float drop)
        {
            _vertices[dst] = _vertices[src] + Vector3.down * drop;
            _normals[dst] = _normals[src];
        }

        static int Quad(int t, int a, int b, int c, int d)
        {
            _indices[t++] = a; _indices[t++] = b; _indices[t++] = c;
            _indices[t++] = a; _indices[t++] = c; _indices[t++] = d;
            return t;
        }

        static void Ensure<T>(ref T[] arr, int size)
        {
            if (arr.Length < size) arr = new T[size];
        }
    }
}
