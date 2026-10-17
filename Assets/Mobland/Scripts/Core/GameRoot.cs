using Mobland.CameraControl;
using Mobland.City;
using Mobland.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mobland.Core
{
    /// <summary>
    /// Entry point. Put this on one GameObject in the scene (the Main scene is created automatically
    /// in the editor) - it sets up camera, lighting, the city and the temporary HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameRoot : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the built-in defaults.")]
        [SerializeField] CityConfig config;

        public CityConfig Config => config;
        public CityModel City { get; private set; }
        public bool OverlayVisible { get; private set; }

        public CameraRig Rig { get; private set; }
        public BuildingSelector Selector { get; private set; }
        public CityHud Hud { get; private set; }

        Transform cityRoot;
        GameObject overlay;

        void Awake()
        {
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CityConfig>();
                config.name = "CityConfig (defaults)";
            }

            Camera cam = SetupCamera();
            SetupLighting(cam);

            Rig = gameObject.AddComponent<CameraRig>();
            Rig.Cam = cam;

            Hud = gameObject.AddComponent<CityHud>();
            Selector = gameObject.AddComponent<BuildingSelector>();
            Selector.Cam = cam;
            Selector.Hud = Hud;
            Hud.Root = this;
            Hud.Selector = Selector;
            Hud.Cam = cam;
        }

        void Start()
        {
            int seed = config.randomSeedOnStart ? Random.Range(1, 999999) : config.seed;
            BuildCity(seed);
        }

        void Update()
        {
            if (GameInput.RegenerateDown()) BuildCity(Random.Range(1, 999999));
            if (GameInput.OverlayToggleDown()) SetOverlay(!OverlayVisible);
        }

        public void BuildCity(int seed)
        {
            if (cityRoot != null) Destroy(cityRoot.gameObject);
            Selector.ClearSelection();

            var result = new CityGenerator(config).Build(seed, transform);
            City = result.Model;
            cityRoot = result.Root;
            overlay = result.DistrictOverlay;
            SetOverlay(OverlayVisible);

            Rig.Frame(City.Bounds);
            Debug.Log($"[Mobland] City generated: seed {seed}, {City.Districts.Count} districts, {City.Buildings.Count} lots.");
        }

        public void SetOverlay(bool visible)
        {
            OverlayVisible = visible;
            if (overlay != null) overlay.SetActive(visible);
        }

        static Camera SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 3000f;
            return cam;
        }

        static void SetupLighting(Camera cam)
        {
            Light sun = null;
#if UNITY_2023_1_OR_NEWER
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            var lights = FindObjectsOfType<Light>();
#endif
            foreach (var l in lights)
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            // Late-afternoon sun over the Hudson.
            sun.transform.rotation = Quaternion.Euler(47f, -38f, 0f);
            sun.color = new Color(1f, 0.89f, 0.74f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;

            Color haze = new Color(0.60f, 0.58f, 0.54f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.59f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.43f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = haze;
            RenderSettings.fogStartDistance = 450f;
            RenderSettings.fogEndDistance = 1400f;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = haze;

            // Shadows need to reach the zoomed-out view.
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 600f);
        }
    }
}
