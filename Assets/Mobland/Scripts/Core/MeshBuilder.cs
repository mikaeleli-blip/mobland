using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mobland.Core
{
    /// <summary>
    /// Accumulates flat-shaded geometry (quads, boxes, cylinders, cones, spheres) into one mesh with one
    /// sub-mesh per material. UVs default to world-space planar mapping in metres / tile size,
    /// so textures stay continuous across neighbouring pieces. Tangents are generated for normal maps.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Material> materials = new List<Material>();
        readonly List<List<int>> triangles = new List<List<int>>();
        readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();

        /// <summary>World position that becomes (0,0,0) in the output mesh.</summary>
        public Vector3 Origin;

        public int VertexCount => vertices.Count;
        public bool IsEmpty => vertices.Count == 0;

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

        /// <summary>World-space planar UV for a point on a face with the given normal.</summary>
        public static Vector2 PlanarUV(Vector3 p, Vector3 n, float tile)
        {
            float inv = 1f / Mathf.Max(0.0001f, tile);
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x * inv, p.z * inv);
            if (a.x >= a.z) return new Vector2((n.x > 0 ? p.z : -p.z) * inv, p.y * inv);
            return new Vector2((n.z > 0 ? -p.x : p.x) * inv, p.y * inv);
        }

        /// <summary>Quad with explicit UVs. Corners in order around the edge; winding fixed to face <paramref name="normal"/>.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
            Vector3 normal, Material m)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                Vector3 t = b; b = d; d = t;
                Vector2 tu = ub; ub = ud; ud = tu;
            }
            int i = vertices.Count;
            vertices.Add(a - Origin); vertices.Add(b - Origin); vertices.Add(c - Origin); vertices.Add(d - Origin);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            var t2 = Tris(m);
            t2.Add(i); t2.Add(i + 1); t2.Add(i + 2);
            t2.Add(i); t2.Add(i + 2); t2.Add(i + 3);
        }

        /// <summary>Quad with world-planar UVs.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Material m, float tile = 1f)
        {
            AddQuad(a, b, c, d, PlanarUV(a, normal, tile), PlanarUV(b, normal, tile), PlanarUV(c, normal, tile),
                PlanarUV(d, normal, tile), normal, m);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Material m, float tile = 1f)
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
            uvs.Add(PlanarUV(a, n, tile)); uvs.Add(PlanarUV(b, n, tile)); uvs.Add(PlanarUV(c, n, tile));
            var t2 = Tris(m);
            t2.Add(i); t2.Add(i + 1); t2.Add(i + 2);
        }

        /// <summary>
        /// Axis-aligned box. <paramref name="top"/> is used for the +Y face, <paramref name="side"/> (or top) for the rest.
        /// Bottom face is skipped unless requested.
        /// </summary>
        public void AddBox(Vector3 min, Vector3 max, Material top, Material side = null, float tile = 1f, bool bottom = false)
        {
            side = side ?? top;
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            Vector3 p0 = new Vector3(lo.x, lo.y, lo.z), p1 = new Vector3(hi.x, lo.y, lo.z);
            Vector3 p2 = new Vector3(hi.x, hi.y, lo.z), p3 = new Vector3(lo.x, hi.y, lo.z);
            Vector3 p4 = new Vector3(lo.x, lo.y, hi.z), p5 = new Vector3(hi.x, lo.y, hi.z);
            Vector3 p6 = new Vector3(hi.x, hi.y, hi.z), p7 = new Vector3(lo.x, hi.y, hi.z);

            AddQuad(p0, p3, p2, p1, Vector3.back, side, tile);
            AddQuad(p5, p6, p7, p4, Vector3.forward, side, tile);
            AddQuad(p4, p7, p3, p0, Vector3.left, side, tile);
            AddQuad(p1, p2, p6, p5, Vector3.right, side, tile);
            AddQuad(p3, p7, p6, p2, Vector3.up, top, tile);
            if (bottom) AddQuad(p4, p0, p1, p5, Vector3.down, side, tile);
        }

        public void AddBoxXZ(float xMin, float xMax, float yMin, float yMax, float zMin, float zMax, Material m,
            bool bottom = false, float tile = 1f, Material side = null)
        {
            AddBox(new Vector3(xMin, yMin, zMin), new Vector3(xMax, yMax, zMax), m, side, tile, bottom);
        }

        /// <summary>Box rotated around Y by <paramref name="yawDeg"/>, centred at <paramref name="center"/> (centre of the bottom face).</summary>
        public void AddOrientedBox(Vector3 center, Vector3 size, float yawDeg, Material top, Material side = null, float tile = 1f, bool bottom = false)
        {
            side = side ?? top;
            Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 hx = q * new Vector3(size.x * 0.5f, 0, 0), hz = q * new Vector3(0, 0, size.z * 0.5f), up = new Vector3(0, size.y, 0);
            Vector3 c0 = center - hx - hz, c1 = center + hx - hz, c2 = center + hx + hz, c3 = center - hx + hz;
            Vector3 nx = q * Vector3.right, nz = q * Vector3.forward;
            AddQuad(c0, c0 + up, c1 + up, c1, -nz, side, tile);
            AddQuad(c1, c1 + up, c2 + up, c2, nx, side, tile);
            AddQuad(c2, c2 + up, c3 + up, c3, nz, side, tile);
            AddQuad(c3, c3 + up, c0 + up, c0, -nx, side, tile);
            AddQuad(c0 + up, c3 + up, c2 + up, c1 + up, Vector3.up, top, tile);
            if (bottom) AddQuad(c0, c1, c2, c3, Vector3.down, side, tile);
        }

        /// <summary>Square-section beam between two points (stairs, braces, rails).</summary>
        public void AddBeam(Vector3 from, Vector3 to, float thickness, Material m, float depth = -1f)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-4f) return;
            dir /= len;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            float hw = thickness * 0.5f, hd = (depth > 0 ? depth : thickness) * 0.5f;
            Vector3 s = side * hw, u = up * hd;
            Vector3 a0 = from - s - u, a1 = from + s - u, a2 = from + s + u, a3 = from - s + u;
            Vector3 b0 = to - s - u, b1 = to + s - u, b2 = to + s + u, b3 = to - s + u;
            AddQuad(a0, b0, b1, a1, -up, m);
            AddQuad(a1, b1, b2, a2, side, m);
            AddQuad(a2, b2, b3, a3, up, m);
            AddQuad(a3, b3, b0, a0, -side, m);
            AddQuad(a0, a1, a2, a3, -dir, m);
            AddQuad(b0, b3, b2, b1, dir, m);
        }

        /// <summary>Vertical cylinder standing on <paramref name="baseCenter"/>. UVs wrap around (u = arc length / tile).</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Material m, bool capTop = true, float tile = 1f)
        {
            segments = Mathf.Max(3, segments);
            Vector3 top = baseCenter + Vector3.up * height;
            float inv = 1f / Mathf.Max(0.0001f, tile);
            float seg = 2f * Mathf.PI * radius / segments;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments;
                float a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 n = (d0 + d1).normalized;
                float u0 = s * seg * inv, u1 = (s + 1) * seg * inv;
                float v0 = baseCenter.y * inv, v1 = top.y * inv;
                AddQuad(baseCenter + d0 * radius, top + d0 * radius, top + d1 * radius, baseCenter + d1 * radius,
                    new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), n, m);
                if (capTop) AddTriangle(top, top + d0 * radius, top + d1 * radius, Vector3.up, m, tile);
            }
        }

        /// <summary>Cylinder along an arbitrary axis (e.g. wheels). Caps both ends.</summary>
        public void AddCylinderAxis(Vector3 center, Vector3 axis, float radius, float length, int segments, Material side, Material cap)
        {
            segments = Mathf.Max(3, segments);
            axis.Normalize();
            Vector3 t1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(axis, t1);
            Vector3 e0 = center - axis * length * 0.5f, e1 = center + axis * length * 0.5f;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = t1 * Mathf.Cos(a0) + t2 * Mathf.Sin(a0);
                Vector3 d1 = t1 * Mathf.Cos(a1) + t2 * Mathf.Sin(a1);
                AddQuad(e0 + d0 * radius, e1 + d0 * radius, e1 + d1 * radius, e0 + d1 * radius, (d0 + d1).normalized, side);
                AddTriangle(e1, e1 + d0 * radius, e1 + d1 * radius, axis, cap);
                AddTriangle(e0, e0 + d1 * radius, e0 + d0 * radius, -axis, cap);
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

        /// <summary>Low-poly faceted sphere (UV sphere), optionally squashed.</summary>
        public void AddSphere(Vector3 center, Vector3 radii, int segments, int rings, Material m)
        {
            for (int r = 0; r < rings; r++)
            {
                float p0 = Mathf.PI * r / rings - Mathf.PI / 2, p1 = Mathf.PI * (r + 1) / rings - Mathf.PI / 2;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = 2 * Mathf.PI * s / segments, a1 = 2 * Mathf.PI * (s + 1) / segments;
                    Vector3 P(float p, float a) => center + Vector3.Scale(radii, new Vector3(Mathf.Cos(p) * Mathf.Cos(a), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Sin(a)));
                    Vector3 v00 = P(p0, a0), v01 = P(p0, a1), v10 = P(p1, a0), v11 = P(p1, a1);
                    Vector3 n = ((v00 + v01 + v10 + v11) * 0.25f - center).normalized;
                    if (r == 0) AddTriangle(v00, v10, v11, n, m);
                    else if (r == rings - 1) AddTriangle(v00, v10, v01, n, m);
                    else AddQuad(v00, v10, v11, v01, n, m);
                }
            }
        }

        public Mesh ToMesh(string name, out Material[] mats)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mats = materials.ToArray();
            return mesh;
        }

        /// <summary>Creates a GameObject with this geometry. Returns null if nothing was added.</summary>
        public GameObject Spawn(string name, Transform parent, bool castShadows = true)
        {
            if (IsEmpty) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Origin;
            var mesh = ToMesh(name, out var mats);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            if (!castShadows) mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public void Clear()
        {
            vertices.Clear(); normals.Clear(); uvs.Clear(); materials.Clear(); triangles.Clear(); materialIndex.Clear();
        }
    }
}
