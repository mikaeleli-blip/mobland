using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Mobland.Core
{
    /// <summary>
    /// Day/night cycle: sun + moon on the one shadow-casting directional light, procedural sky, ambient,
    /// fog, post-processing grade, and the night factor that lights windows, lamps and signs.
    /// T pauses/resumes the clock, N skips to the next part of the day.
    /// </summary>
    public sealed class TimeOfDay : MonoBehaviour
    {
        [Range(0f, 24f)] public float hour = 17.5f;
        [Tooltip("Real seconds for a full 24h cycle.")] public float dayLengthSeconds = 600f;
        public bool paused;

        public Light Sun;
        public Volume Volume;

        public float Night { get; private set; }
        public float Daylight { get; private set; }

        Material sky;
        Bloom bloom;
        ColorAdjustments color;
        Vignette vignette;
        WhiteBalance white;
        float lastEnvUpdate = -999f;
        float envHour = -1f;

        static readonly float[] Phases = { 7.0f, 12.5f, 18.6f, 22.0f };

        public string Clock
        {
            get
            {
                int h = Mathf.FloorToInt(hour) % 24, m = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f);
                string ap = h < 12 ? "AM" : "PM";
                int h12 = h % 12 == 0 ? 12 : h % 12;
                return $"{h12}:{m:00} {ap}";
            }
        }

        public void Init(Light sun, Camera cam)
        {
            Sun = sun;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;
            RenderSettings.sun = Sun;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                sky = new Material(skyShader) { name = "Mobland Sky" };
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetColor("_GroundColor", new Color(0.33f, 0.31f, 0.29f));
                RenderSettings.skybox = sky;
                cam.clearFlags = CameraClearFlags.Skybox;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // Post-processing.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = true;

            var go = new GameObject("Post Processing");
            go.transform.SetParent(transform, false);
            Volume = go.AddComponent<Volume>();
            Volume.isGlobal = true;
            Volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Volume.sharedProfile = profile;

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(true);

            color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(18f);
            color.saturation.Override(-14f);
            color.postExposure.Override(0.3f);

            white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(6f);

            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.95f, 1.0f, 1.08f, -0.02f));
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.93f, 0f));

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.22f);
            grain.response.Override(0.75f);

            Apply(true);
        }

        void Update()
        {
            if (GameInput.TimePauseDown()) paused = !paused;
            if (GameInput.TimeSkipDown()) SkipToNextPhase();

            if (!paused && dayLengthSeconds > 0f)
                hour = Mathf.Repeat(hour + Time.deltaTime * 24f / dayLengthSeconds, 24f);
            Apply(false);
        }

        public void SkipToNextPhase()
        {
            foreach (float p in Phases)
                if (p > hour + 0.05f) { hour = p; Apply(true); return; }
            hour = Phases[0];
            Apply(true);
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static readonly Color SunLow = Hex("#ff8a4a"), SunMid = Hex("#ffd8a8"), SunHigh = Hex("#fff1de");
        static readonly Color MoonColor = Hex("#8ea6d6");

        void Apply(bool force)
        {
            if (Sun == null) return;

            // Sun path: rises in the east at 6, highest (55 deg) at 12-13, sets in the west at ~19.
            float t = (hour - 6f) / 13f;                 // 0 sunrise .. 1 sunset
            float sunSin = Mathf.Sin(t * Mathf.PI);      // >0 when above horizon
            if (t < 0f || t > 1f) sunSin = -Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * 0.6f - 0.05f;
            float elev = Mathf.Max(sunSin, 0f) * 55f;
            float yaw = Mathf.Lerp(-100f, 100f, Mathf.Clamp01(t));

            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.2f, sunSin));
            Night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.12f, sunSin));
            float golden = Mathf.Clamp01(1f - Mathf.Abs(sunSin - 0.12f) / 0.2f) * Daylight;   // warm low-sun boost

            Quaternion sunRot = Quaternion.Euler(Mathf.Max(elev, 4f), yaw, 0f);
            Quaternion moonRot = Quaternion.Euler(38f, 160f, 0f);
            Sun.transform.rotation = Quaternion.Slerp(moonRot, sunRot, Daylight);

            Color sunCol = elev < 18f ? Color.Lerp(SunLow, SunMid, elev / 18f) : Color.Lerp(SunMid, SunHigh, (elev - 18f) / 37f);
            Sun.color = Color.Lerp(MoonColor, sunCol, Daylight);
            Sun.intensity = Mathf.Lerp(0.22f, 1.25f + 0.5f * Mathf.Clamp01(elev / 40f), Daylight);
            Sun.shadowStrength = Mathf.Lerp(0.6f, 0.85f, Daylight);

            // Ambient light.
            Color skyDay = Hex("#7f8ea3"), skyDusk = Hex("#8a6f6a"), skyNight = Hex("#1a2234");
            Color eqDay = Hex("#7c7468"), eqDusk = Hex("#6e4f40"), eqNight = Hex("#151820");
            Color grDay = Hex("#3b352e"), grNight = Hex("#0b0c10");
            Color skyC = Color.Lerp(Color.Lerp(skyNight, skyDay, Daylight), skyDusk, golden * 0.6f);
            RenderSettings.ambientSkyColor = skyC;
            RenderSettings.ambientEquatorColor = Color.Lerp(Color.Lerp(eqNight, eqDay, Daylight), eqDusk, golden * 0.6f);
            RenderSettings.ambientGroundColor = Color.Lerp(grNight, grDay, Daylight);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.25f, 0.9f, Daylight);

            // Fog / haze.
            Color fogDay = Hex("#a7a59c"), fogDusk = Hex("#b28469"), fogNight = Hex("#141a26");
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(fogNight, fogDay, Daylight), fogDusk, golden * 0.8f);
            RenderSettings.fogStartDistance = Mathf.Lerp(250f, 480f, Daylight);
            RenderSettings.fogEndDistance = Mathf.Lerp(1100f, 1600f, Daylight);

            if (sky != null)
            {
                sky.SetFloat("_Exposure", Mathf.Lerp(0.06f, 1.15f, Daylight));
                sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.55f, 1.05f, Daylight) + golden * 0.5f);
                sky.SetColor("_SkyTint", Color.Lerp(Hex("#3a4a6a"), Hex("#8a8f99"), Daylight));
            }

            // Grade.
            if (color != null)
            {
                color.postExposure.value = Mathf.Lerp(0.25f, 0.85f, Night);
                color.saturation.value = Mathf.Lerp(-12f, -22f, Night);
                color.colorFilter.value = Color.Lerp(Color.white, Hex("#c9d6ff"), Night * 0.6f);
                bloom.intensity.value = Mathf.Lerp(0.35f, 1.1f, Night);
                bloom.threshold.value = Mathf.Lerp(1.05f, 0.85f, Night);
                white.temperature.value = Mathf.Lerp(4f, -6f, Night) + golden * 12f;
                vignette.intensity.value = Mathf.Lerp(0.28f, 0.38f, Night);
            }

            MaterialLibrary.SetNight(Night);

            // Refresh environment reflections now and then (cheap enough every few game-minutes).
            if (force || (Mathf.Abs(Mathf.DeltaAngle(hour * 15f, envHour * 15f)) > 7.5f && Time.unscaledTime - lastEnvUpdate > 1f))
            {
                envHour = hour;
                lastEnvUpdate = Time.unscaledTime;
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
