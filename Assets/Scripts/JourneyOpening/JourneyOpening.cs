using System.Collections.Generic;
using MusePico.Dialogue;
using MusePico.Journey;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// The start of her journey as one walk: the Gate (stage 1) hands its question to the Company
    /// (stage 2). When the Gate's doors open, her six masters stand in a row across the path in front
    /// of them; the visitor points to invite one to three (Monet, Van Gogh and Socrates preselected, her
    /// demo preset), presses A, and the chosen step to the visitor's side and each answers the
    /// question in one line, in turn, live from the model, with her subtitle panel over the speaker.
    ///
    /// The Gate and the Company keep their own logic (GateStage, CompanyStage); this only joins them.
    /// </summary>
    public sealed class JourneyOpening : MonoBehaviour
    {
        public GateStage gate;
        public MuseumDialogue dialogue;
        [Tooltip("The six masters in her row order: Monet, Van Gogh, Socrates, Frida, Hilma, Morisot.")]
        public GameObject[] masterPrefabs = new GameObject[6];

        [Tooltip("How far down the walk from the Gate spawn the row stands. A Marble capture is sharp only within ~15 m of its centre; the doors are ~50 m out, in its fog.")]
        public float rowFromSpawn = 7f;
        public float rowSpacing = 1.0f;

        public CompanyStage Company { get; private set; }
        public IReadOnlyList<string> Companions { get; private set; } = new string[0];

        readonly Dictionary<string, string> _lines = new Dictionary<string, string>();
        bool _asked, _answersReady;

        void Start()
        {
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            if (gate != null) gate.Flow.PhaseChanged += OnGatePhase;
        }

        void OnDestroy()
        {
            if (gate != null) gate.Flow.PhaseChanged -= OnGatePhase;
        }

        void OnGatePhase(GateFlow.Phase phase)
        {
            if (phase == GateFlow.Phase.DoorsOpen && Company == null) BuildCompany();
        }

        void BuildCompany()
        {
            var eye = gate.eye != null ? gate.eye : Camera.main.transform;
            var doorway = gate.Doorway;
            var from = gate.spawn != null ? gate.spawn.position : eye.position;
            var toDoor = doorway - from; toDoor.y = 0f; toDoor.Normalize();
            var toVisitor = -toDoor;
            var centre = from + toDoor * rowFromSpawn;
            centre.y = from.y;
            var facing = Quaternion.LookRotation(toVisitor, Vector3.up);   // a figure's front (+Z) toward the visitor
            var right = Vector3.Cross(Vector3.up, toVisitor).normalized;

            var root = new GameObject("Company").transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(centre, facing);
            var standees = new Dictionary<string, Transform>();
            for (var i = 0; i < Masters.Row.Count; i++)
            {
                var id = Masters.Row[i];
                var slot = new GameObject("Standee " + id).transform;
                slot.SetParent(root, false);
                slot.SetPositionAndRotation(centre + right * ((i - 2.5f) * rowSpacing), facing);
                var prefab = i < masterPrefabs.Length ? masterPrefabs[i] : null;
                if (prefab != null)
                {
                    var figure = Instantiate(prefab, slot);
                    figure.transform.localPosition = Vector3.zero; figure.transform.localRotation = Quaternion.identity;
                }
                // Pointable needs a collider the size of a standing figure.
                var box = slot.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.9f, 0f); box.size = new Vector3(0.6f, 1.8f, 0.4f);
                standees[id] = slot;
            }

            Company = CompanyStage.Make(root.gameObject, standees);
            Company.Question = gate.Flow.Question;
            Company.Group.Head = eye;
            Company.Group.LineFor = id => _lines.TryGetValue(id, out var l) ? l : Masters.Name(id) + " considers your question.";
            Company.ReadyToAnswer = () => _answersReady;
            Company.PhaseChanged += OnCompanyPhase;
            Company.Completed += ids => Companions = ids;
            root.gameObject.AddComponent<SubtitleRig>().Group = Company.Group;
            Company.Preselect(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates });   // her demo preset
            AddFill(eye);
            Debug.Log("[Opening] the Company stands on the walk; the question is: " + Company.Question);
        }

        async void OnCompanyPhase(CompanyStage.Phase phase)
        {
            if (phase != CompanyStage.Phase.Stepping || _asked) return;
            _asked = true;
            var chosen = Company.Invitation.SpeakingOrder();
            if (dialogue == null) { _answersReady = true; return; }

            dialogue.invitedMasterIds = new List<string>();
            foreach (var id in chosen) dialogue.invitedMasterIds.Add(RosterId(id));
            dialogue.exactlyInvited = true;
            dialogue.speakReplies = false;   // the turns are paced by the Company; the subtitle carries each line
            var question = string.IsNullOrWhiteSpace(Company.Question) ? "What is worth keeping?" : Company.Question;
            Debug.Log("[Opening] asking " + string.Join(", ", dialogue.invitedMasterIds) + ": " + question);
            DialogueResult result = null;
            try { result = await dialogue.AskAsync(question); }
            catch (System.Exception ex) { Debug.LogWarning("[Opening] the masters could not be asked: " + ex.Message); }

            if (result != null)
                foreach (var p in result.Perspectives)
                    foreach (var id in chosen)
                        if (RosterId(id) == p.speakerId) _lines[id] = OneLine(p.text);
            Debug.Log("[Opening] answers ready (" + _lines.Count + " of " + chosen.Count + ", live " + (result != null && result.Live) + ")");
            _answersReady = true;
        }

        /// <summary>The museum's camera-mounted fill (MuseumJourney: point 2.0, her Point 2.4 converted), so a
        /// master standing against the sun is a face, not a silhouette. Meshes only; splats are unlit.</summary>
        static void AddFill(Transform eye)
        {
            if (eye.GetComponentInChildren<Light>() != null) return;
            var l = new GameObject("Companion Fill").AddComponent<Light>();
            l.transform.SetParent(eye, false);
            l.type = LightType.Point; l.intensity = 2.0f; l.range = 9f; l.color = Color.white; l.shadows = LightShadows.None;
        }

        /// <summary>Her "one line": the first sentence of the master's reading.</summary>
        public static string OneLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            text = text.Trim();
            for (var i = 20; i < text.Length; i++)
                if ((text[i] == '.' || text[i] == '?' || text[i] == '!') && (i + 1 == text.Length || text[i + 1] == ' '))
                    return text.Substring(0, i + 1);
            return text;
        }

        /// <summary>The Company's ids are the Slots spellings; masters.json uses short ones.</summary>
        public static string RosterId(string id) => id switch
        {
            Masters.Frida => "frida", Masters.Hilma => "hilma", Masters.Morisot => "morisot", _ => id,
        };
    }
}
