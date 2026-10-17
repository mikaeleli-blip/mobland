using System.Collections.Generic;
using UnityEngine;

namespace Mobland.City
{
    public enum DistrictType { Docks, Tenements, LittleItaly, Bowery, FinancialDistrict, Uptown, Industrial }

    public enum BuildingType { Tenement, Shopfront, Brownstone, Warehouse, Factory, Office, EmptyLot }

    /// <summary>A named neighbourhood made of blocks. Later: gang control, police presence, demand.</summary>
    public sealed class DistrictInfo
    {
        public int Id;
        public string Name;
        public DistrictType Type;
        public DistrictProfile Profile;
        public Vector3 Center;
        public readonly List<BlockInfo> Blocks = new List<BlockInfo>();
        public int BuildingCount;
        public Color MapColor => Profile != null ? Profile.mapColor : Color.white;
    }

    /// <summary>One city block between two avenues and two streets. Rect is in XZ (Rect.y = world Z).</summary>
    public sealed class BlockInfo
    {
        public int X, Z;
        public Rect Rect;
        public DistrictInfo District;
        public bool IsPark;
        public readonly List<BuildingInfo> Buildings = new List<BuildingInfo>();
        public Vector3 Center => new Vector3(Rect.center.x, 0f, Rect.center.y);
    }

    /// <summary>One lot / building. This is the unit players will buy, take over and run rackets in.</summary>
    public sealed class BuildingInfo
    {
        public int Id;
        public BlockInfo Block;
        public DistrictInfo District;
        public BuildingType Type;
        public FacadeStyle Style;
        public Rect Footprint;           // XZ, Rect.y = world Z
        public int Floors;               // total storeys including the ground floor
        public int UpperFloors;
        public int TowerFloors;          // setback tower floors (offices), counted in UpperFloors
        public float GroundHeight;
        public float FloorHeight;
        public float Height;             // height of the main mass (below any setback tower)
        public float TotalHeight;        // including tower
        public bool FacesNorth;          // front door faces +Z street if true, -Z street otherwise
        public bool CornerWest, CornerEast;
        public bool HasFireEscape, HasWaterTower, HasBulkhead, HasChimney, HasBillboard, HasSign;
        public int AtlasU, AtlasV;       // random 0..3 offsets into the facade atlas
        public int SignIndex, BillboardIndex;
        public Color AccentColor;        // awning
        public Color CorniceColor;
        public string Address;

        public Vector3 Center => new Vector3(Footprint.center.x, 0f, Footprint.center.y);
        public bool IsBuilt => Type != BuildingType.EmptyLot;
    }

    public sealed class CityModel
    {
        public int Seed;
        public float Width, Depth;
        public Bounds Bounds;
        public BlockInfo[,] Blocks;
        public float[] AvenueCenters;
        public string[] AvenueNames;
        public string[] StreetNames;
        public readonly List<DistrictInfo> Districts = new List<DistrictInfo>();
        public readonly List<BuildingInfo> Buildings = new List<BuildingInfo>();
        public int ParkCount;
    }

    public static class CityText
    {
        public static string Ordinal(int n)
        {
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        public static string Pretty(DistrictType t)
        {
            switch (t)
            {
                case DistrictType.Docks: return "Waterfront";
                case DistrictType.Tenements: return "Tenement District";
                case DistrictType.LittleItaly: return "Little Italy";
                case DistrictType.Bowery: return "Vice District";
                case DistrictType.FinancialDistrict: return "Financial District";
                case DistrictType.Uptown: return "Uptown";
                case DistrictType.Industrial: return "Industrial";
                default: return t.ToString();
            }
        }

        public static string Pretty(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.Office: return "Office Building";
                case BuildingType.EmptyLot: return "Empty Lot";
                default: return t.ToString();
            }
        }
    }
}
