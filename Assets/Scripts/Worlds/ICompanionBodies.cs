using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// The figures that stand for the invited masters in the world, as the journey drives them:
    /// who is where, and who is talking. <see cref="CompanionParty"/> (static Tripo figures that
    /// glide) and <see cref="PainterEscort"/> (rigged painters that walk) both answer to it, so the
    /// journey does not care which one the scene carries.
    /// </summary>
    public interface ICompanionBodies
    {
        int Count { get; }
        Transform TransformOf(int index);
        void SetTalking(int index, bool talking);

        /// <summary>The figure playing <paramref name="masterId"/>, or -1 when the bodies do not know
        /// who they are (the caller then pairs by invitation order).</summary>
        int IndexOf(string masterId);
    }
}
