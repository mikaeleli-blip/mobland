using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    public sealed class CityBuildResult
    {
        public CityModel Model;
        public Transform Root;
        public GameObject DistrictOverlay;
        public List<Vector3> LampLights;
        public List<Vector3> SmokePoints;
        public Material Water;
    }

    /// <summary>
    /// Generates a Manhattan-style grid city: long blocks between north-south avenues,
    /// districts grown from seed points, lots subdivided along each street, and placeholder 3D buildings.
    /// The data (CityModel) is kept separate from the visuals so gameplay never depends on meshes.
    /// </summary>
    public sealed class CityGenerator
    {
        readonly CityConfig cfg;
        SeededRandom rng;
        CityModel city;
        Transform root;

        float[] colX, colW, rowZ, rowD;
        readonly List<int> elAvenues = new List<int>();
        readonly List<int> trolleyAvenues = new List<int>();
        Material waterMat;

        public CityGenerator(CityConfig config) { cfg = config; }

        public CityBuildResult Build(int seed, Transform parent)
        {
            rng = new SeededRandom(seed);
            city = new CityModel { Seed = seed };

            var rootGo = new GameObject($"City (seed {seed})");
            root = rootGo.transform;
            root.SetParent(parent, false);

            LayoutGrid();
            AssignDistricts();
            PickParks();
            NameStreets();
            SubdivideLots();

            PickRailAvenues();
            BuildingMesher.SmokePoints.Clear();

            BuildGround();
            GameObject overlay = BuildDistrictOverlay();
            BuildBuildings();
            var dresser = new StreetDresser(cfg, city, rng, elAvenues, trolleyAvenues);
            dresser.Build(root);

            StaticBatchingUtility.Combine(rootGo);
            return new CityBuildResult
            {
                Model = city, Root = root, DistrictOverlay = overlay,
                LampLights = dresser.LampLights,
                SmokePoints = new List<Vector3>(BuildingMesher.SmokePoints),
                Water = waterMat,
            };
        }

        // ====================================================================
        // DATA
        // ====================================================================

        void LayoutGrid()
        {
            int bx = cfg.blocksX, bz = cfg.blocksZ;
            colX = new float[bx]; colW = new float[bx];
            rowZ = new float[bz]; rowD = new float[bz];

            float x = cfg.avenueWidth;
            for (int i = 0; i < bx; i++)
            {
                colW[i] = Mathf.Round(rng.Range(cfg.blockLengthMin, cfg.blockLengthMax));
                colX[i] = x;
                x += colW[i] + cfg.avenueWidth;
            }
            city.Width = x;

            float z = cfg.streetWidth;
            for (int j = 0; j < bz; j++)
            {
                rowD[j] = Mathf.Round(rng.Range(cfg.blockDepthMin, cfg.blockDepthMax));
                rowZ[j] = z;
                z += rowD[j] + cfg.streetWidth;
            }
            city.Depth = z;

            city.AvenueCenters = new float[bx + 1];
            city.AvenueCenters[0] = cfg.avenueWidth * 0.5f;
            for (int i = 0; i < bx; i++) city.AvenueCenters[i + 1] = colX[i] + colW[i] + cfg.avenueWidth * 0.5f;

            city.Blocks = new BlockInfo[bx, bz];
            for (int i = 0; i < bx; i++)
                for (int j = 0; j < bz; j++)
                    city.Blocks[i, j] = new BlockInfo { X = i, Z = j, Rect = new Rect(colX[i], rowZ[j], colW[i], rowD[j]) };

            city.Bounds = new Bounds(new Vector3(city.Width * 0.5f, 0f, city.Depth * 0.5f), new Vector3(city.Width, 10f, city.Depth));
        }

        struct DistrictSeed
        {
            public DistrictType Type;
            public Vector2 Pos;
            public float Scale; // >1 makes the district smaller
            public bool West;
        }

        void AssignDistricts()
        {
            float W = city.Width, D = city.Depth;
            int bx = cfg.blocksX;
            var seeds = new List<DistrictSeed>();

            int docksPerSide = Mathf.Max(1, Mathf.RoundToInt(cfg.blocksZ / 6f));
            void AddDocks(float xPos, bool west)
            {
                for (int k = 0; k < docksPerSide; k++)
                {
                    float z0 = D * k / docksPerSide, z1 = D * (k + 1) / docksPerSide;
                    seeds.Add(new DistrictSeed { Type = DistrictType.Docks, Pos = new Vector2(xPos, rng.Range(z0, z1)), Scale = 2.2f, West = west });
                }
            }
            if (cfg.waterfrontWest) AddDocks(colX[0] + colW[0] * 0.5f, true);
            if (cfg.waterfrontEast && bx > 1) AddDocks(colX[bx - 1] + colW[bx - 1] * 0.5f, false);

            // Money downtown (south), wealth uptown (north) - like Manhattan.
            seeds.Add(new DistrictSeed { Type = DistrictType.FinancialDistrict, Pos = new Vector2(rng.Range(W * 0.3f, W * 0.7f), rng.Range(0f, D * 0.18f)), Scale = 1f });
            seeds.Add(new DistrictSeed { Type = DistrictType.Uptown, Pos = new Vector2(rng.Range(W * 0.25f, W * 0.75f), rng.Range(D * 0.78f, D)), Scale = 1f });

            var core = new List<DistrictType> { DistrictType.Tenements, DistrictType.LittleItaly, DistrictType.Bowery, DistrictType.Industrial };
            rng.Shuffle(core);
            var extra = new List<DistrictType>
            {
                DistrictType.Tenements, DistrictType.Tenements, DistrictType.Tenements,
                DistrictType.Bowery, DistrictType.Bowery, DistrictType.Industrial, DistrictType.Industrial, DistrictType.LittleItaly,
            };

            int remaining = Mathf.Max(0, cfg.districtCount - seeds.Count);
            for (int k = 0; k < remaining; k++)
            {
                DistrictType type = k < core.Count ? core[k] : rng.Pick(extra);
                // Best-candidate sampling: keeps seeds spread out.
                Vector2 best = Vector2.zero; float bestDist = -1f;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var p = new Vector2(rng.Range(W * 0.08f, W * 0.92f), rng.Range(D * 0.05f, D * 0.95f));
                    float minD = float.MaxValue;
                    foreach (var s in seeds) minD = Mathf.Min(minD, Vector2.Distance(p, s.Pos) * s.Scale);
                    if (minD > bestDist) { bestDist = minD; best = p; }
                }
                seeds.Add(new DistrictSeed { Type = type, Pos = best, Scale = type == DistrictType.Industrial ? 1.15f : 1f });
            }

            // One DistrictInfo per seed, with a unique name.
            var used = new HashSet<string>();
            var districts = new List<DistrictInfo>();
            foreach (var s in seeds)
            {
                var profile = cfg.GetProfile(s.Type);
                string name = s.Type == DistrictType.Docks
                    ? UniqueFrom(s.West ? WestDocks : EastDocks, used, s.West ? "Hudson Piers" : "East River Docks")
                    : UniqueName(profile, used);
                districts.Add(new DistrictInfo { Type = s.Type, Profile = profile, Name = name });
            }

            // Nearest (scaled) seed wins each block.
            foreach (var block in city.Blocks)
            {
                Vector2 c = block.Rect.center;
                int bestIdx = 0; float bestD = float.MaxValue;
                for (int i = 0; i < seeds.Count; i++)
                {
                    float d = Vector2.Distance(c, seeds[i].Pos) * seeds[i].Scale;
                    if (d < bestD) { bestD = d; bestIdx = i; }
                }
                block.District = districts[bestIdx];
                districts[bestIdx].Blocks.Add(block);
            }

            foreach (var d in districts)
            {
                if (d.Blocks.Count == 0) continue;
                Vector3 sum = Vector3.zero;
                foreach (var b in d.Blocks) sum += b.Center;
                d.Center = sum / d.Blocks.Count;
                d.Id = city.Districts.Count;
                city.Districts.Add(d);
            }
        }

        static readonly string[] WestDocks = { "Hudson Piers", "West Side Docks", "Chelsea Piers", "Gansevoort Piers", "Hell's Kitchen Piers" };
        static readonly string[] EastDocks = { "East River Docks", "South Street Seaport", "Coenties Slip", "Corlears Hook", "Kips Bay Wharf" };

        string UniqueFrom(string[] names, HashSet<string> used, string fallback)
        {
            var pool = new List<string>(names);
            rng.Shuffle(pool);
            foreach (var n in pool) if (used.Add(n)) return n;
            for (int i = 2; ; i++) { string n = $"{fallback} {i}"; if (used.Add(n)) return n; }
        }

        string UniqueName(DistrictProfile profile, HashSet<string> used)
        {
            var pool = new List<string>(profile.names ?? System.Array.Empty<string>());
            rng.Shuffle(pool);
            foreach (var n in pool)
                if (used.Add(n)) return n;
            string baseName = pool.Count > 0 ? pool[0] : CityText.Pretty(profile.type);
            for (int i = 2; ; i++)
            {
                string n = $"{baseName} {i}";
                if (used.Add(n)) return n;
            }
        }

        void PickParks()
        {
            var candidates = new List<BlockInfo>();
            foreach (var block in city.Blocks)
            {
                var t = block.District.Type;
                bool allowed = t == DistrictType.Tenements || t == DistrictType.LittleItaly || t == DistrictType.Bowery || t == DistrictType.Uptown;
                if (!allowed) continue;
                candidates.Add(block);
                float chance = t == DistrictType.Uptown ? cfg.parkChance * 1.5f : cfg.parkChance;
                if (rng.Chance(chance)) block.IsPark = true;
            }

            int parks = 0;
            foreach (var b in city.Blocks) if (b.IsPark) parks++;
            if (parks == 0 && candidates.Count > 0)
            {
                rng.Pick(candidates).IsPark = true;
                parks = 1;
            }
            city.ParkCount = parks;
        }

        static readonly string[] AvenuePool =
        {
            "12th Ave", "11th Ave", "10th Ave", "9th Ave", "8th Ave", "7th Ave", "6th Ave", "5th Ave",
            "Madison Ave", "Park Ave", "Lexington Ave", "3rd Ave", "2nd Ave", "1st Ave",
            "Avenue A", "Avenue B", "Avenue C", "Avenue D",
        };

        void NameStreets()
        {
            int count = cfg.blocksX + 1;
            city.AvenueNames = new string[count];
            int start = Mathf.Max(0, (AvenuePool.Length - count) / 2);
            for (int i = 0; i < count; i++)
            {
                int idx = start + i;
                city.AvenueNames[i] = idx < AvenuePool.Length ? AvenuePool[idx] : $"Avenue {(char)('E' + idx - AvenuePool.Length)}";
            }

            int firstStreet = rng.RangeInclusive(1, 40);
            city.StreetNames = new string[cfg.blocksZ + 1];
            for (int j = 0; j <= cfg.blocksZ; j++)
                city.StreetNames[j] = CityText.Ordinal(firstStreet + j) + " St";
        }


        void PickRailAvenues()
        {
            elAvenues.Clear();
            trolleyAvenues.Clear();
            int bx = cfg.blocksX;
            var interior = new List<int>();
            for (int i = 1; i < bx; i++) interior.Add(i);
            rng.Shuffle(interior);
            int k = 0;
            for (; k < Mathf.Min(cfg.elevatedRailways, interior.Count); k++) elAvenues.Add(interior[k]);
            for (int t = 0; t < 2 && k < interior.Count; t++, k++) trolleyAvenues.Add(interior[k]);
        }

        void SubdivideLots()
        {
            float sw = cfg.sidewalkWidth;
            float midX = city.Width * 0.5f;

            foreach (var block in city.Blocks)
            {
                if (block.IsPark) continue;
                var prof = block.District.Profile;
                Rect r = block.Rect;
                Rect inner = new Rect(r.xMin + sw, r.yMin + sw, r.width - 2f * sw, r.height - 2f * sw);
                float rowDepth = inner.height * 0.5f;

                for (int side = 0; side < 2; side++)
                {
                    bool facesNorth = side == 1;

                    var widths = new List<float>();
                    float x = inner.xMin, end = inner.xMax;
                    float minW = Mathf.Min(prof.lotWidthMin, inner.width);
                    float maxW = Mathf.Max(minW, prof.lotWidthMax);
                    while (end - x > 0.01f)
                    {
                        float w = Mathf.Round(rng.Range(minW, maxW) * 2f) * 0.5f;
                        if (end - x - w < minW) w = end - x;
                        widths.Add(w);
                        x += w;
                    }

                    x = inner.xMin;
                    for (int i = 0; i < widths.Count; i++)
                    {
                        float w = widths[i];
                        var type = rng.Chance(cfg.emptyLotChance) ? BuildingType.EmptyLot : prof.PickType(rng);

                        float depthFactor;
                        switch (type)
                        {
                            case BuildingType.EmptyLot: depthFactor = 1f; break;
                            case BuildingType.Warehouse:
                            case BuildingType.Factory:
                            case BuildingType.Office: depthFactor = rng.Range(0.88f, 0.98f); break;
                            default: depthFactor = rng.Range(0.7f, 0.92f); break;
                        }
                        float depth = rowDepth * depthFactor;
                        float z0 = facesNorth ? inner.yMax - depth : inner.yMin;

                        var b = new BuildingInfo
                        {
                            Id = city.Buildings.Count,
                            Block = block,
                            District = block.District,
                            Type = type,
                            Footprint = new Rect(x, z0, w, depth),
                            FacesNorth = facesNorth,
                            CornerWest = i == 0,
                            CornerEast = i == widths.Count - 1,
                            AccentColor = rng.Pick(Palette.Awnings),
                            AtlasU = rng.Range(0, 4),
                            AtlasV = rng.Range(0, 4),
                            SignIndex = rng.Range(0, 16),
                            BillboardIndex = rng.Range(0, 8),
                        };
                        SetMassing(b, prof);

                        int streetIdx = facesNorth ? block.Z + 1 : block.Z;
                        float cx = x + w * 0.5f;
                        int number = 2 * Mathf.FloorToInt(Mathf.Abs(cx - midX) / 4f) + (facesNorth ? 2 : 1);
                        string dir = cx < midX ? "W" : "E";
                        b.Address = $"{number} {dir} {city.StreetNames[streetIdx]}";

                        block.Buildings.Add(b);
                        city.Buildings.Add(b);
                        block.District.BuildingCount++;
                        x += w;
                    }
                }
            }
        }

        void SetMassing(BuildingInfo b, DistrictProfile prof)
        {
            if (b.Type == BuildingType.EmptyLot)
            {
                b.Floors = 0; b.Height = b.TotalHeight = 0.2f;
                return;
            }

            b.Style = FacadeCatalog.Pick(b.Type, b.District.Type, rng);
            b.GroundHeight = b.Style.GroundHeight;
            b.FloorHeight = b.Style.FloorHeight;
            b.CorniceColor = rng.Pick(b.Style.Cornices);
            b.HasSign = b.Style.Shop && rng.Chance(0.75f);

            int f = rng.RangeInclusive(prof.minFloors, Mathf.Max(prof.minFloors, prof.maxFloors));
            float w = b.Footprint.width, d = b.Footprint.height;
            switch (b.Type)
            {
                case BuildingType.Brownstone: f = Mathf.Clamp(f, 3, 4); break;
                case BuildingType.Shopfront: f = Mathf.Clamp(f, 2, 5); break;
                case BuildingType.Tenement: f = Mathf.Clamp(f, 3, 7); b.HasFireEscape = rng.Chance(0.8f); break;
                case BuildingType.Warehouse: f = Mathf.Clamp(f, 2, 6); break;
                case BuildingType.Factory: f = Mathf.Clamp(f, 2, 4); b.HasChimney = rng.Chance(0.7f); break;
                case BuildingType.Office:
                    f = Mathf.Max(f, 6);
                    if (f >= 12 && w >= 10f && d >= 9f) b.TowerFloors = Mathf.RoundToInt(f * rng.Range(0.3f, 0.5f));
                    break;
            }

            // Brownstones: "ground" is the half-sunk basement, all listed floors are above it.
            b.UpperFloors = b.Type == BuildingType.Brownstone ? f : f - 1;
            b.Floors = b.Type == BuildingType.Brownstone ? f + 1 : f;
            b.Height = b.GroundHeight + (b.UpperFloors - b.TowerFloors) * b.FloorHeight;
            b.TotalHeight = b.GroundHeight + b.UpperFloors * b.FloorHeight;

            bool flatRoofHousing = b.Type == BuildingType.Tenement || b.Type == BuildingType.Shopfront || b.Type == BuildingType.Office || b.Type == BuildingType.Warehouse;
            b.HasWaterTower = flatRoofHousing && w >= 5f && d >= 6f && rng.Chance(prof.waterTowerChance);
            b.HasBulkhead = b.Type != BuildingType.Factory && rng.Chance(0.5f);

            var dt = b.District.Type;
            bool adDistrict = dt == DistrictType.Bowery || dt == DistrictType.Tenements || dt == DistrictType.Docks || dt == DistrictType.Industrial;
            b.HasBillboard = adDistrict && !b.HasWaterTower && w >= 7f && rng.Chance(0.06f);
        }

        // ====================================================================
        // GROUND
        // ====================================================================

        static Material S(string name) => MaterialLibrary.Surface(name);

        void BuildGround()
        {
            float W = city.Width, D = city.Depth, sw = cfg.sidewalkWidth;
            var mb = new MeshBuilder();
            Material asphalt = S("asphalt"), sidewalk = S("sidewalk"), curb = S("curb"), yard = S("dirt");
            Material quay = S("wall_stone_lime"), cobbles = S("cobbles");

            // City slab: asphalt top, granite quay walls.
            mb.AddBoxXZ(0f, W, -3f, 0f, 0f, D, asphalt, tile: 8f, side: quay);

            // Land beyond the edges that are not waterfront.
            Material outskirts = S("dirt");
            float ww = cfg.waterWidth;
            if (!cfg.waterfrontWest) mb.AddBoxXZ(-ww, 0f, -3f, -0.02f, cfg.waterfrontSouth ? 0f : -ww, D + ww, outskirts, tile: 8f);
            if (!cfg.waterfrontEast) mb.AddBoxXZ(W, W + ww, -3f, -0.02f, cfg.waterfrontSouth ? 0f : -ww, D + ww, outskirts, tile: 8f);
            if (!cfg.waterfrontSouth) mb.AddBoxXZ(0f, W, -3f, -0.02f, -ww, 0f, outskirts, tile: 8f);
            mb.AddBoxXZ(0f, W, -3f, -0.02f, D, D + ww, outskirts, tile: 8f);

            // Cobbled streets around the waterfront and industrial blocks.
            float hs = cfg.streetWidth * 0.5f, ha = cfg.avenueWidth * 0.5f;
            foreach (var block in city.Blocks)
            {
                var t = block.District.Type;
                if (t != DistrictType.Docks && t != DistrictType.Industrial) continue;
                Rect r = block.Rect;
                float y = 0.012f;
                Rect o = new Rect(r.xMin - ha, r.yMin - hs, r.width + 2 * ha, r.height + 2 * hs);
                mb.AddBoxXZ(o.xMin, o.xMax, 0f, y, o.yMin, r.yMin, cobbles, tile: 4f);
                mb.AddBoxXZ(o.xMin, o.xMax, 0f, y, r.yMax, o.yMax, cobbles, tile: 4f);
                mb.AddBoxXZ(o.xMin, r.xMin, 0f, y, r.yMin, r.yMax, cobbles, tile: 4f);
                mb.AddBoxXZ(r.xMax, o.xMax, 0f, y, r.yMin, r.yMax, cobbles, tile: 4f);
            }

            foreach (var block in city.Blocks)
            {
                Rect r = block.Rect;
                const float h = 0.18f;
                mb.AddBoxXZ(r.xMin, r.xMax, 0f, h, r.yMin, r.yMin + sw, sidewalk, tile: 6f, side: curb);
                mb.AddBoxXZ(r.xMin, r.xMax, 0f, h, r.yMax - sw, r.yMax, sidewalk, tile: 6f, side: curb);
                mb.AddBoxXZ(r.xMin, r.xMin + sw, 0f, h, r.yMin + sw, r.yMax - sw, sidewalk, tile: 6f, side: curb);
                mb.AddBoxXZ(r.xMax - sw, r.xMax, 0f, h, r.yMin + sw, r.yMax - sw, sidewalk, tile: 6f, side: curb);

                if (block.IsPark) BuildPark(mb, block);
                else mb.AddBoxXZ(r.xMin + sw, r.xMax - sw, 0f, 0.1f, r.yMin + sw, r.yMax - sw, yard, tile: 6f);
            }

            BuildTrolleyTracks(mb);
            BuildPiers(mb);
            BuildElevatedRailways(mb);
            mb.Spawn("Ground", root);

            // Water: separate object so its material can scroll.
            var wm = new MeshBuilder();
            waterMat = new Material(S("water")) { name = "Water (runtime)" };
            wm.AddQuad(new Vector3(-ww, -1.2f, -ww), new Vector3(-ww, -1.2f, D + ww), new Vector3(W + ww, -1.2f, D + ww),
                new Vector3(W + ww, -1.2f, -ww), Vector3.up, waterMat, 14f);
            wm.Spawn("Water", root, false);
        }

        void BuildPark(MeshBuilder mb, BlockInfo block)
        {
            float sw = cfg.sidewalkWidth;
            Rect r = block.Rect;
            Rect inner = new Rect(r.xMin + sw, r.yMin + sw, r.width - 2f * sw, r.height - 2f * sw);
            mb.AddBoxXZ(inner.xMin, inner.xMax, 0f, 0.16f, inner.yMin, inner.yMax, S("grass"), tile: 8f);

            Material path = S("sidewalk");
            float cx = inner.center.x, cz = inner.center.y;
            mb.AddBoxXZ(inner.xMin, inner.xMax, 0f, 0.17f, cz - 1.3f, cz + 1.3f, path, tile: 6f);
            mb.AddBoxXZ(cx - 1.3f, cx + 1.3f, 0f, 0.17f, inner.yMin, inner.yMax, path, tile: 6f);
            // central fountain / monument
            mb.AddCylinder(new Vector3(cx, 0.17f, cz), 3.2f, 0.5f, 16, S("wall_stone_lime"), true, 2f);
            mb.AddCylinder(new Vector3(cx, 0.17f, cz), 2.8f, 0.52f, 16, S("water"), true, 4f);
            mb.AddCylinder(new Vector3(cx, 0.6f, cz), 0.5f, 2.6f, 8, S("wall_stone_lime"), true, 2f);
            mb.AddSphere(new Vector3(cx, 3.4f, cz), new Vector3(0.6f, 0.8f, 0.6f), 8, 5, MaterialLibrary.Get(new Color(0.22f, 0.33f, 0.28f), 0.5f, 0.7f));

            // iron railing around the park
            Material iron = MaterialLibrary.Get(Palette.CastIron, 0.3f, 0.5f);
            float y = 0.17f;
            void Rail(Vector3 a, Vector3 b)
            {
                mb.AddBeam(a + Vector3.up * (y + 0.9f), b + Vector3.up * (y + 0.9f), 0.05f, iron);
                float len = Vector3.Distance(a, b);
                for (float t = 0; t <= len; t += 2.5f)
                {
                    Vector3 p = Vector3.Lerp(a, b, t / len);
                    mb.AddBeam(p + Vector3.up * y, p + Vector3.up * (y + 1.0f), 0.06f, iron);
                }
            }
            Rail(new Vector3(inner.xMin, 0, inner.yMin), new Vector3(cx - 1.4f, 0, inner.yMin));
            Rail(new Vector3(cx + 1.4f, 0, inner.yMin), new Vector3(inner.xMax, 0, inner.yMin));
            Rail(new Vector3(inner.xMin, 0, inner.yMax), new Vector3(cx - 1.4f, 0, inner.yMax));
            Rail(new Vector3(cx + 1.4f, 0, inner.yMax), new Vector3(inner.xMax, 0, inner.yMax));
            Rail(new Vector3(inner.xMin, 0, inner.yMin), new Vector3(inner.xMin, 0, cz - 1.4f));
            Rail(new Vector3(inner.xMin, 0, cz + 1.4f), new Vector3(inner.xMin, 0, inner.yMax));
            Rail(new Vector3(inner.xMax, 0, inner.yMin), new Vector3(inner.xMax, 0, cz - 1.4f));
            Rail(new Vector3(inner.xMax, 0, cz + 1.4f), new Vector3(inner.xMax, 0, inner.yMax));

            // trees and benches
            int trees = Mathf.RoundToInt(inner.width * inner.height / 70f);
            for (int i = 0; i < trees; i++)
            {
                float x = rng.Range(inner.xMin + 2f, inner.xMax - 2f);
                float z = rng.Range(inner.yMin + 2f, inner.yMax - 2f);
                if (Mathf.Abs(x - cx) < 3.5f || Mathf.Abs(z - cz) < 3.5f) continue;
                StreetDresser.Tree(mb, new Vector3(x, 0.16f, z), rng.Range(0.9f, 1.4f), rng);
            }
            Material wood = MaterialLibrary.Get(new Color(0.3f, 0.22f, 0.14f), 0.2f);
            for (int i = 0; i < 6; i++)
            {
                bool alongX = rng.Chance(0.5f);
                float x = alongX ? rng.Range(inner.xMin + 3f, inner.xMax - 3f) : cx + (rng.Chance(0.5f) ? 2.2f : -2.2f);
                float z = alongX ? cz + (rng.Chance(0.5f) ? 2.2f : -2.2f) : rng.Range(inner.yMin + 3f, inner.yMax - 3f);
                mb.AddOrientedBox(new Vector3(x, 0.17f, z), new Vector3(1.8f, 0.45f, 0.5f), alongX ? 0f : 90f, wood, iron);
            }
        }

        void BuildTrolleyTracks(MeshBuilder mb)
        {
            Material rail = MaterialLibrary.Get(Palette.Rail, 0.6f, 0.8f);
            Material bed = S("cobbles");
            foreach (int a in trolleyAvenues)
            {
                float ax = city.AvenueCenters[a];
                mb.AddBoxXZ(ax - 3.4f, ax + 3.4f, 0f, 0.015f, 0f, city.Depth, bed, tile: 4f);
                foreach (float track in new[] { -1.8f, 1.8f })
                    foreach (float g in new[] { -0.72f, 0.72f })
                    {
                        float x = ax + track + g;
                        mb.AddBoxXZ(x - 0.04f, x + 0.04f, 0f, 0.045f, 0f, city.Depth, rail);
                    }
            }
        }

        void BuildPiers(MeshBuilder mb)
        {
            Material wood = S("planks"), shedWall = S("wall_brick_soot"), roof = S("roof_tar"), pile = S("planks");
            Material hull = MaterialLibrary.Get(Palette.BoatHull, 0.4f), cabin = MaterialLibrary.Get(Palette.BoatCabin, 0.3f);
            Material funnel = MaterialLibrary.Get(new Color(0.55f, 0.15f, 0.1f), 0.3f), black = MaterialLibrary.Get(new Color(0.05f, 0.05f, 0.05f), 0.3f);
            int bx = cfg.blocksX;

            void Pier(BlockInfo block, bool west)
            {
                if (block.District.Type != DistrictType.Docks) return;
                float zc = block.Rect.center.y;
                float width = rng.Range(10f, 14f);
                float len = cfg.pierLength * rng.Range(0.8f, 1.15f);
                float x0 = west ? -len : city.Width;
                float x1 = west ? 0f : city.Width + len;
                mb.AddBoxXZ(x0, x1, -0.3f, 0.05f, zc - width * 0.5f, zc + width * 0.5f, wood, tile: 4f);
                for (float x = Mathf.Min(x0, x1) + 1f; x < Mathf.Max(x0, x1); x += 3f)
                    foreach (float side in new[] { -1f, 1f })
                        mb.AddCylinder(new Vector3(x, -2.5f, zc + side * (width * 0.5f - 0.3f)), 0.22f, 2.5f, 6, pile, false, 2f);

                if (rng.Chance(0.7f))
                {
                    float i0 = Mathf.Min(x0, x1) + 2f, i1 = Mathf.Max(x0, x1) - 2f;
                    float h = rng.Range(5f, 7f);
                    float s0 = zc - width * 0.5f + 1.5f, s1 = zc + width * 0.5f - 1.5f;
                    mb.AddBoxXZ(i0, i1, 0.05f, h, s0, s1, roof, side: shedWall, tile: 2f);
                    // pitched roof
                    float mid = (s0 + s1) / 2;
                    Vector3 a = new Vector3(i0, h, s0), b = new Vector3(i1, h, s0), c = new Vector3(i1, h + 1.6f, mid), d = new Vector3(i0, h + 1.6f, mid);
                    mb.AddQuad(a, b, c, d, new Vector3(0, 1, -1).normalized, roof, 6f);
                    Vector3 a2 = new Vector3(i0, h, s1), b2 = new Vector3(i1, h, s1);
                    mb.AddQuad(a2, d, c, b2, new Vector3(0, 1, 1).normalized, roof, 6f);
                    mb.AddTriangle(a, d, a2, Vector3.left, shedWall, 2f);
                    mb.AddTriangle(b, c, b2, Vector3.right, shedWall, 2f);
                }

                if (rng.Chance(0.5f))
                {
                    // freighter moored alongside
                    float side = rng.Chance(0.5f) ? 1f : -1f;
                    float bw = rng.Range(6f, 8f);
                    float bz0 = zc + side * (width * 0.5f + 1f);
                    float bz1 = bz0 + side * bw;
                    float zmin = Mathf.Min(bz0, bz1), zmax = Mathf.Max(bz0, bz1);
                    float bl = Mathf.Min(len - 4f, rng.Range(22f, 34f));
                    float bxs = west ? -len + 2f : city.Width + len - 2f - bl;
                    float bowX = west ? bxs : bxs + bl;
                    float sternX = west ? bxs + bl : bxs;
                    float dir = west ? 1f : -1f; // from bow towards stern
                    float bodyStart = bowX + dir * 4f;
                    mb.AddBoxXZ(Mathf.Min(bodyStart, sternX), Mathf.Max(bodyStart, sternX), -2f, 1.2f, zmin, zmax, hull);
                    // pointed bow
                    Vector3 tip = new Vector3(bowX, 1.2f, (zmin + zmax) / 2), tipLow = new Vector3(bowX + dir * 1f, -2f, (zmin + zmax) / 2);
                    Vector3 l = new Vector3(bodyStart, 1.2f, zmin), rr = new Vector3(bodyStart, 1.2f, zmax);
                    Vector3 lL = new Vector3(bodyStart, -2f, zmin), rL = new Vector3(bodyStart, -2f, zmax);
                    mb.AddQuad(lL, l, tip, tipLow, new Vector3(-dir, 0, -1).normalized, hull);
                    mb.AddQuad(rL, rr, tip, tipLow, new Vector3(-dir, 0, 1).normalized, hull);
                    mb.AddTriangle(l, tip, rr, Vector3.up, MaterialLibrary.Get(new Color(0.3f, 0.25f, 0.2f), 0.2f));
                    // superstructure + funnel
                    float cMid = bxs + bl * (west ? 0.65f : 0.35f);
                    mb.AddBoxXZ(cMid - 3f, cMid + 3f, 1.2f, 4.2f, zmin + 1f, zmax - 1f, cabin);
                    mb.AddBoxXZ(cMid - 2f, cMid + 2f, 4.2f, 5.6f, zmin + 1.5f, zmax - 1.5f, cabin);
                    mb.AddCylinder(new Vector3(cMid + dir * 0.5f, 5.6f, (zmin + zmax) / 2), 0.8f, 3.2f, 10, funnel, true);
                    mb.AddCylinder(new Vector3(cMid + dir * 0.5f, 8.2f, (zmin + zmax) / 2), 0.82f, 0.6f, 10, black, true);
                    // cargo hatches + mast
                    mb.AddBoxXZ(bodyStart + dir * 2f - 2f, bodyStart + dir * 2f + 2f, 1.2f, 1.7f, zmin + 1.5f, zmax - 1.5f, S("planks"), tile: 2f);
                    mb.AddBeam(new Vector3(bodyStart + dir * 5f, 1.2f, (zmin + zmax) / 2), new Vector3(bodyStart + dir * 5f, 11f, (zmin + zmax) / 2), 0.25f, black);
                }
            }

            for (int j = 0; j < cfg.blocksZ; j++)
            {
                if (cfg.waterfrontWest) Pier(city.Blocks[0, j], true);
                if (cfg.waterfrontEast) Pier(city.Blocks[bx - 1, j], false);
            }
        }

        void BuildElevatedRailways(MeshBuilder mb)
        {
            Material steel = S("steel"), ties = S("planks");
            Material rail = MaterialLibrary.Get(Palette.Rail, 0.6f, 0.8f);
            float h = cfg.elevatedHeight, D = city.Depth;
            foreach (int a in elAvenues)
            {
                float ax = city.AvenueCenters[a];
                mb.AddBoxXZ(ax - 4.2f, ax + 4.2f, h, h + 0.35f, 0f, D, steel, true, 2f);       // deck
                mb.AddBoxXZ(ax - 4.2f, ax - 3.8f, h - 0.9f, h + 1.3f, 0f, D, steel, true, 2f);  // side girders
                mb.AddBoxXZ(ax + 3.8f, ax + 4.2f, h - 0.9f, h + 1.3f, 0f, D, steel, true, 2f);
                mb.AddBoxXZ(ax - 1.2f, ax + 1.2f, h - 0.7f, h, 0f, D, steel, true, 2f);         // centre girder
                foreach (float track in new[] { -1.9f, 1.9f })
                {
                    mb.AddBoxXZ(ax + track - 1.1f, ax + track + 1.1f, h + 0.35f, h + 0.5f, 0f, D, ties, tile: 2f);
                    foreach (float g in new[] { -0.72f, 0.72f })
                        mb.AddBoxXZ(ax + track + g - 0.05f, ax + track + g + 0.05f, h + 0.5f, h + 0.62f, 0f, D, rail);
                }
                for (float z = 6f; z < D; z += 13f)
                {
                    foreach (float side in new[] { -3.4f, 3.4f })
                    {
                        mb.AddBoxXZ(ax + side - 0.3f, ax + side + 0.3f, 0f, h, z - 0.3f, z + 0.3f, steel, tile: 2f);
                        mb.AddBoxXZ(ax + side - 0.5f, ax + side + 0.5f, 0f, 0.5f, z - 0.5f, z + 0.5f, S("concrete"), tile: 2f);
                        mb.AddBeam(new Vector3(ax + side, h - 2.4f, z), new Vector3(ax + side * 0.25f, h - 0.7f, z), 0.25f, steel);
                    }
                    mb.AddBoxXZ(ax - 3.8f, ax + 3.8f, h - 0.9f, h, z - 0.25f, z + 0.25f, steel, true, 2f);  // cross girder
                }
            }
        }

        readonly Dictionary<DistrictInfo, Material> overlayMats = new Dictionary<DistrictInfo, Material>();

        GameObject BuildDistrictOverlay()
        {
            var mb = new MeshBuilder();
            float sw = cfg.sidewalkWidth;
            foreach (var block in city.Blocks)
            {
                Rect r = block.Rect;
                if (!overlayMats.TryGetValue(block.District, out var m))
                {
                    m = new Material(MaterialLibrary.Template()) { name = "Overlay " + block.District.Name };
                    MaterialLibrary.SetColor(m, block.District.MapColor);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", block.District.MapColor * 1.5f);
                    overlayMats[block.District] = m;
                }
                float y0 = 0.19f, y1 = 0.26f;
                mb.AddBoxXZ(r.xMin, r.xMax, y0, y1, r.yMin, r.yMin + sw, m);
                mb.AddBoxXZ(r.xMin, r.xMax, y0, y1, r.yMax - sw, r.yMax, m);
                mb.AddBoxXZ(r.xMin, r.xMin + sw, y0, y1, r.yMin + sw, r.yMax - sw, m);
                mb.AddBoxXZ(r.xMax - sw, r.xMax, y0, y1, r.yMin + sw, r.yMax - sw, m);
            }
            var go = mb.Spawn("District Overlay", root, false);
            if (go != null) go.SetActive(false);
            return go;
        }

        void BuildBuildings()
        {
            var districtRoots = new Dictionary<DistrictInfo, Transform>();
            foreach (var d in city.Districts)
            {
                var t = new GameObject(d.Name).transform;
                t.SetParent(root, false);
                districtRoots[d] = t;
            }

            foreach (var b in city.Buildings)
            {
                var mb = new MeshBuilder { Origin = b.Center };
                var detailRng = new SeededRandom(city.Seed ^ (b.Id * 7919 + 17));
                BuildingMesher.Build(mb, b, detailRng);

                var go = mb.Spawn($"{CityText.Pretty(b.Type)} - {b.Address}", districtRoots[b.District]);
                if (go == null) continue;

                var col = go.AddComponent<BoxCollider>();
                float h = Mathf.Max(0.3f, b.TotalHeight);
                col.center = new Vector3(0f, h * 0.5f, 0f);
                col.size = new Vector3(b.Footprint.width, h, b.Footprint.height);

                go.AddComponent<BuildingView>().Info = b;
            }
        }
    }
}
