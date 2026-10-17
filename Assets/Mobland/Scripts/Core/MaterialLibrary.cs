using System.Collections.Generic;
using UnityEngine;

namespace Mobland.Core
{
    /// <summary>
    /// Creates and caches simple coloured materials at runtime.
    /// Uses the default material's shader, so it works in the Built-in pipeline and URP/HDRP alike.
    /// (Placeholder until we have real Blender assets and material files.)
    /// </summary>
    public static class MaterialLibrary
    {
        static Material baseMaterial;
        static Material lineMaterial;
        static readonly Dictionary<Color32, Material> cache = new Dictionary<Color32, Material>();

        public static Material Get(Color color, float smoothness = 0.08f)
        {
            Color32 key = color;
            if (cache.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(Base()) { name = $"Mobland_{key.r:X2}{key.g:X2}{key.b:X2}" };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        /// <summary>Unlit material for selection outlines and similar.</summary>
        public static Material Line()
        {
            if (lineMaterial != null) return lineMaterial;
            Shader s = Shader.Find("Sprites/Default");
            lineMaterial = s != null ? new Material(s) : new Material(Base());
            lineMaterial.name = "Mobland_Line";
            return lineMaterial;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        static Material Base()
        {
            if (baseMaterial != null) return baseMaterial;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tmp.SetActive(false);
            baseMaterial = new Material(tmp.GetComponent<Renderer>().sharedMaterial) { name = "Mobland_Base" };
            Object.Destroy(tmp);
            return baseMaterial;
        }
    }

    /// <summary>Shared colour palette for the placeholder art.</summary>
    public static class Palette
    {
        public static readonly Color Asphalt = new Color(0.17f, 0.17f, 0.18f);
        public static readonly Color Sidewalk = new Color(0.56f, 0.55f, 0.51f);
        public static readonly Color Yard = new Color(0.30f, 0.26f, 0.21f);
        public static readonly Color Rubble = new Color(0.42f, 0.37f, 0.31f);
        public static readonly Color Outskirts = new Color(0.27f, 0.27f, 0.23f);
        public static readonly Color Water = new Color(0.11f, 0.20f, 0.26f);
        public static readonly Color Grass = new Color(0.26f, 0.38f, 0.20f);
        public static readonly Color TreeLeaves = new Color(0.17f, 0.30f, 0.15f);
        public static readonly Color TreeTrunk = new Color(0.30f, 0.22f, 0.15f);
        public static readonly Color Roof = new Color(0.16f, 0.15f, 0.15f);
        public static readonly Color Trim = new Color(0.80f, 0.76f, 0.68f);
        public static readonly Color Window = new Color(0.07f, 0.08f, 0.10f);
        public static readonly Color LitWindow = new Color(0.85f, 0.70f, 0.38f);
        public static readonly Color Steel = new Color(0.20f, 0.21f, 0.21f);
        public static readonly Color PierWood = new Color(0.36f, 0.28f, 0.20f);
        public static readonly Color PierShed = new Color(0.36f, 0.40f, 0.37f);
        public static readonly Color WaterTower = new Color(0.45f, 0.33f, 0.22f);
        public static readonly Color BoatHull = new Color(0.12f, 0.12f, 0.13f);
        public static readonly Color BoatCabin = new Color(0.75f, 0.72f, 0.65f);

        public static readonly Color[] Awnings =
        {
            new Color(0.58f, 0.14f, 0.11f),
            new Color(0.14f, 0.34f, 0.20f),
            new Color(0.18f, 0.24f, 0.44f),
            new Color(0.62f, 0.47f, 0.18f),
        };
    }
}
