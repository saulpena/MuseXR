using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MusePico.Dialogue;
using MusePico.Generation;
using MuseXR.Worlds;          // WorldCatalog, WorldCycler and WorldDefinition live here, not MusePico.Worlds

namespace MusePico.Journey
{
    /// <summary>
    /// Walks the visitor through muse-infinity's ten stages, in VR.
    ///
    /// <b>This is her <c>setStage</c> and <c>act()</c>, and nothing more.</b> What each stage SAYS
    /// lives in <see cref="JourneyScript"/>; what the journey ALLOWS lives in
    /// <see cref="MuseumJourney"/>; both are pure and tested. This class only connects them to a
    /// scene: it activates the matching <c>Stage_*</c> root, draws the panel, routes the buttons,
    /// and loads the world a chapter needs.
    ///
    /// <b>Walking is a stage property, not a global.</b> Her stages 00-03 are screens the visitor
    /// stands still in front of; 04 is the gallery they walk. Locomotion follows the stage, so the
    /// opening reads as being addressed rather than as being dumped in an empty room with a menu.
    ///
    /// <b>The world only exists from stage 04.</b> Hers loads a Marble world when
    /// <c>world_exploration</c> opens and covers it with a veil until it is framed; ours keeps the
    /// splat renderer off until the same moment, for the same reason — the visitor should not watch
    /// a room assemble itself behind a title card.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuseumJourneyRunner : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("The root holding one child per stage, named Stage_<StageName>.")]
        public Transform journeyRoot;

        [Tooltip("Drawn every frame. Created if left empty.")]
        public JourneyPanel panel;

        [Tooltip("Loads the chapter worlds. Put it in drivenExternally mode; this does that on Start.")]
        public WorldCycler worlds;

        [Tooltip("The three masters who walk with the visitor. Hidden until stage 04.")]
        public Transform companions;

        [Tooltip("The hung artworks. Hidden until stage 04, as hers are.")]
        public Transform wall;

        [Tooltip("Assets/Dialogue/masters.json — the cast for stage 02. Read-only product; never hand-edit.")]
        public TextAsset mastersJson;

        [Tooltip("The seven master portraits. Matched to a master by file name: a master called " +
                 "Claude Monet takes the texture named portrait-claude-monet.")]
        public Texture2D[] portraits;

        [Tooltip("The nine chapter thumbnails, IN SPINE ORDER — 01-entrance-conservatory first. " +
                 "Order is the join, because the file names and the scene ids do not match.")]
        public Texture2D[] chapterImages;

        [Tooltip("Assets/Museum/artworks.json — her sceneCollections, four works per chapter.")]
        public TextAsset artworksJson;

        [Tooltip("The 36 artwork images. Matched to a record by file name: aic-110541.jpg for id aic-110541.")]
        public Texture2D[] artworkImages;

        [Tooltip("Metres tall. Each work keeps its own aspect, so only the height is shared.")]
        public float artworkHeight = 1.4f;

        [Tooltip("How far from the spawn a hung work may be. Big captures have big walk boxes; " +
                 "this keeps the four works a gallery rather than a hike.")]
        public float GalleryReach = 14f;

        [Tooltip("Wall positions swept from each capture's collider in the Editor. A capture with " +
                 "no entry falls back to the walk box.")]
        public WallAnchors wallAnchors;

        [Tooltip("The world behind the opening stages. Empty means a black void, which is what " +
                 "stages 00-03 were before one existed.")]
        public string homeWorldKey = "grand-conservatory-garden-path" + MuseXR.Worlds.WorldCatalog.SmallSuffix;

        [Header("Dialogue")]
        [Tooltip("Optional. Without it the gallery still walks, and asking says so honestly.")]
        public MuseumDialogue dialogue;

        [Tooltip("Optional. Needed for stage 01 to take a spoken question.")]
        public VoiceCapture voice;

        [Tooltip("The background score. Follows the journey; ducks under the masters.")]
        public MuseumScoreSource score;

        [Header("Locomotion")]
        /// <summary>
        /// The rig's whole Locomotion subtree, not one provider.
        ///
        /// <b>Measured, by falling 4,791 metres.</b> Disabling <c>DynamicMoveProvider</c> alone
        /// leaves <c>GravityProvider</c> running as a separate component, and stages 00-03 have no
        /// world and therefore no floor — so the visitor drops out of the scene before the opening
        /// title can be read, and every frame after that is black. Toggling the root cannot miss a
        /// provider, including one added later.
        /// </summary>
        [Tooltip("The rig's Locomotion object. Active only while the visitor is walking a loaded world.")]
        public GameObject locomotionRoot;

        [Tooltip("The rig's CharacterController. Switched off with locomotion so nothing falls.")]
        public CharacterController body;

        [Tooltip("The rig itself, returned here whenever the visitor is not walking.")]
        public Transform rig;

        public MuseumJourney Journey { get; private set; }

        ClosingEnding _ending;
        XrButtons _buttons;
        MasterRosterData _roster;
        readonly Dictionary<Stage, GameObject> _stageRoots = new Dictionary<Stage, GameObject>();
        Coroutine _worldLoad;

        /// <summary>
        /// The world we have ASKED for, which is not the same as the one that has arrived.
        ///
        /// Guarding on <c>worlds.Current</c> alone is wrong while a load is in flight: Current is
        /// still null, so the next caller starts the same load again and the first is cancelled
        /// part-way. Measured — the threshold world was loading three times on a single entry to
        /// Play Mode, which is most of the startup delay it is being blamed for.
        /// </summary>
        string _requestedWorldKey;

        /// <summary>Which hung work the walk is heading for. Advances when the visitor engages it.</summary>
        int _tourIndex;

        /// <summary>The wall order the compass walks, nearest-first from where the chapter began.</summary>
        readonly List<int> _tourOrder = new List<int>();

        /// <summary>
        /// What the salon is saying right now — a status line, or the master currently speaking.
        ///
        /// It overrides the stage's own lede while a turn is running, because during those ~20
        /// seconds the only thing the visitor cares about is whether they were heard.
        /// </summary>
        string _speech = string.Empty;

        /// <summary>The companion whose ask form is open, or null when it is not.</summary>
        MasterLens _asking;

        /// <summary>What is in the question box of that form. Dictated, or defaulted from the work.</summary>
        string _askQuestion = string.Empty;

        /// <summary>The three readings, once they arrive.</summary>
        string _askReplies = string.Empty;

        /// <summary>
        /// True while the box holds a question the visitor did not type — the pre-fill, or the
        /// question already answered — so the first key replaces it (<see cref="AskTyping"/>).
        /// </summary>
        bool _askIsSuggestion;

        /// <summary>Characters typed since the last frame, from the keyboard's text event.</summary>
        readonly System.Text.StringBuilder _typed = new System.Text.StringBuilder();

        UnityEngine.InputSystem.Keyboard _keyboard;

        void OnTextInput(char c) { if (_asking != null) _typed.Append(c); }

        /// <summary>The work or object whose popup is open, or null when none is.</summary>
        ArtworkRecord _artOpen;

        /// <summary>The master currently speaking in that popup — the opener, then the reactor.</summary>
        string _artSpeakerId;

        /// <summary>The scripted line shown: the opening, then the reaction once answered.</summary>
        string _artLine = string.Empty;

        /// <summary>The three live readings of the work, or a status while they load.</summary>
        string _artLive = string.Empty;

        bool _artAnswered;

        /// <summary>Her <c>artDialogueTurn</c>: the invited masters take turns opening.</summary>
        int _artTurn;

        /// <summary>Her <c>dialogueToken</c>: a reply for a popup since closed is dropped.</summary>
        int _artToken;

        /// <summary>The work last stopped at, which seeds the question exactly as hers does.</summary>
        ArtworkRecord _focused;

        ArtworkCatalogData _artworks;

        /// <summary>The records currently on the wall, in the order the wall holds them.</summary>
        readonly List<ArtworkRecord> _hanging = new List<ArtworkRecord>();

        /// <summary>
        /// Guards the closing call. Hers refuses a second request while one is in flight or done
        /// ("the idempotency guard in requestRoundtable"), because the roundtable costs money and
        /// a visitor who re-enters stage 06 has not asked for a second one.
        /// </summary>
        bool _roundtableAsked;
        float _transformationStarted;

        void Awake()
        {
            Journey = new MuseumJourney();
            if (artworksJson != null) _artworks = ArtworkCatalog.Parse(artworksJson.text);

            if (mastersJson != null)
            {
                try { _roster = MasterRoster.Parse(mastersJson.text); }
                catch (System.Exception e)
                {
                    Debug.LogError("[MuseumJourneyRunner] masters.json did not parse, so stage 02 " +
                                   "will offer nobody: " + e.Message);
                }
            }
            else Debug.LogWarning("[MuseumJourneyRunner] no masters.json assigned; stage 02 will " +
                                  "have no cast to invite.");

            IndexStageRoots();

            if (panel == null)
            {
                var go = new GameObject("Journey Panel");
                go.transform.SetParent(transform, false);
                panel = go.AddComponent<JourneyPanel>();
            }
            panel.ChoiceTaken += OnChoice;
            panel.ActionTaken += OnAction;
            panel.BackTaken += OnBack;
            panel.NavTaken += OnNav;
            panel.ImageFor = PortraitFor;

            if (worlds != null)
            {
                worlds.drivenExternally = true;
                // Start the threshold world in Awake rather than waiting for Start and the first
                // EnterStage. It is the one world that is ALWAYS needed, and every frame it is not
                // requested is a frame the visitor spends in the dark.
                OpenHomeWorld();
                // Only once the world is up does the visitor have a floor; only then may they walk.
                worlds.WorldChanged += loaded =>
                {
                    SetWalking(WalkingAllowed(Journey.Current, FreeWalk, floorReady: true));
                    HangChapterWall(loaded); // her syncSceneWall: four new works per chapter
                    RebuildTour();           // the spawn moved, so the walk order did too
                };
            }
        }

        void OnEnable()
        {
            _buttons = new XrButtons();
            _keyboard = UnityEngine.InputSystem.Keyboard.current;
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
            if (dialogue != null)
            {
                dialogue.StatusChanged -= OnDialogueStatus;
                dialogue.PerspectiveReady -= OnPerspective;
                dialogue.TextDictated -= OnDictated;
            }

            if (panel == null) return;
            panel.ChoiceTaken -= OnChoice;
            panel.ActionTaken -= OnAction;
            panel.BackTaken -= OnBack;
            panel.NavTaken -= OnNav;
        }

        void OnDialogueStatus(string status) => _speech = status ?? string.Empty;

        /// <summary>
        /// Dictated words arrive here and go in the box. That is the whole of it - no call, no
        /// master, no spend. The visitor reads what was heard and submits it themselves.
        /// </summary>
        void OnDictated(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Whichever box asked for the words gets them. Neither one sends anything.
            if (_asking != null) { _askQuestion = text.Trim(); _askIsSuggestion = false; }
            else Journey.SetQuestion(text.Trim());

            _speech = string.Empty;
        }

        /// <summary>
        /// One master has answered: say it, animate them, and write it into the record.
        ///
        /// <see cref="VisitSession"/> is the only ground the closing roundtable stands on — it
        /// keeps the newest line per speaker, so a visitor who asks five questions still gets a
        /// synthesis built from three masters rather than fifteen fragments.
        /// </summary>
        void OnPerspective(Perspective p)
        {
            if (p == null) return;

            _speech = p.speaker + " — " + p.text;
            Journey.Session.RecordPerspective(p.speakerId, p.speaker, p.text);
            SetTalkingMaster(p.speakerId, true);

            // Each reading goes into its master's bubble as they start speaking, and stays: after
            // the third, all three can be read side by side (Saul, 27 Sep). The panel only says who
            // is talking — putting the readings there showed one at a time, each replacing the last.
            _liveIndex++;
            var total = Mathf.Max(Journey.InvitedMasterIds.Count, _liveIndex);
            QuietBubbles();
            if (_bubbles.TryGetValue(p.speakerId, out var bubble) && bubble != null)
                bubble.Show(p.speaker, p.text, speaking: true);

            var status = p.speaker.ToUpperInvariant() + " IS SPEAKING (" + _liveIndex + " / " + total +
                         ") · EACH ANSWER STAYS ABOVE ITS MASTER";
            if (_artOpen != null) _artLive = status;
            else if (_asking != null) _askReplies = status;
        }

        const string AllAnswered = "ALL THREE HAVE ANSWERED · READ EACH ONE ABOVE ITS MASTER";

        /// <summary>How many readings of the current question have been shown.</summary>
        int _liveIndex;

        /// <summary>
        /// Drive the walk animation for whichever companion is speaking.
        ///
        /// <c>CompanionParty.SetTalking</c> has existed since the party was written and nothing has
        /// ever called it — the cheapest visible win available, and the difference between three
        /// figures standing there and a conversation.
        /// </summary>
        void SetTalkingMaster(string speakerId, bool talking)
        {
            if (companions == null) return;
            var party = companions.GetComponent<MusePico.Worlds.CompanionParty>();
            if (party == null) return;

            var invited = Journey.InvitedMasterIds;
            for (var i = 0; i < invited.Count && i < party.Count; i++)
            {
                if (invited[i] == speakerId) { party.SetTalking(i, talking); return; }
            }
        }

        void Start()
        {
            if (dialogue != null)
            {
                dialogue.StatusChanged += OnDialogueStatus;
                dialogue.PerspectiveReady += OnPerspective;
                dialogue.TextDictated += OnDictated;
                if (dialogue.mastersJson == null) dialogue.mastersJson = mastersJson;
            }

            Journey.StageChanged += (_, to) => EnterStage(to);
            EnterStage(Journey.Current);
            MakeObjectsPointable();
            if (LaunchOptions.SelfTest) StartCoroutine(SelfTest());
        }

        /// <summary>
        /// The <c>musexr.selfTest</c> launch switch: the gallery's dialogue path, driven through
        /// the same methods a ray or a click reaches, logged as <c>[SelfTest]</c> lines. Each step
        /// waits on the state it needs rather than on a clock, and gives up loudly after a limit.
        /// </summary>
        IEnumerator SelfTest()
        {
            void Log(string s) => Debug.Log("[SelfTest] " + s);
            Log("start");

            Journey.GoTo(Stage.CompanionSelection);
            foreach (var id in new[] { "monet", "van_gogh", "socrates" })
                if (!Journey.IsInvited(id)) Journey.ToggleCompanion(id);
            Journey.GoTo(Stage.WorldExploration);
            Log("invited " + string.Join(",", Journey.InvitedMasterIds));

            var t = 0f;
            while ((wall == null || wall.childCount == 0) && t < 60f) { t += Time.deltaTime; yield return null; }
            if (wall == null || wall.childCount == 0) { Log("FAIL no artworks hung after 60 s"); yield break; }
            yield return new WaitForSeconds(3f);
            Log("gallery ready: " + wall.childCount + " works, world " +
                (worlds != null && worlds.Current != null ? worlds.Current.key : "?"));

            var work = wall.GetChild(0).GetComponent<DesktopPointable>();
            work.Pick();
            Log("artwork opened: " + (_artOpen != null ? _artOpen.title : "NOT OPENED") + " · opener " + _artSpeakerId);
            yield return WaitForDialogue(Log, "artwork readings");
            Log("artwork live: " + _artLive);

            OnArtChoice("emotion");
            Log("answered emotion -> reactor " + _artSpeakerId + " · score P" + Journey.Session.Philosophy.Perception +
                " E" + Journey.Session.Philosophy.Emotion + " I" + Journey.Session.Philosophy.Invention);
            yield return new WaitForSeconds(4f);
            CloseArt();

            var roster = Roster();
            MasterLens first = null;
            foreach (var m in roster) if (m.id == Journey.InvitedMasterIds[0]) first = m;
            OpenAsk(first);
            _askQuestion = "What should I carry out of this garden?";
            _askIsSuggestion = false;
            Log("ask open: " + (_asking != null ? _asking.fullName : "NOT OPENED"));
            AskTheMasters();
            yield return WaitForDialogue(Log, "ask replies");
            Log("ask live: " + _askReplies);
            CloseAsk();
            Log("done");
        }

        IEnumerator WaitForDialogue(System.Action<string> log, string what)
        {
            var t = 0f;
            while ((dialogue == null || !dialogue.IsBusy) && t < 5f) { t += Time.deltaTime; yield return null; }
            var seen = -1;
            t = 0f;
            while (dialogue != null && dialogue.IsBusy && t < 180f)
            {
                if (_liveIndex != seen) { seen = _liveIndex; log(what + ": reading " + seen + " showing at " + t.ToString("0.0") + " s"); }
                t += Time.deltaTime;
                yield return null;
            }
            log(what + (t >= 180f ? ": TIMEOUT after 180 s" : ": finished after " + t.ToString("0.0") + " s"));
        }

        void Update()
        {
            // Floor tracking puts the rig ON the floor, so this is the height the panel rides —
            // never the head's, which bobs.
            if (rig != null) panel.floorY = rig.position.y;

            // What the microphone is doing, drawn under the panel. Without this the grip is a
            // button with no observable effect until the utterance ends.
            panel.listening = voice != null && voice.IsRecording;
            panel.micLevel = voice == null ? 0f : voice.Level;

            // The ask form takes over the panel while it is open - it IS the stage's foreground,
            // the way her popup covers the gallery.
            if (_asking != null)
            {
                // No headset: the question is typed, and WASD must type rather than walk.
                var typing = !UnityEngine.XR.XRSettings.isDeviceActive;
                DesktopMove.Suspended = typing;
                if (typing && HandleAskTyping()) return;     // Esc closed the form

                panel.Show(JourneyScript.AskDialogue(
                    _asking, _focused == null ? null : _focused.title, _askQuestion, _askReplies, typing));
                panel.ShowCompass(default);      // the tour caption drew over the replies
                HandleAskInput();
                return;
            }
            DesktopMove.Suspended = false;
            _typed.Clear();

            // The artwork popup, the same way: it covers the gallery while it is open.
            if (_artOpen != null)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var escape = !UnityEngine.XR.XRSettings.isDeviceActive && keyboard != null &&
                             keyboard.escapeKey.wasPressedThisFrame;
                if (escape || (_buttons != null && _buttons.CancelPressed)) { CloseArt(); return; }

                panel.Show(JourneyScript.ArtDialogue(
                    Master(_artSpeakerId), _artOpen.title, _artLine, _artLive, _artAnswered));
                panel.ShowCompass(default);      // the tour caption drew over the readings
                return;
            }

            var showing = JourneyScript.For(Journey, Roster(), _ending);

            // A master's reply belongs where the masters are, and nowhere else. It used to be
            // blitted onto the lede of EVERY stage, so an answer appeared on the question screen -
            // a screen that has not chosen a master yet and never calls one.
            if (_speech.Length > 0 && SpeaksHere(Journey.Current)) showing.Lede = _speech;
            panel.Show(showing);

            if (_buttons == null) return;

            // The secondary face button steps back — and the left stick walks the spine backwards
            // in the gallery, matching the right stick that walks it forwards.
            if (_buttons.CancelPressed) OnBack();

            // Push to DICTATE, and only where a text field exists to fill.
            //
            // The microphone replaces the keyboard. It does not talk to anyone: the words land in
            // the question box and the visitor still presses the forward plate to submit them,
            // exactly as typing works in her build. Stage 01 is the only screen in the opening act
            // with a field, so it is the only screen where this does anything.
            if (_buttons.GripPressed && Journey.Current == Stage.LifeQuestion) BeginDictation();

            if (Journey.Current == Stage.WorldTransformation) TickTransformation();

            UpdateCompass();

            EditorShortcuts();
        }

        /// <summary>
        /// Her keyboard shortcuts, same keys, Editor only.
        ///
        /// <c>app.js</c> binds 1-0 to the ten stages and R to reset, so a reviewer can jump
        /// straight to the beat they want to look at. Comparing the two builds means being able to
        /// do that in both; using DIFFERENT keys would make the comparison harder than it needs to
        /// be, so these are hers exactly.
        /// </summary>
        void EditorShortcuts()
        {
#if UNITY_EDITOR
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rKey.wasPressedThisFrame) { Restart(); return; }

            // Space and Enter take the stage's action on EVERY stage.
            //
            // The blind-trigger restriction above is about a controller: a trigger gets pulled by
            // accident while pointing at nothing, and skipping a beat of the arc on that is bad. A
            // keypress is never accidental, and with it restricted the desktop had no way forward
            // at all from stage 01 — "I press space and nothing happens".
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            {
                panel.TakeAction();
                return;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame) { OnBack(); return; }

            // "1".."9" then "0" -> stages 00..09, exactly as her `keys` map reads.
            var digits = new[]
            {
                keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key,
                keyboard.digit5Key, keyboard.digit6Key, keyboard.digit7Key, keyboard.digit8Key,
                keyboard.digit9Key, keyboard.digit0Key,
            };
            for (var i = 0; i < digits.Length; i++)
                if (digits[i].wasPressedThisFrame) { Journey.GoTo((Stage)i); return; }
#endif
        }

        /// <summary>Her <c>setStage</c>: show this beat and only this beat.</summary>
        void EnterStage(Stage stage)
        {
            CloseArt();
            ClearBubbles();
            foreach (var pair in _stageRoots) pair.Value.SetActive(pair.Key == stage);

            var walks = Walks(stage);
            if (companions != null)
            {
                companions.gameObject.SetActive(walks);
                if (walks) MakeCompanionsPointable();
            }
            if (wall != null) wall.gameObject.SetActive(walks);

            // The world stays up through the opening stages, showing the threshold conservatory.
            // Only the companions and the hung wall are gallery-only.
            if (worlds != null) worlds.gameObject.SetActive(true);

            // Gravity stays off until there is something to stand on. It is switched back on by
            // WorldChanged, once the chapter's floor actually exists.
            _speech = string.Empty;
            if (score != null) score.SetStage(stage);
            SetWalking(false);
            if (walks) OpenChapter();
            else OpenHomeWorld();
            // A stage that keeps the world already loaded gets no WorldChanged, so decide here too.
            SetWalking(WalkingAllowed(stage, FreeWalk, FloorReady));
            if (stage == Stage.WorldTransformation) _transformationStarted = Time.time;
            if (stage == Stage.Roundtable) RequestRoundtable();

            panel.Reorient();
            panel.Show(JourneyScript.For(Journey, Roster(), _ending));
        }

        // NOTE: adding `XRInteractionSimulator` in code does NOT work, though it compiles and the
        // component is a plain MonoBehaviour in the XRI package Runtime. Measured: it throws a
        // NullReferenceException from `ReadInputValues` every frame
        // (XRInteractionSimulator.cs:2131), because it depends on serialized InputActionReferences
        // that only the package SAMPLE's prefab carries. Import "XR Interaction Simulator" from the
        // XR Interaction Toolkit samples and drop its prefab in the scene; there is no code path.

        /// <summary>The stages the visitor walks. Hers: only <c>world_exploration</c>.</summary>
        public static bool Walks(Stage stage) => WalkingRule.Walks(stage);

        static bool WalkingAllowed(Stage stage, bool freeWalk, bool floorReady) =>
            WalkingRule.Allowed(stage, freeWalk, floorReady);

        static bool FreeWalk => MuseXR.Worlds.LaunchOptions.FreeWalk;

        /// <summary>The world we asked for has arrived, so its floor exists.</summary>
        bool FloorReady => worlds != null && worlds.Current != null &&
                           worlds.Current.key == _requestedWorldKey;

        void SetWalking(bool on)
        {
            if (locomotionRoot != null) locomotionRoot.SetActive(on);
            if (body != null) body.enabled = on;
        }

        // There is deliberately no "reset the rig" step between stages any more.
        //
        // It used to zero the rig's position AND rotation on every non-walking stage, which fought
        // the world loader for the same transform: WorldCycler.Place puts the visitor at the
        // world's measured spawn — the threshold faces yaw 180, the glass dome — and the reset
        // then swung them to yaw 0, the balustrade, on the very next stage change. That is what
        // "why are we moving when we hit Enter" was, and in a headset an uncommanded 180 degree
        // snap is the single most nauseating thing this scene could do.
        //
        // The world owns the pose. Restart() reloads the world, which re-places the visitor.

        void OpenChapter()
        {
            if (worlds == null) return;
            var chapter = Journey.Spine.Current;
            var key = chapter.EffectiveWorldKey + WorldCatalog.SmallSuffix;

            if (_requestedWorldKey == key)
            {
                // The chapter's world is already up (a home world that is also chapter 01's), so
                // stage 04 arrives without a world load — and hanging the wall and routing the
                // tour only ever happened on WorldChanged. Do it here instead.
                if (worlds.Current != null && worlds.Current.key == key)
                {
                    HangChapterWall(worlds.Current);
                    RebuildTour();
                }
                return;
            }

            _requestedWorldKey = key;
            if (_worldLoad != null) StopCoroutine(_worldLoad);
            _worldLoad = StartCoroutine(worlds.ShowWorldByKey(key));
        }

        /// <summary>
        /// Put the threshold world up behind the opening stages.
        ///
        /// Her web threshold is a photograph of a conservatory; this is that conservatory as a
        /// place, generated from the same image. The visitor cannot walk in it — locomotion is off
        /// until stage 04 — so it is scenery, and it loads once and stays up through 00 to 03
        /// rather than being torn down and rebuilt between beats.
        /// </summary>
        /// <summary>The scene's homeWorldKey unless the launch intent names another world
        /// (<c>--es musexr.homeWorld &lt;key&gt;</c>), so test builds need no scene edits.</summary>
        string HomeWorldKey => MuseXR.Worlds.LaunchOptions.HomeWorldOverride ?? homeWorldKey;

        void OpenHomeWorld()
        {
            var home = HomeWorldKey;
            if (worlds == null || string.IsNullOrEmpty(home)) return;
            if (_requestedWorldKey == home) return;

            _requestedWorldKey = home;
            if (_worldLoad != null) StopCoroutine(_worldLoad);
            _worldLoad = StartCoroutine(worlds.ShowWorldByKey(home));
        }

        /// <summary>
        /// Her guided walk: point at the next stop, count the distance down, and say so on arrival.
        ///
        /// Only in the gallery — everywhere else the visitor is standing still being addressed, and
        /// an arrow would be pointing at nothing.
        /// </summary>
        void UpdateCompass()
        {
            if (panel == null) return;

            if (!Walks(Journey.Current) || wall == null || _tourOrder.Count == 0)
            {
                panel.ShowCompass(default);
                return;
            }

            var head = rig != null ? rig : transform;
            var eye = Camera.main != null ? Camera.main.transform : head;

            // The room's focal object is the first stop, so the arrow and the ask form's ready
            // question point at the same thing (Saul, 27 Sep: "the main object of interest should
            // be the Buddha"). The hung works follow it.
            var focal = CurrentFocal();
            var offset = focal != null ? 1 : 0;
            var total = _tourOrder.Count + offset;
            if (focal != null && !_focalVisited)
            {
                var fr = focal.GetComponentsInChildren<Renderer>();
                var at = focal.transform.position;
                if (fr.Length > 0) { var fb = fr[0].bounds; foreach (var r in fr) fb.Encapsulate(r.bounds); at = fb.center; }
                var name = char.ToUpperInvariant(focal.title[0]) + focal.title.Substring(1);
                panel.ShowCompass(TourGuide.Describe(eye.position, eye.eulerAngles.y, at, 0, total, name, null));
                return;
            }

            var index = Mathf.Clamp(_tourIndex, 0, _tourOrder.Count - 1);
            var target = wall.GetChild(_tourOrder[index]);

            var wallIndex = _tourOrder[index];
            var record = wallIndex < _hanging.Count ? _hanging[wallIndex] : null;

            panel.ShowCompass(TourGuide.Describe(
                eye.position, eye.eulerAngles.y, target.position, index + offset, total,
                record != null ? record.title : null,
                record != null ? record.artist : null));
        }

        /// <summary>
        /// Order the wall for the walk when a chapter opens: nearest first from the spawn, so the
        /// route moves outward rather than doubling back. Her `advanceTour` steps through it as
        /// each stop is discussed.
        /// </summary>
        void RebuildTour()
        {
            _tourOrder.Clear();
            _tourIndex = 0;
            if (wall == null || wall.childCount == 0) return;

            var hung = new List<HungArtwork>();
            for (var i = 0; i < wall.childCount; i++)
            {
                var t = wall.GetChild(i);
                hung.Add(new HungArtwork(t.position, t.rotation, i));
            }

            var from = rig != null ? rig.position : Vector3.zero;
            foreach (var i in GalleryWall.TourOrder(hung, from)) _tourOrder.Add(hung[i].Index);
        }

        /// <summary>
        /// Her <c>syncSceneWall</c>: the chapter's own four works, hung in the room just loaded.
        ///
        /// <b>The wall is not one wall.</b> Eight chapters carry thirty-six different works, four
        /// at a time, and swapping them is most of what makes the spine feel like a museum rather
        /// than one room redecorated. Ours used to hang eight fixed quads placed against van-gogh's
        /// geometry, so every other chapter showed the same eight pictures in the wrong places.
        ///
        /// Placement is <see cref="GalleryWall.LayInRoom"/> — the playtested walk box. That is a
        /// weaker placement than the collider sweep <c>MuseumWallSetup</c> does in the Editor, and
        /// deliberately so: the sweep needs the collider mesh loaded, which is an 85k-triangle
        /// import per chapter at runtime. Baking the swept anchors per chapter is the upgrade.
        /// </summary>
        void HangChapterWall(MuseXR.Worlds.WorldDefinition world)
        {
            CloseArt();                 // the work it was about has just come off the wall
            ClearBubbles();
            _focused = null;            // a new room: its own focal object seeds the question
            _focalVisited = false;
            if (wall == null || world == null) return;

            _hanging.Clear();
            for (var i = wall.childCount - 1; i >= 0; i--) Destroy(wall.GetChild(i).gameObject);

            var chapter = Journey.Spine.Current;
            var works = ArtworkCatalog.For(_artworks, chapter.CollectionId);
            if (works.Count == 0) return;

            // Prefer the swept anchors: they put a work on a surface the visitor can see, where the
            // walk box only says where the floor is. Baked in the Editor because the sweep needs an
            // 85k-triangle collider import that a chapter change cannot afford.
            var baked = wallAnchors != null ? wallAnchors.For(world.key) : null;
            if (baked != null && baked.Length > 0)
            {
                for (var i = 0; i < works.Count && i < baked.Length; i++)
                {
                    _hanging.Add(works[i]);
                    BuildArtwork(works[i], baked[i].position, baked[i].rotation);
                }
                return;
            }

            var box = world.ScaledWalkBounds;
            var have = world.HasWalkBounds;

            // Clamp the hang to a walkable radius around the spawn.
            //
            // Measured: the grand conservatory is a 345 m capture and her playtested walk box is
            // sized to match, so laying four works across it put them EIGHTY METRES apart — a
            // compass reading "WALK TO STOP 1 / 4 · 85 M" and a gallery nobody would cross. Her box
            // gallery never had this problem because it hung works at fixed offsets in a small
            // room. The walk box says where you MAY go; this says how far a visitor should have to
            // go to see the next picture.
            if (have)
            {
                var spawn = world.ScaledSpawn;
                var reach = new Bounds(new Vector3(spawn.x, box.center.y, spawn.z),
                                       new Vector3(GalleryReach * 2f, box.size.y + 1f, GalleryReach * 2f));
                var min = Vector3.Max(box.min, reach.min);
                var max = Vector3.Min(box.max, reach.max);
                // Only if the intersection is still a room — a walk box entirely off to one side
                // would otherwise collapse to nothing.
                if (max.x - min.x > 4f && max.z - min.z > 4f) box = new Bounds((min + max) * 0.5f, max - min);
            }
            // With no measured room, fall back to a modest box around the spawn rather than the
            // capture's bounds — those include sky, and that is how eight works ended up 28 m
            // outside the van-gogh corridor the first time.
            var fallback = new Bounds(world.ScaledSpawn, Vector3.one * 24f);
            var groundY = world.groundY * world.worldScale;

            var hung = GalleryWall.LayInRoom(
                box.min, box.max, have, fallback.min, fallback.max, groundY, works.Count);

            for (var i = 0; i < hung.Count && i < works.Count; i++)
            {
                _hanging.Add(works[i]);
                BuildArtwork(works[i], hung[i].Position, hung[i].QuadRotation);
            }
        }

        /// <summary>One hung work: a quad you can point at, keeping the picture's own proportions.</summary>
        void BuildArtwork(ArtworkRecord record, Vector3 position, Quaternion quadRotation)
        {
            var texture = ArtworkImage(record.id);
            var aspect = texture != null && texture.height > 0
                ? texture.width / (float)texture.height
                : 1.2f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = record.id;
            go.transform.SetParent(wall, false);
            go.transform.position = position;
            // Already the QUAD rotation, from whichever source placed it — a Unity Quad's normal is
            // its own -Z, so the work faces the room only when its +Z points INTO the wall. Both
            // the baker and the walk-box fallback store it that way so this cannot drift.
            go.transform.rotation = quadRotation;
            go.transform.localScale = new Vector3(artworkHeight * aspect, artworkHeight, 1f);

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (texture != null) material.SetTexture("_BaseMap", texture);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            var stopped = record;
            MakePointable(go, () => OnArtworkTaken(stopped));
        }

        Texture2D ArtworkImage(string id)
        {
            if (artworkImages == null || string.IsNullOrEmpty(id)) return null;
            foreach (var t in artworkImages) if (t != null && t.name == id) return t;
            return null;
        }

        /// <summary>
        /// Her <c>focusArtwork</c>: stopping at a work records it and moves the guide on.
        ///
        /// "What the visitor actually walked past is the only ground the closing roundtable stands
        /// on" — her words, and the reason this writes to the session before anything else.
        /// </summary>
        void OnArtworkTaken(ArtworkRecord record)
        {
            if (record == null) return;

            _focused = record;
            Journey.Session.RecordArtwork(record.title, record.artist);
            _speech = record.title + " · " + record.artist +
                      (string.IsNullOrEmpty(record.date) ? "" : " · " + record.date);
            AdvanceTour();
            OpenArt(record);
        }

        /// <summary>
        /// An object standing in the world — a statue, a temple — taken the way a painting is.
        /// Not a tour stop, so the guide does not move on; everything else is her artwork popup.
        /// </summary>
        void OnObjectTaken(ArtworkRecord record)
        {
            if (record == null) return;
            _focused = record;
            Journey.Session.RecordArtwork(record.title, record.artist);
            var focal = CurrentFocal();
            if (focal != null && record.id == focal.gameObject.name) _focalVisited = true;   // the tour moves on
            OpenArt(record);
        }

        /// <summary>
        /// Her <c>openArtDialogue</c>: the next invited master opens with a scripted line about the
        /// work, and all three are asked for a live reading of it.
        /// </summary>
        void OpenArt(ArtworkRecord record)
        {
            if (record == null || Journey.Current != Stage.WorldExploration || _asking != null) return;

            _artOpen = record;
            _artAnswered = false;
            _artToken++;
            _artSpeakerId = ArtworkDialogue.PickOpening(Journey.InvitedMasterIds, _artTurn++);
            _artLine = ArtworkDialogue.Format(
                ArtworkDialogue.VoiceFor(_artSpeakerId).Opening, record.title, record.artist);
            _artLive = string.Empty;
            FetchArtReadings(record, _artToken);
        }

        /// <summary>
        /// Her <c>fetchLivePerspectives("Tell me how you see …")</c>: three live readings of the work,
        /// spoken in each master's voice. Like hers, this is a paid call on every work opened.
        /// The machine-made question is deliberately NOT recorded as the visitor's own.
        /// </summary>
        async void FetchArtReadings(ArtworkRecord record, int token)
        {
            if (dialogue == null || dialogue.IsBusy) return;

            _artLive = "THE MASTERS ARE LOOKING…";
            _liveIndex = 0;
            ClearBubbles();
            dialogue.invitedMasterIds.Clear();
            foreach (var id in Journey.InvitedMasterIds) dialogue.invitedMasterIds.Add(id);
            dialogue.artworkTitle = record.title ?? string.Empty;
            dialogue.artworkArtist = record.artist ?? string.Empty;
            dialogue.artworkDate = record.date ?? string.Empty;

            var result = await dialogue.AskAsync("Tell me how you see “" + record.title + "”.");
            if (token != _artToken) return;            // that popup has closed

            if (result == null || !result.Success)
            {
                _artLive = "The masters could not be reached" +
                           (result == null || string.IsNullOrEmpty(result.Error) ? "." : ": " + result.Error);
                return;
            }
            QuietBubbles();
            _artLive = AllAnswered;
        }

        /// <summary>
        /// Her <c>onArtChoice</c>: the answer moves the philosophy score the closing world is
        /// built from, and the master who champions that answer replies to it.
        /// </summary>
        void OnArtChoice(string choiceId)
        {
            var choice = ArtworkDialogue.ChoiceById(choiceId);
            if (choice == null || _artAnswered) return;

            Journey.Session.ApplyChoice(choice.Delta);
            _artAnswered = true;
            _artSpeakerId = ArtworkDialogue.PickReaction(choiceId, _artSpeakerId, Journey.InvitedMasterIds);
            var voice = ArtworkDialogue.VoiceFor(_artSpeakerId);
            _artLine = voice.Reactions.TryGetValue(choiceId, out var line) ? line : string.Empty;
        }

        void CloseArt()
        {
            _artOpen = null;
            _artToken++;
        }

        MasterLens Master(string id)
        {
            foreach (var m in Roster()) if (m.id == id) return m;
            return null;
        }

        /// <summary>
        /// Pointable by the ray and by the mouse, running the same callback — the runner's twin of
        /// JourneyPanel.Pointable, for things that live in the world rather than on the panel.
        /// </summary>
        static UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable MakePointable(
            GameObject go, System.Action onPick)
        {
            var interactable = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => onPick());
            go.AddComponent<DesktopPointable>().Picked = onPick;
            return interactable;
        }

        /// <summary>
        /// Every prop in every world's props root becomes something you can ask about. Only things
        /// with a renderer: the invisible walls fencing the peach plaza live under the same root.
        /// </summary>
        void MakeObjectsPointable()
        {
            foreach (var props in FindObjectsByType<WorldProps>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (Transform prop in props.transform)
                {
                    if (prop.GetComponent<DesktopPointable>() != null) continue;
                    if (prop.GetComponentInChildren<Renderer>(true) == null) continue;
                    if (prop.GetComponentInChildren<Collider>(true) == null) continue;

                    var focal = prop.GetComponent<FocalObject>();
                    var title = focal != null && !string.IsNullOrEmpty(focal.title)
                        ? char.ToUpperInvariant(focal.title[0]) + focal.title.Substring(1)   // "The Great Buddha"
                        : ArtworkDialogue.TitleFromName(prop.name);
                    var record = new ArtworkRecord { id = prop.name, title = title, artist = string.Empty };
                    MakePointable(prop.gameObject, () => OnObjectTaken(record));
                }
            }
        }

        /// <summary>
        /// Give each invited companion a collider and an interactable, so pointing at one opens
        /// her ask form.
        ///
        /// Without this the masters are scenery: they walk beside you and cannot be spoken to,
        /// which is the state the gallery shipped in. The capsule is sized to a person rather than
        /// to the mesh bounds, because a generated mesh has no agreed unit and a bounds-sized
        /// collider on a 2 m statue swallows the room behind it.
        /// </summary>
        void MakeCompanionsPointable()
        {
            var party = companions == null ? null : companions.GetComponent<MusePico.Worlds.CompanionParty>();
            if (party == null) return;

            var invited = Journey.InvitedMasterIds;
            var roster = Roster();

            for (var i = 0; i < party.Count && i < invited.Count; i++)
            {
                var t = party.TransformOf(i);
                if (t == null) continue;

                MasterLens lens = null;
                foreach (var m in roster) if (m.id == invited[i]) { lens = m; break; }
                if (lens == null) continue;

                var existing = t.Find("Ask Target");
                if (existing != null) continue;             // already wired this world

                // Sized from the figure as RENDERED, in world units. The companion transforms carry
                // the world's scale (~1.8x in the peach world), so fixed local offsets put the
                // capsule at 3.4 m for a 1.5 m figure and the name plate 2 m over its head (seen
                // 27 Sep). Bounds are taken before anything of ours is added under the figure.
                var figure = FigureBounds(t);
                var scale = Mathf.Max(t.lossyScale.y, 1e-3f);

                var target = new GameObject("Ask Target");
                target.transform.SetParent(t, false);
                target.transform.position = figure.center;

                var capsule = target.AddComponent<CapsuleCollider>();
                capsule.height = figure.size.y / scale;
                capsule.radius = 0.38f / scale;
                capsule.isTrigger = true;

                var chosen = lens;                          // capture, not the loop variable
                var body = MakePointable(target, () => OpenAsk(chosen));
                var tag = BuildAskTag(t, lens, figure.max.y + 0.22f, () => OpenAsk(chosen));
                Glow(body, tag);
                // The reading's bubble sits just above the name plate (0.19 m tall at 0.22 up).
                // The third master stands beside the second, 1.5 m apart, and their bubbles
                // overlapped (27 Sep); theirs goes up a tier.
                var tier = i == 2 ? 1.0f : 0f;
                _bubbles[lens.id] = SpeechBubble.Create(t, figure.max.y + 0.22f + 0.14f + tier);
            }
        }

        /// <summary>Each invited master's speech bubble, by master id.</summary>
        readonly Dictionary<string, SpeechBubble> _bubbles = new Dictionary<string, SpeechBubble>();

        void ClearBubbles()
        {
            foreach (var b in _bubbles.Values) if (b != null) b.Hide();
        }

        void QuietBubbles()
        {
            foreach (var b in _bubbles.Values) if (b != null && b.gameObject.activeSelf) b.SetSpeaking(false);
        }

        /// <summary>The world's main object of interest, if the loaded world marks one.</summary>
        FocalObject CurrentFocal() => FindFirstObjectByType<FocalObject>();

        /// <summary>Whether the tour has already taken the visitor to the focal object.</summary>
        bool _focalVisited;

        /// <summary>
        /// The question the ask form opens with, so it can be asked without typing or speaking: the
        /// work the visitor last stopped at, else the room's focal object, else the room itself.
        /// </summary>
        string DefaultQuestion()
        {
            var focal = CurrentFocal();
            string stoppedAt = null;
            if (_focused != null)
                stoppedAt = focal != null && _focused.id == focal.gameObject.name ? focal.title : _focused.title;
            return ArtworkDialogue.DefaultQuestion(stoppedAt, focal != null ? focal.title : null);
        }

        /// <summary>The world bounds of everything rendered under a figure, or a 1.8 m box at its feet.</summary>
        static Bounds FigureBounds(Transform figure)
        {
            var renderers = figure.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(figure.position + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>
        /// A name plate floating over a master's head: their name and "ASK A QUESTION".
        ///
        /// Her masters are clickable, and the web says so with a cursor. A headset has no cursor,
        /// and an invisible trigger capsule gave no sign that a figure could be spoken to at all —
        /// "we need a way to start the interaction with the philosophers" (Saul, 27 Sep). The
        /// plate is itself pointable, so aiming at the words works as well as aiming at the body.
        /// </summary>
        MeshRenderer BuildAskTag(Transform master, MasterLens lens, float worldY, System.Action onPick)
        {
            var tag = new GameObject("Ask Tag");
            tag.transform.SetParent(master, false);
            // World height and world size: undo the figure's scale so the plate is 0.62 m wide
            // whatever the world's scale is.
            tag.transform.position = new Vector3(master.position.x, worldY, master.position.z);
            tag.transform.localScale = Vector3.one / Mathf.Max(master.lossyScale.y, 1e-3f);
            tag.AddComponent<FaceViewer>();

            var box = tag.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.62f, 0.20f, 0.04f);

            var pill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pill.name = "Pill";
            Destroy(pill.GetComponent<Collider>());
            pill.transform.SetParent(tag.transform, false);
            pill.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            pill.transform.localScale = new Vector3(0.62f, 0.19f, 1f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", TagRest);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
            var renderer = pill.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var text = new GameObject("Label").AddComponent<TMPro.TextMeshPro>();
            text.transform.SetParent(tag.transform, false);
            text.fontSize = 0.62f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.rectTransform.sizeDelta = new Vector2(0.60f, 0.19f);
            text.text = "<b>" + (string.IsNullOrEmpty(lens.name) ? lens.fullName : lens.name) + "</b>\n" +
                        "<size=62%><color=#C9AA72><cspace=0.18em>ASK A QUESTION</cspace></color></size>";

            MakePointable(tag, onPick);
            return renderer;
        }

        static readonly Color TagRest = new Color(0.165f, 0.129f, 0.094f, 0.72f);
        static readonly Color TagHot = new Color(0.60f, 0.49f, 0.30f, 0.90f);

        /// <summary>Light the plate while either the body or the plate is pointed at.</summary>
        static void Glow(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable body, MeshRenderer tag)
        {
            var tagInteractable = tag.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            foreach (var i in new[] { body, tagInteractable })
            {
                if (i == null) continue;
                i.hoverEntered.AddListener(_ => { if (tag != null) tag.sharedMaterial.SetColor("_BaseColor", TagHot); });
                i.hoverExited.AddListener(_ => { if (tag != null) tag.sharedMaterial.SetColor("_BaseColor", TagRest); });
            }
        }

        /// <summary>
        /// Her <c>selectCompanion</c>: clicking a master in the gallery opens the ask form.
        /// The question box is pre-filled from the work you are standing at, as hers is.
        /// </summary>
        void OpenAsk(MasterLens companion)
        {
            if (companion == null || Journey.Current != Stage.WorldExploration) return;

            CloseArt();                                 // one popup at a time, as hers
            _asking = companion;
            _askReplies = string.Empty;
            _askQuestion = DefaultQuestion();
            _askIsSuggestion = true;
            _typed.Clear();
        }

        /// <summary>
        /// The keyboard while the ask form is open on desktop — her <c>#artAskForm</c>: type,
        /// Enter submits, Escape leaves. Returns true when the form was closed.
        /// </summary>
        bool HandleAskTyping()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;

            if (keyboard.escapeKey.wasPressedThisFrame) { CloseAsk(); _typed.Clear(); return true; }

            // Windows delivers Backspace through the text event too, with key repeat; count those,
            // and fall back to the key itself on platforms that do not.
            var typed = _typed.ToString();
            _typed.Clear();
            var backspaces = 0;
            foreach (var c in typed) if (c == '\b') backspaces++;
            if (backspaces == 0 && keyboard.backspaceKey.wasPressedThisFrame) backspaces = 1;

            if (dialogue == null || !dialogue.IsBusy)
                _askQuestion = AskTyping.Apply(_askQuestion, ref _askIsSuggestion, typed, backspaces);

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                AskTheMasters();
            return false;
        }

        /// <summary>Close the ask form and go back to walking.</summary>
        void CloseAsk()
        {
            _asking = null;
            _askQuestion = string.Empty;
            _askReplies = string.Empty;
            DesktopMove.Suspended = false;
        }

        /// <summary>
        /// Input while the ask form is open. Only two controls exist here, and neither of them
        /// sends anything by itself: the grip fills the box, and the back plate leaves.
        /// The forward plate is what asks, and it is a press the visitor makes.
        /// </summary>
        void HandleAskInput()
        {
            if (_buttons == null) return;
            if (_buttons.CancelPressed) { CloseAsk(); return; }
            if (_buttons.GripPressed) BeginDictation();
        }

        /// <summary>
        /// Send the question in the box to the three invited masters. The one paid call in the arc
        /// that a visitor makes deliberately, and it happens here and nowhere else.
        /// </summary>
        async void AskTheMasters()
        {
            if (dialogue == null || _asking == null) return;
            if (string.IsNullOrWhiteSpace(_askQuestion)) return;
            if (dialogue.IsBusy) return;

            Journey.Session.RecordQuestion(_askQuestion);
            _askReplies = "THE MASTERS ARE READING YOUR QUESTION\u2026";
            _liveIndex = 0;
            ClearBubbles();

            dialogue.invitedMasterIds.Clear();
            foreach (var id in Journey.InvitedMasterIds) dialogue.invitedMasterIds.Add(id);
            // Always name the subject. With nothing stopped at, the question used to go out with
            // whatever work the dialogue last held — asked "What do you see in the Great Buddha?",
            // van Gogh answered "you find no Great Buddha here; you face blue-green water, lily
            // pads" (live, 27 Sep). The room's focal object is the subject then.
            var focal = CurrentFocal();
            dialogue.artworkTitle = _focused != null ? _focused.title
                                  : focal != null ? focal.title : string.Empty;
            dialogue.artworkArtist = _focused != null ? _focused.artist ?? string.Empty : string.Empty;
            dialogue.artworkDate = _focused != null ? _focused.date ?? string.Empty : string.Empty;

            var result = await dialogue.AskAsync(_askQuestion);

            // Whatever came back, the next thing typed is a NEW question, not an edit of this one.
            _askIsSuggestion = true;

            // Honest failure, never invented prose. A salon that could not be reached says so;
            // a canned line here would be indistinguishable from a real answer.
            if (result == null || !result.Success)
            {
                _askReplies = "The masters could not be reached" +
                              (result == null || string.IsNullOrEmpty(result.Error)
                                  ? "." : ": " + result.Error);
                return;
            }
            QuietBubbles();
            _askReplies = AllAnswered;
        }

        /// <summary>Her <c>advanceTour</c>: the stop is done, move the guide on.</summary>
        public void AdvanceTour()
        {
            if (_tourOrder.Count == 0) return;
            _tourIndex = Mathf.Min(_tourIndex + 1, _tourOrder.Count - 1);
        }

        /// <summary>
        /// Take a spoken question to the masters.
        ///
        /// The subject is whatever the walk is looking at: hers sets `state.focusedArtwork` when a
        /// painting is clicked, and the lenses read it. Ours passes the chapter the visitor is
        /// standing in, because in a splat capture the ROOM is the work — there is no single
        /// painting the way her box gallery had one.
        /// </summary>
        /// <summary>
        /// The stages where a master's words can legitimately appear on the panel.
        ///
        /// Her /api/dialogue is reachable from the GALLERY only, and /api/roundtable from the
        /// summoning onwards. Everything before that is a form being filled in.
        /// </summary>
        static bool SpeaksHere(Stage stage) =>
            stage == Stage.WorldExploration || stage == Stage.Summoning ||
            stage == Stage.Roundtable || stage == Stage.Manifesto;

        /// <summary>
        /// Open the microphone to fill the question box. Transcription ends at a string; the
        /// masters are not involved.
        /// </summary>
        void BeginDictation()
        {
            if (dialogue == null) { _speech = "No microphone wired in this scene."; return; }
            if (dialogue.IsBusy) return;
            dialogue.ListenForText();
        }

        void BeginListening()
        {
            if (dialogue == null)
            {
                // Say so rather than swallowing the press. The hint promises speech; if the scene
                // has no salon in it, the visitor deserves to be told, not left pressing a button.
                _speech = "No salon in this scene — dialogue is not wired here.";
                return;
            }
            if (dialogue.IsBusy) return;

            dialogue.invitedMasterIds.Clear();
            foreach (var id in Journey.InvitedMasterIds) dialogue.invitedMasterIds.Add(id);

            var chapter = Journey.Spine.Current;
            dialogue.artworkTitle = chapter.Title;
            dialogue.artworkArtist = chapter.Artist ?? string.Empty;
            dialogue.artworkDate = chapter.Chapter ?? string.Empty;

            _speech = "Listening…";
            dialogue.Listen();
        }

        /// <summary>
        /// The closing synthesis: the masters read back the walk that actually happened.
        ///
        /// <b>One call returning three threads, not a fan-out.</b> The threads synthesise a single
        /// trajectory and have to be aware of the same one; asking three times would give three
        /// readings of three different visits.
        ///
        /// A failure is reported as a failure. There is no canned closing anywhere in this path —
        /// <see cref="JourneyScript.Ending"/> falls back to her generic ending and the manifesto
        /// then LABELS itself generic, which is the honest outcome rather than a pleasant lie.
        /// </summary>
        async void RequestRoundtable()
        {
            if (_roundtableAsked) return;
            _roundtableAsked = true;

            if (_roster == null) { _speech = "No roster: the salon cannot be convened."; return; }

            var key = await MusePico.Generation.FallbackKeySource.ForOpenAi().GetKeyAsync();
            if (string.IsNullOrEmpty(key))
            {
                _speech = "No OpenAI key — the salon cannot read your walk back.";
                return;
            }

            _speech = "The masters are reading your walk…";
            var invited = MasterRoster.Select(_roster, Journey.InvitedMasterIds);
            var client = new RoundtableClient(
                new MusePico.Tripo.TripoWebRequestTransport(key, ResponsesCall.DefaultEndpoint), _roster);

            var result = await client.AskAsync(Journey.Session, invited);

            if (!result.Success)
            {
                _speech = "The salon did not answer: " + result.Error;
                return;
            }

            SetEnding(result.worldTitle, result.synthesis, result.Live);
            _speech = result.synthesis;
        }

        void NextChapter()
        {
            if (!Journey.Spine.Advance()) return;
            OpenChapter();
            panel.Show(JourneyScript.For(Journey, Roster(), _ending));
        }

        /// <summary>Her <c>choose</c> and her companion / question pickers, by stage.</summary>
        void OnChoice(string id)
        {
            if (_artOpen != null) { OnArtChoice(id); return; }

            switch (Journey.Current)
            {
                case Stage.LifeQuestion:
                    Journey.SetQuestion(id);          // the choice id IS her question text
                    break;

                case Stage.CompanionSelection:
                    Journey.ToggleCompanion(id);      // a fourth is refused, not swapped
                    break;

                case Stage.Decision:
                    foreach (var choice in JourneyScript.DecisionChoices)
                        if (choice.Id == id)
                        {
                            Journey.Session.ApplyChoice(choice.Delta);
                            Journey.GoTo(Stage.WorldTransformation);
                            return;
                        }
                    break;
            }
        }

        /// <summary>
        /// Step back, keeping everything already chosen.
        ///
        /// In the gallery the same gesture means the PREVIOUS ROOM instead, which is her
        /// `.scene-arrow` with `data-scene-direction="-1"` — walking the spine backwards is not
        /// undoing anything, so it stays available where stepping back through the arc does not.
        /// </summary>
        void OnBack()
        {
            if (_artOpen != null) { CloseArt(); return; }
            if (_asking != null) { CloseAsk(); return; }
            if (Journey.Current == Stage.WorldExploration) { PreviousChapter(); return; }
            Journey.Back();
        }

        /// <summary>
        /// Her <c>goToExhibitionScene</c>: jump to a room by index, from an arrow or a dot.
        /// Out-of-range is ignored rather than clamped, so a disabled arrow that somehow fires
        /// cannot wrap the visitor round to the other end of the exhibition.
        /// </summary>
        void OnNav(int index)
        {
            if (Journey.Current != Stage.WorldExploration) return;
            if (index < 0 || index >= Journey.Spine.Count) return;
            if (index == Journey.Spine.Index) return;

            Journey.Spine.GoTo(index);
            OpenChapter();
            panel.Show(JourneyScript.For(Journey, Roster(), _ending));
        }

        /// <summary>Her scene navigator's left arrow.</summary>
        void PreviousChapter()
        {
            if (!Journey.Spine.Back()) return;
            OpenChapter();
            panel.Show(JourneyScript.For(Journey, Roster(), _ending));
        }

        /// <summary>Her <c>act()</c>: what the forward button does, stage by stage.</summary>
        void OnAction()
        {
            if (_artOpen != null) { CloseArt(); return; }     // NOT NOW, or CONTINUE THE WALK
            if (_asking != null) { AskTheMasters(); return; }

            switch (Journey.Current)
            {
                case Stage.LifeQuestion:
                    // Her submit: an empty box takes the first suggestion rather than blocking.
                    if (string.IsNullOrWhiteSpace(Journey.Question))
                        Journey.SetQuestion(JourneyScript.LifeQuestions[0]);
                    Journey.Advance();
                    break;

                case Stage.WorldExploration:
                    // Her `summon`, and her `reset` once the visitor is standing in the final world.
                    if (Journey.Spine.InFinalWorld) Restart();
                    else Journey.GoTo(Stage.Summoning);
                    break;

                case Stage.Manifesto:
                    // Her `enter-final-world`: back into the gallery, now showing the dream world.
                    Journey.Spine.EnterFinalWorld();
                    Journey.GoTo(Stage.WorldExploration);
                    break;

                case Stage.WorldTransformation:
                    break;                            // it runs on its own clock

                default:
                    Journey.Advance();
                    break;
            }
        }

        /// <summary>Her <c>scheduleTransformation</c>: three writes, then the manifesto.</summary>
        void TickTransformation()
        {
            var ratio = (Time.time - _transformationStarted) / JourneyScript.TransformationSeconds;
            if (ratio >= 1f) { Journey.GoTo(Stage.Manifesto); return; }

            var line = JourneyScript.TransformationBeats[0].Value;
            foreach (var beat in JourneyScript.TransformationBeats)
                if (ratio >= beat.Key) line = beat.Value;

            var showing = JourneyScript.For(Journey, Roster(), _ending);
            showing.Lede = line;
            panel.Show(showing);
        }

        void Restart()
        {
            _requestedWorldKey = null;      // forces the threshold world to reload and re-place us
            _roundtableAsked = false;
            _speech = string.Empty;
            Journey.Reset();
            _ending = default;
            EnterStage(Journey.Current);
        }

        /// <summary>
        /// The closing synthesis, once the roundtable has produced one. Left unset it stays her
        /// labelled fallback, so a manifesto that was never synthesised says so on the panel.
        /// </summary>
        public void SetEnding(string worldTitle, string synthesis, bool live) =>
            _ending = JourneyScript.Ending(worldTitle, synthesis, live);

        /// <summary>
        /// The portrait for a master, matched on the name rather than on a hand-kept list.
        ///
        /// <c>masters.json</c> carries no portrait field — it is the product of an export from
        /// muse-infinity and is not hand-edited — so the join is the one thing both sides already
        /// agree on: the full name. "Claude Monet" finds "portrait-claude-monet". A master with no
        /// portrait simply draws as a name, which is what the seventh would do if one went missing.
        /// </summary>
        Texture2D PortraitFor(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return null;

            // Chapters address themselves by position, masters by id. The prefix keeps the two
            // namespaces from colliding on a master whose id ever looked like a number.
            const string chapterPrefix = "chapter:";
            if (imageId.StartsWith(chapterPrefix))
            {
                if (chapterImages == null) return null;
                int index;
                if (!int.TryParse(imageId.Substring(chapterPrefix.Length), out index)) return null;
                return index >= 0 && index < chapterImages.Length ? chapterImages[index] : null;
            }

            if (portraits == null || portraits.Length == 0) return null;

            var master = MasterRoster.Find(_roster, imageId);
            if (master == null || string.IsNullOrEmpty(master.fullName)) return null;

            var wanted = "portrait-" + Slug(master.fullName);
            foreach (var texture in portraits)
                if (texture != null && texture.name == wanted) return texture;

            return null;
        }

        /// <summary>"Vincent van Gogh" -> "vincent-van-gogh".</summary>
        public static string Slug(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('-');
                // Anything else — an apostrophe, a comma — is dropped rather than transliterated.
            }
            return sb.ToString();
        }

        IReadOnlyList<MasterLens> Roster() =>
            _roster?.masters != null ? (IReadOnlyList<MasterLens>)_roster.masters : null;

        void IndexStageRoots()
        {
            _stageRoots.Clear();
            if (journeyRoot == null) return;

            foreach (Stage stage in System.Enum.GetValues(typeof(Stage)))
            {
                var child = journeyRoot.Find("Stage_" + stage);
                if (child != null) _stageRoots[stage] = child.gameObject;
                else Debug.LogWarning("[MuseumJourneyRunner] no Stage_" + stage + " under " +
                                      journeyRoot.name + "; that beat will have no set dressing.");
            }
        }
    }
}
