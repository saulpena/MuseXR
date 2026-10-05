using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// Brings something into the world gently instead of popping it in (Saul, 4 Oct: the Gate's paintings
    /// appear once the companions are chosen). Now <see cref="MuseXR.Interaction.Appear.In"/>, which fades
    /// URP and glTF materials, canvases and text alike.
    /// </summary>
    public static class FadeIn
    {
        public static void Reveal(GameObject go, float seconds = 1.4f) => MuseXR.Interaction.Appear.In(go, seconds);
    }
}
