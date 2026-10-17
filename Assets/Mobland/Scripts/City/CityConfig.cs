using System;
using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    [Serializable]
    public struct WeightedBuildingType
    {
        public BuildingType type;
        public float weight;
        public WeightedBuildingType(BuildingType type, float weight) { this.type = type; this.weight = weight; }
    }

    /// <summary>How one kind of district looks and what gets built there.</summary>
    [Serializable]
    public class DistrictProfile
    {
        public DistrictType type;
        public Color mapColor = Color.white;
        [Min(1)] public int minFloors = 3;
        [Min(1)] public int maxFloors = 6;
        [Min(3f)] public float lotWidthMin = 6f;
        [Min(3f)] public float lotWidthMax = 10f;
        [Range(0f, 1f)] public float waterTowerChance = 0.2f;
        public Color[] facadeColors = Array.Empty<Color>();
        public WeightedBuildingType[] buildingTypes = Array.Empty<WeightedBuildingType>();
        public string[] names = Array.Empty<string>();

        public BuildingType PickType(SeededRandom rng)
        {
            float total = 0f;
            foreach (var w in buildingTypes) total += Mathf.Max(0f, w.weight);
            if (total <= 0f) return BuildingType.Tenement;
            float r = rng.Range(0f, total);
            foreach (var w in buildingTypes)
            {
                r -= Mathf.Max(0f, w.weight);
                if (r <= 0f) return w.type;
            }
            return buildingTypes[buildingTypes.Length - 1].type;
        }

        public Color PickFacade(SeededRandom rng)
        {
            if (facadeColors == null || facadeColors.Length == 0) return new Color(0.55f, 0.25f, 0.2f);
            Color c = rng.Pick(facadeColors);
            float v = rng.Range(0.9f, 1.08f); // slight weathering variation
            return new Color(c.r * v, c.g * v, c.b * v);
        }
    }

    /// <summary>
    /// All tunables for the procedural city. Create one via Assets > Create > Mobland > City Config
    /// and assign it to the GameRoot, or leave empty to use these defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "Mobland/City Config", fileName = "CityConfig")]
    public class CityConfig : ScriptableObject
    {
        [Header("Seed")]
        public int seed = 1920;
        public bool randomSeedOnStart = true;

        [Header("Grid (avenues run north-south, streets east-west)")]
        [Range(2, 16)] public int blocksX = 6;
        [Range(2, 30)] public int blocksZ = 14;
        public float blockLengthMin = 56f;
        public float blockLengthMax = 72f;
        public float blockDepthMin = 24f;
        public float blockDepthMax = 30f;
        public float avenueWidth = 16f;
        public float streetWidth = 10f;
        public float sidewalkWidth = 3f;
        public float floorHeight = 3.2f;

        [Header("Districts")]
        [Range(3, 20)] public int districtCount = 9;
        [Range(0f, 0.3f)] public float parkChance = 0.04f;
        [Range(0f, 0.3f)] public float emptyLotChance = 0.05f;
        public List<DistrictProfile> districtProfiles = DefaultProfiles();

        [Header("Waterfront")]
        public bool waterfrontWest = true;
        public bool waterfrontEast = true;
        public bool waterfrontSouth = true;
        public float waterWidth = 260f;
        public float pierLength = 42f;

        [Header("Elevated railway")]
        [Range(0, 4)] public int elevatedRailways = 2;
        public float elevatedHeight = 6.5f;

        public DistrictProfile GetProfile(DistrictType type)
        {
            foreach (var p in districtProfiles)
                if (p != null && p.type == type) return p;
            foreach (var p in DefaultProfiles())
                if (p.type == type) return p;
            return new DistrictProfile { type = type };
        }

        void Reset() { districtProfiles = DefaultProfiles(); }

        // ---- Defaults ------------------------------------------------------

        static Color C(float r, float g, float b) => new Color(r, g, b);

        static readonly Color BrickRed = C(0.55f, 0.24f, 0.18f);
        static readonly Color DarkBrick = C(0.42f, 0.20f, 0.16f);
        static readonly Color Brownstone = C(0.43f, 0.31f, 0.24f);
        static readonly Color Sandstone = C(0.70f, 0.60f, 0.46f);
        static readonly Color GreyStone = C(0.58f, 0.58f, 0.56f);
        static readonly Color YellowBrick = C(0.74f, 0.60f, 0.38f);
        static readonly Color Limestone = C(0.78f, 0.75f, 0.68f);
        static readonly Color Soot = C(0.33f, 0.31f, 0.30f);
        static readonly Color WarehouseBrick = C(0.50f, 0.30f, 0.22f);
        static readonly Color PaintedWood = C(0.40f, 0.44f, 0.41f);

        public static List<DistrictProfile> DefaultProfiles()
        {
            return new List<DistrictProfile>
            {
                new DistrictProfile
                {
                    type = DistrictType.Docks, mapColor = C(0.25f, 0.55f, 0.85f),
                    minFloors = 2, maxFloors = 4, lotWidthMin = 14f, lotWidthMax = 26f, waterTowerChance = 0.05f,
                    facadeColors = new[] { WarehouseBrick, Soot, PaintedWood, DarkBrick },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Warehouse, 6f),
                        new WeightedBuildingType(BuildingType.Shopfront, 1.5f),
                        new WeightedBuildingType(BuildingType.Tenement, 1f),
                    },
                    names = new[] { "Hudson Piers", "West Side Docks", "Chelsea Piers", "East River Docks", "South Street Seaport", "Coenties Slip", "Gansevoort Piers", "Corlears Hook" },
                },
                new DistrictProfile
                {
                    type = DistrictType.Tenements, mapColor = C(0.95f, 0.55f, 0.20f),
                    minFloors = 4, maxFloors = 6, lotWidthMin = 6f, lotWidthMax = 9f, waterTowerChance = 0.25f,
                    facadeColors = new[] { BrickRed, DarkBrick, Soot, YellowBrick },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Tenement, 8f),
                        new WeightedBuildingType(BuildingType.Shopfront, 2.5f),
                    },
                    names = new[] { "Five Points", "Hell's Kitchen", "Lower East Side", "Kleindeutschland", "San Juan Hill", "Yorkville Flats", "Chinatown" },
                },
                new DistrictProfile
                {
                    type = DistrictType.LittleItaly, mapColor = C(0.30f, 0.75f, 0.35f),
                    minFloors = 3, maxFloors = 5, lotWidthMin = 6f, lotWidthMax = 9f, waterTowerChance = 0.2f,
                    facadeColors = new[] { YellowBrick, Sandstone, BrickRed, C(0.62f, 0.42f, 0.30f) },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Tenement, 6f),
                        new WeightedBuildingType(BuildingType.Shopfront, 4f),
                    },
                    names = new[] { "Little Italy", "Mulberry Bend", "Italian Harlem", "Greenwich South" },
                },
                new DistrictProfile
                {
                    type = DistrictType.Bowery, mapColor = C(0.75f, 0.30f, 0.75f),
                    minFloors = 2, maxFloors = 5, lotWidthMin = 7f, lotWidthMax = 11f, waterTowerChance = 0.15f,
                    facadeColors = new[] { DarkBrick, Soot, Brownstone, BrickRed },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Shopfront, 5f),
                        new WeightedBuildingType(BuildingType.Tenement, 4f),
                        new WeightedBuildingType(BuildingType.Warehouse, 1f),
                    },
                    names = new[] { "The Bowery", "The Tenderloin", "Satan's Circus", "Chatham Square", "Times Square" },
                },
                new DistrictProfile
                {
                    type = DistrictType.FinancialDistrict, mapColor = C(0.95f, 0.80f, 0.25f),
                    minFloors = 8, maxFloors = 22, lotWidthMin = 14f, lotWidthMax = 24f, waterTowerChance = 0.3f,
                    facadeColors = new[] { Limestone, GreyStone, Sandstone },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Office, 8f),
                        new WeightedBuildingType(BuildingType.Shopfront, 1f),
                    },
                    names = new[] { "Wall Street", "Financial District", "Printing House Square" },
                },
                new DistrictProfile
                {
                    type = DistrictType.Uptown, mapColor = C(0.20f, 0.75f, 0.75f),
                    minFloors = 3, maxFloors = 5, lotWidthMin = 7f, lotWidthMax = 10f, waterTowerChance = 0.15f,
                    facadeColors = new[] { Brownstone, Sandstone, Limestone },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Brownstone, 7f),
                        new WeightedBuildingType(BuildingType.Shopfront, 1f),
                        new WeightedBuildingType(BuildingType.Office, 0.5f),
                    },
                    names = new[] { "Upper West Side", "Gramercy Park", "Murray Hill", "Sutton Place", "Morningside Heights" },
                },
                new DistrictProfile
                {
                    type = DistrictType.Industrial, mapColor = C(0.65f, 0.35f, 0.30f),
                    minFloors = 2, maxFloors = 5, lotWidthMin = 14f, lotWidthMax = 28f, waterTowerChance = 0.1f,
                    facadeColors = new[] { WarehouseBrick, Soot, DarkBrick },
                    buildingTypes = new[]
                    {
                        new WeightedBuildingType(BuildingType.Factory, 5f),
                        new WeightedBuildingType(BuildingType.Warehouse, 4f),
                    },
                    names = new[] { "Meatpacking District", "Gashouse Row", "Turtle Bay Yards", "Tenth Avenue Freight Yards", "Hunters Point" },
                },
            };
        }
    }
}
