using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>
    /// The ten acts of MUSE∞, in order. Names and order are muse-infinity's <c>STAGES</c> array
    /// (<c>app.js:81</c>) — do not reorder, several stages read state the earlier ones wrote.
    /// </summary>
    public enum Stage
    {
        Threshold = 0,
        LifeQuestion = 1,
        CompanionSelection = 2,
        AiCuration = 3,
        WorldExploration = 4,
        Summoning = 5,
        Roundtable = 6,
        Decision = 7,
        WorldTransformation = 8,
        Manifesto = 9,
    }

    /// <summary>
    /// The journey's state machine: which act the visitor is in, and what they have accumulated.
    ///
    /// Pure C# — no UnityEngine — so the whole arc can be walked in EditMode with no scene, no
    /// headset and no key. The scene layer subscribes to <see cref="StageChanged"/> and shows or
    /// hides one root per stage; it never decides what comes next.
    ///
    /// <b>One scene, ten stage roots — not ten scenes.</b> Loading a scene would tear down the splat
    /// world and the XR rig, and the rig is what the visitor is standing in.
    ///
    /// <b>Her rule, ported:</b> <c>setStage</c> begins with <c>narrator.stop()</c> — a stage change
    /// always interrupts narration. <see cref="StageChanged"/> fires after the change so a listener
    /// can do exactly that, and <see cref="LeavingStage"/> fires before it.
    /// </summary>
    public sealed class MuseumJourney
    {
        public Stage Current { get; private set; } = Stage.Threshold;

        /// <summary>Set once ENTER YOUR WORLD is taken at stage 09; the machine stops driving stages.</summary>
        public bool HandedOff { get; private set; }

        public VisitSession Session { get; } = new VisitSession();
        public ExhibitionSpine Spine { get; } = new ExhibitionSpine();

        /// <summary>The visitor's own question, from stage 01. Every later stage reads it.</summary>
        public string Question { get; private set; } = string.Empty;

        /// <summary>Master ids invited at stage 02, in the order chosen. At most three.</summary>
        public IReadOnlyList<string> InvitedMasterIds => _invited;
        /// <summary>
        /// Her opening company, pre-invited: `selectedCompanions: new Set(["monet","van_gogh",
        /// "socrates"])`. Starting empty made the forward button dead on arrival and told the
        /// visitor the screen was broken before they had touched anything.
        /// </summary>
        public static readonly string[] DefaultCompany = { "monet", "van_gogh", "socrates" };

        readonly List<string> _invited = new List<string>(DefaultCompany);

        /// <summary>Fires before the change, with the stage being left.</summary>
        public event Action<Stage> LeavingStage;

        /// <summary>Fires after the change: (from, to).</summary>
        public event Action<Stage, Stage> StageChanged;

        public const int MaxCompanions = 3;

        /// <summary>The next stage, or null at the end of the arc.</summary>
        public Stage? Next => Current == Stage.Manifesto ? (Stage?)null : Current + 1;

        /// <summary>
        /// Move to the next act. Returns false at stage 09, which has no successor — the two plates
        /// there are <see cref="HandOff"/> and <see cref="Reset"/>, not Advance.
        /// </summary>
        public bool Advance()
        {
            var next = Next;
            if (next == null) return false;
            GoTo(next.Value);
            return true;
        }

        /// <summary>
        /// The stage a visitor may step back to, or null when there is none.
        ///
        /// <b>Only the opening act is reversible.</b> Stages 00-03 are a form being filled in —
        /// your question, your company, the reading of them — and changing your mind there costs
        /// nothing. From the gallery onwards the journey has consequences: the walk is recorded,
        /// the salon reads it back, the choice scores the axes and the museum rewrites itself.
        /// Stepping back through those would either erase a visit that already happened or let the
        /// same walk be read twice, and neither is a thing her build can do either.
        /// </summary>
        public Stage? Previous =>
            Current == Stage.LifeQuestion || Current == Stage.CompanionSelection ||
            Current == Stage.AiCuration
                ? (Stage?)(Current - 1)
                : null;

        /// <summary>
        /// Step back one stage. Returns false where there is nothing to go back to.
        ///
        /// Nothing is cleared on the way: a visitor who returns to the question stage finds their
        /// question still in the box and their companions still invited, which is the point of
        /// going back rather than starting again. <see cref="Reset"/> is how you start again.
        /// </summary>
        public bool Back()
        {
            var previous = Previous;
            if (previous == null) return false;
            GoTo(previous.Value);
            return true;
        }

        /// <summary>
        /// Jump to a stage. Used by the scene for debugging and by stage 09's ENTER AGAIN; the
        /// journey itself only ever calls <see cref="Advance"/>, so the arc cannot be skipped by
        /// accident.
        /// </summary>
        public void GoTo(Stage stage)
        {
            if (stage == Current) return;
            var from = Current;
            LeavingStage?.Invoke(from);
            Current = stage;
            StageChanged?.Invoke(from, stage);
        }

        public void SetQuestion(string question)
        {
            Question = VisitSession.Clamp(question, VisitSession.QuestionChars);
        }

        /// <summary>
        /// Invite or withdraw a master. Hers caps at three and silently ignores a fourth
        /// (<c>app.js:562</c>); withdrawing is a second click on the same one.
        /// </summary>
        public bool ToggleCompanion(string masterId)
        {
            if (string.IsNullOrWhiteSpace(masterId)) return false;
            var id = masterId.Trim();

            var at = _invited.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { _invited.RemoveAt(at); return false; }

            if (_invited.Count >= MaxCompanions) return false;
            _invited.Add(id);
            return true;
        }

        public bool IsInvited(string masterId) =>
            _invited.FindIndex(x => string.Equals(x, masterId, StringComparison.OrdinalIgnoreCase)) >= 0;

        /// <summary>
        /// Stage 09's ENTER YOUR WORLD: leave the journey and hand the visitor the final world to
        /// walk freely. The machine stops driving stages; the digest is left intact so the manifesto
        /// stays readable.
        /// </summary>
        public void HandOff()
        {
            if (Current != Stage.Manifesto) return;
            HandedOff = true;
        }

        /// <summary>
        /// Stage 09's ENTER AGAIN. Clears the digest, the axes, the question, the invitations and
        /// the spine, and returns to the threshold — so the next visitor does not inherit a
        /// stranger's walk.
        /// </summary>
        public void Reset()
        {
            Session.Reset();
            Spine.Reset();
            _invited.Clear();
            _invited.AddRange(DefaultCompany);
            Question = string.Empty;
            HandedOff = false;
            GoTo(Stage.Threshold);
        }
    }
}
