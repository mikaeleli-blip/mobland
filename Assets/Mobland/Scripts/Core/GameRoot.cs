using Mobland.CameraControl;
using Mobland.City;
using Mobland.UI;
using UnityEngine;

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
        public TimeOfDay DayCycle { get; private set; }
        public CityAmbience Ambience { get; private set; }

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
            DayCycle = gameObject.AddComponent<TimeOfDay>();
            DayCycle.Init(FindOrCreateSun(), cam);

            Rig = gameObject.AddComponent<CameraRig>();
            Rig.Cam = cam;

            Hud = gameObject.AddComponent<CityHud>();
            Selector = gameObject.AddComponent<BuildingSelector>();
            Selector.Cam = cam;
            Selector.Hud = Hud;
            Hud.Root = this;
            Hud.Selector = Selector;
            Hud.Cam = cam;
            Hud.DayCycle = DayCycle;

            Ambience = gameObject.AddComponent<CityAmbience>();
            Ambience.Init(DayCycle, Rig, cam);
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

            Ambience.SetCity(result.LampLights, result.SmokePoints, result.Water, cityRoot);
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
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 3000f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            return cam;
        }

        static Light FindOrCreateSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                var go = GameObject.Find("Directional Light");
                if (go != null) sun = go.GetComponent<Light>();
            }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            return sun;
        }
    }
}
