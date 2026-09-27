using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// Keeps a flat label turned toward the camera, upright.
    ///
    /// A TMP label or a Quad is visible when its own +Z points AWAY from the viewer (CLAUDE.md), so
    /// the rotation looks from the viewer THROUGH the label rather than at the viewer.
    /// </summary>
    public sealed class FaceViewer : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var away = transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
