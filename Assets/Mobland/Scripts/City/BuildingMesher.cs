using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    /// <summary>
    /// Builds a building's 3D geometry from its data: textured facades (one atlas cell per window bay),
    /// party walls, roofs with parapets, cornices, shop awnings and signs, stoops, fire escapes,
    /// water towers, chimneys and rooftop billboards.
    /// </summary>
    public static class BuildingMesher
    {
        static Material C(Color c, float smooth = 0.2f, float metal = 0f) => MaterialLibrary.Get(c, smooth, metal);

        static readonly Color[] RoofTints =
        {
            new Color(1f, 1f, 1f), new Color(0.82f, 0.8f, 0.78f), new Color(1.15f, 1.12f, 1.05f),
            new Color(0.95f, 0.85f, 0.75f), new Color(0.7f, 0.7f, 0.72f), new Color(1.25f, 1.2f, 1.1f),
        };

        static Material skylightMat;
        static Material S(string name) => MaterialLibrary.Surface(name);

        /// <summary>World positions where smoke should rise (factory chimneys). Filled during generation.</summary>
        public static readonly List<Vector3> SmokePoints = new List<Vector3>();

        // ---- face helpers ---------------------------------------------------

        struct Face
        {
            public Vector3 LeftBottom;  // y = 0
            public Vector3 Right;       // unit vector along the face, left -> right as seen from outside
            public Vector3 Normal;
            public float Width;
            public Vector3 At(float along, float y) => LeftBottom + Right * along + Vector3.up * y;
        }

        static Face[] Faces(float x0, float x1, float z0, float z1)
        {
            return new[]
            {
                new Face { LeftBottom = new Vector3(x1, 0, z1), Right = Vector3.left, Normal = Vector3.forward, Width = x1 - x0 }, // +Z (north)
                new Face { LeftBottom = new Vector3(x0, 0, z0), Right = Vector3.right, Normal = Vector3.back, Width = x1 - x0 },   // -Z (south)
                new Face { LeftBottom = new Vector3(x1, 0, z0), Right = Vector3.forward, Normal = Vector3.right, Width = z1 - z0 }, // +X (east)
                new Face { LeftBottom = new Vector3(x0, 0, z1), Right = Vector3.back, Normal = Vector3.left, Width = z1 - z0 },     // -X (west)
            };
        }

        const int North = 0, South = 1, East = 2, West = 3;

        /// <summary>Textured band on a face: atlas has 4 columns and <paramref name="atlasRows"/> rows; one cell = one bay x cellHeight.</summary>
        static void Band(MeshBuilder mb, Face f, float y0, float y1, Material m, float bayNominal, float cellHeight, int atlasRows,
            int uOff, int vOff)
        {
            if (y1 - y0 < 0.01f || f.Width < 0.05f) return;
            int bays = Mathf.Max(1, Mathf.RoundToInt(f.Width / bayNominal));
            float u0 = uOff * 0.25f, u1 = u0 + bays * 0.25f;
            float v0 = (float)vOff / atlasRows, v1 = v0 + (y1 - y0) / cellHeight / atlasRows;
            mb.AddQuad(f.At(0, y0), f.At(f.Width, y0), f.At(f.Width, y1), f.At(0, y1),
                new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), f.Normal, m);
        }

        static void Plain(MeshBuilder mb, Face f, float y0, float y1, Material m, float tile)
        {
            if (y1 - y0 < 0.01f || f.Width < 0.05f) return;
            mb.AddQuad(f.At(0, y0), f.At(f.Width, y0), f.At(f.Width, y1), f.At(0, y1), f.Normal, m, tile);
        }

        /// <summary>A box sticking out of a face (cornices, belt courses, signs).</summary>
        static void Ledge(MeshBuilder mb, Face f, float a0, float a1, float y0, float y1, float depth, Material top, Material side = null, float tile = 1f)
        {
            Vector3 p0 = f.At(a0, y0), p1 = f.At(a1, y1) + f.Normal * depth;
            mb.AddBox(Vector3.Min(p0, p1), Vector3.Max(p0, p1), top, side, tile, bottom: true);
        }

        // ---- building --------------------------------------------------------

        public static void Build(MeshBuilder mb, BuildingInfo b, SeededRandom rng)
        {
            Rect r = b.Footprint;
            if (b.Type == BuildingType.EmptyLot) { BuildEmptyLot(mb, b, rng); return; }

            FacadeStyle st = b.Style;
            float x0 = r.xMin, x1 = r.xMax, z0 = r.yMin, z1 = r.yMax;
            float gh = b.GroundHeight, fh = b.FloorHeight;
            float H = b.Height;
            var faces = Faces(x0, x1, z0, z1);
            int front = b.FacesNorth ? North : South, back = b.FacesNorth ? South : North;

            Material upper = st.UpperMat, ground = st.GroundMat, wall = st.WallMat;
            Material roof = MaterialLibrary.Tinted("roof_tar", RoofTints[rng.Range(0, RoofTints.Length)]);
            Material coping = st.Wall.StartsWith("wall_brick") ? wall : S("concrete");
            Material cornice = C(b.CorniceColor, 0.3f, 0.2f);
            Material stone = C(Palette.Stone, 0.15f);

            bool[] facade = new bool[4];
            facade[front] = true;
            facade[back] = true;
            facade[West] |= b.CornerWest || b.Type == BuildingType.Office;
            facade[East] |= b.CornerEast || b.Type == BuildingType.Office;

            for (int i = 0; i < 4; i++)
            {
                Face f = faces[i];
                if (!facade[i]) { Plain(mb, f, 0f, H, wall, st.WallTile); continue; }
                if (i == back)
                {
                    // rear: upper texture all the way down (back yards)
                    Band(mb, f, 0f, H, upper, st.BayWidth, fh, 4, (b.AtlasU + 2) % 4, b.AtlasV);
                    continue;
                }
                Band(mb, f, 0f, gh, ground, st.GroundBay, gh, 1, b.AtlasU, 0);
                Band(mb, f, gh, H, upper, st.BayWidth, fh, 4, b.AtlasU, b.AtlasV);
                if (!st.Stoop) Ledge(mb, f, -0.05f, f.Width + 0.05f, gh - 0.12f, gh + 0.1f, 0.12f, stone);
                // cornice: two tiers
                Ledge(mb, f, -0.15f, f.Width + 0.15f, H - 0.55f, H + 0.35f, 0.55f, cornice);
                Ledge(mb, f, 0f, f.Width, H - 0.85f, H - 0.55f, 0.28f, cornice);
            }

            Roof(mb, x0, x1, z0, z1, H, roof, coping, wall, st.WallTile);

            Face ff = faces[front];
            if (st.Shop) ShopDressing(mb, b, ff, gh);
            if (st.Stoop) Stoop(mb, b, ff, gh, rng);
            if (b.Type == BuildingType.Tenement) DoorSteps(mb, b, ff, st, 2);
            if (b.HasFireEscape && b.UpperFloors >= 3) FireEscape(mb, b, ff, gh, fh, rng);

            float roofY = H + 0.05f;
            float zFront = b.FacesNorth ? z1 : z0, zBack = b.FacesNorth ? z0 : z1;

            // Setback tower.
            if (b.TowerFloors > 0)
            {
                float inset = Mathf.Min(r.width, r.height) * 0.2f;
                float tx0 = x0 + inset, tx1 = x1 - inset, tz0 = z0 + inset, tz1 = z1 - inset;
                float top = b.TotalHeight;
                var tf = Faces(tx0, tx1, tz0, tz1);
                for (int i = 0; i < 4; i++)
                {
                    Band(mb, tf[i], H, top, upper, st.BayWidth, fh, 4, (b.AtlasU + i) % 4, (b.AtlasV + 1) % 4);
                    Ledge(mb, tf[i], -0.2f, tf[i].Width + 0.2f, top - 0.5f, top + 0.3f, 0.45f, cornice);
                }
                Roof(mb, tx0, tx1, tz0, tz1, top, roof, coping, wall, st.WallTile);
                float cx = (tx0 + tx1) / 2, cz = (tz0 + tz1) / 2;
                mb.AddBoxXZ(cx - 2f, cx + 2f, top, top + 3f, cz - 1.5f, cz + 1.5f, roof, side: S("wall_stone_lime"), tile: 2f);
            }

            // Rooftop clutter.
            if (b.HasBulkhead && r.width > 4.5f && r.height > 6f && b.TowerFloors == 0)
            {
                float bx = rng.Range(x0 + 1.8f, x1 - 1.8f);
                float bz = Mathf.Lerp(zFront, zBack, rng.Range(0.35f, 0.55f));
                mb.AddBoxXZ(bx - 1.1f, bx + 1.1f, roofY, roofY + 2.5f, bz - 1.3f, bz + 1.3f, roof, side: wall, tile: st.WallTile);
            }

            if (b.Type == BuildingType.Tenement || b.Type == BuildingType.Shopfront || b.Type == BuildingType.Brownstone)
            {
                int stacks = rng.RangeInclusive(1, 3);
                for (int k = 0; k < stacks; k++)
                {
                    float cx = rng.Chance(0.5f) ? x0 + 0.45f : x1 - 0.45f;
                    float cz = rng.Range(z0 + 1f, z1 - 1f);
                    float ch = rng.Range(0.9f, 1.8f);
                    mb.AddBoxXZ(cx - 0.35f, cx + 0.35f, roofY, roofY + ch, cz - 0.45f, cz + 0.45f, coping, side: wall, tile: st.WallTile);
                }
            }

            if (b.TowerFloors == 0) RoofExtras(mb, b, roofY, rng, roof);

            if (b.HasWaterTower && b.TowerFloors == 0)
            {
                float wx = rng.Range(x0 + 2f, Mathf.Max(x0 + 2f, x1 - 2f));
                float wz = Mathf.Lerp(zBack, zFront, 0.3f);
                WaterTower(mb, new Vector3(wx, roofY, wz), rng);
            }

            if (b.HasChimney)
            {
                float cx = Mathf.Lerp(x0, x1, rng.Range(0.2f, 0.8f));
                float cz = Mathf.Lerp(zBack, zFront, 0.25f);
                float h = rng.Range(12f, 20f);
                mb.AddBoxXZ(cx - 1.4f, cx + 1.4f, roofY, roofY + 1.2f, cz - 1.4f, cz + 1.4f, coping, side: wall, tile: 2f);
                mb.AddCylinder(new Vector3(cx, roofY, cz), 0.95f, h, 10, S("wall_brick_red"), false, 2f);
                mb.AddCylinder(new Vector3(cx, roofY + h - 1.2f, cz), 1.05f, 1.2f, 10, C(new Color(0.08f, 0.07f, 0.07f)), true);
                SmokePoints.Add(new Vector3(cx, roofY + h + 0.3f, cz));
            }

            if (b.HasBillboard && b.TowerFloors == 0) Billboard(mb, b, ff, roofY);
        }

        static void RoofExtras(MeshBuilder mb, BuildingInfo b, float y, SeededRandom rng, Material roof)
        {
            Rect r = b.Footprint;
            if (r.width < 4f || r.height < 5f) return;
            if (skylightMat == null) skylightMat = MaterialLibrary.Glow(new Color(0.18f, 0.2f, 0.2f), new Color(1f, 0.78f, 0.5f), 1.6f);
            Material metal = C(new Color(0.32f, 0.33f, 0.33f), 0.35f, 0.6f);
            Material wood = C(new Color(0.38f, 0.3f, 0.22f), 0.1f);
            float X() => rng.Range(r.xMin + 1.2f, r.xMax - 1.2f);
            float Z() => rng.Range(r.yMin + 1.2f, r.yMax - 1.2f);

            // skylights (glow at night)
            int sky = b.Type == BuildingType.Warehouse || b.Type == BuildingType.Factory ? rng.RangeInclusive(2, 5) : rng.Range(0, 2);
            for (int i = 0; i < sky; i++)
            {
                float x = X(), z = Z();
                float w = b.Type == BuildingType.Factory ? 1.4f : 0.7f, l = b.Type == BuildingType.Factory ? 4f : 1f;
                mb.AddBoxXZ(x - w, x + w, y, y + 0.35f, z - l, z + l, skylightMat, side: metal);
            }
            // vents
            int vents = rng.RangeInclusive(1, 4);
            for (int i = 0; i < vents; i++)
            {
                Vector3 p = new Vector3(X(), y, Z());
                mb.AddCylinder(p, 0.18f, rng.Range(0.5f, 1.2f), 6, metal, true);
                if (rng.Chance(0.5f)) mb.AddCone(p + Vector3.up * 1.2f, 0.32f, 0.25f, 6, metal);
            }
            // roof hatch
            if (rng.Chance(0.5f))
            {
                float x = X(), z = Z();
                mb.AddBoxXZ(x - 0.5f, x + 0.5f, y, y + 0.4f, z - 0.6f, z + 0.6f, metal);
            }
            // pigeon coop / shed on tenements
            if ((b.Type == BuildingType.Tenement || b.Type == BuildingType.Shopfront) && rng.Chance(0.12f))
            {
                float x = X(), z = Z();
                mb.AddBoxXZ(x - 1.2f, x + 1.2f, y, y + 1.6f, z - 0.8f, z + 0.8f, roof, side: wood);
            }
            // tar patch (darker rectangle)
            if (rng.Chance(0.6f))
            {
                float x = X(), z = Z(), w = rng.Range(1f, 3f), l = rng.Range(1f, 3f);
                mb.AddQuad(new Vector3(x - w, y + 0.02f, z - l), new Vector3(x - w, y + 0.02f, z + l), new Vector3(x + w, y + 0.02f, z + l),
                    new Vector3(x + w, y + 0.02f, z - l), Vector3.up, MaterialLibrary.Tinted("roof_tar", new Color(0.6f, 0.6f, 0.62f)), 6f);
            }
        }

        static void Roof(MeshBuilder mb, float x0, float x1, float z0, float z1, float y, Material roof, Material coping, Material wall, float wallTile)
        {
            mb.AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, roof, 6f);
            const float t = 0.3f, h = 0.6f;
            mb.AddBoxXZ(x0, x1, y, y + h, z0, z0 + t, coping, side: wall, tile: wallTile);
            mb.AddBoxXZ(x0, x1, y, y + h, z1 - t, z1, coping, side: wall, tile: wallTile);
            mb.AddBoxXZ(x0, x0 + t, y, y + h, z0 + t, z1 - t, coping, side: wall, tile: wallTile);
            mb.AddBoxXZ(x1 - t, x1, y, y + h, z0 + t, z1 - t, coping, side: wall, tile: wallTile);
        }

        // ---- ground-floor dressing --------------------------------------------

        static void ShopDressing(MeshBuilder mb, BuildingInfo b, Face f, float gh)
        {
            // Sign board in the band between the shop and the first-floor windows.
            float a0 = 0.35f, a1 = f.Width - 0.35f;
            float y0 = gh + 0.08f, y1 = gh + 0.62f;
            Vector3 o = f.Normal * 0.08f;
            Material signs = MaterialLibrary.Surface("signs", 3.2f);
            int row = b.SignIndex % 16;
            float v1 = 1f - row / 16f, v0 = v1 - 1f / 16f;
            Ledge(mb, f, a0 - 0.05f, a1 + 0.05f, y0 - 0.05f, y1 + 0.05f, 0.06f, C(new Color(0.12f, 0.1f, 0.08f)));
            mb.AddQuad(f.At(a0, y0) + o, f.At(a1, y0) + o, f.At(a1, y1) + o, f.At(a0, y1) + o,
                new Vector2(0.01f, v0 + 0.002f), new Vector2(0.99f, v0 + 0.002f), new Vector2(0.99f, v1 - 0.002f), new Vector2(0.01f, v1 - 0.002f),
                f.Normal, signs);

            if (!b.HasSign) return; // HasSign here = has awning

            // Sloped canvas awning.
            Material canvas = C(b.AccentColor, 0.05f);
            float top = gh - 0.2f, low = gh - 1.15f, depth = 1.6f;
            float w0 = 0.25f, w1 = f.Width - 0.25f;
            Vector3 n = f.Normal;
            Vector3 p0 = f.At(w0, top), p1 = f.At(w1, top);
            Vector3 q0 = f.At(w0, low) + n * depth, q1 = f.At(w1, low) + n * depth;
            Vector3 slopeN = (n + Vector3.up * (depth / (top - low))).normalized;
            mb.AddQuad(p0, p1, q1, q0, slopeN, canvas);
            mb.AddQuad(p0, q0, q1, p1, -slopeN, canvas);
            Vector3 drop = Vector3.down * 0.28f;
            mb.AddQuad(q0, q1, q1 + drop, q0 + drop, n, canvas);
            mb.AddQuad(q0, q0 + drop, q1 + drop, q1, -n, canvas);
            mb.AddTriangle(p0, q0, f.At(w0, low), -f.Right, canvas);
            mb.AddTriangle(p1, q1, f.At(w1, low), f.Right, canvas);
        }

        static void DoorSteps(MeshBuilder mb, BuildingInfo b, Face f, FacadeStyle st, int doorColumn)
        {
            int bays = Mathf.Max(1, Mathf.RoundToInt(f.Width / st.GroundBay));
            float bw = f.Width / bays;
            Material stone = S("curb");
            for (int i = 0; i < bays; i++)
            {
                if ((b.AtlasU + i) % 4 != doorColumn) continue;
                float c = (i + 0.5f) * bw;
                Ledge(mb, f, c - 0.75f, c + 0.75f, 0f, 0.18f, 0.45f, stone);
            }
        }

        static void Stoop(MeshBuilder mb, BuildingInfo b, Face f, float gh, SeededRandom rng)
        {
            int bays = Mathf.Max(1, Mathf.RoundToInt(f.Width / b.Style.BayWidth));
            float bw = f.Width / bays;
            int bay = rng.Chance(0.5f) ? 0 : bays - 1;
            float c = (bay + 0.5f) * bw;
            Material stone = S("wall_stone_brown");
            Material door = C(new Color(0.16f, 0.1f, 0.06f), 0.4f);
            Material iron = C(Palette.CastIron, 0.3f, 0.5f);

            // door surround + door on the parlour floor (covers the window in that bay)
            Ledge(mb, f, c - 0.85f, c + 0.85f, gh - 0.05f, gh + 2.95f, 0.08f, stone, null, 2f);
            Ledge(mb, f, c - 0.6f, c + 0.6f, gh, gh + 2.45f, 0.1f, door);
            Ledge(mb, f, c - 1.0f, c + 1.0f, gh + 2.95f, gh + 3.15f, 0.3f, stone, null, 2f);

            // steps from the sidewalk up to the parlour floor
            int steps = Mathf.Max(3, Mathf.RoundToInt(gh / 0.18f));
            const float run = 0.3f, w = 1.5f;
            for (int s = 0; s < steps; s++)
            {
                float y = gh * (s + 1) / steps;
                float out1 = 0.9f + (steps - 1 - s) * run;
                Vector3 a = f.At(c - w / 2, 0f), d = f.At(c + w / 2, y) + f.Normal * out1;
                mb.AddBox(Vector3.Min(a, d), Vector3.Max(a, d), stone, stone, 2f);
            }
            // railings
            float reach = 0.9f + steps * run;
            foreach (float side in new[] { -w / 2 - 0.05f, w / 2 + 0.05f })
            {
                Vector3 top0 = f.At(c + side, gh + 0.9f) + f.Normal * 0.9f;
                Vector3 bot = f.At(c + side, 0.9f) + f.Normal * reach;
                mb.AddBeam(top0, bot, 0.05f, iron);
                mb.AddBeam(f.At(c + side, 0f) + f.Normal * reach, bot, 0.05f, iron);
            }
        }

        static void FireEscape(MeshBuilder mb, BuildingInfo b, Face f, float gh, float fh, SeededRandom rng)
        {
            int bays = Mathf.Max(1, Mathf.RoundToInt(f.Width / b.Style.BayWidth));
            if (bays < 2) return;
            float bw = f.Width / bays;
            int k = rng.Range(0, bays - 1);
            float a0 = k * bw + bw * 0.2f, a1 = (k + 2) * bw - bw * 0.2f;
            Material iron = C(Palette.CastIron, 0.25f, 0.4f);
            const float depth = 1.05f;
            Vector3 n = f.Normal;
            int floors = b.UpperFloors - b.TowerFloors;

            for (int i = 0; i < floors; i++)
            {
                float y = gh + i * fh + 0.08f;
                Vector3 p0 = f.At(a0, y), p1 = f.At(a1, y + 0.06f) + n * depth;
                mb.AddBox(Vector3.Min(p0, p1), Vector3.Max(p0, p1), iron, iron, 1f, true);
                Vector3 r0 = f.At(a0, y + 0.95f) + n * depth, r1 = f.At(a1, y + 0.95f) + n * depth;
                mb.AddBeam(r0, r1, 0.05f, iron);
                mb.AddBeam(f.At(a0, y + 0.5f) + n * depth, f.At(a1, y + 0.5f) + n * depth, 0.025f, iron);
                mb.AddBeam(f.At(a0, y + 0.95f), r0, 0.05f, iron);
                mb.AddBeam(f.At(a1, y + 0.95f), r1, 0.05f, iron);
                for (int p = 0; p <= 4; p++)
                {
                    float a = Mathf.Lerp(a0, a1, p / 4f);
                    mb.AddBeam(f.At(a, y) + n * depth, f.At(a, y + 0.95f) + n * depth, 0.035f, iron);
                }
                if (i < floors - 1)
                {
                    bool leftToRight = i % 2 == 0;
                    float s0 = leftToRight ? a0 + 0.3f : a1 - 0.3f;
                    float s1 = leftToRight ? a1 - 1.2f : a0 + 1.2f;
                    Vector3 from = f.At(s0, y + 0.06f) + n * (depth * 0.55f);
                    Vector3 to = f.At(s1, y + fh) + n * (depth * 0.55f);
                    mb.AddBeam(from, to, 0.08f, iron, 0.55f);
                }
            }
            Vector3 l0 = f.At(a1 - 0.5f, gh * 0.45f) + n * (depth - 0.1f);
            Vector3 l1 = f.At(a1 - 0.5f, gh + 0.1f) + n * (depth - 0.1f);
            mb.AddBeam(l0 + f.Right * 0.22f, l1 + f.Right * 0.22f, 0.04f, iron);
            mb.AddBeam(l0 - f.Right * 0.22f, l1 - f.Right * 0.22f, 0.04f, iron);
        }

        static void WaterTower(MeshBuilder mb, Vector3 p, SeededRandom rng)
        {
            Material wood = C(Palette.WaterTowerWood * rng.Range(0.8f, 1.1f), 0.1f);
            Material steel = C(Palette.Steel, 0.3f, 0.5f);
            float leg = rng.Range(2.2f, 3.2f), half = 1.05f;
            Vector3[] feet =
            {
                p + new Vector3(-half, 0, -half), p + new Vector3(half, 0, -half),
                p + new Vector3(half, 0, half), p + new Vector3(-half, 0, half),
            };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = feet[i], b2 = feet[(i + 1) % 4];
                mb.AddBeam(a, a + Vector3.up * leg, 0.16f, steel);
                mb.AddBeam(a + Vector3.up * 0.2f, b2 + Vector3.up * (leg - 0.2f), 0.05f, steel);
            }
            mb.AddBoxXZ(p.x - 1.4f, p.x + 1.4f, p.y + leg, p.y + leg + 0.15f, p.z - 1.4f, p.z + 1.4f, wood);
            Vector3 tank = p + Vector3.up * (leg + 0.15f);
            float h = rng.Range(2.6f, 3.4f);
            mb.AddCylinder(tank, 1.35f, h, 12, wood, false);
            for (int k = 1; k <= 3; k++)
                mb.AddCylinder(tank + Vector3.up * (h * k / 4f), 1.38f, 0.07f, 12, steel, false);
            mb.AddCone(tank + Vector3.up * h, 1.5f, 1.1f, 12, steel);
        }

        static void Billboard(MeshBuilder mb, BuildingInfo b, Face f, float roofY)
        {
            float w = Mathf.Min(f.Width - 1f, 9f);
            if (w < 5f) return;
            float h = w / 4f;
            float c = f.Width / 2;
            const float back = 2.2f;
            Vector3 n = f.Normal;
            Material steel = C(Palette.Steel, 0.2f, 0.3f);
            Material ads = S("billboards");
            float y0 = roofY + 1.6f, y1 = y0 + h;
            Vector3 o = -n * back;
            int row = b.BillboardIndex % 8;
            float v1 = 1f - row / 8f, v0 = v1 - 1f / 8f;
            mb.AddQuad(f.At(c - w / 2, y0) + o, f.At(c + w / 2, y0) + o, f.At(c + w / 2, y1) + o, f.At(c - w / 2, y1) + o,
                new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1), n, ads);
            mb.AddQuad(f.At(c - w / 2, y0) + o, f.At(c - w / 2, y1) + o, f.At(c + w / 2, y1) + o, f.At(c + w / 2, y0) + o, -n, steel);
            for (int i = 0; i <= 3; i++)
            {
                float a = c - w / 2 + w * i / 3f;
                mb.AddBeam(f.At(a, roofY) + o - n * 0.1f, f.At(a, y1) + o - n * 0.1f, 0.14f, steel);
                mb.AddBeam(f.At(a, roofY) + o - n * 1.6f, f.At(a, y1 - 0.3f) + o - n * 0.15f, 0.08f, steel);
            }
            mb.AddBeam(f.At(c - w / 2, y0 - 0.2f) + o + n * 0.4f, f.At(c + w / 2, y0 - 0.2f) + o + n * 0.4f, 0.12f, steel);
        }

        static void BuildEmptyLot(MeshBuilder mb, BuildingInfo b, SeededRandom rng)
        {
            Rect f = b.Footprint;
            Material dirt = S("dirt");
            mb.AddBoxXZ(f.xMin, f.xMax, 0.0f, 0.12f, f.yMin, f.yMax, dirt, tile: 6f);
            int piles = rng.RangeInclusive(2, 5);
            for (int i = 0; i < piles; i++)
            {
                float x = rng.Range(f.xMin + 1.2f, f.xMax - 1.2f), z = rng.Range(f.yMin + 1.2f, f.yMax - 1.2f);
                mb.AddSphere(new Vector3(x, 0.1f, z), new Vector3(rng.Range(0.6f, 1.6f), rng.Range(0.3f, 0.8f), rng.Range(0.6f, 1.6f)), 7, 3, dirt);
            }
            Material brick = S("wall_brick_red");
            for (int i = 0; i < 6; i++)
            {
                float x = rng.Range(f.xMin + 0.8f, f.xMax - 0.8f), z = rng.Range(f.yMin + 0.8f, f.yMax - 0.8f);
                mb.AddOrientedBox(new Vector3(x, 0.12f, z), new Vector3(rng.Range(0.3f, 0.9f), rng.Range(0.1f, 0.4f), rng.Range(0.3f, 0.9f)), rng.Range(0f, 90f), brick, brick, 2f);
            }
            Material planks = S("planks");
            float fz = b.FacesNorth ? f.yMax - 0.1f : f.yMin + 0.1f;
            mb.AddBoxXZ(f.xMin + 0.1f, f.xMax - 0.1f, 0f, 2.1f, fz - 0.05f, fz + 0.05f, planks, tile: 2f);
            for (float x = f.xMin + 0.2f; x < f.xMax; x += 2.4f)
                mb.AddBoxXZ(x - 0.07f, x + 0.07f, 0f, 2.2f, fz - 0.15f, fz + 0.15f, planks, tile: 2f);
        }
    }
}
