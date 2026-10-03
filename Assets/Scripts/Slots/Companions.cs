using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>The six masters of her Company stage, in her row order.</summary>
    public static class Masters
    {
        public const string Monet = "monet", VanGogh = "van_gogh", Socrates = "socrates",
                            Frida = "frida_kahlo", Hilma = "hilma_af_klint", Morisot = "berthe_morisot";

        public static readonly IReadOnlyList<string> Row = new[] { Monet, VanGogh, Socrates, Frida, Hilma, Morisot };

        /// <summary>Her demo preset: Monet, Van Gogh, Socrates.</summary>
        public static readonly IReadOnlyList<string> DefaultTrio = new[] { Monet, VanGogh, Socrates };

        public static string Name(string id) => id switch
        {
            Monet => "Claude Monet", VanGogh => "Vincent van Gogh", Socrates => "Socrates",
            Frida => "Frida Kahlo", Hilma => "Hilma af Klint", Morisot => "Berthe Morisot", _ => id,
        };

        /// <summary>Her speaker colours (§3.4). The three she did not colour have none yet (Q4).</summary>
        public static string Colour(string id) => id switch
        {
            Monet => "#5f9c92", VanGogh => "#c4952f", Socrates => "#7b7266", _ => "#8a8580",
        };

        /// <summary>
        /// Her fixed order, "observe (Monet) -> feel (Van Gogh) -> question (Socrates)". Frida, Hilma and
        /// Morisot have no axis yet (open question Q4); until it is answered they follow the trio in the
        /// order they were invited.
        /// </summary>
        public static int Rank(string id) => id switch { Monet => 0, VanGogh => 1, Socrates => 2, _ => 3 };
    }

    /// <summary>
    /// Her Company stage (2.2): "six standees in a row; ray-select 1-3". Selecting toggles an
    /// invitation; a fourth is refused ("with visible and audible feedback" - the scene's job, on
    /// <see cref="Result.Refused"/>). Pure.
    /// </summary>
    public sealed class Invitation
    {
        public const int Min = 1, Max = 3;

        public enum Result { Added, Removed, Refused, Unknown }

        readonly List<string> _chosen = new List<string>();
        readonly HashSet<string> _known;

        public Invitation(IEnumerable<string> row = null) { _known = new HashSet<string>(row ?? Masters.Row); }

        /// <summary>In the order they were invited.</summary>
        public IReadOnlyList<string> Chosen => _chosen;
        public bool CanProceed => _chosen.Count >= Min;
        public bool IsChosen(string id) => _chosen.Contains(id);

        public Result Toggle(string id)
        {
            if (id == null || !_known.Contains(id)) return Result.Unknown;
            if (_chosen.Remove(id)) return Result.Removed;
            if (_chosen.Count >= Max) return Result.Refused;
            _chosen.Add(id);
            return Result.Added;
        }

        /// <summary>The speaking order: her fixed ranks, ties broken by invitation order.</summary>
        public IReadOnlyList<string> SpeakingOrder()
        {
            var order = new List<string>(_chosen);
            var invited = new Dictionary<string, int>();
            for (var i = 0; i < _chosen.Count; i++) invited[_chosen[i]] = i;
            order.Sort((a, b) => Masters.Rank(a) != Masters.Rank(b) ? Masters.Rank(a).CompareTo(Masters.Rank(b))
                                                                     : invited[a].CompareTo(invited[b]));
            return order;
        }
    }

    /// <summary>
    /// Where the companions stand (§3.4): "off the main path, 1.5-2.2 m from the player; always within
    /// ±60° of forward, never behind". The three marks follow her chapter diagrams (diagram-C): the
    /// first speaker on the left, the second near on the right, the third further off ahead-right.
    /// Nobody stands on the path ahead (±20°), so nobody blocks the work. Pure: bearings are degrees
    /// from the visitor's forward, positive to the right.
    /// </summary>
    public static class CompanionMarks
    {
        public const float MinDistance = 1.5f, MaxDistance = 2.2f, MaxBearing = 60f, PathHalfWidth = 20f;

        public readonly struct Mark
        {
            public readonly float Bearing, Distance;
            public Mark(float bearing, float distance) { Bearing = bearing; Distance = distance; }
            public override string ToString() => Bearing + "° " + Distance + " m";
        }

        // The third at 22° / 2.15 m, not 26° / 2.1: a blind review saw the second and third boards
        // nearly coplanar and touching from the visitor's eye (about 1° apart at their edges); now ~5°.
        static readonly Mark[] Slots = { new Mark(-48f, 1.7f), new Mark(46f, 1.6f), new Mark(22f, 2.15f) };

        /// <summary>The mark for the companion who speaks <paramref name="order"/>th (0-based).</summary>
        public static Mark For(int order) => Slots[Math.Max(0, Math.Min(Slots.Length - 1, order))];

        /// <summary>
        /// A mark the scene found blocked (a wall, the path) is retried nearer, then mirrored, staying
        /// inside her limits. Returns the candidates in the order to try.
        /// </summary>
        public static IEnumerable<Mark> Candidates(int order)
        {
            var m = For(order);
            yield return m;
            yield return new Mark(m.Bearing, MinDistance);
            yield return new Mark(-m.Bearing, m.Distance);
            yield return new Mark(-m.Bearing, MinDistance);
        }

        public static bool Allowed(Mark m) =>
            Math.Abs(m.Bearing) <= MaxBearing && Math.Abs(m.Bearing) >= PathHalfWidth &&
            m.Distance >= MinDistance - 1e-4f && m.Distance <= MaxDistance + 1e-4f;
    }

    /// <summary>
    /// Her turn-taking (§3.4): one speaker at a time in the fixed order; A advances, or it advances on
    /// its own 2 s after a line ends; and the gaze gate: "the next speaker waits until the player's
    /// gaze is back within 60°, so nobody speaks from behind". Pure: the scene reports when a line
    /// has finished playing and where the next speaker stands relative to the gaze.
    /// </summary>
    public sealed class TurnTaking
    {
        public const float AutoAdvanceSeconds = 2f;
        public const float GazeGateDegrees = 60f;

        public enum Phase { Idle, WaitingForGaze, Speaking, Pausing, Done }

        readonly List<string> _order;
        float _pause;

        public TurnTaking(IReadOnlyList<string> order) { _order = new List<string>(order ?? Array.Empty<string>()); }

        public Phase Current { get; private set; } = Phase.Idle;
        public int Index { get; private set; } = -1;
        public string Speaker => Index >= 0 && Index < _order.Count ? _order[Index] : null;
        public IReadOnlyList<string> Order => _order;

        public event Action<string> Started;
        public event Action<string> Ended;
        public event Action Finished;

        public void Begin()
        {
            if (Current != Phase.Idle) return;
            Index = 0;
            Current = _order.Count == 0 ? Phase.Done : Phase.WaitingForGaze;
            if (Current == Phase.Done) Finished?.Invoke();
        }

        /// <summary>
        /// Each frame. <paramref name="speakerAngleFromGaze"/> is the angle between the visitor's gaze
        /// and the speaker who is up next (or speaking), degrees.
        /// </summary>
        public void Tick(float dt, float speakerAngleFromGaze)
        {
            switch (Current)
            {
                case Phase.WaitingForGaze:
                    if (speakerAngleFromGaze <= GazeGateDegrees)
                    {
                        Current = Phase.Speaking;
                        Started?.Invoke(Speaker);
                    }
                    break;
                case Phase.Pausing:
                    _pause -= dt > 0f ? dt : 0f;
                    if (_pause <= 0f) Next();
                    break;
            }
        }

        /// <summary>The current line has finished playing.</summary>
        public void LineFinished()
        {
            if (Current != Phase.Speaking) return;
            Ended?.Invoke(Speaker);
            Current = Phase.Pausing;
            _pause = AutoAdvanceSeconds;
        }

        /// <summary>A: skip the pause (or the rest of the current line) and go to the next speaker.</summary>
        public void Advance()
        {
            if (Current == Phase.Speaking) { Ended?.Invoke(Speaker); Next(); }
            else if (Current == Phase.Pausing) Next();
        }

        void Next()
        {
            Index++;
            if (Index >= _order.Count)
            {
                Current = Phase.Done;
                Finished?.Invoke();
                return;
            }
            Current = Phase.WaitingForGaze;
        }
    }

    /// <summary>
    /// The Palace card fallback (2.4): "two exhibit cards that are ray-selected, then flip and glow;
    /// the reason step is unchanged". Selecting one turns it face up and glowing and turns the other
    /// back; selecting the face-up one again turns it back. The choice goes through a ChoiceConfirm.
    /// Pure.
    /// </summary>
    public sealed class CardChoice
    {
        readonly string[] _ids;

        public CardChoice(params string[] ids)
        {
            if (ids == null || ids.Length == 0) throw new ArgumentException("no cards");
            _ids = ids;
        }

        public ChoiceConfirm Choice { get; } = new ChoiceConfirm();
        public int FaceUp { get; private set; } = -1;
        public IReadOnlyList<string> Ids => _ids;

        /// <summary>(card, faceUp) for every card whose face changed.</summary>
        public event Action<int, bool> Flipped;

        public bool Select(int card)
        {
            if (card < 0 || card >= _ids.Length || Choice.IsConfirmed) return false;
            var was = FaceUp;
            if (was == card)
            {
                FaceUp = -1;
                Choice.Clear();
                Flipped?.Invoke(card, false);
                return true;
            }
            FaceUp = card;
            if (was >= 0) Flipped?.Invoke(was, false);
            Flipped?.Invoke(card, true);
            Choice.Clear();
            Choice.Make(_ids[card]);
            return true;
        }

        public bool Confirm() => Choice.Confirm();

        /// <summary>B within the undo bar: the card turns back.</summary>
        public bool Redo()
        {
            if (FaceUp < 0 || !Choice.Redo()) return false;
            var c = FaceUp;
            FaceUp = -1;
            Flipped?.Invoke(c, false);
            return true;
        }
    }
}
