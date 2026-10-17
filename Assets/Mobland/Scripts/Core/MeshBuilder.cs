using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mobland.Core
{
    /// <summary>
    /// Accumulates simple flat-shaded geometry (boxes, quads, cylinders, cones) into one mesh
    /// with one sub-mesh per material. All faces are oriented automatically from their outward normal.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Material> materials = new List<Material>();
        readonly List<List<int>> triangles = new List<List<int>>();
        readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();

        /// <summary>World position that becomes (0,0,0) in the output mesh.</summary>
        public Vector3 Origin;

        public int VertexCount => vertices.Count;

        List<int> Tris(Material m)
        {
            if (!materialIndex.TryGetValue(m, out int i))
            {
                i = materials.Count;
                materials.Add(m);
                triangles.Add(new List<int>());
                materialIndex[m] = i;
            }
            return triangles[i];
        }

        /// <summary>Quad from four corners (in order around the edge). Winding is fixed to face <paramref name="normal"/>.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Material m)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                Vector3 t = b; b = d; d = t;
            }
            int i = vertices.Count;
            vertices.Add(a - Origin); vertices.Add(b - Origin); vertices.Add(c - Origin); vertices.Add(d - Origin);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            var t2 = Tris(m);
            t2.Add(i); t2.Add(i + 1); t2.Add(i + 2);
            t2.Add(i); t2.Add(i + 2); t2.Add(i + 3);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Material m)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 t = b; b = c; c = t;
                n = -n;
            }
            n.Normalize();
            int i = vertices.Count;
            vertices.Add(a - Origin); vertices.Add(b - Origin); vertices.Add(c - Origin);
            normals.Add(n); normals.Add(n); normals.Add(n);
            var t2 = Tris(m);
            t2.Add(i); t2.Add(i + 1); t2.Add(i + 2);
        }

        /// <summary>Axis-aligned box between two corners. Bottom face is skipped unless requested.</summary>
        public void AddBox(Vector3 min, Vector3 max, Material m, bool bottom = false)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            Vector3 p0 = new Vector3(lo.x, lo.y, lo.z), p1 = new Vector3(hi.x, lo.y, lo.z);
            Vector3 p2 = new Vector3(hi.x, hi.y, lo.z), p3 = new Vector3(lo.x, hi.y, lo.z);
            Vector3 p4 = new Vector3(lo.x, lo.y, hi.z), p5 = new Vector3(hi.x, lo.y, hi.z);
            Vector3 p6 = new Vector3(hi.x, hi.y, hi.z), p7 = new Vector3(lo.x, hi.y, hi.z);

            AddQuad(p0, p3, p2, p1, Vector3.back, m);
            AddQuad(p5, p6, p7, p4, Vector3.forward, m);
            AddQuad(p4, p7, p3, p0, Vector3.left, m);
            AddQuad(p1, p2, p6, p5, Vector3.right, m);
            AddQuad(p3, p7, p6, p2, Vector3.up, m);
            if (bottom) AddQuad(p4, p0, p1, p5, Vector3.down, m);
        }

        /// <summary>Box helper using centre/size on X and Z with explicit Y range.</summary>
        public void AddBoxXZ(float xMin, float xMax, float yMin, float yMax, float zMin, float zMax, Material m, bool bottom = false)
        {
            AddBox(new Vector3(xMin, yMin, zMin), new Vector3(xMax, yMax, zMax), m, bottom);
        }

        /// <summary>Vertical cylinder standing on <paramref name="baseCenter"/>.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Material m, bool capTop = true)
        {
            segments = Mathf.Max(3, segments);
            Vector3 top = baseCenter + Vector3.up * height;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments;
                float a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 n = (d0 + d1).normalized;
                AddQuad(baseCenter + d0 * radius, top + d0 * radius, top + d1 * radius, baseCenter + d1 * radius, n, m);
                if (capTop) AddTriangle(top, top + d0 * radius, top + d1 * radius, Vector3.up, m);
            }
        }

        /// <summary>Cone with its base on <paramref name="baseCenter"/>, pointing up.</summary>
        public void AddCone(Vector3 baseCenter, float radius, float height, int segments, Material m)
        {
            segments = Mathf.Max(3, segments);
            Vector3 apex = baseCenter + Vector3.up * height;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments;
                float a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 outward = (d0 + d1).normalized + Vector3.up * (radius / Mathf.Max(0.01f, height));
                AddTriangle(baseCenter + d0 * radius, apex, baseCenter + d1 * radius, outward, m);
            }
        }

        public bool IsEmpty => vertices.Count == 0;

        public Mesh ToMesh(string name, out Material[] mats)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            mats = materials.ToArray();
            return mesh;
        }

        /// <summary>Creates a GameObject with this geometry. Returns null if nothing was added.</summary>
        public GameObject Spawn(string name, Transform parent)
        {
            if (IsEmpty) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Origin;
            var mesh = ToMesh(name, out var mats);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }
    }
}
