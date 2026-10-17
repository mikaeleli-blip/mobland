using System.Collections.Generic;
using Mobland.Core;
using UnityEngine;

namespace Mobland.City
{
    /// <summary>
    /// Cars, trucks and boats built in Blender (Tools/blender) and placed in the city.
    /// Each model uses named material slots; a <see cref="Look"/> supplies the per-vehicle colours.
    /// </summary>
    public static class VehicleKit
    {
        public struct Look
        {
            public Color Paint, Paint2, Box, Hull, Super, Funnel, Band;
        }

        // ------------------------------------------------------------------ materials

        static readonly Dictionary<string, Material> fixedSlots = new Dictionary<string, Material>();

        static Material Fixed(string slot)
        {
            if (fixedSlots.TryGetValue(slot, out var m) && m != null) return m;
            switch (slot)
            {
                case "Black": m = MaterialLibrary.Get(new Color(0.02f, 0.02f, 0.02f), 0.6f, 0.1f); break;
                case "Chrome": m = MaterialLibrary.Get(new Color(0.78f, 0.78f, 0.76f), 0.9f, 1f); break;
                case "Glass": m = MaterialLibrary.Get(new Color(0.03f, 0.04f, 0.05f), 0.95f); break;
                case "Lens": m = MaterialLibrary.Get(new Color(0.85f, 0.83f, 0.72f), 0.9f); break;
                case "Tire": m = MaterialLibrary.Get(new Color(0.035f, 0.035f, 0.035f), 0.12f); break;
                case "Spokes": m = MaterialLibrary.Get(new Color(0.48f, 0.36f, 0.22f), 0.35f); break;
                case "Whitewall":
                case "Boot": m = MaterialLibrary.Get(new Color(0.82f, 0.8f, 0.74f), 0.3f); break;
                case "Leather": m = MaterialLibrary.Get(new Color(0.32f, 0.16f, 0.08f), 0.45f); break;
                case "Canvas": m = MaterialLibrary.Get(new Color(0.3f, 0.27f, 0.2f), 0.05f); break;
                case "Wood": m = MaterialLibrary.Surface("planks"); break;
                case "Deck": m = MaterialLibrary.Tinted("planks", new Color(1.05f, 0.98f, 0.9f)); break;
                case "Steel": m = MaterialLibrary.Get(Palette.Steel, 0.4f, 0.6f); break;
                case "Tail":
                case "Red": m = MaterialLibrary.Get(new Color(0.58f, 0.06f, 0.04f), 0.5f); break;
                case "Tarp": m = MaterialLibrary.Get(new Color(0.22f, 0.26f, 0.18f), 0.05f); break;
                case "Bottom": m = MaterialLibrary.Get(new Color(0.38f, 0.09f, 0.06f), 0.3f); break;
                case "Rope": m = MaterialLibrary.Get(new Color(0.5f, 0.42f, 0.28f), 0.05f); break;
                case "Mahogany": m = MaterialLibrary.Get(new Color(0.34f, 0.11f, 0.04f), 0.85f); break;
                case "Coal": m = MaterialLibrary.Get(new Color(0.05f, 0.05f, 0.05f), 0.35f); break;
                case "CabinGlow": m = MaterialLibrary.Glow(new Color(0.22f, 0.2f, 0.17f), new Color(1f, 0.74f, 0.42f), 2.2f); break;
                case "Lamp": m = MaterialLibrary.Glow(new Color(0.85f, 0.85f, 0.8f), new Color(1f, 0.9f, 0.7f), 5f); break;
                default: m = MaterialLibrary.Get(new Color(0.5f, 0.5f, 0.5f)); break;
            }
            fixedSlots[slot] = m;
            return m;
        }

        static Material Paint(Color c) => MaterialLibrary.Get(c, 0.62f, 0.1f);

        public static System.Func<string, Material> Mapper(Look look)
        {
            return slot =>
            {
                switch (slot)
                {
                    case "Paint": return Paint(look.Paint);
                    case "Paint2":
                    case "Rim": return Paint(look.Paint2);
                    case "Box": return MaterialLibrary.Get(look.Box, 0.35f);
                    case "Hull": return MaterialLibrary.Get(look.Hull, 0.4f);
                    case "Super": return MaterialLibrary.Get(look.Super, 0.35f);
                    case "Funnel": return MaterialLibrary.Get(look.Funnel, 0.35f);
                    case "Band": return MaterialLibrary.Get(look.Band, 0.35f);
                    default: return Fixed(slot);
                }
            };
        }

        // ------------------------------------------------------------------ cars

        static Color C(float r, float g, float b) => new Color(r, g, b);

        static readonly Color[] CarPaints =
        {
            C(0.025f, 0.025f, 0.025f), C(0.025f, 0.025f, 0.025f), C(0.025f, 0.025f, 0.025f), C(0.025f, 0.025f, 0.025f),
            C(0.09f, 0.15f, 0.11f), C(0.24f, 0.05f, 0.05f), C(0.07f, 0.09f, 0.18f), C(0.2f, 0.15f, 0.1f),
            C(0.3f, 0.29f, 0.26f), C(0.42f, 0.36f, 0.24f),
        };
        static readonly Color[] LimoPaints =
        {
            C(0.02f, 0.02f, 0.02f), C(0.02f, 0.02f, 0.02f), C(0.22f, 0.04f, 0.05f), C(0.06f, 0.08f, 0.16f),
            C(0.08f, 0.14f, 0.1f), C(0.33f, 0.31f, 0.27f),
        };
        static readonly Color[] VanBoxes =
        {
            C(0.17f, 0.25f, 0.17f), C(0.32f, 0.08f, 0.06f), C(0.27f, 0.19f, 0.12f), C(0.7f, 0.64f, 0.5f), C(0.1f, 0.13f, 0.24f),
        };

        public static readonly Dictionary<string, float> Lengths = new Dictionary<string, float>
        {
            { "car_sedan", 4.0f }, { "car_touring", 4.0f }, { "car_limo", 5.2f }, { "truck_stake", 5.6f }, { "truck_van", 5.6f },
        };

        public static string PickCar(SeededRandom rng, DistrictType d)
        {
            float r = rng.Value;
            switch (d)
            {
                case DistrictType.Uptown:
                case DistrictType.FinancialDistrict:
                    return r < 0.5f ? "car_sedan" : r < 0.8f ? "car_limo" : "car_touring";
                case DistrictType.Bowery:
                    return r < 0.45f ? "car_sedan" : r < 0.78f ? "car_limo" : r < 0.92f ? "car_touring" : "truck_van";
                case DistrictType.Docks:
                case DistrictType.Industrial:
                    return r < 0.3f ? "car_sedan" : r < 0.65f ? "truck_stake" : r < 0.9f ? "truck_van" : "car_touring";
                default:
                    return r < 0.58f ? "car_sedan" : r < 0.73f ? "car_touring" : r < 0.83f ? "car_limo" : r < 0.94f ? "truck_van" : "truck_stake";
            }
        }

        public static Look CarLook(SeededRandom rng, string model)
        {
            var look = new Look { Paint = rng.Pick(CarPaints), Paint2 = C(0.02f, 0.02f, 0.02f), Box = rng.Pick(VanBoxes) };
            if (model == "car_limo")
            {
                look.Paint = rng.Pick(LimoPaints);
                look.Paint2 = rng.Chance(0.75f) ? C(0.02f, 0.02f, 0.02f) : look.Paint * 0.6f;
            }
            return look;
        }

        /// <summary>Adds a parked vehicle to a block's prop mesh. Returns its length (for spacing), 0 if the model is missing.</summary>
        public static float Car(MeshBuilder mb, SeededRandom rng, string model, Vector3 pos, float yaw)
        {
            var data = ModelLibrary.Get(model);
            if (data == null) return 0f;
            mb.AddModel(data, pos, yaw + rng.Range(-1.5f, 1.5f), Mapper(CarLook(rng, model)), 1.5f);
            return Lengths.TryGetValue(model, out float l) ? l : 4f;
        }

        // ------------------------------------------------------------------ boats

        static readonly Color[] HullColors = { C(0.03f, 0.03f, 0.03f), C(0.03f, 0.03f, 0.03f), C(0.2f, 0.07f, 0.05f), C(0.07f, 0.12f, 0.09f), C(0.2f, 0.21f, 0.21f) };
        static readonly Color[] SuperColors = { C(0.82f, 0.79f, 0.7f), C(0.88f, 0.86f, 0.8f), C(0.72f, 0.62f, 0.45f) };
        static readonly Color[] FunnelColors = { C(0.6f, 0.13f, 0.08f), C(0.75f, 0.58f, 0.28f), C(0.03f, 0.03f, 0.03f), C(0.12f, 0.2f, 0.38f), C(0.82f, 0.79f, 0.7f) };

        public static Look BoatLook(SeededRandom rng)
        {
            var l = new Look
            {
                Hull = rng.Pick(HullColors), Super = rng.Pick(SuperColors), Funnel = rng.Pick(FunnelColors),
            };
            l.Band = l.Funnel.grayscale < 0.2f ? C(0.85f, 0.83f, 0.78f) : C(0.03f, 0.03f, 0.03f);
            return l;
        }

        /// <summary>Spawns a boat as its own object (so it can bob on the water). Waterline at <paramref name="pos"/>.y.</summary>
        public static GameObject Boat(string model, Vector3 pos, float yaw, Look look, Transform parent, float bob)
        {
            var data = ModelLibrary.Get(model);
            if (data == null) return null;
            var mb = new MeshBuilder { Origin = pos };
            mb.AddModel(data, pos, yaw, Mapper(look), 2f);
            var go = mb.Spawn(model, parent);
            if (go == null) return null;
            var b = go.AddComponent<BoatBob>();
            b.amplitude = 0.05f * bob;
            b.roll = 0.6f * bob;
            b.pitch = 0.25f * bob;
            b.speed = Random.Range(0.55f, 0.8f) / Mathf.Sqrt(Mathf.Max(0.5f, bob));
            b.phase = Random.Range(0f, 100f);
            return go;
        }
    }
}
