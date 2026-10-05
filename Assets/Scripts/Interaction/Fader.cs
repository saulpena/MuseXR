using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Fades a figure out and switches it off: the uninvited masters once the visitor has chosen
    /// (Saul, 3 Oct 2026: "fade out"). Now <see cref="Appear.Out"/>, which also fades glTF-shaded models;
    /// what it adds is that a fading figure can no longer be pointed at.
    /// </summary>
    public static class Fader
    {
        public const float DefaultSeconds = 0.6f;

        public static Appear FadeOut(GameObject go, float seconds = DefaultSeconds)
        {
            if (go == null) return null;
            foreach (var p in go.GetComponentsInChildren<Pointable>()) p.Interactive = false;
            return Appear.Out(go, seconds);
        }
    }
}
