using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Saul's rule for everything on show - hung works, hero pieces, statues, props: the masters speak when
    /// it is clicked and when the visitor walks up to it, and the time spent looking at it goes to the
    /// journey record, which Your world builds from (the works stayed with longest). Hung works get this from
    /// <see cref="WorldPaintings"/>; <see cref="Sweep"/> gives it to everything else that is a master target
    /// or can be replicated, so a chapter only has to make its piece clickable or replicable.
    /// </summary>
    public static class Exhibit
    {
        /// <summary>Make <paramref name="go"/> an exhibit: talked about on click and approach, and tracked.</summary>
        public static InsightTarget Make(GameObject go, string id, string title, string artist = null)
        {
            var t = go.GetComponent<InsightTarget>();
            if (t == null) t = InsightTarget.Add(go, title, artist, id);
            Track(t);
            return t;
        }

        /// <summary>
        /// Walking up counts from its edge, not its pivot: a 6 m picture or a statue group is "approached"
        /// at the same 2.2 m from where it stands as a hand-sized work is.
        /// </summary>
        public static float ReachFor(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) - 0.5f, 0f, 6f);
        }

        static void Track(InsightTarget t)
        {
            if (t.reach <= 0f) t.reach = ReachFor(t.gameObject);
            var go = t.gameObject;
            if (go.GetComponent<ArtworkWatcher>() != null) return;
            var mark = new GameObject("Viewing mark").transform;   // at the piece: it moves with its chapter frame
            mark.SetParent(go.transform, false);
            var p = Pointable.Make(go, t.id);
            var w = ArtworkWatcher.Make(go, t.id, mark, () => Pointer.AnyOn(p));
            go.AddComponent<DwellReporter>().Watcher = w;
            // The same card as a hung work, on the same 0.4 s point or step close (Saul, 5 Oct: "make it uniform").
            w.CardWanted += _ => ArtworkCard.ShowFor(t);
        }

        /// <summary>Every replicable piece and every master target gets the same treatment as a hung work.</summary>
        public static void Sweep()
        {
            foreach (var r in Object.FindObjectsByType<Replicable>(FindObjectsSortMode.None))
                if (r.GetComponent<InsightTarget>() == null && !string.IsNullOrEmpty(r.label))
                    Make(r.gameObject, "hero-" + r.label.ToLowerInvariant().Replace(' ', '-'), r.label);
            for (var i = InsightTarget.All.Count - 1; i >= 0; i--)
            {
                var t = InsightTarget.All[i];
                if (t != null) Track(t);
            }
        }
    }

    /// <summary>
    /// Her "seen" counts once at 4 s, but Your world wants the works stayed with LONGEST: once seen,
    /// the whole gaze total goes to the journey record and keeps growing while the visitor looks.
    /// A click counts as seen (the watcher's ray dwell), so a tapped piece is tracked from the first look.
    /// </summary>
    public sealed class DwellReporter : MonoBehaviour
    {
        public ArtworkWatcher Watcher;
        float _reported;

        void Update()
        {
            if (Watcher == null || Watcher.Attention == null || !Watcher.Attention.Seen) return;
            var more = Watcher.GazeSeconds - _reported;
            if (more < 0.5f) return;
            JourneyMemory.Record.AddDwell(Watcher.ArtworkId, more);
            _reported = Watcher.GazeSeconds;
        }
    }
}
