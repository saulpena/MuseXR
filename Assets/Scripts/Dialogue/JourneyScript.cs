using System.Collections.Generic;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>One pointable option on a stage panel.</summary>
    public readonly struct StageChoice
    {
        public readonly string Id;
        public readonly string Label;
        public readonly bool Selected;

        /// <summary>
        /// A picture to show instead of a line of text, looked up by the renderer.
        ///
        /// Her companion stage is a grid of PORTRAITS — real public-domain paintings and
        /// photographs of each master — and reducing that to seven floating names throws away most
        /// of what the stage is for. Empty means this choice is text.
        /// </summary>
        public readonly string ImageId;

        public StageChoice(string id, string label, bool selected = false, string imageId = null)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Selected = selected;
            ImageId = imageId ?? string.Empty;
        }
    }

    /// <summary>
    /// Everything one stage puts in front of the visitor. The renderer draws this and nothing else,
    /// so what a stage says is decided here, in a place tests can read.
    /// </summary>
    public sealed class StagePanel
    {
        /// <summary>Small line above the eyebrow. Only the threshold uses it.</summary>
        public string Marker = string.Empty;

        /// <summary>Her <c>.eyebrow</c> — "01 / BEGIN WITH YOUR LIFE".</summary>
        public string Eyebrow = string.Empty;

        /// <summary>Her <c>h1</c>/<c>h2</c>.</summary>
        public string Heading = string.Empty;

        /// <summary>Her <c>.lede</c>.</summary>
        public string Lede = string.Empty;

        public IReadOnlyList<StageChoice> Choices = new List<StageChoice>();

        /// <summary>
        /// A small row shown with the copy and not pointable — her <c>.curation-companions</c>,
        /// the masters you invited, standing under the theme they were read into.
        ///
        /// Separate from <see cref="Choices"/> because these are not choices: pointing at one does
        /// nothing, and a control that looks like a button and refuses to be pressed is worse than
        /// a picture that never looked like one.
        /// </summary>
        public IReadOnlyList<StageChoice> Aside = new List<StageChoice>();

        /// <summary>Her <c>.primary-action</c> label, or empty when the stage has no forward button.</summary>
        public string Action = string.Empty;

        /// <summary>False renders the action greyed, as her <c>disabled</c> does.</summary>
        public bool ActionEnabled = true;

        /// <summary>
        /// The label on the step-back control, or empty where the stage cannot be reversed.
        /// </summary>
        public string Back = string.Empty;

        /// <summary>
        /// Her <c>.scene-navigator</c>: which room of the spine you are in, drawn as a back
        /// arrow, a counter and one dot per room, all of them pointable.
        ///
        /// <b>This is how a visitor changes room, and it is the ONLY way.</b> Her build has no
        /// keyboard or gamepad path to it - `[data-scene-direction]` and `[data-scene-index]` are
        /// click handlers on buttons. Binding it to a thumbstick, as an earlier version did, both
        /// invents a control she does not have and collides with XRI's snap-turn on the same
        /// stick, so the visitor changes world every time they try to look round.
        ///
        /// -1 means the stage has no navigator.
        /// </summary>
        public int NavIndex = -1;

        /// <summary>How many rooms the spine holds. Zero means no navigator.</summary>
        public int NavTotal;

        /// <summary>
        /// False draws the action as a quiet secondary control rather than the way forward.
        ///
        /// Her gallery's "FORM MY ANSWER" is `.salon-next`, tucked in a corner — the stage is for
        /// walking and looking, and a primary button in the middle of the room argues with that.
        /// </summary>
        public bool ActionIsPrimary = true;

        /// <summary>
        /// The action lives behind a small toggle instead of on the panel. The gallery's
        /// "FORM MY ANSWER" ends the walk, so it is kept out of the way until the visitor has
        /// explored and asked their questions (Saul, 27 Sep 2026): pressing
        /// <see cref="MenuLabel"/> reveals <see cref="MenuNote"/> and the action.
        /// </summary>
        public bool ActionInMenu;

        /// <summary>The quiet toggle that opens the menu holding the action.</summary>
        public string MenuLabel = string.Empty;

        /// <summary>One line shown above the action once the menu is open.</summary>
        public string MenuNote = string.Empty;

        /// <summary>The room the navigator's back arrow goes to, or empty at the first room.</summary>
        public string NavPrevLabel = string.Empty;

        /// <summary>The room the navigator's forward arrow goes to, or empty at the last room.</summary>
        public string NavNextLabel = string.Empty;

        /// <summary>The control line. Hers says what the mouse does; ours says what the hands do.</summary>
        public string Hint = string.Empty;

        /// <summary>
        /// Her <c>&lt;textarea id="lifeQuestion"&gt;</c>: the box holding the question, drawn at a
        /// FIXED height whatever is in it.
        ///
        /// <b>This exists so the panel stops resizing.</b> The question used to be appended to the
        /// hint, so choosing an option replaced one short line with a long quoted one, the copy
        /// re-wrapped, and every control below it jumped to a new place. A form whose fields move
        /// when you fill them in is unusable, and in a headset it also breaks the ray you were
        /// already aiming.
        ///
        /// Null means the stage has no field. Empty means it has one and it is unfilled.
        /// </summary>
        public string Field;

        /// <summary>Greyed prompt shown in an empty <see cref="Field"/>. Her <c>placeholder</c>.</summary>
        public string FieldPlaceholder = string.Empty;

        /// <summary>Small print below — the honesty notices, and the AI-interpretation disclaimer.</summary>
        public string Notice = string.Empty;

        /// <summary>
        /// The portrait of the master speaking, drawn beside the copy at its top left (her
        /// .art-dialogue-head: the speaker's face next to their words). Empty for no portrait.
        /// </summary>
        public string SpeakerImageId = string.Empty;

        /// <summary>
        /// Cards set side by side under the copy (her .roundtable-threads): each master's face,
        /// name and remark in a column of their own, so two or three of them read across instead of
        /// stacking the panel down through the floor.
        /// </summary>
        public IReadOnlyList<StageCard> Cards = new List<StageCard>();
    }

    /// <summary>One of her three closing choices, with the axis delta it applies.</summary>
    public readonly struct DecisionChoice
    {
        public readonly string Id;
        public readonly string Label;
        public readonly PhilosophyAxes Delta;

        public DecisionChoice(string id, string label, PhilosophyAxes delta)
        {
            Id = id; Label = label; Delta = delta;
        }
    }

    /// <summary>One master's closing remark at the roundtable (her data.threads entry).</summary>
    public readonly struct ClosingThread
    {
        public readonly string SpeakerId, Speaker, Text;
        public ClosingThread(string speakerId, string speaker, string text)
        {
            SpeakerId = speakerId ?? string.Empty; Speaker = speaker ?? string.Empty; Text = text ?? string.Empty;
        }
    }

    /// <summary>A card in a row under the copy: a face, a name and what they said.</summary>
    public readonly struct StageCard
    {
        public readonly string ImageId, Title, Body, Footnote;
        public StageCard(string imageId, string title, string body, string footnote = null)
        {
            ImageId = imageId ?? string.Empty; Title = title ?? string.Empty; Body = body ?? string.Empty; Footnote = footnote ?? string.Empty;
        }
    }

    /// <summary>The ending the manifesto reads out, and whether it is the real one.</summary>
    public readonly struct ClosingEnding
    {
        public readonly string Title;
        public readonly string Copy;

        /// <summary>False when this is <see cref="JourneyScript.FallbackTitle"/> — no synthesis happened.</summary>
        public readonly bool FromRoundtable;

        /// <summary>False when the synthesis exists but no live model produced it.</summary>
        public readonly bool Live;

        /// <summary>Each master's closing remark (her data.threads).</summary>
        public readonly IReadOnlyList<ClosingThread> Threads;

        /// <summary>True while the roundtable is being asked (her status "loading").</summary>
        public readonly bool Loading;

        /// <summary>Why the roundtable failed (her status "error"), or empty.</summary>
        public readonly string Error;

        public ClosingEnding(string title, string copy, bool fromRoundtable, bool live,
                             IReadOnlyList<ClosingThread> threads = null,
                             bool loading = false, string error = null)
        {
            Title = title; Copy = copy; FromRoundtable = fromRoundtable; Live = live;
            Threads = threads ?? new List<ClosingThread>();
            Loading = loading; Error = error ?? string.Empty;
        }
    }

    /// <summary>
    /// What each of the ten stages says, ported from muse-infinity's view functions.
    ///
    /// <b>This is her copy, not a paraphrase.</b> The strings below are lifted from
    /// <c>app.js</c> — <c>thresholdView</c>, <c>lifeQuestionView</c>, <c>companionSelectionView</c>,
    /// <c>aiCurationView</c>, <c>worldExplorationView</c>, <c>summoningView</c>,
    /// <c>roundtableView</c>, <c>decisionView</c>, <c>transformationView</c>,
    /// <c>manifestoView</c> — so the two builds read the same at every beat. Where a line describes
    /// a mouse it is restated for hands, and that is the only class of change.
    ///
    /// Pure: no scene, no GameObjects, no TMP. The renderer is <c>JourneyPanel</c>.
    /// </summary>
    public static class JourneyScript
    {
        /// <summary>
        /// The spine scenes her curation stage previews: <c>exhibitionScenes[1], [3], [5]</c>.
        /// Not the first three — she spreads the preview across the arc.
        /// </summary>
        public static readonly IReadOnlyList<int> RouteScenes = new List<int> { 1, 3, 5 };

        /// <summary>
        /// The image key for a chapter, by its position in the spine.
        ///
        /// Position rather than name, because her thumbnail files are numbered in spine order
        /// (<c>01-entrance-conservatory</c>, <c>02-court-of-light</c>, ...) while the scene ids are
        /// not derived from those file names — <c>threshold-conservatory</c> against
        /// <c>01-entrance-conservatory</c> would never join.
        /// </summary>
        public static string ChapterImage(int spineIndex) => "chapter:" + spineIndex;

        /// <summary>Her three suggested questions, in her order. An empty answer takes the first.</summary>
        public static readonly IReadOnlyList<string> LifeQuestions = new List<string>
        {
            "What makes a life meaningful?",
            "How do I live with uncertainty?",
            "What should I keep, and what should I let go?",
        };

        /// <summary>Her <c>choices</c> array, labels and deltas both.</summary>
        public static readonly IReadOnlyList<DecisionChoice> DecisionChoices = new List<DecisionChoice>
        {
            new DecisionChoice("perception", "Art should teach us to see the world again.",
                PhilosophyAxes.PerceptionChoice),
            new DecisionChoice("emotion", "Art should turn inner experience into a shared language.",
                PhilosophyAxes.EmotionChoice),
            new DecisionChoice("invention", "Art should create realities that did not exist before.",
                PhilosophyAxes.InventionChoice),
        };

        /// <summary>
        /// Her <c>scheduleTransformation</c> beats, as (fraction of the run, line). The stage writes
        /// each line over the last at those points and then moves to the manifesto.
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<float, string>> TransformationBeats =
            new List<KeyValuePair<float, string>>
            {
                new KeyValuePair<float, string>(0f,
                    "Sound falls away. Your chosen idea enters the salon ring."),
                new KeyValuePair<float, string>(0.20f,
                    "The active perspective expands. Character memories leave their seats."),
                new KeyValuePair<float, string>(0.46f,
                    "Architecture dissolves. A second artistic system passes through the darkness."),
                new KeyValuePair<float, string>(0.73f,
                    "Particles reassemble around the philosophy you chose."),
            };

        /// <summary>Her non-demo transformation duration: 12000 ms.</summary>
        public const float TransformationSeconds = 12f;

        public const string FallbackTitle = "The World Between Worlds";

        public const string FallbackCopy =
            "You believe art must balance perception, emotion and invention without allowing any " +
            "one truth to become final.";

        /// <summary>Her standing disclaimer, carried wherever the masters are shown as people.</summary>
        public const string InterpretationNotice =
            "Historical figures are represented as AI interpretations grounded in documented " +
            "themes — not authentic quotations or endorsements.";

        /// <summary>
        /// Her <c>finalWorldData</c>: the ending is the roundtable's synthesis, and the fallback is
        /// labelled as a failure rather than passed off as the real thing.
        /// </summary>
        public static ClosingEnding Ending(string worldTitle, string synthesis, bool live,
                                           IReadOnlyList<ClosingThread> threads = null)
        {
            var title = (worldTitle ?? string.Empty).Trim();
            var copy = (synthesis ?? string.Empty).Trim();
            if (title.Length == 0 || copy.Length == 0)
                return new ClosingEnding(FallbackTitle, FallbackCopy, false, false);
            return new ClosingEnding(title, copy, true, live, threads);
        }

        /// <summary>The roundtable has been asked and has not answered (her status "loading").</summary>
        public static ClosingEnding RoundtableLoading() =>
            new ClosingEnding(FallbackTitle, FallbackCopy, false, false, loading: true);

        /// <summary>The roundtable failed (her status "error"): the manifesto still falls back.</summary>
        public static ClosingEnding RoundtableFailed(string error) =>
            new ClosingEnding(FallbackTitle, FallbackCopy, false, false, error: error ?? "unknown error");

        /// <summary>
        /// The panel for whichever stage the visitor is standing in.
        ///
        /// <paramref name="masters"/> supplies stage 02's cast; pass the whole roster.
        /// <paramref name="ending"/> is only read by stages 06 and 09.
        /// </summary>
        public static StagePanel For(
            MuseumJourney journey,
            IReadOnlyList<MasterLens> masters = null,
            ClosingEnding ending = default)
        {
            if (journey == null) return new StagePanel();

            // No ending passed means no roundtable has run. That is exactly her fallback case, and
            // it must arrive labelled as one rather than as a panel full of nulls.
            if (ending.Title == null) ending = Ending(null, null, false);

            switch (journey.Current)
            {
                case Stage.Threshold: return Threshold();
                case Stage.LifeQuestion: return WithBack(LifeQuestion(journey), journey);
                case Stage.CompanionSelection: return WithBack(CompanionSelection(journey, masters), journey);
                case Stage.AiCuration: return WithBack(AiCuration(journey, masters), journey);
                case Stage.WorldExploration: return WorldExploration(journey);
                case Stage.Summoning: return Summoning(journey);
                case Stage.Roundtable: return Roundtable(journey, ending);
                case Stage.Decision: return Decision();
                case Stage.WorldTransformation: return Transformation();
                case Stage.Manifesto: return Manifesto(journey, ending);
                default: return new StagePanel();
            }
        }

        /// <summary>
        /// Offer the step back, when the journey allows one.
        ///
        /// The label names the stage being returned to rather than saying "BACK", because in a
        /// headset there is no browser history to reason from and no breadcrumb above the panel —
        /// the control has to say where it goes.
        /// </summary>
        static StagePanel WithBack(StagePanel panel, MuseumJourney journey)
        {
            var previous = journey.Previous;
            if (previous == null) return panel;

            switch (previous.Value)
            {
                case Stage.Threshold: panel.Back = "← THE THRESHOLD"; break;
                case Stage.LifeQuestion: panel.Back = "← MY QUESTION"; break;
                case Stage.CompanionSelection: panel.Back = "← MY COMPANY"; break;
                default: panel.Back = "← BACK"; break;
            }
            return panel;
        }

        /// <summary>
        /// Her <c>openAskDialogue</c>: the popup that opens when you click a companion in the
        /// gallery. A question goes in, and all three masters answer.
        ///
        /// <b>This is the ONLY screen in the whole arc that reaches a master</b>, together with the
        /// artwork popup beside it. Everything before the gallery is a form being filled in, and
        /// putting a model call anywhere else - which an earlier build did, on the question screen -
        /// produces an answer on a screen that has not chosen a master yet.
        ///
        /// The microphone fills <paramref name="question"/> and does nothing else. Pressing ASK is
        /// what sends it, exactly as her form needs a submit.
        /// </summary>
        public static StagePanel AskDialogue(
            MasterLens companion, string artworkTitle, string question, string replies) =>
            AskDialogue(companion, artworkTitle, question, replies, typing: false);

        /// <summary>The choice id of the ready-made question plate on the ask form.</summary>
        public const string SuggestedQuestionId = "ask-suggested";

        /// <summary>
        /// The same popup with a keyboard. <paramref name="typing"/> is true when there is no
        /// headset, so the question is typed rather than dictated: the box shows a caret and the
        /// hint names the keys instead of the grip.
        ///
        /// <paramref name="suggestion"/> is a ready-made question offered as its own plate UNDER
        /// the box, never put in it: the box is for the visitor's own question. Pre-filling it made
        /// clicking a master a second way of asking about the Buddha (Saul, 27 Sep: "now we can not
        /// ask questions, it is only talking about Buddha").
        /// </summary>
        public static StagePanel AskDialogue(
            MasterLens companion, string artworkTitle, string question, string replies, bool typing,
            string suggestion = null)
        {
            var panel = AskDialogueFor(companion, artworkTitle, question, replies);
            var hasQuestion = !string.IsNullOrWhiteSpace(question);
            var hasSuggestion = !string.IsNullOrWhiteSpace(suggestion);

            if (hasSuggestion)
                panel.Choices = new List<StageChoice> { new StageChoice(SuggestedQuestionId, "OR ASK: " + suggestion) };

            if (!typing)
            {
                panel.Hint = hasQuestion
                    ? "HOLD GRIP TO SAY IT AGAIN · TRIGGER ON ASK"
                    : hasSuggestion ? "HOLD GRIP AND SPEAK YOUR QUESTION · OR POINT AT THE SUGGESTION"
                                    : "HOLD GRIP AND SPEAK YOUR QUESTION";
                return panel;
            }

            // No quotes while typing: a caret drawn inside the closing quote read as a stray glyph
            // (independent review, 27 Sep).
            panel.Heading = hasQuestion ? question + "|" : "What do you want to ask?|";
            panel.Hint = hasQuestion
                ? "TYPE TO CHANGE IT · ENTER TO ASK · ESC TO GO BACK"
                : hasSuggestion ? "TYPE YOUR QUESTION · ENTER TO ASK · OR CLICK THE SUGGESTION"
                                : "TYPE YOUR QUESTION · ENTER TO ASK · ESC TO GO BACK";
            return panel;
        }

        /// <summary>The three phases of the object popup, in the order a visitor goes through them.</summary>
        public enum ArtPhase { Looking, Choosing, Answered }

        /// <summary>
        /// Her artwork popup, as Saul described it working in the web build (27 Sep): click an
        /// object, and all three masters say something about it (each reading in its bubble, kept);
        /// then choose one of her three answers; then ONE master replies to that answer, and the only
        /// way on is to continue the walk. No "NOT NOW".
        ///
        /// The answers appear only once the readings have been heard. Offered at once, a choice made
        /// mid-reading had the reply spoken over the masters still talking, and the reply replaced a
        /// line on the panel that looked unrelated to anything the visitor had chosen.
        /// </summary>
        /// <param name="status">What is happening: the masters looking, who is speaking, or the prompt to answer.</param>
        /// <param name="youSaid">The answer chosen, once answered.</param>
        /// <param name="reactor">The master replying, once answered.</param>
        /// <param name="reply">Their reply, once answered.</param>
        /// <summary>The hint on a painting that has a world to walk into (JourneyPaintingPortal).</summary>
        public const string WalkInHint = "TO WALK INTO IT, HOLD A CONTROLLER ON ITS CENTRE FOR 2 SECONDS · P IN THE EDITOR";

        /// <summary>The panel while the visitor stands inside a painting.</summary>
        public static StagePanel InsidePainting(string title) => new StagePanel
        {
            Marker = "INSIDE THE PAINTING",
            Eyebrow = "YOU HAVE STEPPED THROUGH THE FRAME",
            Heading = string.IsNullOrEmpty(title) ? "This work" : title,
            Lede = "Walk where the painter stood. The painting you came through is still behind you: " +
                   "walk back up to it and it opens onto the gallery.",
            Hint = "TO LEAVE, WALK BACK THROUGH THE PAINTING",
        };

        public static StagePanel ArtDialogue(string title, ArtPhase phase, string status,
                                             string youSaid = null, MasterLens reactor = null, string reply = null,
                                             bool canStepInside = false)
        {
            var heading = string.IsNullOrEmpty(title) ? "This work" : title;

            if (phase == ArtPhase.Answered)
            {
                var who = reactor == null ? "A MASTER" : reactor.fullName.ToUpperInvariant();
                return new StagePanel
                {
                    // Who said it, as a face beside the words, the way her popup shows the master.
                    SpeakerImageId = reactor != null ? reactor.id : string.Empty,
                    Eyebrow = who + " REPLIES",
                    Heading = heading,
                    Lede = "<color=" + GoldHex + ">You said:</color> “" + (youSaid ?? string.Empty) + "”\n\n" +
                           (reply ?? string.Empty),
                    Action = "CONTINUE THE WALK →",
                    ActionIsPrimary = true,
                    Notice = ArtworkDialogue.Disclaimer,
                };
            }

            // The answers are there from the start: the masters' readings appear above their heads
            // as they arrive, and nobody has to wait for them to finish speaking to answer.
            var choices = new List<StageChoice>();
            for (var i = 0; i < ArtworkDialogue.Choices.Count; i++)
            {
                var c = ArtworkDialogue.Choices[i];
                choices.Add(new StageChoice(c.Id, "0" + (i + 1) + "  " + c.Label));
            }

            return new StagePanel
            {
                Eyebrow = "HOW DOES IT LEAVE YOU?",
                Heading = heading,
                Lede = status ?? string.Empty,      // empty unless something went wrong
                Choices = choices,
                Hint = canStepInside ? WalkInHint : string.Empty,
                Notice = ArtworkDialogue.Disclaimer,
            };
        }

        static StagePanel AskDialogueFor(
            MasterLens companion, string artworkTitle, string question, string replies)
        {
            var name = companion == null ? "your companion" : companion.fullName;
            var hasQuestion = !string.IsNullOrWhiteSpace(question);

            return new StagePanel
            {
                // Says who was asked AND that all three answer. "04 / SOCRATES" over an answer
                // labelled "1 / 3 · Claude Monet" read as a contradiction (independent review, 27 Sep).
                Marker = "ALL THREE MASTERS ANSWER",
                Eyebrow = "YOU ASKED " + name.ToUpperInvariant(),

                // REVERTED with the question field, for the same reason. See LifeQuestion.
                Heading = hasQuestion ? "“" + question + "”" : "What do you want to ask?",
                Lede = string.IsNullOrEmpty(replies)
                    ? (string.IsNullOrEmpty(artworkTitle)
                        ? name + " turns toward you. Ask, and all three masters answer in their own voice."
                        : name + " turns toward " + artworkTitle +
                          ". Ask, and all three masters answer in their own voice.")
                    : replies,
                Action = "ASK",
                ActionEnabled = hasQuestion,
                ActionIsPrimary = true,
                // "\u2190 BACK TO THE GALLERY" ellipsised on its 0.315-width plate (seen 27 Sep).
                Back = "\u2190 GALLERY",
                Hint = hasQuestion
                    ? "HOLD GRIP TO SAY IT AGAIN \u00b7 TRIGGER TO ASK"
                    : "HOLD GRIP AND SPEAK YOUR QUESTION",
            };
        }

        /// <summary>Her `--gold`. Duplicated from the panel so the copy can colour its own text.</summary>
        public const string GoldHex = "#C9AA72";

        static StagePanel Threshold() => new StagePanel
        {
            // Her `.spatial-coordinate`: 7px at 26% alpha, top-left, deliberately almost
            // subliminal. It is the Met's location — flavour, not information. Ours rendered it
            // at 70% size and 60% alpha, which read as a headline nobody could parse.
            Marker = "MEMORY SITE 00 · 40°46′N / 73°58′W",
            Eyebrow = "A LIVING ARCHIVE BEYOND TIME",
            Heading = "The Impossible Museum",
            Lede = "Enter a cultural memory where artists disagree — and your answer becomes " +
                   "part of the architecture.",
            Action = "ENTER",
            Hint = "LOOK AROUND · POINT AT ENTER AND PULL THE TRIGGER",
            // Her "MOVE TO LOOK · CLICK TO CROSS". The trigger really does cross here, and only
            // here — see MuseumJourneyRunner, where a blind trigger is refused on every other
            // stage so a stray pull cannot skip a beat of the arc.
        };

        static StagePanel LifeQuestion(MuseumJourney journey)
        {
            var choices = new List<StageChoice>();
            foreach (var q in LifeQuestions)
                choices.Add(new StageChoice(q, q, string.Equals(q, journey.Question)));

            var spoken = (journey.Question ?? string.Empty).Trim();
            return new StagePanel
            {
                Eyebrow = "01 / BEGIN WITH YOUR LIFE",
                Heading = "What question are you carrying?",
                Lede = "There is no correct question. The museum will use it as the curatorial " +
                       "thread connecting every artwork, companion and space.",
                Choices = choices,
                Action = "CHOOSE WHO WALKS WITH ME →",
                // Her form is `required`, but her submit falls back to the first suggestion. Ours
                // does the same, so the button is never a dead end.
                ActionEnabled = true,

                // REVERTED to the 11:33 shape while a comfort problem is bisected. The field was
                // the right fix for the panel resizing, but it also made the backdrop noticeably
                // taller, and a large body-locked plane over a splat world is a candidate for the
                // headache reported from the headset. Field stays in StagePanel, unused here, so
                // it can be put back in one line once the cause is known.
                Hint = spoken.Length > 0
                    ? "“" + spoken + "”   ·   HOLD GRIP TO SPEAK AGAIN"
                    : "HOLD GRIP TO SPEAK · OR POINT AND PULL THE TRIGGER",
            };
        }

        static StagePanel CompanionSelection(MuseumJourney journey, IReadOnlyList<MasterLens> masters)
        {
            var choices = new List<StageChoice>();
            if (masters != null)
                foreach (var m in masters)
                    choices.Add(new StageChoice(m.id, m.fullName, journey.IsInvited(m.id), m.id));

            var count = journey.InvitedMasterIds.Count;
            return new StagePanel
            {
                Eyebrow = "02 / INVITE UP TO " + (MuseumJourney.MaxCompanions == 2 ? "TWO" : MuseumJourney.MaxCompanions.ToString()) + " MINDS",
                Heading = "Who will walk the museum with you?",
                Lede = "Choose real historical portraits with public-domain sources. In the " +
                       "gallery, each becomes an interpretive AI companion — not a clone or " +
                       "authentic quotation.",
                Choices = choices,
                Action = "LET AI CURATE →",
                ActionEnabled = count > 0,
                // The count leads, in gold and half again the size: "how many have I picked" is
                // the question this screen exists to answer, and it was set in the same small grey
                // as the instruction beside it, where nobody found it.
                // The count gets its own line. Sharing one with the instruction made the
                // instruction wrap and orphan its last two words beside a 150% headline.
                Hint = "<size=150%><color=" + GoldHex + ">" + count + " / " +
                       MuseumJourney.MaxCompanions + " SELECTED</color></size>\n" +
                       "POINT AT A PORTRAIT AND PULL THE TRIGGER",
                Notice = InterpretationNotice,
            };
        }

        static StagePanel AiCuration(MuseumJourney journey, IReadOnlyList<MasterLens> masters)
        {
            var reading = CurationData.For(journey.Question);

            // Her `.curation-companions`: the invited masters shown with the theme their question
            // was read into, so the curation visibly belongs to the company you chose rather than
            // arriving from nowhere. She uses the SHORT name here, not the full one.
            var invited = new List<StageChoice>();
            if (masters != null)
                foreach (var m in masters)
                    if (journey.IsInvited(m.id))
                        invited.Add(new StageChoice(m.id, m.name, true, m.id));

            var choices = new List<StageChoice>();
            for (var i = 0; i < reading.Chapters.Count; i++)
            {
                // Her `routeScenes`: the curated themes are previewed against scenes 02, 04 and 06
                // of the spine, each with its thumbnail, title and presiding artist. Without the
                // pictures this stage is three words and a button.
                var spineIndex = RouteScenes[i];
                var scene = ExhibitionSpine.Chapters[spineIndex];
                choices.Add(new StageChoice(
                    "chapter" + i,
                    "CHAPTER 0" + (i + 1) + "   " + reading.Chapters[i] +
                        "\n<size=80%>" + scene.Title + " · " + scene.Artist + "</size>",
                    false,
                    ChapterImage(spineIndex)));
            }

            return new StagePanel
            {
                Aside = invited,
                Eyebrow = "03 / AI THEME CURATION COMPLETE",
                Heading = reading.Title,
                Lede = "“" + (journey.Question ?? string.Empty) + "”",
                Choices = choices,
                Action = "ENTER THE EXHIBITION →",
                Hint = "CURATORIAL SPINE READY",
            };
        }

        /// <summary>The title of room <paramref name="index"/>, or empty when there is no such room.</summary>
        static string RoomTitle(MuseumJourney journey, int index)
        {
            if (journey.Spine.InFinalWorld) return string.Empty;
            if (index < 0 || index >= ExhibitionSpine.Chapters.Count) return string.Empty;
            return ExhibitionSpine.Chapters[index].Title ?? string.Empty;
        }

        static StagePanel WorldExploration(MuseumJourney journey)
        {
            var chapter = journey.Spine.Current;
            var index = journey.Spine.InFinalWorld ? journey.Spine.Count : journey.Spine.Index + 1;
            var total = journey.Spine.Count;

            return new StagePanel
            {
                Marker = journey.Spine.InFinalWorld
                    ? "YOUR IMPOSSIBLE WORLD"
                    : Pad(index) + " / " + Pad(total),
                Eyebrow = chapter.Chapter + " · " + (chapter.Artist ?? string.Empty),
                Heading = chapter.Title,
                Lede = chapter.Prompt ?? string.Empty,
                Action = journey.Spine.InFinalWorld ? "BEGIN AGAIN →" : "FORM MY ANSWER →",
                ActionIsPrimary = false,
                // Changing world is the gallery's main control; ending the walk is a menu away.
                ActionInMenu = true,
                // No trailing ellipsis: on a small plate it read as truncated text (review, 27 Sep).
                MenuLabel = journey.Spine.InFinalWorld ? "LEAVE" : "FINISH THE WALK",
                MenuNote = journey.Spine.InFinalWorld
                    ? "Start the museum again from the threshold."
                    : "Explored enough, and asked the masters? They will read your walk back to you.",
                NavIndex = journey.Spine.InFinalWorld ? -1 : journey.Spine.Index,
                NavTotal = journey.Spine.InFinalWorld ? 0 : total,
                NavPrevLabel = RoomTitle(journey, journey.Spine.Index - 1),
                NavNextLabel = RoomTitle(journey, journey.Spine.Index + 1),
                Hint = "POINT AT A MASTER TO ASK THEM A QUESTION · WALK WITH THE STICK · ARROWS CHANGE WORLD",
                Notice = "OPEN ACCESS COLLECTION · LOCAL CURATION",
            };
        }

        static StagePanel Summoning(MuseumJourney journey)
        {
            // Her summoningView: the walk as a ledger, one line per stop and per question — read, not
            // pressed. (They were plates here, which looked like buttons and did nothing.)
            var lines = SummoningLedger(journey);
            return new StagePanel
            {
                Eyebrow = "05 / SEVEN MINDS. ONE EMPTY SEAT.",
                Heading = "The Salon Outside Time",
                Lede = lines.Count > 0
                    ? "Your walk is entering the record.\n\n" + string.Join("\n", lines)
                    : "You arrive with an empty record — no stop, no question. The salon will " +
                      "have only that to read.",
                Action = "OPEN THE SALON →",
                Notice = InterpretationNotice,
            };
        }

        /// <summary>Her walkTrailEntries: "Stopped at …" for each work, "Asked “…”" for each question.</summary>
        public static List<string> SummoningLedger(MuseumJourney journey)
        {
            var lines = new List<string>();
            foreach (var art in journey.Session.VisitedArtworks) lines.Add("Stopped at " + art.Title);
            foreach (var q in journey.Session.AskedQuestions) lines.Add("Asked “" + q + "”");
            return lines;
        }

        static StagePanel Roundtable(MuseumJourney journey, ClosingEnding ending)
        {
            // Her roundtableView: the heading becomes the world's title once it is ready; under the
            // walk trail, the status, each master's closing remark with its own disclaimer, and the
            // synthesis. TRY AGAIN only after a failure. FACE THE CONTRADICTION is never locked.
            var ready = ending.FromRoundtable;
            var body = new StringBuilder(WalkTrail(journey));
            if (ending.Loading) body.Append("\n\n<color=").Append(GoldHex).Append(">THE SALON IS READING YOUR WALK…</color>");
            else if (!string.IsNullOrEmpty(ending.Error)) body.Append("\n\n<color=#E06C5A>THE ROUNDTABLE COULD NOT BE REACHED — ").Append(ending.Error).Append("</color>");
            var cards = new List<StageCard>();
            if (!ending.Loading && string.IsNullOrEmpty(ending.Error) && ready)
            {
                if (!ending.Live) body.Append("\n\n<color=#E06C5A>LOCAL FALLBACK — this closing was not produced by a live model.</color>");
                if (ending.Threads != null)
                    foreach (var thread in ending.Threads)
                        cards.Add(new StageCard(thread.SpeakerId, thread.Speaker, thread.Text, ArtworkDialogue.Disclaimer));
                body.Append("\n\n<i>“").Append(ending.Copy).Append("”</i>");
            }
            return new StagePanel
            {
                Eyebrow = "06 / THE CLOSING ROUNDTABLE",
                Heading = ready ? ending.Title : "The masters read back your walk",
                Lede = body.ToString(),
                Cards = cards,
                Action = "FACE THE CONTRADICTION →",
                ActionEnabled = true,
                Back = !string.IsNullOrEmpty(ending.Error) ? "TRY AGAIN" : string.Empty,
            };
        }

        static StagePanel Decision()
        {
            var choices = new List<StageChoice>();
            foreach (var c in DecisionChoices) choices.Add(new StageChoice(c.Id, c.Label));

            return new StagePanel
            {
                Eyebrow = "07 / SOCRATES ASKS YOU",
                Heading = "If art can alter reality, what responsibility should it carry?",
                Choices = choices,
                Hint = "POINT AT AN ANSWER AND PULL THE TRIGGER",
            };
        }

        static StagePanel Transformation() => new StagePanel
        {
            Eyebrow = "08 / YOUR ANSWER HAS ENTERED THE WORLD",
            Heading = "The museum is rewriting itself.",
            Lede = TransformationBeats[0].Value,
        };

        static StagePanel Manifesto(MuseumJourney journey, ClosingEnding ending)
        {
            var axes = journey.Session.Philosophy;
            var notice = !ending.FromRoundtable
                ? "GENERIC ENDING — the closing roundtable never completed, so this world was " +
                  "not synthesised from your walk."
                : !ending.Live
                    ? "LOCAL FALLBACK — no live model produced this synthesis."
                    : string.Empty;

            return new StagePanel
            {
                Eyebrow = "YOUR IMPOSSIBLE WORLD",
                Heading = ending.Title,
                Lede = ending.Copy,
                Action = "ENTER YOUR WORLD →",
                Back = "ENTER AGAIN",
                Hint = "PERCEPTION " + Pad(axes.Perception) +
                       "   ·   EMOTION " + Pad(axes.Emotion) +
                       "   ·   INVENTION " + Pad(axes.Invention),
                Notice = notice,
            };
        }

        /// <summary>Her <c>walkTrailMarkup</c>, flattened to one line for a panel.</summary>
        public static string WalkTrail(MuseumJourney journey)
        {
            if (journey == null) return string.Empty;
            var parts = new List<string>();
            foreach (var art in journey.Session.VisitedArtworks) parts.Add("Stopped at " + art.Title);
            foreach (var q in journey.Session.AskedQuestions) parts.Add("Asked “" + q + "”");
            return parts.Count == 0
                ? "No stop, no question — the salon has only that to read."
                : string.Join("   ·   ", parts.ToArray());
        }

        static string Pad(int value) => value < 10 ? "0" + value : value.ToString();
    }
}
