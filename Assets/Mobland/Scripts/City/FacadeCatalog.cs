using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    /// <summary>
    /// A building skin: which texture atlases to use and the real-world size of one atlas cell.
    /// Upper atlases are 4 bays x 4 floors; ground atlases 4 bays x 1 floor.
    /// </summary>
    public sealed class FacadeStyle
    {
        public string Name;
        public string Upper;          // texture set for upper floors
        public float BayWidth;        // nominal metres per window bay
        public float FloorHeight;     // metres per upper floor
        public string Ground;         // texture set for the ground floor
        public float GroundHeight;
        public float GroundBay;
        public string Wall;           // blank party walls
        public float WallTile = 2f;
        public Color[] Cornices;
        public bool Shop;             // ground floor is a shop (awning + sign)
        public bool Stoop;            // brownstone stoop up to the parlour floor

        public Material UpperMat => MaterialLibrary.Surface(Upper, 1.6f);
        public Material GroundMat => MaterialLibrary.Surface(Ground, Shop ? 2.4f : 1.6f);
        public Material WallMat => MaterialLibrary.Surface(Wall);
    }

    public static class FacadeCatalog
    {
        static readonly Color[] MetalCornices = { Palette.CorniceGreen, Palette.CorniceGrey, Palette.CorniceBrown, new Color(0.15f, 0.15f, 0.15f) };
        static readonly Color[] StoneCornices = { Palette.Stone, new Color(0.58f, 0.55f, 0.48f) };

        static readonly Dictionary<string, FacadeStyle> cache = new Dictionary<string, FacadeStyle>();

        static readonly string[] ShopPaints = { "gf_shop_green", "gf_shop_black", "gf_shop_red" };

        /// <summary>Brick colours that fit each district.</summary>
        static string[] BrickFor(DistrictType d)
        {
            switch (d)
            {
                case DistrictType.Docks: return new[] { "red", "soot", "soot" };
                case DistrictType.Tenements: return new[] { "red", "brown", "soot", "yellow" };
                case DistrictType.LittleItaly: return new[] { "yellow", "red", "yellow", "brown" };
                case DistrictType.Bowery: return new[] { "brown", "soot", "red" };
                case DistrictType.Uptown: return new[] { "red", "yellow" };
                case DistrictType.Industrial: return new[] { "soot", "brown", "red" };
                default: return new[] { "red" };
            }
        }

        public static FacadeStyle Pick(BuildingType type, DistrictType district, SeededRandom rng)
        {
            switch (type)
            {
                case BuildingType.Tenement:
                {
                    string c = rng.Pick(BrickFor(district));
                    return Get($"tenement_{c}", () => new FacadeStyle
                    {
                        Upper = $"fac_tenement_{c}", BayWidth = 2.6f, FloorHeight = 3.2f,
                        Ground = "gf_residential", GroundHeight = 3.6f, GroundBay = 2.6f,
                        Wall = $"wall_brick_{c}", Cornices = MetalCornices,
                    });
                }
                case BuildingType.Shopfront:
                {
                    string paint = rng.Pick(ShopPaints);
                    if (district == DistrictType.Uptown && rng.Chance(0.5f))
                    {
                        string stone = rng.Chance(0.5f) ? "brownstone" : "sandstone";
                        return Get($"shop_{stone}_{paint}", () => new FacadeStyle
                        {
                            Upper = $"fac_{stone}", BayWidth = 2.6f, FloorHeight = 3.3f,
                            Ground = paint, GroundHeight = 4.0f, GroundBay = 2.8f,
                            Wall = "wall_stone_brown", Cornices = StoneCornices, Shop = true,
                        });
                    }
                    string c = rng.Pick(BrickFor(district));
                    return Get($"shop_{c}_{paint}", () => new FacadeStyle
                    {
                        Upper = $"fac_tenement_{c}", BayWidth = 2.6f, FloorHeight = 3.2f,
                        Ground = paint, GroundHeight = 4.0f, GroundBay = 2.8f,
                        Wall = $"wall_brick_{c}", Cornices = MetalCornices, Shop = true,
                    });
                }
                case BuildingType.Brownstone:
                {
                    string stone = rng.Chance(0.65f) ? "brownstone" : "sandstone";
                    return Get($"rowhouse_{stone}", () => new FacadeStyle
                    {
                        Upper = $"fac_{stone}", BayWidth = 2.6f, FloorHeight = 3.3f,
                        Ground = "gf_basement", GroundHeight = 1.6f, GroundBay = 2.6f,
                        Wall = "wall_stone_brown", Cornices = StoneCornices, Stoop = true,
                    });
                }
                case BuildingType.Office:
                {
                    string v = rng.Pick(new[] { "lime", "granite", "terra", "lime" });
                    return Get($"office_{v}", () => new FacadeStyle
                    {
                        Upper = $"fac_office_{v}", BayWidth = 2.8f, FloorHeight = 3.4f,
                        Ground = "gf_lobby", GroundHeight = 4.6f, GroundBay = 2.8f,
                        Wall = "wall_stone_lime", Cornices = StoneCornices,
                    });
                }
                case BuildingType.Warehouse:
                {
                    string v = rng.Chance(0.6f) ? "red" : "soot";
                    return Get($"warehouse_{v}", () => new FacadeStyle
                    {
                        Upper = $"fac_warehouse_{v}", BayWidth = 3.4f, FloorHeight = 4.2f,
                        Ground = "gf_loading", GroundHeight = 4.4f, GroundBay = 4.2f,
                        Wall = $"wall_brick_{v}", Cornices = new[] { new Color(0.28f, 0.25f, 0.22f) },
                    });
                }
                case BuildingType.Factory:
                    return Get("factory", () => new FacadeStyle
                    {
                        Upper = "fac_factory", BayWidth = 3.6f, FloorHeight = 4.6f,
                        Ground = "gf_loading", GroundHeight = 4.6f, GroundBay = 4.2f,
                        Wall = "wall_brick_brown", Cornices = new[] { new Color(0.25f, 0.2f, 0.17f) },
                    });
                default:
                    return null;
            }
        }

        static FacadeStyle Get(string key, System.Func<FacadeStyle> make)
        {
            if (!cache.TryGetValue(key, out var s))
            {
                s = make();
                s.Name = key;
                cache[key] = s;
            }
            return s;
        }
    }
}
