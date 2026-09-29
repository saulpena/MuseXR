using System.Collections.Generic;
using MuseXR.Worlds;
using UnityEngine;
using UnityEngine.AI;

namespace MusePico.Worlds
{
    /// <summary>Which stage of a journey is current, given which ones have been left. Pure.</summary>
    public static class EscortStages
    {
        /// <summary>The first stage not yet left; the last one once every stage has been left.</summary>
        public static int Current(IReadOnlyList<bool> left)
        {
            if (left == null || left.Count == 0) return -1;
            for (int i = 0; i < left.Count; i++) if (!left[i]) return i;
            return left.Count - 1;
        }
    }

    /// <summary>
    /// The painters' NavMesh, built at runtime from the walk area of whichever world the visitor is
    /// in, and rebuilt when they cross into the next.
    ///
    /// <b>Why not one baked NavMesh.</b> The worlds overlap in space - the gallery stands where the
    /// Buddha Hall is, the garden rises where the temple was - so a NavMesh of every world at once
    /// would let a painter walk through one world's wall along another's floor. Only the current
    /// world's floor is ever walkable.
    ///
    /// A world is left when its transition reports the visitor has crossed (door, painting), when
    /// the rising front starts (water), or when the world has been destroyed.
    /// Obstacles (<see cref="NavMeshObstacle"/> with carving, e.g. the Buddha) still apply.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // build before PainterEscort.Start places its agents
    public sealed class EscortNavMesh : MonoBehaviour
    {
        [System.Serializable]
        public class Stage
        {
            public SplatWalkArea area;
            [Tooltip("The transition that leaves this world: a SplatPortalDoor, PaintingPortal or " +
                     "SplatRevealTransition. Empty for the last world.")]
            public MonoBehaviour leaveWhen;
        }

        public Stage[] stages = new Stage[0];

        [Tooltip("Rebuild if the current world has moved this far since the last build, metres - " +
                 "the temple follows the painting while it is carried.")]
        public float moveTolerance = 0.05f;

        NavMeshData _data;
        NavMeshDataInstance _instance;
        AsyncOperation _pending;
        int _built = -2;
        Matrix4x4 _builtPose;
        readonly List<bool> _left = new List<bool>();
        readonly List<NavMeshBuildSource> _sources = new List<NavMeshBuildSource>();

        /// <summary>Index into <see cref="stages"/> of the world whose floor is walkable.</summary>
        public int CurrentStage => _built;

        void Awake()
        {
            _data = new NavMeshData(0);
            _instance = NavMesh.AddNavMeshData(_data);
            Rebuild(Current(), synchronous: true);
        }

        void OnDestroy()
        {
            if (_instance.valid) _instance.Remove();
        }

        void Update()
        {
            if (_pending != null && !_pending.isDone) return;
            _pending = null;

            int want = Current();
            if (want != _built) { Rebuild(want, synchronous: false); return; }

            var area = want >= 0 ? stages[want].area : null;
            if (area != null && Moved(area.transform.localToWorldMatrix, _builtPose))
                Rebuild(want, synchronous: false);
        }

        int Current()
        {
            _left.Clear();
            foreach (var s in stages) _left.Add(s == null || s.area == null || HasLeft(s.leaveWhen));
            return EscortStages.Current(_left);
        }

        /// <summary>Has the visitor gone through this transition?</summary>
        public static bool HasLeft(MonoBehaviour transition)
        {
            if (transition == null) return false;
            if (transition is ITransitionStep step) return step.HasCrossed;
            if (transition is SplatRevealTransition reveal)
                return reveal.leavingWorld == null || reveal.Sequence.Phase >= RevealPhase.Rising;
            return false;
        }

        void Rebuild(int stage, bool synchronous)
        {
            _built = stage;
            _sources.Clear();
            var area = stage >= 0 ? stages[stage].area : null;
            if (area == null || !area.TryGetSource(out var source))
            {
                Debug.LogWarning($"[EscortNavMesh] stage {stage} has no walk area; the painters have nowhere to walk.");
                return;
            }
            _sources.Add(source);
            _builtPose = area.transform.localToWorldMatrix;

            var bounds = area.WorldBounds;
            bounds.Expand(new Vector3(2f, 4f, 2f));
            var settings = NavMesh.GetSettingsByID(0);

            if (synchronous) NavMeshBuilder.UpdateNavMeshData(_data, settings, _sources, bounds);
            else _pending = NavMeshBuilder.UpdateNavMeshDataAsync(_data, settings, _sources, bounds);
            Debug.Log($"[EscortNavMesh] walkable floor is now '{area.transform.parent?.name ?? area.name}' (stage {stage}).");
        }

        bool Moved(Matrix4x4 a, Matrix4x4 b)
        {
            return (a.GetColumn(3) - b.GetColumn(3)).sqrMagnitude > moveTolerance * moveTolerance
                   || Vector3.Angle(a.GetColumn(2), b.GetColumn(2)) > 1f;
        }
    }
}
