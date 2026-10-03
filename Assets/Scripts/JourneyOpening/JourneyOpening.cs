using System.Collections.Generic;
using MusePico.Dialogue;
using MusePico.Journey;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using TMPro;
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
        // Her explicit prompt at the row: what to do, in her kit (icon, words and the controller letter).
        GameObject _prompt;
        TextMeshProUGUI _promptTitle, _promptHint;

        void Start()
        {
            if (gate == null) gate = FindAnyObjectByType<GateStage>();
            // Pointing needs a pointer and grip on each controller - the trigger selected nothing without it.
            if (FindAnyObjectByType<HandsBootstrap>() == null) gameObject.AddComponent<HandsBootstrap>();
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
            gate.HidePrompt();   // the Gate is answered; its "hold X to speak" must not linger
            BuildPrompt(centre, facing, toVisitor);
            Company.Toggled += (id, r) => RefreshPrompt(r == Invitation.Result.Refused ? "Three is the most. Point at one to let them go first." : null);
            RefreshPrompt();
            Debug.Log("[Opening] the Company stands on the walk; the question is: " + Company.Question);
        }

        void BuildPrompt(Vector3 centre, Quaternion facing, Vector3 toVisitor)
        {
            var anchor = new GameObject("Company Prompt").transform;
            anchor.SetParent(transform, false);
            // Above the middle of the row, facing the visitor: +Z away from them (a flat thing reads that way).
            anchor.SetPositionAndRotation(centre + Vector3.up * 2.35f, Quaternion.LookRotation(-toVisitor, Vector3.up));
            var c = MuseUi.Canvas(anchor, "Prompt", rowFromSpawn, 420f);
            var glass = MuseUi.Glass(c, 420f, gap: 8f);
            MuseUi.Kicker(glass, "Invite companions", MuseTheme.Gold);
            _promptTitle = MuseUi.Title(glass, "Choose up to three", 22f);
            _promptHint = MuseUi.Body(glass, "");
            var how = MuseUi.Row(glass, 10f);
            MuseUi.Pill(how, "A", "Continue", true);
            _prompt = anchor.gameObject;
        }

        void RefreshPrompt(string note = null)
        {
            if (_prompt == null || Company == null) return;
            switch (Company.Current)
            {
                case CompanyStage.Phase.Choosing:
                    int n = Company.Invitation.SpeakingOrder().Count;
                    _promptTitle.text = "Choose up to three  ·  " + n + " chosen";
                    _promptHint.text = note ?? "Point at a master and pull the trigger to invite or release them. Press A when you are ready.";
                    break;
                case CompanyStage.Phase.Stepping:
                case CompanyStage.Phase.Answering:
                    _promptTitle.text = "Your companions answer";
                    _promptHint.text = _answersReady ? "Each answers your question in turn. A moves to the next." : "They are thinking about your question…";
                    break;
                case CompanyStage.Phase.Done:
                    _promptTitle.text = "Your companions walk with you";
                    _promptHint.text = "Next in her journey: the curation lanterns and the first chapter door (not built yet).";
                    break;
            }
        }

        void Update()
        {
            // The prompt follows the stage; it hides while a companion is speaking, so the subtitle is the
            // only thing to read.
            if (_prompt == null || Company == null) return;
            bool speaking = Company.Current == CompanyStage.Phase.Answering && _answersReady;
            if (_prompt.activeSelf == speaking) _prompt.SetActive(!speaking);
        }

        async void OnCompanyPhase(CompanyStage.Phase phase)
        {
            RefreshPrompt();
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
            RefreshPrompt();
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
