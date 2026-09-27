using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// A doorway you walk through into another world: a trigger volume placed in a splat capture
    /// where its door is, which hands the visitor to <see cref="MuseumJourneyRunner.EnterWorld"/>.
    ///
    /// A splat has no geometry, so the door in the picture cannot be collided with; this box is
    /// laid over it by measurement (the Marble collider swept down the centre line). The rig's
    /// CharacterController is what enters it, so it fires only when the visitor actually walks —
    /// and the runner refuses outside a walking stage anyway.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DoorwayPortal : MonoBehaviour
    {
        [Tooltip("WorldCatalog key including the size suffix, e.g. empty-chinese-imperial-temple-hall-500k")]
        public string targetWorldKey;

        MuseumJourneyRunner _runner;

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            _runner = FindFirstObjectByType<MuseumJourneyRunner>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<CharacterController>() == null) return;   // the visitor, nothing else
            if (_runner == null) _runner = FindFirstObjectByType<MuseumJourneyRunner>();
            if (_runner != null) _runner.EnterWorld(targetWorldKey);
        }
    }
}
