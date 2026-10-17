using System.Collections.Generic;
using Mobland.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mobland.Core
{
    /// <summary>
    /// Living-city effects: a pool of real point lights that follows the camera through the street lamps at night,
    /// chimney smoke, and scrolling water.
    /// </summary>
    public sealed class CityAmbience : MonoBehaviour
    {
        public int maxLampLights = 160;
        public float lampRange = 17f;
        public float lampIntensity = 24f;

        TimeOfDay time;
        CameraRig rig;
        Camera cam;
        List<Vector3> lamps = new List<Vector3>();
        readonly List<Light> pool = new List<Light>();
        readonly List<GameObject> smoke = new List<GameObject>();
        Material water;
        float nextRefresh;
        Vector3 lastFocus = new Vector3(1e9f, 0, 0);
        readonly List<int> order = new List<int>();

        static readonly Color LampColor = new Color(1f, 0.74f, 0.45f);

        public void Init(TimeOfDay t, CameraRig r, Camera c)
        {
            time = t; rig = r; cam = c;
            for (int i = 0; i < maxLampLights; i++)
            {
                var go = new GameObject("Lamp Light");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = lampRange;
                l.color = LampColor;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                go.SetActive(false);
                pool.Add(l);
            }
        }

        public void SetCity(List<Vector3> lampPositions, List<Vector3> smokePoints, Material waterMat, Transform cityRoot)
        {
            lamps = lampPositions ?? new List<Vector3>();
            water = waterMat;
            lastFocus = new Vector3(1e9f, 0, 0);
            foreach (var s in smoke) if (s != null) Destroy(s);
            smoke.Clear();
            if (smokePoints != null)
                foreach (var p in smokePoints) smoke.Add(MakeSmoke(p, cityRoot));
        }

        void Update()
        {
            if (water != null)
            {
                float t = Time.time;
                water.SetTextureOffset("_BaseMap", new Vector2(t * 0.004f, t * 0.002f));
                water.SetTextureOffset("_BumpMap", new Vector2(-t * 0.006f, t * 0.003f));
            }

            float night = time != null ? time.Night : 0f;
            bool on = night > 0.25f;
            if (!on)
            {
                foreach (var l in pool) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
                return;
            }

            // Place the light pool on the lamps nearest the camera's view centre.
            Vector3 focus = cam != null ? GroundFocus() : Vector3.zero;
            if (Time.unscaledTime >= nextRefresh || (focus - lastFocus).sqrMagnitude > 64f)
            {
                nextRefresh = Time.unscaledTime + 0.4f;
                lastFocus = focus;
                order.Clear();
                for (int i = 0; i < lamps.Count; i++) order.Add(i);
                order.Sort((a, b) => (lamps[a] - focus).sqrMagnitude.CompareTo((lamps[b] - focus).sqrMagnitude));
                for (int i = 0; i < pool.Count; i++)
                {
                    bool use = i < order.Count;
                    pool[i].gameObject.SetActive(use);
                    if (use) pool[i].transform.position = lamps[order[i]];
                }
            }
            float k = Mathf.InverseLerp(0.25f, 0.7f, night);
            foreach (var l in pool) l.intensity = lampIntensity * k;
        }

        Vector3 GroundFocus()
        {
            Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Mathf.Abs(r.direction.y) < 1e-3f) return cam.transform.position;
            float d = -r.origin.y / r.direction.y;
            return r.origin + r.direction * Mathf.Max(0f, d);
        }

        static Material smokeMat;

        static GameObject MakeSmoke(Vector3 p, Transform parent)
        {
            var go = new GameObject("Chimney Smoke");
            go.transform.SetParent(parent, false);
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.32f, 0.31f, 0.3f, 0.55f), new Color(0.45f, 0.43f, 0.41f, 0.45f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;

            var em = ps.emission;
            em.rateOverTime = 4f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.6f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.5f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SmokeMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 10f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return go;
        }

        static Material SmokeMaterial()
        {
            if (smokeMat != null) return smokeMat;
            Material baseMat = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultParticleMaterial : null;
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            smokeMat = baseMat != null ? new Material(baseMat) : new Material(s != null ? s : Shader.Find("Sprites/Default"));
            smokeMat = MaterialLibrary.Transparent(smokeMat, false);
            var tex = Resources.Load<Texture2D>("Mobland/Tex/smoke");
            if (tex != null)
            {
                smokeMat.SetTexture("_BaseMap", tex);
                smokeMat.mainTexture = tex;
            }
            MaterialLibrary.SetColor(smokeMat, new Color(0.6f, 0.58f, 0.56f, 1f));
            smokeMat.name = "Chimney Smoke";
            return smokeMat;
        }
    }
}
