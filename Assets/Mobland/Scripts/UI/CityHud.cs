using System.Collections.Generic;
using Mobland.City;
using Mobland.Core;
using UnityEngine;

namespace Mobland.UI
{
    /// <summary>
    /// Temporary IMGUI overlay for Milestone 1: city stats, controls, selected building info and district labels.
    /// Will be replaced by a proper UI (UI Toolkit) once the gameplay screens exist.
    /// </summary>
    public sealed class CityHud : MonoBehaviour
    {
        public GameRoot Root;
        public BuildingSelector Selector;
        public Camera Cam;
        public TimeOfDay DayCycle;

        bool showHelp = true;
        readonly List<Rect> hudRects = new List<Rect>();   // screen-space (GUI coords, unscaled) for click blocking
        float uiScale = 1f;

        GUIStyle panel, title, subtitle, label, header, districtLabel, districtShadow;
        Texture2D panelTex;

        static readonly Color Gold = new Color(0.96f, 0.78f, 0.38f);
        static readonly Color Cream = new Color(0.94f, 0.90f, 0.82f);
        static readonly Color Muted = new Color(0.70f, 0.66f, 0.58f);

        void Update()
        {
            if (GameInput.HelpToggleDown()) showHelp = !showHelp;
        }

        /// <summary>Mouse position in screen pixels (origin bottom-left).</summary>
        public bool IsPointerOverHud(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in hudRects) if (r.Contains(p)) return true;
            return false;
        }

        void EnsureStyles()
        {
            if (panel != null) return;
            panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panelTex.SetPixel(0, 0, new Color(0.07f, 0.06f, 0.05f, 0.86f));
            panelTex.Apply();

            panel = new GUIStyle { normal = { background = panelTex }, padding = new RectOffset(14, 14, 12, 12) };
            title = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
            subtitle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Italic, normal = { textColor = Muted } };
            header = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Cream } };
            label = new GUIStyle { fontSize = 13, normal = { textColor = Cream }, richText = true };
            districtLabel = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Cream } };
            districtShadow = new GUIStyle(districtLabel) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f) } };
        }

        void OnGUI()
        {
            if (Root == null || Root.City == null) return;
            EnsureStyles();
            hudRects.Clear();

            uiScale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            float sw = Screen.width / uiScale, sh = Screen.height / uiScale;

            CityModel city = Root.City;

            if (Root.OverlayVisible) DrawDistrictLabels(city);
            DrawCityPanel(city);
            DrawBuildingPanel(sh);
            if (Root.OverlayVisible) DrawLegend(city, sw, sh);
        }

        void Register(Rect r)
        {
            hudRects.Add(new Rect(r.x * uiScale, r.y * uiScale, r.width * uiScale, r.height * uiScale));
        }

        void DrawCityPanel(CityModel city)
        {
            var lines = new List<string>
            {
                $"Seed <b>{city.Seed}</b>",
                $"{city.Districts.Count} districts  ·  {city.Buildings.Count} lots  ·  {city.ParkCount} parks",
            };
            if (DayCycle != null) lines.Insert(0, $"<size=18><b>{DayCycle.Clock}</b></size>{(DayCycle.paused ? "  <color=#A9A08F>(paused)</color>" : "")}");
            if (showHelp)
            {
                lines.Add("");
                lines.Add("<b>WASD / Arrows</b>  pan   <b>Shift</b>  faster");
                lines.Add("<b>Mouse wheel</b>  zoom");
                lines.Add("<b>Q / E</b> or <b>middle-drag</b>  rotate");
                lines.Add("<b>Right-drag</b>  grab & pan");
                lines.Add("<b>Left-click</b>  select building   <b>Esc</b>  clear");
                lines.Add("<b>Tab</b>  district map   <b>Home</b>  reset view");
                lines.Add("<b>T</b>  pause time   <b>N</b>  skip to next time of day");
                lines.Add("<b>R</b>  generate a new city");
                lines.Add("<b>F1</b>  hide help");
            }
            else lines.Add("<b>F1</b>  show help");

            const float lineH = 19f;
            var rect = new Rect(12f, 12f, 360f, 14f + 28f + 20f + lines.Count * lineH + 14f);
            Register(rect);
            GUI.Box(rect, GUIContent.none, panel);

            float x = rect.x + 14f, y = rect.y + 12f;
            GUI.Label(new Rect(x, y, 330f, 28f), "MOBLAND", title); y += 28f;
            GUI.Label(new Rect(x, y, 330f, 20f), "New York, 1925", subtitle); y += 22f;
            foreach (var l in lines) { GUI.Label(new Rect(x, y, 330f, lineH), l, label); y += lineH; }
        }

        void DrawBuildingPanel(float screenH)
        {
            if (Selector == null) return;
            BuildingView view = Selector.Selected != null ? Selector.Selected : Selector.Hovered;
            if (view == null || view.Info == null) return;
            BuildingInfo b = view.Info;
            bool selected = view == Selector.Selected;

            var lines = new List<string>
            {
                $"<color=#{ColorUtility.ToHtmlStringRGB(b.District.MapColor)}>■</color>  {b.District.Name}  <color=#A9A08F>({CityText.Pretty(b.District.Type)})</color>",
                $"Type: <b>{CityText.Pretty(b.Type)}</b>",
                b.IsBuilt ? $"Floors: <b>{b.Floors}</b>" : "Vacant - could be built on",
                $"Lot: {b.Footprint.width:0.#} × {b.Footprint.height:0.#} m",
                "Owner: <color=#A9A08F>Unclaimed</color>",
                "Racket: <color=#A9A08F>—</color>",
            };

            const float lineH = 19f;
            float h = 12f + 24f + 18f + lines.Count * lineH + 12f;
            var rect = new Rect(12f, screenH - h - 12f, 360f, h);
            Register(rect);
            GUI.Box(rect, GUIContent.none, panel);

            float x = rect.x + 14f, y = rect.y + 12f;
            GUI.Label(new Rect(x, y, 330f, 24f), b.Address, header); y += 24f;
            GUI.Label(new Rect(x, y, 330f, 18f), selected ? "Selected" : "Hover - click to select", subtitle); y += 18f;
            foreach (var l in lines) { GUI.Label(new Rect(x, y, 330f, lineH), l, label); y += lineH; }
        }

        void DrawLegend(CityModel city, float screenW, float screenH)
        {
            const float lineH = 20f;
            float h = 12f + 24f + city.Districts.Count * lineH + 12f;
            var rect = new Rect(screenW - 300f - 12f, screenH - h - 12f, 300f, h);
            Register(rect);
            GUI.Box(rect, GUIContent.none, panel);

            float x = rect.x + 14f, y = rect.y + 12f;
            GUI.Label(new Rect(x, y, 270f, 24f), "Districts", header); y += 24f;
            foreach (var d in city.Districts)
            {
                Color old = GUI.color;
                GUI.color = d.MapColor;
                GUI.DrawTexture(new Rect(x, y + 4f, 12f, 12f), Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(new Rect(x + 20f, y, 250f, lineH), $"{d.Name}  <color=#A9A08F>{CityText.Pretty(d.Type)}</color>", label);
                y += lineH;
            }
        }

        void DrawDistrictLabels(CityModel city)
        {
            if (Cam == null) return;
            foreach (var d in city.Districts)
            {
                Vector3 sp = Cam.WorldToScreenPoint(d.Center + Vector3.up * 30f);
                if (sp.z <= 0f) continue;
                float x = sp.x / uiScale, y = (Screen.height - sp.y) / uiScale;
                var r = new Rect(x - 140f, y - 14f, 280f, 28f);
                GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), d.Name.ToUpperInvariant(), districtShadow);
                GUI.Label(r, d.Name.ToUpperInvariant(), districtLabel);
            }
        }

        void OnDestroy()
        {
            if (panelTex != null) Destroy(panelTex);
        }
    }
}
