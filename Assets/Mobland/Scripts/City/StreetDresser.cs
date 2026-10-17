using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    /// <summary>
    /// Street furniture and life: gas/electric street lamps, hydrants, parked period cars and trucks,
    /// street trees, manholes, laundry lines over tenement back yards, crates and barrels at the docks.
    /// Props are merged into one mesh per block.
    /// </summary>
    public sealed class StreetDresser
    {
        readonly CityConfig cfg;
        readonly CityModel city;
        readonly SeededRandom rng;
        readonly HashSet<int> elAvenues, trolleyAvenues;

        public readonly List<Vector3> LampLights = new List<Vector3>();

        static Material iron, lampGlass, tire, hub, glass, chrome, hydrant, crate, barrel, steelHoop, rope;
        static readonly Dictionary<Color, Material> paints = new Dictionary<Color, Material>();
        static Material[] cloths;

        public StreetDresser(CityConfig cfg, CityModel city, SeededRandom rng, List<int> el, List<int> trolley)
        {
            this.cfg = cfg;
            this.city = city;
            this.rng = rng;
            elAvenues = new HashSet<int>(el);
            trolleyAvenues = new HashSet<int>(trolley);
            EnsureMaterials();
        }

        static void EnsureMaterials()
        {
            if (iron != null) return;
            iron = MaterialLibrary.Get(Palette.CastIron, 0.35f, 0.5f);
            lampGlass = MaterialLibrary.Glow(new Color(0.85f, 0.82f, 0.7f), Palette.LampGlow, 7f);
            tire = MaterialLibrary.Get(Palette.Tire, 0.1f);
            hub = MaterialLibrary.Get(new Color(0.35f, 0.33f, 0.3f), 0.5f, 0.6f);
            glass = MaterialLibrary.Get(new Color(0.05f, 0.06f, 0.07f), 0.9f);
            chrome = MaterialLibrary.Get(Palette.Chrome, 0.85f, 1f);
            hydrant = MaterialLibrary.Get(Palette.HydrantRed, 0.35f);
            crate = MaterialLibrary.Surface("planks");
            barrel = MaterialLibrary.Get(new Color(0.33f, 0.22f, 0.13f), 0.2f);
            steelHoop = MaterialLibrary.Get(Palette.Steel, 0.4f, 0.6f);
            rope = MaterialLibrary.Get(new Color(0.25f, 0.24f, 0.22f), 0.1f);
            cloths = new[]
            {
                MaterialLibrary.Get(new Color(0.9f, 0.88f, 0.82f), 0.05f), MaterialLibrary.Get(new Color(0.92f, 0.9f, 0.86f), 0.05f),
                MaterialLibrary.Get(new Color(0.6f, 0.15f, 0.12f), 0.05f), MaterialLibrary.Get(new Color(0.25f, 0.35f, 0.55f), 0.05f),
                MaterialLibrary.Get(new Color(0.75f, 0.68f, 0.45f), 0.05f), MaterialLibrary.Get(new Color(0.35f, 0.45f, 0.3f), 0.05f),
            };
        }

        static Material Paint(Color c)
        {
            if (!paints.TryGetValue(c, out var m)) paints[c] = m = MaterialLibrary.Get(c, 0.75f, 0.1f);
            return m;
        }

        public void Build(Transform root)
        {
            var parent = new GameObject("Street Props").transform;
            parent.SetParent(root, false);
            foreach (var block in city.Blocks)
            {
                var mb = new MeshBuilder { Origin = block.Center };
                DressBlock(mb, block);
                mb.Spawn($"Props {block.X},{block.Z}", parent);
            }
        }

        void DressBlock(MeshBuilder mb, BlockInfo block)
        {
            Rect r = block.Rect;
            float sw = cfg.sidewalkWidth;
            var dt = block.District.Type;
            bool uptown = dt == DistrictType.Uptown;

            // ---- lamps along all four curbs
            float inset = 0.45f;
            LampRow(mb, new Vector3(r.xMin + 2f, 0, r.yMin + inset), new Vector3(r.xMax - 2f, 0, r.yMin + inset), Vector3.back);
            LampRow(mb, new Vector3(r.xMin + 2f, 0, r.yMax - inset), new Vector3(r.xMax - 2f, 0, r.yMax - inset), Vector3.forward);
            LampRow(mb, new Vector3(r.xMin + inset, 0, r.yMin + 2f), new Vector3(r.xMin + inset, 0, r.yMax - 2f), Vector3.left);
            LampRow(mb, new Vector3(r.xMax - inset, 0, r.yMin + 2f), new Vector3(r.xMax - inset, 0, r.yMax - 2f), Vector3.right);

            // ---- hydrants on a couple of corners
            for (int k = 0; k < 4; k++)
            {
                if (!rng.Chance(0.4f)) continue;
                float x = (k & 1) == 0 ? r.xMin + 1.6f : r.xMax - 1.6f;
                float z = (k & 2) == 0 ? r.yMin + 0.6f : r.yMax - 0.6f;
                Hydrant(mb, new Vector3(x, 0.18f, z));
            }

            // ---- street trees uptown (and around parks)
            if (uptown || block.IsPark)
            {
                for (float x = r.xMin + 6f; x < r.xMax - 6f; x += rng.Range(9f, 13f))
                {
                    Tree(mb, new Vector3(x, 0.18f, r.yMin + 1.1f), rng.Range(0.75f, 1.0f), rng);
                    Tree(mb, new Vector3(x + 3f, 0.18f, r.yMax - 1.1f), rng.Range(0.75f, 1.0f), rng);
                }
            }

            // ---- parked cars: street sides (south + north of block) and avenue sides (west + east)
            float park = 1.25f; // distance from curb to car centre line
            ParkRow(mb, r.xMin + 6f, r.xMax - 6f, r.yMin - park, true, dt, 180f);
            ParkRow(mb, r.xMin + 6f, r.xMax - 6f, r.yMax + park, true, dt, 0f);
            if (!elAvenues.Contains(block.X)) ParkRow(mb, r.yMin + 5f, r.yMax - 5f, r.xMin - park, false, dt, 0f);
            if (!elAvenues.Contains(block.X + 1)) ParkRow(mb, r.yMin + 5f, r.yMax - 5f, r.xMax + park, false, dt, 180f);

            // ---- manholes in the street south of the block
            int holes = rng.RangeInclusive(1, 2);
            for (int i = 0; i < holes; i++)
            {
                float x = rng.Range(r.xMin + 5f, r.xMax - 5f);
                mb.AddCylinder(new Vector3(x, 0.01f, r.yMin - cfg.streetWidth * 0.5f), 0.45f, 0.025f, 10, iron, true);
            }

            // ---- crates and barrels by warehouses and on the docks
            if (dt == DistrictType.Docks || dt == DistrictType.Industrial)
            {
                foreach (var b in block.Buildings)
                {
                    if (b.Type != BuildingType.Warehouse && b.Type != BuildingType.Factory) continue;
                    if (!rng.Chance(0.6f)) continue;
                    float z = b.FacesNorth ? r.yMax - sw + 0.9f : r.yMin + sw - 0.9f;
                    float x = rng.Range(b.Footprint.xMin + 1f, b.Footprint.xMax - 1f);
                    Cargo(mb, new Vector3(x, 0.18f, z));
                }
            }

            // ---- laundry lines across tenement back yards
            if (dt == DistrictType.Tenements || dt == DistrictType.LittleItaly || dt == DistrictType.Bowery || dt == DistrictType.Docks)
                Laundry(mb, block);
        }

        // ------------------------------------------------------------------ lamps

        void LampRow(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 streetDir)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 26f));
            for (int i = 0; i <= n; i++)
            {
                if (n > 1 && (i == 0 || i == n) && rng.Chance(0.5f)) continue;
                Vector3 p = Vector3.Lerp(a, b, n == 0 ? 0.5f : (float)i / n);
                p.y = 0.18f;
                Lamp(mb, p, streetDir);
            }
        }

        void Lamp(MeshBuilder mb, Vector3 p, Vector3 streetDir)
        {
            mb.AddCylinder(p, 0.17f, 0.7f, 8, iron, true);
            mb.AddCylinder(p + Vector3.up * 0.7f, 0.07f, 3.9f, 6, iron, false);
            Vector3 top = p + Vector3.up * 4.6f;
            Vector3 head = top + streetDir * 0.9f;
            mb.AddBeam(top, head + Vector3.up * 0.1f, 0.06f, iron);
            mb.AddBeam(p + Vector3.up * 4.1f, top + streetDir * 0.5f, 0.04f, iron);
            // lantern: glass body + cap
            mb.AddBox(head + new Vector3(-0.2f, -0.5f, -0.2f), head + new Vector3(0.2f, -0.05f, 0.2f), lampGlass, lampGlass, 1f, true);
            mb.AddCone(head + Vector3.up * -0.05f, 0.32f, 0.3f, 6, iron);
            LampLights.Add(head + Vector3.down * 0.4f);
        }

        void Hydrant(MeshBuilder mb, Vector3 p)
        {
            mb.AddCylinder(p, 0.17f, 0.15f, 8, hydrant, true);
            mb.AddCylinder(p + Vector3.up * 0.15f, 0.12f, 0.5f, 8, hydrant, true);
            mb.AddCone(p + Vector3.up * 0.65f, 0.14f, 0.14f, 8, hydrant);
            mb.AddCylinderAxis(p + Vector3.up * 0.48f, Vector3.right, 0.05f, 0.42f, 6, hydrant, hydrant);
        }

        // ------------------------------------------------------------------ trees

        public static void Tree(MeshBuilder mb, Vector3 p, float s, SeededRandom rng)
        {
            Material trunk = MaterialLibrary.Get(Palette.TreeTrunk, 0.1f);
            Material leaves = MaterialLibrary.Get(rng.Chance(0.5f) ? Palette.TreeLeaves : Palette.TreeLeaves2, 0.05f);
            float h = 2.6f * s;
            mb.AddCylinder(p, 0.16f * s, h + 0.6f, 6, trunk, false);
            mb.AddSphere(p + Vector3.up * (h + 1.4f * s), new Vector3(2.0f, 1.5f, 2.0f) * s, 8, 4, leaves);
            for (int i = 0; i < 3; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.2f * s + Vector3.up * rng.Range(0.6f, 2.0f) * s;
                mb.AddSphere(p + Vector3.up * h + o, new Vector3(1.3f, 1.0f, 1.3f) * s * rng.Range(0.8f, 1.1f), 7, 4, leaves);
            }
        }

        // ------------------------------------------------------------------ vehicles

        void ParkRow(MeshBuilder mb, float from, float to, float line, bool alongX, DistrictType dt, float yaw)
        {
            float density = dt == DistrictType.Uptown || dt == DistrictType.FinancialDistrict ? 0.38f : 0.26f;
            float t = from + rng.Range(0f, 6f);
            while (t < to - 6f)
            {
                if (rng.Chance(density))
                {
                    string model = VehicleKit.PickCar(rng, dt);
                    float len = VehicleKit.Lengths.TryGetValue(model, out float l) ? l : 4f;
                    float c = t + len * 0.5f;
                    Vector3 p = alongX ? new Vector3(c, 0f, line) : new Vector3(line, 0f, c);
                    float y = alongX ? yaw + 90f : yaw;
                    if (rng.Chance(0.5f)) y += 180f;     // parked either way round
                    if (VehicleKit.Car(mb, rng, model, p, y) > 0f) t += len + 0.6f;
                }
                t += rng.Range(1.5f, 7f);
            }
        }
        // ------------------------------------------------------------------ cargo + laundry

        void Cargo(MeshBuilder mb, Vector3 p)
        {
            int n = rng.RangeInclusive(2, 6);
            for (int i = 0; i < n; i++)
            {
                Vector3 o = p + new Vector3(rng.Range(-1.5f, 1.5f), 0f, rng.Range(-0.6f, 0.6f));
                if (rng.Chance(0.55f))
                {
                    float s = rng.Range(0.6f, 1.0f);
                    mb.AddOrientedBox(o, new Vector3(s, s, s), rng.Range(0f, 30f), crate, crate, 1f);
                    if (rng.Chance(0.4f)) mb.AddOrientedBox(o + Vector3.up * s, new Vector3(s * 0.8f, s * 0.8f, s * 0.8f), rng.Range(0f, 30f), crate, crate, 1f);
                }
                else
                {
                    mb.AddCylinder(o, 0.3f, 0.9f, 8, barrel, true);
                    mb.AddCylinder(o + Vector3.up * 0.2f, 0.31f, 0.05f, 8, steelHoop, false);
                    mb.AddCylinder(o + Vector3.up * 0.7f, 0.31f, 0.05f, 8, steelHoop, false);
                }
            }
        }

        void Laundry(MeshBuilder mb, BlockInfo block)
        {
            var north = new List<BuildingInfo>();
            var south = new List<BuildingInfo>();
            foreach (var b in block.Buildings)
            {
                if (b.Type != BuildingType.Tenement && b.Type != BuildingType.Shopfront) continue;
                (b.FacesNorth ? north : south).Add(b);
            }
            if (north.Count == 0 || south.Count == 0) return;

            int lines = rng.RangeInclusive(2, 6);
            for (int i = 0; i < lines; i++)
            {
                var a = rng.Pick(south);
                var b = rng.Pick(north);
                float xa = rng.Range(a.Footprint.xMin + 0.8f, a.Footprint.xMax - 0.8f);
                float xb = Mathf.Clamp(xa + rng.Range(-4f, 4f), b.Footprint.xMin + 0.8f, b.Footprint.xMax - 0.8f);
                float maxY = Mathf.Min(a.Height, b.Height) - 2f;
                if (maxY < 5f) continue;
                float y = rng.Range(4.5f, maxY);
                Vector3 pa = new Vector3(xa, y, a.Footprint.yMax);   // south row's back face is its north side
                Vector3 pb = new Vector3(xb, y, b.Footprint.yMin);   // north row's back face is its south side
                if (pb.z - pa.z < 2f) continue;
                Vector3 mid = (pa + pb) * 0.5f + Vector3.down * 0.35f;
                mb.AddBeam(pa, mid, 0.025f, rope);
                mb.AddBeam(mid, pb, 0.025f, rope);

                int cloth = Mathf.RoundToInt(Vector3.Distance(pa, pb) / 1.2f);
                for (int c = 0; c < cloth; c++)
                {
                    if (!rng.Chance(0.75f)) continue;
                    float t = (c + 0.5f) / cloth;
                    Vector3 hang = t < 0.5f ? Vector3.Lerp(pa, mid, t * 2f) : Vector3.Lerp(mid, pb, (t - 0.5f) * 2f);
                    float w = rng.Range(0.35f, 0.7f), h = rng.Range(0.4f, 0.9f);
                    Material m = rng.Pick(cloths);
                    Vector3 dx = Vector3.forward * w * 0.5f;
                    Vector3 a0 = hang - dx, a1 = hang + dx, b0 = a0 + Vector3.down * h, b1 = a1 + Vector3.down * h;
                    mb.AddQuad(a0, a1, b1, b0, Vector3.right, m);
                    mb.AddQuad(a0, b0, b1, a1, Vector3.left, m);
                }
            }
        }
    }
}
