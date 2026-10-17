using UnityEngine;

namespace Mobland.Core
{
    /// <summary>Gentle bobbing and rolling for moored boats (they stay in place).</summary>
    public sealed class BoatBob : MonoBehaviour
    {
        public float amplitude = 0.05f, roll = 0.6f, pitch = 0.25f, speed = 0.7f, phase;
        Vector3 basePos;
        Quaternion baseRot;

        void Start()
        {
            basePos = transform.localPosition;
            baseRot = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time * speed + phase;
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(t) * amplitude);
            transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.83f + 1.3f) * pitch, 0f, Mathf.Sin(t * 0.61f + 0.4f) * roll);
        }
    }
}
