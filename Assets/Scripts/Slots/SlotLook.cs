using System;

namespace MuseXR.Slots
{
    /// <summary>Her three slot states (chatplan §3.2, her UI 4.3).</summary>
    public enum SlotState { Empty, Aligned, Placed }

    /// <summary>
    /// How a state is drawn. Her rule is "shape, text and haptics, never colour alone", so each
    /// state has its own outline: a dashed ring, a solid ring with a halo, a filled disc.
    /// </summary>
    public enum SlotShape { Dashed, SolidHalo, Filled }

    public enum HapticPulse { None, Light, Confirm }

    /// <summary>The four chapters that leave a choice by hand.</summary>
    public enum Chapter { Palace, Grotto, VanGogh, Monet }

    /// <summary>Each chapter's confirm sound. The same sound replays when the piece appears in Your world.</summary>
    public enum ChapterSound { BronzeBell, StoneChime, Wood, Water }

    /// <summary>Everything a slot shows in one state: outline, the word in the ring, title, caption, pulse.</summary>
    public readonly struct SlotLook
    {
        public readonly SlotShape Shape;
        /// <summary>The word inside the ring on her card: "Empty", "Release", "Saved".</summary>
        public readonly string Badge;
        public readonly string Title;
        public readonly string Caption;
        public readonly HapticPulse Haptic;

        public SlotLook(SlotShape shape, string badge, string title, string caption, HapticPulse haptic)
        {
            Shape = shape; Badge = badge; Title = title; Caption = caption; Haptic = haptic;
        }

        /// <summary>
        /// Her table, word for word where she gives words: Empty "Waiting · Hold Grip, place here";
        /// Aligned "Aligned" (snap preview); Placed "Placed · Undo 3s · B". The placed caption counts
        /// the undo bar down, and once it has run out names the two ways left: A keeps, lifting it
        /// out changes it ("B within 3s, or lift it back out to reselect").
        /// </summary>
        public static SlotLook For(SlotState state, float undoLeft = 0f) => state switch
        {
            SlotState.Aligned => new SlotLook(SlotShape.SolidHalo, "Release", "Aligned", "Release to place", HapticPulse.Light),
            SlotState.Placed => new SlotLook(SlotShape.Filled, "Saved", "Placed",
                undoLeft > 0f ? "Undo " + (int)Math.Ceiling(undoLeft) + "s · B" : "A keep · lift out to change",
                HapticPulse.Confirm),
            _ => new SlotLook(SlotShape.Dashed, "Empty", "Waiting", "Hold Grip, place here", HapticPulse.None),
        };
    }

    public static class SlotRules
    {
        /// <summary>Her snap radius: 12 cm, from the piece's base to the slot's centre.</summary>
        public const float SnapRadius = 0.12f;

        /// <summary>Her undo bar.</summary>
        public const float UndoSeconds = 3f;

        /// <summary>Haptics: a light tick on alignment, a firmer one on placing.</summary>
        public const float LightAmplitude = 0.2f, LightSeconds = 0.03f;
        public const float ConfirmAmplitude = 0.6f, ConfirmSeconds = 0.12f;

        public static ChapterSound SoundOf(Chapter chapter) => chapter switch
        {
            Chapter.Palace => ChapterSound.BronzeBell,
            Chapter.Grotto => ChapterSound.StoneChime,
            Chapter.VanGogh => ChapterSound.Wood,
            _ => ChapterSound.Water,
        };
    }
}
