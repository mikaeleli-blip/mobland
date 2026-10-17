using Mobland.City;
using Mobland.Core;
using UnityEngine;

namespace Mobland.UI
{
    /// <summary>Hover and click-to-select buildings, with an outline drawn around the roof.</summary>
    public sealed class BuildingSelector : MonoBehaviour
    {
        public Camera Cam;
        public CityHud Hud;

        public BuildingView Hovered { get; private set; }
        public BuildingView Selected { get; private set; }

        LineRenderer hoverLine, selectLine;
        Vector2 pressPos;

        static readonly Color HoverColor = new Color(1f, 0.95f, 0.7f, 0.9f);
        static readonly Color SelectColor = new Color(1f, 0.75f, 0.2f, 1f);

        void Awake()
        {
            hoverLine = CreateLine("Hover Outline", HoverColor, 0.35f);
            selectLine = CreateLine("Selection Outline", SelectColor, 0.6f);
        }

        public void ClearSelection() { Selected = null; Hovered = null; }

        void Update()
        {
            Vector2 mouse = GameInput.MousePosition();
            bool overUi = Hud != null && Hud.IsPointerOverHud(mouse);

            Hovered = null;
            if (!overUi && Cam != null)
            {
                Ray ray = Cam.ScreenPointToRay(mouse);
                if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
                    Hovered = hit.collider.GetComponentInParent<BuildingView>();
            }

            // Select on click (not after a drag).
            if (GameInput.MouseDown(0)) pressPos = mouse;
            if (GameInput.MouseUp(0) && !overUi && (mouse - pressPos).sqrMagnitude < 36f)
                Selected = Hovered;
            if (GameInput.CancelDown()) Selected = null;

            UpdateLine(hoverLine, Hovered != Selected ? Hovered : null);
            UpdateLine(selectLine, Selected);
        }

        LineRenderer CreateLine(string name, Color c, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = MaterialLibrary.Line();
            lr.startColor = lr.endColor = c;
            lr.widthMultiplier = width;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.positionCount = 4;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        static void UpdateLine(LineRenderer lr, BuildingView view)
        {
            if (view == null || view.Info == null) { lr.enabled = false; return; }
            BuildingInfo b = view.Info;
            Rect f = b.Footprint;
            float y = Mathf.Max(0.35f, b.Height + 0.45f);
            float pad = 0.3f;
            lr.SetPosition(0, new Vector3(f.xMin - pad, y, f.yMin - pad));
            lr.SetPosition(1, new Vector3(f.xMax + pad, y, f.yMin - pad));
            lr.SetPosition(2, new Vector3(f.xMax + pad, y, f.yMax + pad));
            lr.SetPosition(3, new Vector3(f.xMin - pad, y, f.yMax + pad));
            lr.enabled = true;
        }
    }
}
