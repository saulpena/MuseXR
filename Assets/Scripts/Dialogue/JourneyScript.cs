using System.Collections.Generic;

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

    /// <summary>The ending the manifesto reads out, and whether it is the real one.</summary>
    public readonly struct ClosingEnding
    {
        public readonly string Title;
        public readonly string Copy;

        /// <summary>False when this is <see cref="JourneyScript.FallbackTitle"/> — no synthesis happened.</summary>
        public readonly bool FromRoundtable;

        /// <summary>False when the synthesis exists but no live model produced it.</summary>
        public readonly bool Live;

        public ClosingEnding(string title, string copy, bool fromRoundtable, bool live)
        {
            Title = title; Copy = copy; FromRoundtable = fromRoundtable; Live = live;
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
        public static ClosingEnding Ending(string worldTitle, string synthesis, bool live)
        {
            var title = (worldTitle ?? string.Empty).Trim();
            var copy = (synthesis ?? string.Empty).Trim();
            if (title.Length == 0 || copy.Length == 0)
                return new ClosingEnding(FallbackTitle, FallbackCopy, false, false);
            return new ClosingEnding(title, copy, true, live);
        }

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

        /// <summary>
        /// The same popup with a keyboard. <paramref name="typing"/> is true when there is no
        /// headset, so the question is typed rather than dictated: the box shows a caret and the
        /// hint names the keys instead of the grip.
        /// </summary>
        public static StagePanel AskDialogue(
            MasterLens companion, string artworkTitle, string question, string replies, bool typing)
        {
            var panel = AskDialogueFor(companion, artworkTitle, question, replies);
            if (!typing) return panel;

            var hasQuestion = !string.IsNullOrWhiteSpace(question);
            // No quotes while typing: a caret drawn inside the closing quote read as a stray glyph
            // (independent review, 27 Sep).
            panel.Heading = (question ?? string.Empty) + "|";
            panel.Hint = hasQuestion
                ? "TYPE TO CHANGE IT · ENTER TO ASK · ESC TO GO BACK"
                : "TYPE YOUR QUESTION · ENTER TO ASK · ESC TO GO BACK";
            return panel;
        }

        /// <summary>
        /// Her artwork popup: clicking a work (or an object standing in the world) opens it. One
        /// master speaks to you about the work, the live readings of all three arrive under that
        /// line, and you answer with one of her three fixed choices. After answering, the master
        /// who champions that answer reacts and the only way on is back to the walk.
        /// </summary>
        /// <param name="line">The scripted line being shown: the opening, or the reaction once answered.</param>
        /// <param name="live">The three live readings, a status while they load, or empty.</param>
        public static StagePanel ArtDialogue(
            MasterLens speaker, string title, string line, string live, bool answered)
        {
            var name = speaker == null ? "YOUR COMPANION" : speaker.fullName.ToUpperInvariant();

            var choices = new List<StageChoice>();
            if (!answered)
                for (var i = 0; i < ArtworkDialogue.Choices.Count; i++)
                {
                    var c = ArtworkDialogue.Choices[i];
                    choices.Add(new StageChoice(c.Id, "0" + (i + 1) + "  " + c.Label));
                }

            return new StagePanel
            {
                // The eyebrow names whose SCRIPTED line is shown, and what they are doing — the live
                // readings under it carry their own speaker. A bare name over another master's
                // reading read as a contradiction, and the switch to the reactor looked unexplained.
                Marker = answered ? "IN REPLY TO YOUR ANSWER" : "ABOUT THIS WORK",
                Eyebrow = name + (answered ? " REPLIES" : " SPEAKS TO YOU"),
                Heading = string.IsNullOrEmpty(title) ? "This work" : title,
                Lede = string.IsNullOrEmpty(live) ? line ?? string.Empty : (line ?? string.Empty) + "\n\n" + live,
                Choices = choices,
                // Her × closes it unanswered; the panel draws a back plate only beside an action,
                // so the way out before answering IS the action, kept quiet.
                Action = answered ? "CONTINUE THE WALK →" : "NOT NOW",
                ActionIsPrimary = answered,
                Hint = answered ? string.Empty : "HOW DOES IT LEAVE YOU? POINT AT AN ANSWER",
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
                Eyebrow = "02 / INVITE UP TO THREE MINDS",
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
            var choices = new List<StageChoice>();
            foreach (var art in journey.Session.VisitedArtworks)
                choices.Add(new StageChoice("stop", "Stopped at " + art.Title));
            foreach (var q in journey.Session.AskedQuestions)
                choices.Add(new StageChoice("asked", "Asked “" + q + "”"));

            return new StagePanel
            {
                Eyebrow = "05 / SEVEN MINDS. ONE EMPTY SEAT.",
                Heading = "The Salon Outside Time",
                Lede = choices.Count > 0
                    ? "Your walk is entering the record."
                    : "You arrive with an empty record — no stop, no question. The salon will " +
                      "have only that to read.",
                Choices = choices,
                Action = "OPEN THE SALON →",
                Hint = "PULL THE TRIGGER TO BE READ BACK",
                Notice = InterpretationNotice,
            };
        }

        static StagePanel Roundtable(MuseumJourney journey, ClosingEnding ending)
        {
            var ready = ending.FromRoundtable;
            return new StagePanel
            {
                Eyebrow = "06 / THE CLOSING ROUNDTABLE",
                Heading = ready ? ending.Title : "The masters read back your walk",
                Lede = ready ? ending.Copy : WalkTrail(journey),
                Action = "FACE THE CONTRADICTION →",
                ActionEnabled = true,
                Hint = ready ? WalkTrail(journey) : "LISTENING TO THE SALON…",
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
