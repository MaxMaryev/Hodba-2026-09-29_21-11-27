using UnityEngine;

namespace Hodba.Field
{
    // Gallery-only loop, deliberately independent of any player/controller system.
    public sealed class FieldTravelerPreview : MonoBehaviour
    {
        [Min(0)] public float speed = 1.3f;
        [Min(1)] public float distance = 8f;
        Vector3 origin;
        float elapsed;
        void Start() { origin = transform.position; }
        void Update()
        {
            elapsed += Time.deltaTime;
            transform.position = origin + Vector3.forward * Mathf.Repeat(elapsed * speed, distance);
        }
    }
}
