using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    /// <summary>
    /// Builds placeholder 3D geometry for a building from its data.
    /// Everything here is meant to be swapped for modular Blender kit pieces later.
    /// </summary>
    public static class BuildingMesher
    {
        static Material M(Color c) => MaterialLibrary.Get(c);

        public static void Build(MeshBuilder mb, BuildingInfo b, SeededRandom rng)
        {
            Rect f = b.Footprint;

            if (b.Type == BuildingType.EmptyLot)
            {
                BuildEmptyLot(mb, f, b.FacesNorth, rng);
                return;
            }

            Material facade = M(b.FacadeColor);
            Material roof = M(Palette.Roof);
            Material trim = M(Palette.Trim);

            int towerFloors = b.TowerFloors;
            int baseFloors = b.Floors - towerFloors;
            float fh = b.FloorHeight;
            float baseTop = b.BaseRaise + baseFloors * fh;

            float s = b.FacesNorth ? 1f : -1f;                   // outward direction of the front
            float zFront = b.FacesNorth ? f.yMax : f.yMin;
            float zBack = b.FacesNorth ? f.yMin : f.yMax;

            // Main mass + roof.
            mb.AddBoxXZ(f.xMin, f.xMax, 0f, baseTop, f.yMin, f.yMax, facade);
            AddRoof(mb, f.xMin, f.xMax, f.yMin, f.yMax, baseTop, roof);

            // Front cornice / parapet.
            mb.AddBoxXZ(f.xMin, f.xMax, baseTop - 0.45f, baseTop + 0.3f, zFront, zFront + s * 0.35f, trim);

            bool allSides = b.Type == BuildingType.Office;
            WindowStyle style = StyleFor(b.Type);
            int groundStart = HasSpecialGroundFloor(b.Type) ? 1 : 0;

            // Windows on front and back.
            AddWindowsZFace(mb, f.xMin, f.xMax, zFront, s, b.BaseRaise, fh, groundStart, baseFloors, style, rng);
            AddWindowsZFace(mb, f.xMin, f.xMax, zBack, -s, b.BaseRaise, fh, 1, baseFloors, style, rng);
            // Side windows for corner buildings (exposed to avenues) and free-standing offices.
            if (b.CornerWest || allSides) AddWindowsXFace(mb, f.yMin, f.yMax, f.xMin, -1f, b.BaseRaise, fh, 1, baseFloors, style, rng);
            if (b.CornerEast || allSides) AddWindowsXFace(mb, f.yMin, f.yMax, f.xMax, 1f, b.BaseRaise, fh, 1, baseFloors, style, rng);

            // Ground floor by type.
            switch (b.Type)
            {
                case BuildingType.Shopfront: AddShopfront(mb, f, zFront, s, M(b.AccentColor)); break;
                case BuildingType.Tenement: AddTenementEntrance(mb, f, zFront, s, rng); break;
                case BuildingType.Brownstone: AddStoop(mb, f, zFront, s, b.BaseRaise, facade, rng); break;
                case BuildingType.Warehouse: AddLoadingDoors(mb, f, zFront, s, rng); break;
                case BuildingType.Factory: AddLoadingDoors(mb, f, zFront, s, rng); break;
                case BuildingType.Office: AddOfficeLobby(mb, f, zFront, s, trim); break;
            }

            if (b.HasFireEscape && baseFloors >= 3) AddFireEscape(mb, f, zFront, s, fh, baseFloors, rng);

            float roofTop = baseTop + 0.08f;

            // Setback tower.
            if (towerFloors > 0)
            {
                float inset = Mathf.Min(f.width, f.height) * 0.2f;
                float tx0 = f.xMin + inset, tx1 = f.xMax - inset, tz0 = f.yMin + inset, tz1 = f.yMax - inset;
                float towerTop = baseTop + towerFloors * fh;
                mb.AddBoxXZ(tx0, tx1, baseTop, towerTop, tz0, tz1, facade);
                AddRoof(mb, tx0, tx1, tz0, tz1, towerTop, roof);
                mb.AddBoxXZ(tx0 - 0.2f, tx1 + 0.2f, towerTop - 0.4f, towerTop + 0.25f, tz0 - 0.2f, tz1 + 0.2f, trim);
                float tzFront = b.FacesNorth ? tz1 : tz0, tzBack = b.FacesNorth ? tz0 : tz1;
                // Window rows start at the base roof; floor index offset keeps spacing consistent.
                AddWindowsZFace(mb, tx0, tx1, tzFront, s, baseTop, fh, 0, towerFloors, style, rng);
                AddWindowsZFace(mb, tx0, tx1, tzBack, -s, baseTop, fh, 0, towerFloors, style, rng);
                AddWindowsXFace(mb, tz0, tz1, tx0, -1f, baseTop, fh, 0, towerFloors, style, rng);
                AddWindowsXFace(mb, tz0, tz1, tx1, 1f, baseTop, fh, 0, towerFloors, style, rng);
            }

            // Rooftop clutter.
            if (b.HasBulkhead && f.width > 4f && f.height > 5f)
            {
                float bxc = rng.Range(f.xMin + 1.6f, f.xMax - 1.6f);
                float bzc = Mathf.Lerp(zFront, zBack, rng.Range(0.3f, 0.5f));
                mb.AddBoxXZ(bxc - 1f, bxc + 1f, roofTop, roofTop + 2.3f, bzc - 1.1f, bzc + 1.1f, facade);
                mb.AddBoxXZ(bxc - 1.1f, bxc + 1.1f, roofTop + 2.3f, roofTop + 2.4f, bzc - 1.2f, bzc + 1.2f, roof);
            }

            if (b.HasWaterTower && towerFloors == 0)
            {
                float wx = rng.Range(f.xMin + 1.8f, Mathf.Max(f.xMin + 1.8f, f.xMax - 1.8f));
                float wz = Mathf.Lerp(zBack, zFront, 0.25f);
                AddWaterTower(mb, new Vector3(wx, roofTop, wz));
            }

            if (b.HasChimney)
            {
                float cx = Mathf.Lerp(f.xMin, f.xMax, rng.Range(0.2f, 0.8f));
                float cz = Mathf.Lerp(zBack, zFront, 0.2f);
                mb.AddCylinder(new Vector3(cx, roofTop, cz), 0.9f, rng.Range(9f, 15f), 8, M(new Color(0.36f, 0.22f, 0.17f)));
            }
        }

        // ---- Pieces --------------------------------------------------------

        struct WindowStyle
        {
            public float Spacing;     // target distance between window centres
            public float Width;       // window width
            public float HeightFrac;  // fraction of floor height
            public float LitChance;
        }

        static WindowStyle StyleFor(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.Warehouse: return new WindowStyle { Spacing = 4.5f, Width = 2.2f, HeightFrac = 0.4f, LitChance = 0.03f };
                case BuildingType.Factory: return new WindowStyle { Spacing = 3.4f, Width = 2.6f, HeightFrac = 0.55f, LitChance = 0.05f };
                case BuildingType.Office: return new WindowStyle { Spacing = 2.2f, Width = 1.3f, HeightFrac = 0.55f, LitChance = 0.12f };
                default: return new WindowStyle { Spacing = 2.5f, Width = 1.1f, HeightFrac = 0.5f, LitChance = 0.08f };
            }
        }

        static bool HasSpecialGroundFloor(BuildingType t) =>
            t == BuildingType.Shopfront || t == BuildingType.Warehouse || t == BuildingType.Factory || t == BuildingType.Office || t == BuildingType.Tenement;

        static void AddRoof(MeshBuilder mb, float x0, float x1, float z0, float z1, float y, Material roof)
        {
            float inset = 0.2f;
            if (x1 - x0 <= inset * 2f || z1 - z0 <= inset * 2f) return;
            mb.AddBoxXZ(x0 + inset, x1 - inset, y, y + 0.08f, z0 + inset, z1 - inset, roof);
        }

        static void AddWindowsZFace(MeshBuilder mb, float x0, float x1, float z, float s, float yBase, float fh,
            int firstFloor, int floors, WindowStyle st, SeededRandom rng)
        {
            float w = x1 - x0;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w - 0.6f) / st.Spacing));
            float spacing = w / cols;
            float ww = Mathf.Min(st.Width, spacing * 0.7f);
            Material dark = M(Palette.Window), lit = M(Palette.LitWindow);
            float zq = z + s * 0.03f;
            var n = new Vector3(0f, 0f, s);

            for (int fl = firstFloor; fl < floors; fl++)
            {
                float y0 = yBase + fl * fh + fh * 0.28f;
                float y1 = y0 + fh * st.HeightFrac;
                for (int c = 0; c < cols; c++)
                {
                    float cx = x0 + spacing * (c + 0.5f);
                    Material m = rng.Chance(st.LitChance) ? lit : dark;
                    mb.AddQuad(new Vector3(cx - ww * 0.5f, y0, zq), new Vector3(cx + ww * 0.5f, y0, zq),
                               new Vector3(cx + ww * 0.5f, y1, zq), new Vector3(cx - ww * 0.5f, y1, zq), n, m);
                }
            }
        }

        static void AddWindowsXFace(MeshBuilder mb, float z0, float z1, float x, float s, float yBase, float fh,
            int firstFloor, int floors, WindowStyle st, SeededRandom rng)
        {
            float d = z1 - z0;
            int cols = Mathf.Max(1, Mathf.FloorToInt((d - 0.6f) / st.Spacing));
            float spacing = d / cols;
            float ww = Mathf.Min(st.Width, spacing * 0.7f);
            Material dark = M(Palette.Window), lit = M(Palette.LitWindow);
            float xq = x + s * 0.03f;
            var n = new Vector3(s, 0f, 0f);

            for (int fl = firstFloor; fl < floors; fl++)
            {
                float y0 = yBase + fl * fh + fh * 0.28f;
                float y1 = y0 + fh * st.HeightFrac;
                for (int c = 0; c < cols; c++)
                {
                    float cz = z0 + spacing * (c + 0.5f);
                    Material m = rng.Chance(st.LitChance) ? lit : dark;
                    mb.AddQuad(new Vector3(xq, y0, cz - ww * 0.5f), new Vector3(xq, y0, cz + ww * 0.5f),
                               new Vector3(xq, y1, cz + ww * 0.5f), new Vector3(xq, y1, cz - ww * 0.5f), n, m);
                }
            }
        }

        static void FrontQuad(MeshBuilder mb, float x0, float x1, float y0, float y1, float z, float s, Material m)
        {
            float zq = z + s * 0.04f;
            mb.AddQuad(new Vector3(x0, y0, zq), new Vector3(x1, y0, zq), new Vector3(x1, y1, zq), new Vector3(x0, y1, zq),
                new Vector3(0f, 0f, s), m);
        }

        static void AddShopfront(MeshBuilder mb, Rect f, float zFront, float s, Material awning)
        {
            FrontQuad(mb, f.xMin + 0.5f, f.xMax - 0.5f, 0.35f, 2.6f, zFront, s, M(Palette.Window));
            // Awning sticking out over the sidewalk.
            mb.AddBoxXZ(f.xMin + 0.4f, f.xMax - 0.4f, 2.75f, 2.95f, zFront, zFront + s * 1.5f, awning, true);
            // Sign band.
            mb.AddBoxXZ(f.xMin + 0.3f, f.xMax - 0.3f, 3.0f, 3.5f, zFront, zFront + s * 0.12f, M(Palette.Trim));
        }

        static void AddTenementEntrance(MeshBuilder mb, Rect f, float zFront, float s, SeededRandom rng)
        {
            float dx = Mathf.Lerp(f.xMin + 1f, f.xMax - 1f, rng.Range(0.3f, 0.7f));
            FrontQuad(mb, dx - 0.6f, dx + 0.6f, 0f, 2.3f, zFront, s, M(Palette.Window));
            // A couple of ground-floor windows either side.
            if (f.width > 6f)
            {
                FrontQuad(mb, f.xMin + 0.6f, f.xMin + 1.6f, 1.0f, 2.3f, zFront, s, M(Palette.Window));
                FrontQuad(mb, f.xMax - 1.6f, f.xMax - 0.6f, 1.0f, 2.3f, zFront, s, M(Palette.Window));
            }
        }

        static void AddStoop(MeshBuilder mb, Rect f, float zFront, float s, float raise, Material stone, SeededRandom rng)
        {
            float sx = Mathf.Lerp(f.xMin + 1.2f, f.xMax - 1.2f, rng.Chance(0.5f) ? 0.2f : 0.8f);
            mb.AddBoxXZ(sx - 0.8f, sx + 0.8f, 0f, raise, zFront, zFront + s * 2.2f, stone);
            FrontQuad(mb, sx - 0.55f, sx + 0.55f, raise, raise + 2.2f, zFront, s, M(Palette.Window));
        }

        static void AddLoadingDoors(MeshBuilder mb, Rect f, float zFront, float s, SeededRandom rng)
        {
            int doors = Mathf.Clamp(Mathf.FloorToInt(f.width / 7f), 1, 4);
            float spacing = f.width / doors;
            for (int i = 0; i < doors; i++)
            {
                float cx = f.xMin + spacing * (i + 0.5f);
                FrontQuad(mb, cx - 1.6f, cx + 1.6f, 0f, 3.4f, zFront, s, M(Palette.Window));
            }
        }

        static void AddOfficeLobby(MeshBuilder mb, Rect f, float zFront, float s, Material trim)
        {
            FrontQuad(mb, f.xMin + 1f, f.xMax - 1f, 0.3f, 3.6f, zFront, s, M(Palette.Window));
            mb.AddBoxXZ(f.xMin, f.xMax, 3.8f, 4.3f, zFront, zFront + s * 0.3f, trim);
        }

        static void AddFireEscape(MeshBuilder mb, Rect f, float zFront, float s, float fh, int floors, SeededRandom rng)
        {
            Material steel = M(Palette.Steel);
            float w = Mathf.Min(f.width * 0.55f, 4.2f);
            float cx = Mathf.Lerp(f.xMin + w * 0.5f + 0.3f, f.xMax - w * 0.5f - 0.3f, rng.Range(0f, 1f));
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f;
            float depth = 0.95f;

            for (int fl = 1; fl < floors; fl++)
            {
                float y = fl * fh + 0.05f;
                mb.AddBoxXZ(x0, x1, y, y + 0.08f, zFront, zFront + s * depth, steel, true);             // platform
                mb.AddBoxXZ(x0, x1, y + 0.85f, y + 0.92f, zFront + s * (depth - 0.06f), zFront + s * depth, steel); // rail
            }
            // Ladder down the side.
            float top = (floors - 1) * fh + 0.9f;
            mb.AddBoxXZ(x1 - 0.12f, x1 - 0.04f, fh * 0.6f, top, zFront + s * (depth - 0.1f), zFront + s * depth, steel);
            mb.AddBoxXZ(x0 + 0.04f, x0 + 0.12f, fh + 0.05f, top, zFront + s * (depth - 0.1f), zFront + s * depth, steel);
        }

        static void AddWaterTower(MeshBuilder mb, Vector3 p)
        {
            Material wood = M(Palette.WaterTower), steel = M(Palette.Steel);
            float leg = 1.6f, half = 0.8f;
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    float x = p.x + ix * half, z = p.z + iz * half;
                    mb.AddBoxXZ(x - 0.08f, x + 0.08f, p.y, p.y + leg, z - 0.08f, z + 0.08f, steel);
                }
            mb.AddCylinder(p + Vector3.up * leg, 1.15f, 2.3f, 10, wood);
            mb.AddCone(p + Vector3.up * (leg + 2.3f), 1.3f, 0.9f, 10, steel);
        }

        static void BuildEmptyLot(MeshBuilder mb, Rect f, bool facesNorth, SeededRandom rng)
        {
            Material dirt = M(Palette.Rubble);
            mb.AddBoxXZ(f.xMin + 0.1f, f.xMax - 0.1f, 0.05f, 0.14f, f.yMin + 0.1f, f.yMax - 0.1f, dirt);
            int piles = rng.RangeInclusive(1, 4);
            for (int i = 0; i < piles; i++)
            {
                float x = rng.Range(f.xMin + 1f, f.xMax - 1f), z = rng.Range(f.yMin + 1f, f.yMax - 1f);
                float sx = rng.Range(0.4f, 1.2f), sz = rng.Range(0.4f, 1.2f), h = rng.Range(0.3f, 0.9f);
                mb.AddBoxXZ(x - sx, x + sx, 0.14f, 0.14f + h, z - sz, z + sz, M(Palette.Yard));
            }
            // A bit of fence along the street.
            float fz = facesNorth ? f.yMax - 0.08f : f.yMin;
            mb.AddBoxXZ(f.xMin + 0.1f, f.xMax - 0.1f, 0f, 1.6f, fz, fz + 0.08f, M(Palette.PierWood));
        }
    }
}
