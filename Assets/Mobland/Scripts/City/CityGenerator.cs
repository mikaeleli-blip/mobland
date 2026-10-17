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

            BuildGround();
            GameObject overlay = BuildDistrictOverlay();
            BuildBuildings();

            StaticBatchingUtility.Combine(rootGo);
            return new CityBuildResult { Model = city, Root = root, DistrictOverlay = overlay };
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
        }

        void AssignDistricts()
        {
            float W = city.Width, D = city.Depth;
            int bx = cfg.blocksX;
            var seeds = new List<DistrictSeed>();

            int docksPerSide = Mathf.Max(1, Mathf.RoundToInt(cfg.blocksZ / 6f));
            void AddDocks(float xPos)
            {
                for (int k = 0; k < docksPerSide; k++)
                {
                    float z0 = D * k / docksPerSide, z1 = D * (k + 1) / docksPerSide;
                    seeds.Add(new DistrictSeed { Type = DistrictType.Docks, Pos = new Vector2(xPos, rng.Range(z0, z1)), Scale = 2.2f });
                }
            }
            if (cfg.waterfrontWest) AddDocks(colX[0] + colW[0] * 0.5f);
            if (cfg.waterfrontEast && bx > 1) AddDocks(colX[bx - 1] + colW[bx - 1] * 0.5f);

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
                districts.Add(new DistrictInfo { Type = s.Type, Profile = profile, Name = UniqueName(profile, used) });
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

                    // Split the row into lot widths.
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
                            default: depthFactor = rng.Range(0.7f, 0.95f); break;
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
                            FacadeColor = prof.PickFacade(rng),
                            AccentColor = rng.Pick(Palette.Awnings),
                        };
                        SetMassing(b, prof);

                        // Address: house numbers grow away from the Fifth-Avenue-style centre line.
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
            int f = rng.RangeInclusive(prof.minFloors, Mathf.Max(prof.minFloors, prof.maxFloors));
            float fh = cfg.floorHeight;
            float w = b.Footprint.width, d = b.Footprint.height;

            switch (b.Type)
            {
                case BuildingType.EmptyLot:
                    b.Floors = 0; b.FloorHeight = fh; b.Height = 0.25f;
                    return;
                case BuildingType.Brownstone:
                    f = Mathf.Clamp(f, 3, 4); b.BaseRaise = 1.2f;
                    break;
                case BuildingType.Shopfront:
                    f = Mathf.Clamp(f, 2, 5);
                    break;
                case BuildingType.Tenement:
                    f = Mathf.Clamp(f, 3, 7);
                    b.HasFireEscape = rng.Chance(0.75f);
                    break;
                case BuildingType.Warehouse:
                    f = Mathf.Clamp(f, 2, 6); fh = 4.2f;
                    break;
                case BuildingType.Factory:
                    f = Mathf.Clamp(f, 2, 4); fh = 4.6f;
                    b.HasChimney = true;
                    break;
                case BuildingType.Office:
                    f = Mathf.Max(f, 6);
                    if (f >= 12 && w >= 10f && d >= 9f) b.TowerFloors = Mathf.RoundToInt(f * rng.Range(0.3f, 0.5f));
                    break;
            }

            b.Floors = f;
            b.FloorHeight = fh;
            b.Height = b.BaseRaise + f * fh;

            bool flatRoofHousing = b.Type == BuildingType.Tenement || b.Type == BuildingType.Shopfront || b.Type == BuildingType.Office || b.Type == BuildingType.Warehouse;
            b.HasWaterTower = flatRoofHousing && w >= 5f && d >= 6f && rng.Chance(prof.waterTowerChance);
            b.HasBulkhead = b.Type != BuildingType.Factory && rng.Chance(0.45f);
        }

        // ====================================================================
        // VISUALS
        // ====================================================================

        static Material M(Color c) => MaterialLibrary.Get(c);

        void BuildGround()
        {
            float W = city.Width, D = city.Depth, sw = cfg.sidewalkWidth;
            var mb = new MeshBuilder();
            Material asphalt = M(Palette.Asphalt), sidewalk = M(Palette.Sidewalk), yard = M(Palette.Yard);

            // City slab (its sides become the quay walls).
            mb.AddBoxXZ(0f, W, -3f, 0f, 0f, D, asphalt);

            // Land beyond the edges that are not waterfront, plus the north outskirts.
            Material outskirts = M(Palette.Outskirts);
            float ww = cfg.waterWidth;
            if (!cfg.waterfrontWest) mb.AddBoxXZ(-ww, 0f, -3f, -0.02f, cfg.waterfrontSouth ? 0f : -ww, D + ww, outskirts);
            if (!cfg.waterfrontEast) mb.AddBoxXZ(W, W + ww, -3f, -0.02f, cfg.waterfrontSouth ? 0f : -ww, D + ww, outskirts);
            if (!cfg.waterfrontSouth) mb.AddBoxXZ(0f, W, -3f, -0.02f, -ww, 0f, outskirts);
            mb.AddBoxXZ(0f, W, -3f, -0.02f, D, D + ww, outskirts);

            // River / harbour.
            mb.AddBoxXZ(-ww, W + ww, -8f, -1.2f, -ww, D + ww, M(Palette.Water));

            foreach (var block in city.Blocks)
            {
                Rect r = block.Rect;
                // Sidewalk ring.
                mb.AddBoxXZ(r.xMin, r.xMax, 0f, 0.18f, r.yMin, r.yMin + sw, sidewalk);
                mb.AddBoxXZ(r.xMin, r.xMax, 0f, 0.18f, r.yMax - sw, r.yMax, sidewalk);
                mb.AddBoxXZ(r.xMin, r.xMin + sw, 0f, 0.18f, r.yMin + sw, r.yMax - sw, sidewalk);
                mb.AddBoxXZ(r.xMax - sw, r.xMax, 0f, 0.18f, r.yMin + sw, r.yMax - sw, sidewalk);

                if (block.IsPark) BuildPark(mb, block);
                else mb.AddBoxXZ(r.xMin + sw, r.xMax - sw, 0f, 0.1f, r.yMin + sw, r.yMax - sw, yard);
            }

            BuildPiers(mb);
            BuildElevatedRailways(mb);

            mb.Spawn("Ground", root);
        }

        void BuildPark(MeshBuilder mb, BlockInfo block)
        {
            float sw = cfg.sidewalkWidth;
            Rect r = block.Rect;
            Rect inner = new Rect(r.xMin + sw, r.yMin + sw, r.width - 2f * sw, r.height - 2f * sw);
            mb.AddBoxXZ(inner.xMin, inner.xMax, 0f, 0.16f, inner.yMin, inner.yMax, M(Palette.Grass));

            // Crossing paths.
            Material path = M(Palette.Sidewalk);
            float cx = inner.center.x, cz = inner.center.y;
            mb.AddBoxXZ(inner.xMin, inner.xMax, 0f, 0.17f, cz - 1.2f, cz + 1.2f, path);
            mb.AddBoxXZ(cx - 1.2f, cx + 1.2f, 0f, 0.17f, inner.yMin, inner.yMax, path);

            // Trees.
            Material trunk = M(Palette.TreeTrunk), leaves = M(Palette.TreeLeaves);
            int trees = Mathf.RoundToInt(inner.width * inner.height / 55f);
            for (int i = 0; i < trees; i++)
            {
                float x = rng.Range(inner.xMin + 1.5f, inner.xMax - 1.5f);
                float z = rng.Range(inner.yMin + 1.5f, inner.yMax - 1.5f);
                if (Mathf.Abs(x - cx) < 2.6f || Mathf.Abs(z - cz) < 2.6f) continue;
                float s = rng.Range(0.8f, 1.3f);
                var p = new Vector3(x, 0.16f, z);
                mb.AddCylinder(p, 0.18f * s, 1.8f * s, 5, trunk, false);
                mb.AddCone(p + Vector3.up * 1.4f * s, 1.7f * s, 3.6f * s, 7, leaves);
            }
        }

        void BuildPiers(MeshBuilder mb)
        {
            Material wood = M(Palette.PierWood), shed = M(Palette.PierShed), roof = M(Palette.Roof);
            Material hull = M(Palette.BoatHull), cabin = M(Palette.BoatCabin);
            int bx = cfg.blocksX;

            void Pier(BlockInfo block, bool west)
            {
                if (block.District.Type != DistrictType.Docks) return;
                float zc = block.Rect.center.y;
                float width = rng.Range(9f, 13f);
                float len = cfg.pierLength * rng.Range(0.8f, 1.15f);
                float x0 = west ? -len : city.Width;
                float x1 = west ? 0f : city.Width + len;
                mb.AddBoxXZ(x0, x1, -1.6f, 0.05f, zc - width * 0.5f, zc + width * 0.5f, wood);

                if (rng.Chance(0.65f))
                {
                    float i0 = Mathf.Min(x0, x1) + 2f, i1 = Mathf.Max(x0, x1) - 2f;
                    float h = rng.Range(4.5f, 6.5f);
                    mb.AddBoxXZ(i0, i1, 0.05f, h, zc - width * 0.5f + 1.5f, zc + width * 0.5f - 1.5f, shed);
                    mb.AddBoxXZ(i0 + 0.3f, i1 - 0.3f, h, h + 0.12f, zc - width * 0.5f + 1.8f, zc + width * 0.5f - 1.8f, roof);
                }

                if (rng.Chance(0.45f))
                {
                    // A freighter moored alongside.
                    float side = rng.Chance(0.5f) ? 1f : -1f;
                    float bz0 = zc + side * (width * 0.5f + 1f);
                    float bz1 = bz0 + side * rng.Range(5f, 7f);
                    float bl = Mathf.Min(len - 4f, rng.Range(18f, 30f));
                    float bxStart = west ? -len + 2f : city.Width + len - 2f - bl;
                    mb.AddBoxXZ(bxStart, bxStart + bl, -1.8f, 0.6f, bz0, bz1, hull, false);
                    float cMid = bxStart + bl * 0.65f;
                    mb.AddBoxXZ(cMid - 2.5f, cMid + 2.5f, 0.6f, 3.0f, Mathf.Min(bz0, bz1) + 0.8f, Mathf.Max(bz0, bz1) - 0.8f, cabin);
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
            int bx = cfg.blocksX;
            if (cfg.elevatedRailways <= 0 || bx < 2) return;

            var interior = new List<int>();
            for (int i = 1; i < bx; i++) interior.Add(i);
            rng.Shuffle(interior);

            Material steel = M(Palette.Steel), ties = M(Palette.PierWood);
            float h = cfg.elevatedHeight, D = city.Depth;
            for (int k = 0; k < Mathf.Min(cfg.elevatedRailways, interior.Count); k++)
            {
                float ax = city.AvenueCenters[interior[k]];
                mb.AddBoxXZ(ax - 4f, ax + 4f, h, h + 0.5f, 0f, D, steel, true);            // deck
                mb.AddBoxXZ(ax - 4f, ax - 3.6f, h + 0.5f, h + 1.4f, 0f, D, steel);         // girders
                mb.AddBoxXZ(ax + 3.6f, ax + 4f, h + 0.5f, h + 1.4f, 0f, D, steel);
                mb.AddBoxXZ(ax - 2.6f, ax - 0.6f, h + 0.5f, h + 0.6f, 0f, D, ties);        // track beds
                mb.AddBoxXZ(ax + 0.6f, ax + 2.6f, h + 0.5f, h + 0.6f, 0f, D, ties);
                for (float z = 6f; z < D; z += 14f)
                {
                    mb.AddBoxXZ(ax - 3.6f, ax - 3.1f, 0f, h, z - 0.25f, z + 0.25f, steel);
                    mb.AddBoxXZ(ax + 3.1f, ax + 3.6f, 0f, h, z - 0.25f, z + 0.25f, steel);
                }
            }
        }

        GameObject BuildDistrictOverlay()
        {
            var mb = new MeshBuilder();
            float sw = cfg.sidewalkWidth;
            foreach (var block in city.Blocks)
            {
                Rect r = block.Rect;
                Material m = M(block.District.MapColor);
                float y0 = 0.19f, y1 = 0.24f;
                mb.AddBoxXZ(r.xMin, r.xMax, y0, y1, r.yMin, r.yMin + sw, m);
                mb.AddBoxXZ(r.xMin, r.xMax, y0, y1, r.yMax - sw, r.yMax, m);
                mb.AddBoxXZ(r.xMin, r.xMin + sw, y0, y1, r.yMin + sw, r.yMax - sw, m);
                mb.AddBoxXZ(r.xMax - sw, r.xMax, y0, y1, r.yMin + sw, r.yMax - sw, m);
            }
            var go = mb.Spawn("District Overlay", root);
            if (go != null)
            {
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
            }
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
                float h = Mathf.Max(0.3f, b.Height);
                col.center = new Vector3(0f, h * 0.5f, 0f);
                col.size = new Vector3(b.Footprint.width, h, b.Footprint.height);

                go.AddComponent<BuildingView>().Info = b;
            }
        }
    }
}
