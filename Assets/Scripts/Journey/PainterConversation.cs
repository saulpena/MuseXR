using System.Collections.Generic;
using System.Text;
using MusePico.Dialogue;
using MusePico.Generation;
using MusePico.Worlds;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using MuseXR.Worlds;

namespace MusePico.Journey
{
    /// <summary>
    /// Talk to the painters who walk with you in SplatPortal.
    ///
    /// Point at Monet or Picasso (controller ray, or the mouse in the Editor) and the journey's ask
    /// panel opens with three questions about the room you are in, a "speak your own" option, and
    /// on desktop a typed one. Both painters answer - the one you pointed at first - each in their
    /// own lens (<see cref="DialogueClient"/>, gpt-5.6-luna) and their own MiniMax voice. While a
    /// painter speaks, their reply shows in a bubble above their head, they play a talk animation
    /// (a different gesture each line), the other one listens, and the voice comes from where they
    /// stand. While the panel is open they step to the edges of the view to flank it.
    ///
    /// Everything paid happens in <see cref="MuseumDialogue"/>, which this only drives: nothing is
    /// billed until the visitor picks a question, presses ASK, or speaks.
    ///
    /// The copy is <see cref="RoomTalk"/>'s and tested in EditMode; this is the scene shell.
    /// </summary>
    public sealed class PainterConversation : MonoBehaviour
    {
        [Tooltip("The painters. Their order must match Master Ids.")]
        public PainterEscort escort;
        [Tooltip("Tells which world the visitor is in, and so which room's questions to offer.")]
        public EscortNavMesh navMesh;
        [Tooltip("Asks the masters and speaks their replies. Its Speaker is moved to whoever talks.")]
        public MuseumDialogue dialogue;

        [Tooltip("masters.json ids, in the same order as the escort's painters.")]
        public string[] masterIds = { "monet", "picasso" };

        [Tooltip("Bubble bottom edge, metres above the top of the painter's head.")]
        public float bubbleAbove = 0.3f;

        [Tooltip("Size of the question panel relative to the journey's.")]
        [Range(0.4f, 1f)] public float panelScale = 0.7f;

        [Tooltip("Replies stay above the painters this long after the last one ends, seconds.")]
        public float bubblesLinger = 20f;

        JourneyPanel _panel;
        SpeechBubble[] _bubbles = new SpeechBubble[0];
        float[] _heights = new float[0];
        XrButtons _buttons;
        Keyboard _keyboard;
        readonly StringBuilder _typed = new StringBuilder();

        bool _open;
        int _asked = -1;
        RoomTopic _room = RoomTalk.Anywhere;
        string _question = string.Empty;
        bool _isSuggestion;
        RoomTalk.Phase _phase;
        string _status = string.Empty;
        bool _dictating;
        int _line;
        int _speaking = -1;
        float _hideBubblesAt = -1f;
        Vector3 _askedFrom;
        bool _walkedOff;

        public bool IsOpen => _open;
        public RoomTalk.Phase CurrentPhase => _phase;
        public RoomTopic Room => _room;
        public string Question => _question;

        void Start()
        {
            if (escort == null) escort = FindAnyObjectByType<PainterEscort>();
            if (navMesh == null) navMesh = FindAnyObjectByType<EscortNavMesh>();
            if (dialogue == null) dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (escort == null || dialogue == null)
            {
                Debug.LogWarning("[PainterConversation] needs a PainterEscort and a MuseumDialogue in the scene.");
                enabled = false;
                return;
            }

            dialogue.exactlyInvited = true;
            dialogue.PerspectiveReady += OnPerspective;
            dialogue.StatusChanged += OnStatus;
            dialogue.TextDictated += OnDictated;

            var go = new GameObject("Painter Talk Panel");
            go.transform.SetParent(transform, false);
            _panel = go.AddComponent<JourneyPanel>();
            _panel.ChoiceTaken += OnChoice;
            _panel.ActionTaken += Ask;
            _panel.BackTaken += Close;
            // 70% of the journey's panel: at its full 2.1 m and 2.8 m away it covered some 40
            // degrees of the room and both painters (Saul, 29 Sep 2026).
            go.transform.localScale = Vector3.one * panelScale;
            go.SetActive(false);

            _bubbles = new SpeechBubble[escort.Count];
            _heights = new float[escort.Count];
            for (int i = 0; i < escort.Count; i++) MakeAskable(i);
            LetRaysHitTriggers();
        }

        void OnEnable()
        {
            _buttons = new XrButtons();
            _keyboard = Keyboard.current;
            if (_keyboard != null) _keyboard.onTextInput += OnTextInput;
        }

        void OnDisable()
        {
            _buttons?.Dispose(); _buttons = null;
            if (_keyboard != null) _keyboard.onTextInput -= OnTextInput;
            _keyboard = null;
            DesktopMove.Suspended = false;
        }

        void OnDestroy()
        {
            if (dialogue == null) return;
            dialogue.PerspectiveReady -= OnPerspective;
            dialogue.StatusChanged -= OnStatus;
            dialogue.TextDictated -= OnDictated;
        }

        void OnTextInput(char c) { if (_open) _typed.Append(c); }

        static bool Desktop => !UnityEngine.XR.XRSettings.isDeviceActive;

        // ------------------------------------------------------------------ pointing at a painter

        void MakeAskable(int i)
        {
            var body = escort.TransformOf(i);
            if (body == null) return;

            float height = 1.75f;
            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null) height = Mathf.Max(1.2f, smr.bounds.max.y - body.position.y);
            _heights[i] = height;

            var target = new GameObject("Ask Target");
            target.transform.SetParent(body, false);
            target.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            var capsule = target.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = 0.35f;
            capsule.height = height;
            int index = i;
            MakePointable(target, () => Open(index), capsule);

            _bubbles[i] = SpeechBubble.Create(body, body.position.y + height + bubbleAbove);
        }

        /// <summary>
        /// Pointable by the controller ray and by the mouse, running the same callback. The same
        /// shape as the journey's (MuseumJourneyRunner.MakePointable), including its fix: XRI drops
        /// trigger colliders it collects itself, so they are handed over before it registers.
        /// </summary>
        static void MakePointable(GameObject go, System.Action onPick, Collider collider)
        {
            var wasActive = go.activeSelf;
            go.SetActive(false);
            var interactable = go.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(collider);
            go.SetActive(wasActive);
            interactable.selectEntered.AddListener(_ => onPick());
            go.AddComponent<DesktopPointable>().Picked = onPick;
        }

        /// <summary>The pointing rays must hit trigger colliders, or the ray passes through the
        /// painters on a headset (the journey found this on the Quest, 27 Sep).</summary>
        static void LetRaysHitTriggers()
        {
            foreach (var c in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.CurveInteractionCaster>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                c.raycastTriggerInteraction = QueryTriggerInteraction.Collide;
        }

        /// <summary>
        /// The mouse, while the panel is closed. When it is open the panel's own pick handles
        /// clicks, painters included; doing both would open the conversation twice.
        /// </summary>
        void DesktopPick()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var hits = Physics.RaycastAll(ray, 30f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var p = h.collider.GetComponent<DesktopPointable>();
                if (p != null) { p.Pick(); return; }
                if (!h.collider.isTrigger) return;   // something solid is in front
            }
        }

        // ------------------------------------------------------------------ the conversation

        /// <summary>Open the panel for painter <paramref name="index"/>.</summary>
        public void Open(int index)
        {
            if (_panel == null || index < 0 || index >= masterIds.Length) return;
            if (_open && _asked == index) return;

            _asked = index;
            _room = CurrentRoom();
            if (!_open)
            {
                _question = string.Empty;
                _isSuggestion = false;
                _status = string.Empty;
                _phase = RoomTalk.Phase.Choosing;
            }
            _open = true;

            _panel.gameObject.SetActive(true);
            if (_panel.head == null && Camera.main != null) _panel.head = Camera.main.transform;
            _panel.floorY = 0f;
            _panel.Reorient();
            escort.ClearCentre = true;
            Debug.Log($"[PainterConversation] asked {masterIds[index]} in {_room.Name}");
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (dialogue.IsBusy) dialogue.Cancel();
            if (_dictating && dialogue.voice != null && dialogue.voice.IsRecording) dialogue.voice.Abort();
            _dictating = false;
            _panel.gameObject.SetActive(false);
            escort.ClearCentre = false;
            escort.Conversing = false;
            StopTalking();
            HideBubbles();
            DesktopMove.Suspended = false;
        }

        RoomTopic CurrentRoom()
        {
            if (navMesh != null && navMesh.CurrentStage >= 0 && navMesh.CurrentStage < navMesh.stages.Length)
            {
                var area = navMesh.stages[navMesh.CurrentStage].area;
                if (area != null && area.transform.parent != null) return RoomTalk.ForWorld(area.transform.parent.name);
            }
            return RoomTalk.Anywhere;
        }

        void OnChoice(string id)
        {
            int q = RoomTalk.QuestionIndex(id);
            if (q >= 0 && q < _room.Questions.Count)
            {
                _question = _room.Questions[q];
                _isSuggestion = true;
                Ask();
            }
            else if (id == RoomTalk.SpeakId) StartDictation();
        }

        void StartDictation()
        {
            if (dialogue.IsBusy || dialogue.voice == null) return;
            _dictating = true;
            _status = string.Empty;
            dialogue.ListenForText();
        }

        void OnDictated(string text)
        {
            if (!_open) return;
            _dictating = false;
            _question = text.Trim();
            _isSuggestion = false;
            _status = "Is that right? Press ASK, or speak again.";
        }

        void OnStatus(string s)
        {
            if (!_open || string.IsNullOrEmpty(s)) return;
            // Dictation that ended without words: say so and return to choosing.
            if (_dictating && !dialogue.voice.IsRecording &&
                (s.StartsWith("Nothing heard") || s.StartsWith("Too short") || s.StartsWith("No transcript") ||
                 s.StartsWith("Microphone unavailable") || s.StartsWith("No transcription")))
            {
                _dictating = false;
                _status = s;
            }
        }

        /// <summary>Send the question to both painters. The one paid action here.</summary>
        public async void Ask()
        {
            if (!_open || dialogue.IsBusy || string.IsNullOrWhiteSpace(_question)) return;

            _phase = RoomTalk.Phase.Asking;
            escort.Conversing = true;   // from here on nobody moves unless the visitor walks well off
            _askedFrom = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            _walkedOff = false;
            _status = string.Empty;
            _line = 0;
            StopTalking();
            HideBubbles();
            _hideBubblesAt = -1f;

            var order = new List<string> { masterIds[_asked] };
            for (int i = 0; i < masterIds.Length; i++) if (i != _asked) order.Add(masterIds[i]);
            dialogue.invitedMasterIds = order;
            dialogue.artworkTitle = _room.Focus;
            dialogue.artworkArtist = string.Empty;
            dialogue.artworkDate = string.Empty;

            Debug.Log($"[PainterConversation] asking {string.Join(", ", order)}: \"{_question}\"");
            // Something to look at while the model thinks (a few seconds): the panel has gone.
            if (_bubbles[_asked] != null) _bubbles[_asked].Show(FullName(_asked), "thinking…", false);
            var result = await dialogue.AskAsync(_question);
            if (this == null) return;

            StopTalking();
            for (int i = 0; i < _bubbles.Length; i++) if (_bubbles[i] != null) _bubbles[i].SetSpeaking(false);
            _hideBubblesAt = Time.time + bubblesLinger;

            if (result == null)
            {
                _phase = _open ? RoomTalk.Phase.Failed : RoomTalk.Phase.Choosing;
                _status = "The painters could not answer just now.";
            }
            else if (!result.Success)
            {
                _phase = RoomTalk.Phase.Failed;
                _status = "The painters could not be reached: " + result.Error;
            }
            else
            {
                _phase = RoomTalk.Phase.Answered;
                _status = "Ask another question, or close.";
                // They walked on while the painters were talking: the conversation is over, so do
                // not put the panel back in front of them. The painters finished their lines.
                if (_walkedOff) { Close(); return; }
                Debug.Log($"[PainterConversation] answered in {result.Seconds:0.0}s, live={result.Live}");
            }
        }

        void OnPerspective(Perspective p)
        {
            if (!_open) return;
            int i = IndexOf(p.speakerId);
            if (i < 0) return;

            if (_speaking >= 0 && _speaking != i) escort.SetTalking(_speaking, false);
            _speaking = i;
            escort.SetTalking(i, true, RoomTalk.TalkStyleFor(_line));
            for (int j = 0; j < masterIds.Length; j++) escort.SetListening(j, j != i);

            for (int j = 0; j < _bubbles.Length; j++)
                if (_bubbles[j] != null && j != i && _bubbles[j].gameObject.activeSelf) _bubbles[j].SetSpeaking(false);
            if (i < _bubbles.Length && _bubbles[i] != null) _bubbles[i].Show(RoomTalk.BubbleName(p.speaker), p.text, true);

            _line++;
            _phase = RoomTalk.Phase.Answering;
            _status = (p.speaker ?? masterIds[i]).ToUpperInvariant() + " IS SPEAKING (" + _line + " / " + masterIds.Length + ")";
        }

        int IndexOf(string speakerId)
        {
            for (int i = 0; i < masterIds.Length; i++)
                if (string.Equals(masterIds[i], speakerId, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void StopTalking()
        {
            for (int i = 0; i < masterIds.Length; i++)
            {
                escort.SetTalking(i, false);
                escort.SetListening(i, false);
            }
            _speaking = -1;
        }

        void HideBubbles()
        {
            foreach (var b in _bubbles) if (b != null) b.Hide();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (_panel == null) return;

            // The panel hides while the painters think and answer, so the mouse needs this pick then
            // too; while the panel shows, its own pick handles painters and plates alike.
            bool panelShown = _open && RoomTalk.PanelVisible(_phase);
            if (Desktop && !panelShown) DesktopPick();
            if (!_open) return;

            if (Desktop && !panelShown && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { Close(); return; }
            if (!_walkedOff && (_phase == RoomTalk.Phase.Answering || _phase == RoomTalk.Phase.Asking))
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var d = cam.transform.position - _askedFrom; d.y = 0f;
                    if (d.magnitude > escort.settings.holdFollowDistance) _walkedOff = true;
                }
            }

            if (_buttons != null)
            {
                if (_buttons.CancelPressed) { Close(); return; }
                if (_buttons.GripPressed) StartDictation();
            }
            if (Desktop && panelShown && HandleTyping()) return;

            var voice = dialogue.voice;
            if (_dictating)
                _phase = voice != null && voice.IsRecording ? RoomTalk.Phase.Listening : RoomTalk.Phase.Transcribing;
            else if (_phase == RoomTalk.Phase.Listening || _phase == RoomTalk.Phase.Transcribing)
                _phase = RoomTalk.Phase.Choosing;

            _panel.listening = voice != null && voice.IsRecording;
            _panel.micLevel = voice != null ? voice.Level : 0f;
            // WASD types into the question only while the panel is on screen. Once asked, the panel
            // hides and the keys walk again: holding the visitor still while the painters talked
            // was wrong (Saul, 29 Sep 2026). A headset's thumbstick was never affected.
            DesktopMove.Suspended = Desktop && panelShown;

            if (_panel.gameObject.activeSelf != panelShown)
            {
                _panel.gameObject.SetActive(panelShown);
                if (panelShown) _panel.Reorient();   // come back in front of wherever they now face
            }
            if (panelShown)
                _panel.Show(RoomTalk.Panel(_room, FullName(_asked), Answerers(), _question, _phase, _status, Desktop));

            if (_hideBubblesAt > 0f && Time.time > _hideBubblesAt && !dialogue.IsBusy)
            {
                _hideBubblesAt = -1f;
                HideBubbles();
            }
        }

        void LateUpdate()
        {
            // The voice comes from whoever is speaking, at mouth height, and follows them.
            if (_speaking < 0 || dialogue == null || dialogue.speaker == null) return;
            var body = escort.TransformOf(_speaking);
            if (body != null) dialogue.speaker.transform.position = body.position + Vector3.up * (_heights[_speaking] - 0.15f);
        }

        /// <summary>Desktop typing, as the journey's ask form: type, Enter asks, Esc closes.</summary>
        bool HandleTyping()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            if (keyboard.escapeKey.wasPressedThisFrame) { _typed.Clear(); Close(); return true; }

            var typed = _typed.ToString();
            _typed.Clear();
            int backspaces = 0;
            foreach (var c in typed) if (c == '\b') backspaces++;
            if (backspaces == 0 && keyboard.backspaceKey.wasPressedThisFrame) backspaces = 1;

            if (!dialogue.IsBusy && !_dictating && (typed.Length > 0 || backspaces > 0))
            {
                _question = AskTyping.Apply(_question, ref _isSuggestion, typed, backspaces);
                _status = string.Empty;
                if (_phase == RoomTalk.Phase.Answered || _phase == RoomTalk.Phase.Failed) _phase = RoomTalk.Phase.Choosing;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Ask();
            return false;
        }

        string FullName(int i)
        {
            if (i < 0 || i >= masterIds.Length) return null;
            foreach (var m in dialogue.Masters) if (m.id == masterIds[i]) return m.fullName;
            return Pretty(masterIds[i]);
        }

        List<string> Answerers()
        {
            var names = new List<string>();
            if (_asked >= 0) names.Add(FullName(_asked));
            for (int i = 0; i < masterIds.Length; i++) if (i != _asked) names.Add(FullName(i));
            return names;
        }

        static string Pretty(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }
}
