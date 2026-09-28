using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>
    /// One chapter of the exhibition: a world, a title, and the wall hung in it.
    /// Mirrors an entry of muse-infinity's <c>config/exhibitionScenes.js</c>.
    /// </summary>
    public sealed class ExhibitionChapter
    {
        public string Id;
        public string Chapter;      // "01 / ARRIVAL"
        public string Title;        // "The Threshold Conservatory"
        public string WorldKey;     // maps to WorldCatalog, + SmallSuffix for the 500k asset

        /// <summary>Her <c>artist</c>: the presiding sensibility, shown beside the chapter number.</summary>
        public string Artist;

        /// <summary>
        /// The key this chapter's four artworks hang under in <c>config/sceneCollections.js</c>.
        ///
        /// <b>Stated, not derived.</b> Her collection keys and her scene ids are NOT the same
        /// strings — `water-and-light` against `garden-of-water-and-light`, `burning-sky` against
        /// `studio-of-the-burning-sky` — so any rule that tried to compute one from the other
        /// would work for three chapters and silently hang the wrong pictures in five.
        /// </summary>
        public string CollectionId;

        /// <summary>Her <c>prompt</c>: the question the room puts to the visitor.</summary>
        public string Prompt;
        public bool IsFinal;

        /// <summary>
        /// A world to show INSTEAD of <see cref="WorldKey"/>, because the real one has no 500k
        /// re-export and cannot ship. Null when the chapter has its own world.
        ///
        /// The real key is deliberately kept in <see cref="WorldKey"/> rather than overwritten, so
        /// that clearing this one line is the whole of the fix once the capture is re-exported.
        /// </summary>
        public string PlaceholderWorldKey;

        public bool IsPlaceholder => !string.IsNullOrEmpty(PlaceholderWorldKey);

        /// <summary>
        /// A world MuseXR has chosen for this chapter in place of hers — a design decision, not a
        /// stand-in for a missing export (that is <see cref="PlaceholderWorldKey"/>). Her key stays
        /// in <see cref="WorldKey"/> as the record of what her spine says; clearing this one line
        /// restores it. Null when the chapter uses her world.
        /// </summary>
        public string ChosenWorldKey;

        public bool IsChosen => !string.IsNullOrEmpty(ChosenWorldKey);

        /// <summary>What to actually load: our chosen world, else the placeholder, else hers.</summary>
        public string EffectiveWorldKey =>
            IsChosen ? ChosenWorldKey : IsPlaceholder ? PlaceholderWorldKey : WorldKey;

        public override string ToString() =>
            Chapter + " " + Title + (IsPlaceholder ? "  [placeholder: " + PlaceholderWorldKey + "]" : "");
    }

    /// <summary>
    /// The curated route through the museum.
    ///
    /// <b>The Living Gallery is eight rooms, not one.</b> This was missed in the first four
    /// revisions of the plan, which treated stage 04 as a single world cycled by a button.
    /// <c>exhibitionScenes.js</c> defines an ordered spine of eight chapters, each a different
    /// splat capture with its own name and its own wall, plus a ninth used only as the ending.
    /// <c>state.exhibitionSceneIndex</c> walks them in order.
    ///
    /// Pure C#: the ordering, the index and the final-scene rule are all testable with no scene.
    ///
    /// <b>Two worlds are not shippable yet.</b> <c>sunlit-palace-gardens</c> (06) and
    /// <c>yellow-polka-dot-infinity-room</c> (08) exist only at full resolution in
    /// <c>Assets/Worlds/</c> and have no 500k re-export, so <see cref="MissingAtSmallTier"/> names
    /// them rather than letting a chapter fail silently at load.
    /// </summary>
    public sealed class ExhibitionSpine
    {
        /// <summary>The eight chapters, in her order. Copied from <c>config/exhibitionScenes.js</c>.</summary>
        public static readonly IReadOnlyList<ExhibitionChapter> Chapters = new List<ExhibitionChapter>
        {
            // CHOSEN (Saul, 27 Sep 2026, after Skylar's brief): the opening stages — the question
            // and the choice of company — stand in the Forbidden-City courtyard (the scene's
            // homeWorldKey); "walking inside" is entering the exhibition, so chapter 01 is the
            // Hall of the Great Buddha. Chapter 02 is the Peach Garden / Heavenly Palace with the
            // Queen Mother of the West. Titles, prompts and artworks are still hers. Her worlds
            // stay in WorldKey, so clearing ChosenWorldKey is the revert.
            new ExhibitionChapter { Id = "threshold-conservatory", Chapter = "01 / ARRIVAL",
                Title = "The Threshold Conservatory", WorldKey = "grand-conservatory-with-lush-gardens",
                ChosenWorldKey = "empty-chinese-imperial-temple-hall", Artist = "A cross-temporal salon",
                Prompt = "What must become visible before an answer can begin?" , CollectionId = "threshold-conservatory" },
            new ExhibitionChapter { Id = "court-of-light", Chapter = "02 / QUESTION",
                Title = "The Court of Light", WorldKey = "elegant-floral-palace-interior",
                ChosenWorldKey = "celestial-peach-blossom-paradise", Artist = "Sigmund Freud",
                Prompt = "Which part of your question belongs to you, and which part was inherited?" , CollectionId = "court-of-light" },
            new ExhibitionChapter { Id = "garden-of-water-and-light", Chapter = "03 / PERCEPTION",
                Title = "The Garden of Water and Light", WorldKey = "enchanted-water-garden-sanctuary", Artist = "Claude Monet",
                Prompt = "Can a life change simply because attention becomes more precise?" , CollectionId = "water-and-light" },
            new ExhibitionChapter { Id = "sunset-frame-gallery", Chapter = "04 / INVENTION",
                Title = "The Sunset Frame Gallery", WorldKey = "dreamlike-coastal-villa-gardens", Artist = "Pablo Picasso",
                Prompt = "What changes when the same truth is seen from more than one angle?" , CollectionId = "sunset-frames" },
            new ExhibitionChapter { Id = "studio-of-the-burning-sky", Chapter = "05 / INTENSITY",
                Title = "The Studio of the Burning Sky", WorldKey = "van-gogh-inspired-gallery-interior", Artist = "Vincent van Gogh",
                Prompt = "Can struggle deepen attention without becoming the source of meaning itself?" , CollectionId = "burning-sky" },
            // PLACEHOLDER. Her world is `sunlit-palace-gardens` ("A palace garden bathed in morning
            // light — an open, grand Baroque courtyard"), which exists and works in her build at full
            // resolution but has no 500k re-export here. `enchanted-palace-garden` stands in: it is
            // the one capture in WorldSet.Small that her spine does not use, and it is also a palace
            // garden, so the chapter still reads. See §10.16.
            new ExhibitionChapter { Id = "petal-transition-hall", Chapter = "06 / TRANSFORMATION",
                Title = "The Petal Transition Hall", WorldKey = "sunlit-palace-gardens",
                PlaceholderWorldKey = "enchanted-palace-garden", Artist = "Qi Baishi",
                Prompt = "How little can an image contain and still hold an entire world?" , CollectionId = "petal-transition" },
            new ExhibitionChapter { Id = "courtyard-of-living-memory", Chapter = "07 / IDENTITY",
                Title = "The Courtyard of Living Memory", WorldKey = "mexican-courtyard-bedroom-fantasy", Artist = "Frida Kahlo",
                Prompt = "What can pain become after it is given color, symbol and form?" , CollectionId = "living-memory" },
            // PLACEHOLDER. Her world is `yellow-polka-dot-infinity-room` ("A Kusama-style infinity
            // mirror room — dots and reflections without end"), again full-res only. The nearest
            // capture we can ship is `fantasy-realm-of-shimmering-spheres`, which is itself a
            // dotted, sphere-filled chamber — but it is ALSO the finale, so the visitor meets it
            // twice until the real export lands. That is the cost of this placeholder and the
            // reason it should not survive to a demo. See §10.16.
            new ExhibitionChapter { Id = "infinite-repetition-chamber", Chapter = "08 / INFINITY",
                Title = "The Infinite Repetition Chamber", WorldKey = "yellow-polka-dot-infinity-room",
                PlaceholderWorldKey = "fantasy-realm-of-shimmering-spheres", Artist = "Yayoi Kusama",
                Prompt = "If the self repeats into infinity, what remains uniquely yours?" , CollectionId = "infinite-repetition" },
        };

        /// <summary>
        /// Her <c>finalScene</c>: the world the visitor's answers built. Not part of the walk —
        /// it replaces it once the roundtable has produced an ending.
        /// </summary>
        public static readonly ExhibitionChapter Final = new ExhibitionChapter
        {
            Id = "personal-dream-world",
            CollectionId = "personal-dream-world",
            Chapter = "09 / ANSWER",
            Title = "Your Dream World",
            WorldKey = "fantasy-realm-of-shimmering-spheres",
            Artist = "A world formed from your answer",
            Prompt = "What will you carry back into the life outside this world?",
            IsFinal = true,
        };

        /// <summary>
        /// Chapters whose world has no 500k re-export and therefore cannot ship. Named here so a
        /// build can refuse or warn, rather than a chapter loading nothing on a headset.
        /// Re-exporting these two at 500k clears it.
        /// </summary>
        public static readonly IReadOnlyList<string> MissingAtSmallTier = new List<string>
        {
            "sunlit-palace-gardens",
            "yellow-polka-dot-infinity-room",
        };

        /// <summary>Chapters currently standing in someone else's room. Should be empty before a demo.</summary>
        public static IEnumerable<ExhibitionChapter> Placeholders()
        {
            foreach (var c in Chapters) if (c.IsPlaceholder) yield return c;
        }

        int _index;

        /// <summary>Set once the ending exists; <see cref="Current"/> then returns <see cref="Final"/>.</summary>
        public bool InFinalWorld { get; private set; }

        /// <summary>Zero-based position in the walk. Not meaningful once <see cref="InFinalWorld"/>.</summary>
        public int Index => _index;

        public int Count => Chapters.Count;

        /// <summary>
        /// The chapter the visitor is standing in. Hers: <c>currentScene()</c> returns
        /// <c>finalScene</c> once <c>state.finalWorld</c> is set, otherwise the indexed scene.
        /// </summary>
        public ExhibitionChapter Current => InFinalWorld ? Final : Chapters[_index];

        public bool IsLast => _index >= Chapters.Count - 1;

        /// <summary>Advance one chapter. Returns false at the last; the arc leaves via stage 05, not by running out.</summary>
        public bool Advance()
        {
            if (InFinalWorld || IsLast) return false;
            _index++;
            return true;
        }

        public bool Back()
        {
            if (InFinalWorld || _index == 0) return false;
            _index--;
            return true;
        }

        /// <summary>
        /// Jump straight to a room. Her <c>goToExhibitionScene</c>, which the navigator's dots
        /// call with an absolute index and its arrows call with index +/- 1.
        ///
        /// Refuses rather than clamps: a disabled arrow that somehow fires must do nothing, not
        /// wrap the visitor to the far end of the exhibition.
        /// </summary>
        public bool GoTo(int index)
        {
            if (InFinalWorld || index < 0 || index >= Chapters.Count || index == _index) return false;
            _index = index;
            return true;
        }

        /// <summary>Stage 08: the museum rewrites itself and the final world replaces the walk.</summary>
        public void EnterFinalWorld() => InFinalWorld = true;

        public void Reset()
        {
            _index = 0;
            InFinalWorld = false;
        }

        public static ExhibitionChapter ByWorldKey(string worldKey)
        {
            if (string.IsNullOrEmpty(worldKey)) return null;
            if (string.Equals(Final.WorldKey, worldKey, StringComparison.OrdinalIgnoreCase)) return Final;
            foreach (var c in Chapters)
                if (string.Equals(c.WorldKey, worldKey, StringComparison.OrdinalIgnoreCase) ||
                    (c.IsChosen && string.Equals(c.ChosenWorldKey, worldKey, StringComparison.OrdinalIgnoreCase)))
                    return c;
            return null;
        }
    }
}
