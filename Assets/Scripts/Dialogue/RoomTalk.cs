using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>One room of SplatPortal: what the masters are told it is, and what to ask about it.</summary>
    public sealed class RoomTopic
    {
        public readonly string Id;
        /// <summary>Short name for the panel, "the Buddha Hall".</summary>
        public readonly string Name;
        /// <summary>
        /// What the masters are told they are looking at. Goes into the prompt as the "artwork in
        /// focus", so it describes only what is actually there: the prompt tells the model to
        /// describe it accurately, and an invented detail becomes a confident wrong reading.
        /// </summary>
        public readonly string Focus;
        /// <summary>Exactly three ready questions.</summary>
        public readonly IReadOnlyList<string> Questions;

        public RoomTopic(string id, string name, string focus, params string[] questions)
        {
            Id = id; Name = name; Focus = focus; Questions = questions;
        }
    }

    /// <summary>
    /// The conversation with the two painters in SplatPortal: per-room questions and the panel.
    ///
    /// <b>Why authored questions, not generated ones.</b> muse-infinity has no per-room question
    /// list - clicking a master pre-fills one templated question ("What do you see in X?"), and
    /// the journey here does the same. Three questions per room, written against what each room
    /// actually shows, give the visitor something to pick in one click with no model call (and no
    /// cost) before they have asked anything. The first of each set keeps the journey's
    /// "What do you see in ..." form.
    ///
    /// Engine-free, so the panel copy is tested in EditMode.
    /// </summary>
    public static class RoomTalk
    {
        public static readonly RoomTopic BuddhaHall = new RoomTopic("buddha-hall", "the Buddha Hall",
            "the Great Buddha - a gilded seated Buddha on a lotus throne, about twelve metres tall, " +
            "in a temple hall of red lacquered pillars, carved gold screens and hanging scrolls",
            "What do you see in the Great Buddha?",
            "Why build a figure of stillness this large?",
            "How would you paint the light in this hall?");

        public static readonly RoomTopic VanGoghGallery = new RoomTopic("vangogh-gallery", "the Van Gogh Gallery",
            "a gallery with deep-blue textured walls hung with framed wheat-field and cypress " +
            "landscapes, under a painted starry ceiling with cream drapes",
            "What do you see in these wheat fields and cypresses?",
            "Why paint a sky that seems to move?",
            "What would you hang on these blue walls?");

        public static readonly RoomTopic SmallTemple = new RoomTopic("small-temple", "the Small Temple",
            "a small temple hall of pillars, with a painted dragon screen on its far wall",
            "What is the dragon screen guarding?",
            "Is a painted dragon more real than a living one?",
            "What does this temple ask of the people who enter it?");

        public static readonly RoomTopic GardenCourtyard = new RoomTopic("garden-courtyard", "the Garden Courtyard",
            "a garden courtyard enclosed by glass walls: clipped hedges and a flowering tree around " +
            "a long reflecting pool on stone paving",
            "What do you see in the reflecting pool?",
            "Can a garden be a painting?",
            "How would this courtyard change from dawn to dusk?");

        public static readonly RoomTopic Anywhere = new RoomTopic("anywhere", "this place",
            "the room the visitor is standing in",
            "What should I notice in this place?",
            "What would you paint here?",
            "What is this room trying to make me feel?");

        public static readonly IReadOnlyList<RoomTopic> All =
            new[] { BuddhaHall, VanGoghGallery, SmallTemple, GardenCourtyard };

        /// <summary>The room for a world's scene name ("World A - Buddha Hall (Chisel)").</summary>
        public static RoomTopic ForWorld(string worldName)
        {
            var n = (worldName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("buddha")) return BuddhaHall;
            if (n.Contains("van gogh") || n.Contains("vangogh") || n.Contains("gallery")) return VanGoghGallery;
            if (n.Contains("temple")) return SmallTemple;
            if (n.Contains("garden")) return GardenCourtyard;
            return Anywhere;
        }

        public const string QuestionIdPrefix = "room-q-";
        public const string SpeakId = "speak";

        /// <summary>Choice id → index of the ready question it asks, or -1.</summary>
        public static int QuestionIndex(string choiceId)
        {
            if (choiceId == null || !choiceId.StartsWith(QuestionIdPrefix, StringComparison.Ordinal)) return -1;
            return int.TryParse(choiceId.Substring(QuestionIdPrefix.Length), out var i) ? i : -1;
        }

        /// <summary>What the conversation is doing, for the panel.</summary>
        public enum Phase { Choosing, Listening, Transcribing, Asking, Answering, Answered, Failed }

        /// <summary>
        /// The panel: three ready questions and a "speak your own", the box holding the question,
        /// ASK, and CLOSE. Built from the journey's <see cref="StagePanel"/>, so it looks and points
        /// exactly like the ask form in Museum.unity.
        /// </summary>
        /// <param name="asked">Full name of the painter who was clicked.</param>
        /// <param name="others">Full names of everyone who answers, the clicked one first.</param>
        public static StagePanel Panel(RoomTopic room, string asked, IReadOnlyList<string> others,
                                       string question, Phase phase, string status, bool desktop)
        {
            room ??= Anywhere;
            var busy = phase == Phase.Listening || phase == Phase.Transcribing ||
                       phase == Phase.Asking || phase == Phase.Answering;
            var hasQuestion = !string.IsNullOrWhiteSpace(question);

            var choices = new List<StageChoice>();
            if (!busy)
            {
                for (var i = 0; i < room.Questions.Count && i < 3; i++)
                    choices.Add(new StageChoice(QuestionIdPrefix + i, room.Questions[i],
                        selected: hasQuestion && question == room.Questions[i]));
                choices.Add(new StageChoice(SpeakId, "SPEAK YOUR OWN QUESTION"));
            }

            // Only what the visitor acts on: the question, the choices, ASK and CLOSE. The journey's
            // marker, eyebrow, invitation, hint and disclaimer made this a 2.1 m wall that hid the
            // room and the painters (Saul, 29 Sep 2026). The disclaimer now rides on each bubble.
            return new StagePanel
            {
                Heading = hasQuestion ? "“" + question + "”" : "Ask " + Answerers(others) + " about " + room.Name,
                Lede = status ?? string.Empty,
                Choices = choices,
                Action = busy ? string.Empty : "ASK",
                ActionEnabled = hasQuestion && !busy,
                ActionIsPrimary = true,
                Back = "← CLOSE",
                Hint = Hint(phase),
            };
        }

        /// <summary>Surnames only: "Monet and Picasso".</summary>
        static string Answerers(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "the painters";
            var shortNames = new List<string>();
            foreach (var n in names)
            {
                var parts = (n ?? string.Empty).Trim().Split(' ');
                shortNames.Add(parts[parts.Length - 1]);
            }
            return string.Join(" and ", shortNames);
        }

        /// <summary>A hint only while something is happening that the visitor must know about.</summary>
        static string Hint(Phase phase)
        {
            switch (phase)
            {
                case Phase.Listening: return "LISTENING · IT STOPS WHEN YOU PAUSE";
                case Phase.Transcribing: return "WRITING DOWN WHAT YOU SAID…";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The panel shows only while the visitor is choosing. Once they have asked it gets out of
        /// the way: it is body-locked, so it followed the visitor's head onto whichever painter they
        /// turned to watch. The replies live in the bubbles above the painters.
        /// </summary>
        public static bool PanelVisible(Phase phase) =>
            phase != Phase.Asking && phase != Phase.Answering;

        /// <summary>The name line of a reply bubble, carrying the disclaimer the panel used to.</summary>
        public static string BubbleName(string speaker) => (speaker ?? "") + " · AI READING";

        /// <summary>
        /// Painter.controller TalkStyle for the n-th line of a conversation. Rotates through the
        /// talks that read clearly at conversational distance (measured by the character package:
        /// Passionate, Hands open and Hand on hip read best; Chat, Right hand open and Listen are
        /// subtle), so consecutive lines never repeat a gesture.
        /// </summary>
        public static int TalkStyleFor(int lineIndex)
        {
            int[] styles = { 2, 6, 3, 0, 4 };   // Passionate, Hands open, Hand on hip, Talk, Left hand raised
            return styles[((lineIndex % styles.Length) + styles.Length) % styles.Length];
        }
    }
}
