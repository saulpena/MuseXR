using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Marks a world's main object of interest — the Buddha in the hall, the Queen Mother in the
    /// peach garden. Put it on a prop under that world's <see cref="WorldProps"/> root.
    ///
    /// The journey reads it for two things that must agree (Saul, 27 Sep): the tour's arrow points
    /// here first, and the masters' ask form opens with a ready question about it
    /// ("What do you see in the Great Buddha?"), so a visitor who does not want to type or speak
    /// can simply ask.
    /// </summary>
    public sealed class FocalObject : MonoBehaviour
    {
        [Tooltip("How the object is named in the question and the tour, e.g. \"the Great Buddha\".")]
        public string title = "this work";
    }
}
