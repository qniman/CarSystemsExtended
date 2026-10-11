using System;
using UnityEngine;

namespace CSE.Core
{
    // Something the player can aim at: a part, a bolt, a wire terminal.
    public abstract class Interactable : MonoBehaviour
    {
        /// Called every frame while the crosshair is on this object.
        public abstract void OnHover();
    }

    // Finds the Interactable under the crosshair. Foreign triggers (e.g. the car's big
    // PlayerTrigger) are skipped; any other solid collider blocks the ray.
    public static class Look
    {
        public const float Reach = 1.0f;

        public static Interactable Hovered { get; private set; }

        static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);

        public static void Update()
        {
            Hovered = null;
            Camera cam = GameRefs.Camera;
            if (cam == null) return;

            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, Reach);
            if (hits.Length == 0) return;
            Array.Sort(hits, ByDistance);

            foreach (var hit in hits)
            {
                var target = hit.collider.GetComponent<Interactable>();
                if (target != null && target.enabled)
                {
                    Hovered = target;
                    return;
                }
                if (!hit.collider.isTrigger) return;
            }
        }
    }
}
