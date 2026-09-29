using System.Linq;
using MusePico.Dialogue;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The conversation with SplatPortal's painters: which room's questions are offered, what the
    /// panel says in each phase, and that only the two painters standing there are asked.
    /// </summary>
    public class RoomTalkTests
    {
        [Test]
        public void Every_world_in_SplatPortal_gets_its_own_three_questions()
        {
            // The world names exactly as they stand in the scene.
            var worlds = new[]
            {
                ("World A - Buddha Hall (Chisel)", RoomTalk.BuddhaHall),
                ("World B - Van Gogh Gallery (Chisel)", RoomTalk.VanGoghGallery),
                ("World C - Small Temple (Chisel)", RoomTalk.SmallTemple),
                ("World D - Garden Courtyard (Chisel)", RoomTalk.GardenCourtyard),
            };
            foreach (var (name, expected) in worlds)
            {
                var room = RoomTalk.ForWorld(name);
                Assert.AreSame(expected, room, name);
                Assert.AreEqual(3, room.Questions.Count, name);
                Assert.IsTrue(room.Questions.All(q => q.EndsWith("?")), name);
                Assert.AreEqual(3, room.Questions.Distinct().Count(), name + ": repeated question");
            }
            Assert.AreSame(RoomTalk.Anywhere, RoomTalk.ForWorld("Something Else"));
            Assert.AreSame(RoomTalk.Anywhere, RoomTalk.ForWorld(null));
        }

        [Test]
        public void No_two_rooms_offer_the_same_question()
        {
            var all = RoomTalk.All.SelectMany(r => r.Questions).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count());
        }

        [Test]
        public void Choosing_offers_the_three_questions_and_speak_and_ask_waits_for_a_question()
        {
            var p = RoomTalk.Panel(RoomTalk.BuddhaHall, "Claude Monet", new[] { "Claude Monet", "Pablo Picasso" },
                                   "", RoomTalk.Phase.Choosing, "", desktop: true);
            Assert.AreEqual(4, p.Choices.Count, "three questions and speak");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(RoomTalk.BuddhaHall.Questions[i], p.Choices[i].Label);
                Assert.AreEqual(i, RoomTalk.QuestionIndex(p.Choices[i].Id), "choice id must round-trip to its question");
            }
            Assert.AreEqual(RoomTalk.SpeakId, p.Choices[3].Id);
            Assert.AreEqual(-1, RoomTalk.QuestionIndex(RoomTalk.SpeakId));
            Assert.IsFalse(p.ActionEnabled, "ASK with an empty question");
            Assert.AreEqual("Ask Monet and Picasso about the Buddha Hall", p.Heading);
            Assert.IsNotEmpty(p.Back, "there must be a way out");
            // The fluff that made it a wall (Saul, 29 Sep): none of it comes back.
            Assert.IsEmpty(p.Marker); Assert.IsEmpty(p.Eyebrow); Assert.IsEmpty(p.Notice);
            Assert.IsEmpty(p.Lede, "no invitation paragraph"); Assert.IsEmpty(p.Hint, "no standing hint");
        }

        [Test]
        public void With_a_question_ASK_is_enabled_and_it_is_the_heading()
        {
            var q = "Why paint a sky that seems to move?";
            var p = RoomTalk.Panel(RoomTalk.VanGoghGallery, "Pablo Picasso", new[] { "Pablo Picasso", "Claude Monet" },
                                   q, RoomTalk.Phase.Choosing, "", desktop: false);
            Assert.IsTrue(p.ActionEnabled);
            StringAssert.Contains(q, p.Heading);
            Assert.IsTrue(p.Choices[1].Selected, "the picked question is shown as picked");
        }

        [Test]
        public void While_busy_there_is_nothing_to_press_but_close()
        {
            foreach (var phase in new[] { RoomTalk.Phase.Listening, RoomTalk.Phase.Transcribing,
                                          RoomTalk.Phase.Asking, RoomTalk.Phase.Answering })
            {
                var p = RoomTalk.Panel(RoomTalk.SmallTemple, "Claude Monet", new[] { "Claude Monet", "Pablo Picasso" },
                                       "What is the dragon screen guarding?", phase, "", desktop: true);
                Assert.AreEqual(0, p.Choices.Count, phase + ": choices while busy would start a second ask");
                Assert.IsFalse(p.ActionEnabled, phase.ToString());
                Assert.IsNotEmpty(p.Back, phase + ": no way to stop");
            }
        }

        [Test]
        public void The_panel_gets_out_of_the_way_while_the_painters_think_and_answer()
        {
            Assert.IsTrue(RoomTalk.PanelVisible(RoomTalk.Phase.Choosing));
            Assert.IsTrue(RoomTalk.PanelVisible(RoomTalk.Phase.Listening), "the mic level shows on it");
            Assert.IsTrue(RoomTalk.PanelVisible(RoomTalk.Phase.Answered), "comes back to ask another");
            Assert.IsTrue(RoomTalk.PanelVisible(RoomTalk.Phase.Failed), "the error has to be readable");
            Assert.IsFalse(RoomTalk.PanelVisible(RoomTalk.Phase.Asking));
            Assert.IsFalse(RoomTalk.PanelVisible(RoomTalk.Phase.Answering));
        }

        [Test]
        public void Every_reply_bubble_says_it_is_an_AI_reading()
        {
            StringAssert.Contains("AI READING", RoomTalk.BubbleName("Claude Monet"));
            StringAssert.StartsWith("Claude Monet", RoomTalk.BubbleName("Claude Monet"));
        }

        [Test]
        public void Consecutive_lines_never_repeat_a_talk_gesture_and_stay_in_the_controller()
        {
            for (int i = 0; i < 12; i++)
            {
                int a = RoomTalk.TalkStyleFor(i), b = RoomTalk.TalkStyleFor(i + 1);
                Assert.AreNotEqual(a, b, $"lines {i} and {i + 1}");
                Assert.That(a, Is.InRange(0, 7), "Painter.controller has TalkStyle 0-7");
            }
        }

        [Test]
        public void Exactly_the_two_painters_are_asked_with_no_third_voice()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Dialogue/masters.json");
            Assert.IsNotNull(json, "masters.json");
            var roster = MasterRoster.Parse(json.text);

            var exact = MasterRoster.SelectExactly(roster, new[] { "picasso", "monet" });
            CollectionAssert.AreEqual(new[] { "picasso", "monet" }, exact.Select(m => m.id).ToArray(), "order kept, clicked painter first");

            // Control: the journey's selection tops up to three, which is what must NOT happen here.
            Assert.AreEqual(3, MasterRoster.Select(roster, new[] { "picasso", "monet" }).Count);

            Assert.AreEqual(1, MasterRoster.SelectExactly(roster, new[] { "monet", "nobody" }).Count, "unknown ids dropped");
            Assert.AreEqual(1, MasterRoster.SelectExactly(roster, new[] { "monet", "MONET" }).Count, "duplicates dropped");
        }

        [Test]
        public void Both_painters_have_their_MiniMax_voices()
        {
            var roster = MasterRoster.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Dialogue/masters.json").text);
            Assert.AreEqual("English_MaturePartner", MasterRoster.Find(roster, "monet").voiceId);
            Assert.AreEqual("English_Debator", MasterRoster.Find(roster, "picasso").voiceId);
        }
    }
}
