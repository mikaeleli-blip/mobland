using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mobland.Core
{
    /// <summary>
    /// Runtime materials.
    /// * Surface(name): textured PBR material built in the editor from Tools/textures output
    ///   (Resources/Mobland/Mat/name). Instanced at runtime so night-time emission changes never touch the asset.
    /// * Get(color): plain coloured URP Lit material.
    /// * Emissive / glow materials are registered for the day/night cycle (SetNight).
    /// </summary>
    public static class MaterialLibrary
    {
        static Material template;
        static readonly Dictionary<string, Material> surfaces = new Dictionary<string, Material>();
        static readonly Dictionary<long, Material> colors = new Dictionary<long, Material>();
        static readonly List<(Material mat, Color color, float intensity)> nightEmissive = new List<(Material, Color, float)>();
        static Material lineMaterial;
        static float night;

        public static float Night => night;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");

        /// <summary>Textured surface material by texture-set name (e.g. "fac_tenement_red"). Never null.</summary>
        public static Material Surface(string name, float nightEmission = 0f)
        {
            if (surfaces.TryGetValue(name, out var m) && m != null) return m;
            var asset = Resources.Load<Material>("Mobland/Mat/" + name);
            if (asset != null)
            {
                m = new Material(asset) { name = name };
            }
            else
            {
                Debug.LogWarning($"[Mobland] Missing material '{name}'. Run Mobland > Rebuild Materials.");
                m = new Material(Template()) { name = name + " (missing)" };
                SetColor(m, new Color(0.6f, 0.2f, 0.6f));
            }
            surfaces[name] = m;
            if (nightEmission > 0f && m.HasProperty(EmissionColor))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                RegisterNight(m, Color.white, nightEmission);
            }
            return m;
        }

        /// <summary>Plain coloured lit material (cached by colour + smoothness + metallic).</summary>
        public static Material Get(Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            Color32 c = color;
            long key = ((long)c.r << 32) | ((long)c.g << 24) | ((long)c.b << 16) | ((long)Mathf.RoundToInt(smoothness * 100) << 8) | (long)Mathf.RoundToInt(metallic * 100);
            if (colors.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Template()) { name = $"Col_{c.r:X2}{c.g:X2}{c.b:X2}" };
            SetColor(m, color);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty(Metallic)) m.SetFloat(Metallic, metallic);
            colors[key] = m;
            return m;
        }

        /// <summary>A material that glows at night (lamp bulbs, neon). Intensity is HDR.</summary>
        public static Material Glow(Color baseColor, Color glowColor, float intensity)
        {
            var m = new Material(Template()) { name = "Glow" };
            SetColor(m, baseColor);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            RegisterNight(m, glowColor, intensity);
            return m;
        }

        /// <summary>Transparent (alpha-blended or additive) URP material, e.g. smoke.</summary>
        public static Material Transparent(Material source, bool additive)
        {
            var m = new Material(source);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", additive ? 1f : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            return m;
        }

        public static void RegisterNight(Material m, Color color, float intensity)
        {
            nightEmissive.Add((m, color, intensity));
            ApplyNight(m, color, intensity);
        }

        /// <summary>0 = full day, 1 = full night. Drives all registered emissive materials.</summary>
        public static void SetNight(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(value - night) < 0.002f) return;
            night = value;
            for (int i = nightEmissive.Count - 1; i >= 0; i--)
            {
                var e = nightEmissive[i];
                if (e.mat == null) { nightEmissive.RemoveAt(i); continue; }
                ApplyNight(e.mat, e.color, e.intensity);
            }
        }

        static void ApplyNight(Material m, Color color, float intensity)
        {
            // Smooth ramp; a small amount even at dusk.
            float k = Mathf.SmoothStep(0f, 1f, night);
            m.SetColor(EmissionColor, color * (intensity * k));
        }

        /// <summary>Unlit material for selection outlines and similar.</summary>
        public static Material Line()
        {
            if (lineMaterial != null) return lineMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            lineMaterial = new Material(s) { name = "Mobland_Line" };
            return lineMaterial;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        public static Material Template()
        {
            if (template != null) return template;
            template = Resources.Load<Material>("Mobland/Mat/_LitTemplate");
            if (template == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) template = new Material(s);
                else
                {
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    template = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
                    Object.Destroy(tmp);
                }
            }
            return template;
        }
    }

    /// <summary>Colours for untextured details.</summary>
    public static class Palette
    {
        public static readonly Color Window = new Color(0.07f, 0.08f, 0.10f);
        public static readonly Color Steel = new Color(0.14f, 0.15f, 0.15f);
        public static readonly Color CastIron = new Color(0.09f, 0.10f, 0.09f);
        public static readonly Color CorniceGreen = new Color(0.20f, 0.26f, 0.22f);
        public static readonly Color CorniceGrey = new Color(0.42f, 0.42f, 0.40f);
        public static readonly Color CorniceBrown = new Color(0.30f, 0.23f, 0.18f);
        public static readonly Color Stone = new Color(0.66f, 0.63f, 0.56f);
        public static readonly Color TreeLeaves = new Color(0.20f, 0.30f, 0.15f);
        public static readonly Color TreeLeaves2 = new Color(0.27f, 0.34f, 0.16f);
        public static readonly Color TreeTrunk = new Color(0.25f, 0.19f, 0.13f);
        public static readonly Color WaterTowerWood = new Color(0.42f, 0.31f, 0.21f);
        public static readonly Color BoatHull = new Color(0.12f, 0.12f, 0.13f);
        public static readonly Color BoatCabin = new Color(0.75f, 0.72f, 0.65f);
        public static readonly Color HydrantRed = new Color(0.55f, 0.10f, 0.08f);
        public static readonly Color Rail = new Color(0.33f, 0.31f, 0.28f);
        public static readonly Color Tire = new Color(0.05f, 0.05f, 0.05f);
        public static readonly Color Chrome = new Color(0.6f, 0.6f, 0.6f);
        public static readonly Color LampGlow = new Color(1.0f, 0.78f, 0.48f);

        public static readonly Color[] Awnings =
        {
            new Color(0.50f, 0.13f, 0.10f),
            new Color(0.14f, 0.30f, 0.18f),
            new Color(0.16f, 0.21f, 0.38f),
            new Color(0.55f, 0.42f, 0.17f),
            new Color(0.35f, 0.33f, 0.30f),
        };

        public static readonly Color[] CarPaint =
        {
            new Color(0.03f, 0.03f, 0.03f), new Color(0.03f, 0.03f, 0.03f), new Color(0.03f, 0.03f, 0.03f),
            new Color(0.10f, 0.16f, 0.12f), new Color(0.25f, 0.06f, 0.05f), new Color(0.10f, 0.11f, 0.18f),
            new Color(0.32f, 0.30f, 0.26f),
        };
    }
}
