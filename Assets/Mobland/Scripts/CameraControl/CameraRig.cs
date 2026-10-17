using Mobland.Core;
using UnityEngine;

namespace Mobland.CameraControl
{
    /// <summary>
    /// Strategy camera: orbits a focus point on the ground.
    /// WASD/arrows pan, wheel zooms, Q/E or middle-drag rotates, right-drag grabs and pans, Shift = faster.
    /// Pitch flattens as you zoom in so street level reads better.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam;

        [Header("Zoom")]
        public float minDistance = 14f;
        public float maxDistance = 420f;
        [Range(0.05f, 0.4f)] public float zoomStep = 0.2f;

        [Header("Angle")]
        public float minPitch = 28f;
        public float maxPitch = 68f;
        public float startYaw = 20f;

        [Header("Movement")]
        public float panSpeed = 1.1f;          // fraction of distance per second
        public float rotateSpeed = 100f;       // degrees per second (keys)
        public float dragRotateSensitivity = 0.25f;
        public bool edgePan = false;
        public float edgeSize = 10f;
        public float smoothing = 12f;

        Vector3 focus, targetFocus;
        float yaw, targetYaw;
        float distance, targetDistance;
        Bounds limits;
        Vector2 lastMouse;
        Vector3 homeFocus;
        float homeDistance;

        public float Distance => distance;

        void Start()
        {
            lastMouse = GameInput.MousePosition();
        }

        /// <summary>Point the camera at the given area (e.g. the whole city) and remember it as "home".</summary>
        public void Frame(Bounds area)
        {
            limits = area;
            homeFocus = new Vector3(area.center.x, 0f, area.min.z + area.size.z * 0.35f);
            homeDistance = Mathf.Clamp(Mathf.Max(area.size.x, area.size.z) * 0.35f, minDistance, maxDistance);
            targetYaw = yaw = startYaw;
            GoHome(true);
        }

        public void GoHome(bool snap)
        {
            targetFocus = homeFocus;
            targetDistance = homeDistance;
            if (snap)
            {
                focus = targetFocus;
                distance = targetDistance;
                yaw = targetYaw;
                Apply();
            }
        }

        public void FocusOn(Vector3 worldPoint)
        {
            targetFocus = new Vector3(worldPoint.x, 0f, worldPoint.z);
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Time.unscaledDeltaTime;

            Vector2 mouse = GameInput.MousePosition();
            Vector2 delta = mouse - lastMouse;
            lastMouse = mouse;

            // Zoom.
            float scroll = GameInput.ScrollSteps();
            if (Mathf.Abs(scroll) > 0.001f)
                targetDistance = Mathf.Clamp(targetDistance * Mathf.Pow(1f - zoomStep, scroll), minDistance, maxDistance);

            // Rotate.
            targetYaw += GameInput.RotateAxis() * rotateSpeed * dt;
            if (GameInput.MouseHeld(2)) targetYaw += delta.x * dragRotateSensitivity;

            // Pan.
            Quaternion yawRot = Quaternion.Euler(0f, targetYaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            Vector2 pan = GameInput.PanAxis();
            if (edgePan && Application.isFocused) pan += EdgePan(mouse);
            float speed = panSpeed * targetDistance * (GameInput.FastModifier() ? 2.5f : 1f);
            targetFocus += (right * pan.x + fwd * pan.y) * speed * dt;

            if (GameInput.MouseHeld(1) && delta.sqrMagnitude > 0f)
            {
                float worldPerPixel = 2f * distance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                float pitch = CurrentPitch(distance);
                targetFocus -= (right * delta.x + fwd * (delta.y / Mathf.Max(0.3f, Mathf.Sin(pitch * Mathf.Deg2Rad)))) * worldPerPixel;
            }

            if (GameInput.ResetCameraDown()) GoHome(false);

            // Keep the focus over the city.
            if (limits.size != Vector3.zero)
            {
                targetFocus.x = Mathf.Clamp(targetFocus.x, limits.min.x, limits.max.x);
                targetFocus.z = Mathf.Clamp(targetFocus.z, limits.min.z, limits.max.z);
            }

            // Smooth.
            float k = 1f - Mathf.Exp(-smoothing * dt);
            focus = Vector3.Lerp(focus, targetFocus, k);
            distance = Mathf.Lerp(distance, targetDistance, k);
            yaw = Mathf.Lerp(yaw, targetYaw, k);
            Apply();
        }

        Vector2 EdgePan(Vector2 mouse)
        {
            Vector2 v = Vector2.zero;
            if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height) return v;
            if (mouse.x < edgeSize) v.x -= 1f;
            if (mouse.x > Screen.width - edgeSize) v.x += 1f;
            if (mouse.y < edgeSize) v.y -= 1f;
            if (mouse.y > Screen.height - edgeSize) v.y += 1f;
            return v;
        }

        float CurrentPitch(float dist)
        {
            float t = Mathf.InverseLerp(minDistance, maxDistance, dist);
            return Mathf.Lerp(minPitch, maxPitch, Mathf.Sqrt(t));
        }

        void Apply()
        {
            if (Cam == null) return;
            Quaternion rot = Quaternion.Euler(CurrentPitch(distance), yaw, 0f);
            Cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
