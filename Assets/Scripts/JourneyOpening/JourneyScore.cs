using MusePico.Dialogue;
using MusePico.Journey;
using MuseXR.Interaction;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// Her background score (<c>lib/backgroundMusic.js</c>) in the walkable journey: Promenade at the Gate and in
    /// Your World, Clair de Lune in the chapter worlds, the Gymnopedie once the round table begins — her
    /// STAGE_TRACKS, read from which chapter frame is live. Ducked to her 0.07 while a master's voice plays.
    /// The fades and volumes are <see cref="MuseumScoreSource"/>'s, the same as Museum.unity's.
    /// </summary>
    [RequireComponent(typeof(MuseumScoreSource))]
    public sealed class JourneyScore : MonoBehaviour
    {
        static readonly string[] Chapters = { "Palace Frame", "Grotto Frame", "Van Gogh Frame", "Monet Frame" };

        MuseumScoreSource _score;
        JourneyOpening _opening;
        float _next;

        public Stage Current { get; private set; } = Stage.Threshold;

        void Start()
        {
            _score = GetComponent<MuseumScoreSource>();
            _opening = FindAnyObjectByType<JourneyOpening>();
            _score.duckWhen = () => MasterVoice.Speaking || (_opening != null && _opening.Speaking);
            _score.SetStage(Current);
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            var stage = StageNow();
            if (stage == Current) return;
            Current = stage;
            _score.SetStage(stage);
            Debug.Log("[Score] " + stage + " -> " + MuseumScore.TrackFor(stage));
        }

        static Stage StageNow()
        {
            if (Live("Your World Frame")) return Stage.WorldTransformation;
            if (Live("Monet Frame"))
            {
                var monet = FindAnyObjectByType<MonetFeatures>();
                if (monet != null && monet.AtTable) return Stage.Roundtable;
            }
            foreach (var c in Chapters) if (Live(c)) return Stage.WorldExploration;
            return Stage.Threshold;
        }

        static bool Live(string frame)
        {
            var t = ChapterFeatures.FindRoot(frame);
            return t != null && t.gameObject.activeInHierarchy;
        }
    }
}
