using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>What a point on her diagram is.</summary>
    public enum DiagramKind { Entry, Work, Mark, Interaction, Exit, Prop, Column }

    /// <summary>Which wall a work or exit hangs on, read from where her diagram draws it.</summary>
    public enum WallSide { None, Left, Right, Ahead, Behind }

    /// <summary>One element of her top-down diagram, in her SVG coordinates (600 x 400, y down).</summary>
    public readonly struct DiagramItem
    {
        public readonly string Id;
        public readonly DiagramKind Kind;
        public readonly float X, Y;
        public readonly string Label;
        public readonly WallSide Wall;

        public DiagramItem(string id, DiagramKind kind, float x, float y, string label, WallSide wall = WallSide.None)
        { Id = id; Kind = kind; X = x; Y = y; Label = label; Wall = wall; }
    }

    /// <summary>
    /// Skylar's five top-down chapter diagrams (her VR plan, section 02), transcribed from the SVG in
    /// her page - every coordinate is hers (Docs/HerPlan/diagram-*.png). The entry is where her
    /// "Entry view" cone sits; "forward" is up the page (-Y), which is the direction the cone faces.
    ///
    /// Her diagrams are schematic, not to scale: her own note says "plan positions are design intent;
    /// developers calibrate against each world's collider". So a chapter is placed by
    /// <see cref="ToLocal"/> with per-world metres-per-unit, and works and exits are then snapped to
    /// the real wall on the side she draws them.
    /// </summary>
    public static class ChapterDiagrams
    {
        public sealed class Diagram
        {
            public readonly string Chapter;
            public readonly float EntryX, EntryY;
            public readonly IReadOnlyList<DiagramItem> Items;
            public Diagram(string chapter, float ex, float ey, IReadOnlyList<DiagramItem> items)
            { Chapter = chapter; EntryX = ex; EntryY = ey; Items = items; }

            public DiagramItem Find(string id)
            {
                foreach (var i in Items) if (i.Id == id) return i;
                throw new KeyNotFoundException(Chapter + ": " + id);
            }
        }

        static DiagramItem W(string id, float x, float y, string label, WallSide wall) => new DiagramItem(id, DiagramKind.Work, x, y, label, wall);
        static DiagramItem M(string id, float x, float y) => new DiagramItem(id, DiagramKind.Mark, x, y, id);
        static DiagramItem I(string id, float x, float y, string label) => new DiagramItem(id, DiagramKind.Interaction, x, y, label);
        static DiagramItem E(string id, float x, float y, string label, WallSide wall) => new DiagramItem(id, DiagramKind.Exit, x, y, label, wall);
        static DiagramItem P(string id, float x, float y, string label, WallSide wall = WallSide.None) => new DiagramItem(id, DiagramKind.Prop, x, y, label, wall);

        /// <summary>A · Palace · Court of Keeping.</summary>
        public static readonly Diagram Palace = new Diagram("palace", 280, 372, Columns(new List<DiagramItem>
        {
            P("throne", 280, 52, "Gold screen + throne · non-walkable", WallSide.Ahead),
            P("steps", 280, 115, "Steps · scale cue"),
            P("scroll-case", 50, 245, "Scroll case", WallSide.Left),
            P("crane", 245, 178, "Crane"),
            P("turtle", 341, 178, "Turtle"),
            I("miniature-court", 280, 256, "Miniature court"),
            M("monet", 160, 285), M("van_gogh", 400, 285), M("socrates", 318, 226),
            E("moon-gate", 548, 186, "Moon gate · exit", WallSide.Right),
        }));

        /// <summary>B · Grotto · Hall of Time.</summary>
        public static readonly Diagram Grotto = new Diagram("grotto", 300, 372, new List<DiagramItem>
        {
            P("cliff-buddha", 350, 82, "Cliff Buddha · AI rendition", WallSide.Ahead),
            P("rail", 300, 112, "Viewing rail"),
            W("gandhara", 75, 270, "Gandhara", WallSide.Left),
            W("tang", 525, 270, "Tang / N. Wei", WallSide.Right),
            P("niche-relief", 110, 195, "Niche relief", WallSide.Left),
            I("socket-detail", 130, 205, "Socket · detail"),
            I("socket-whole", 300, 112, "Socket · whole"),
            P("lamp-stand", 178, 338, "Brass stand · lamp"),
            M("monet", 210, 240), M("van_gogh", 390, 230), M("socrates", 250, 160),
            E("arch", 530, 96, "Arch · cobalt and gold", WallSide.Right),
        });

        /// <summary>C · Van Gogh · Studio of the Burning Sky.</summary>
        public static readonly Diagram VanGogh = new Diagram("vangogh", 195, 370, new List<DiagramItem>
        {
            W("aic-28560", 70, 300, "Bedroom", WallSide.Left),
            W("aic-80607", 70, 220, "Self-Portrait", WallSide.Left),
            W("aic-14586", 70, 140, "Poet's Garden", WallSide.Left),
            W("aic-28862", 530, 250, "Peasant Woman", WallSide.Right),
            I("easel", 195, 250, "Easel · colour · stroke"),
            P("window", 195, 70, "Great frame window: fields and sky", WallSide.Ahead),
            M("monet", 130, 270), M("van_gogh", 262, 292), M("socrates", 262, 196),
            E("side-door", 500, 330, "Side door · exit", WallSide.Right),
        });

        /// <summary>D · Monet · Garden of Water and Light.</summary>
        public static readonly Diagram Monet = new Diagram("monet", 400, 385, new List<DiagramItem>
        {
            W("aic-16568", 320, 300, "Water Lilies", WallSide.Left),
            W("aic-16571", 458, 270, "Saint-Lazare", WallSide.Right),
            W("aic-64818", 458, 180, "Stacks of Wheat", WallSide.Right),
            W("aic-14620", 318, 160, "Pourville", WallSide.Left),
            I("time-ring", 380, 222, "Time-ring pedestal"),
            P("rotunda", 480, 60, "Rotunda = roundtable", WallSide.Ahead),
            P("rose-arch", 176, 98, "Rose arch · vista"),
            M("monet", 345, 260), M("van_gogh", 430, 215), M("socrates", 352, 190),
            E("form-answer", 520, 112, "Choose \"Form my answer\"", WallSide.Ahead),
        });

        /// <summary>E · Your world · Walk into your answer.</summary>
        public static readonly Diagram YourWorld = new Diagram("yourworld", 300, 332, new List<DiagramItem>
        {
            P("object", 238, 318, "(1) Object · yaw"),
            P("lamp-socket", 362, 262, "(2) Lamp socket"),
            P("stroke", 300, 206, "(3) Your stroke x1.5"),
            P("works", 300, 118, "(4) Works you stayed with"),
            I("answer-stone", 300, 68, "(5) Answer stone + memento"),
            M("monet", 250, 74), M("van_gogh", 350, 74), M("socrates", 300, 96),
            E("start-again", 300, 362, "Behind: start again", WallSide.Behind),
        });

        public static IEnumerable<Diagram> All => new[] { Palace, Grotto, VanGogh, Monet, YourWorld };

        static List<DiagramItem> Columns(List<DiagramItem> items)
        {
            foreach (var x in new[] { 110, 190, 370, 450 })
                foreach (var y in new[] { 150, 240, 330 })
                    items.Add(new DiagramItem("column-" + x + "-" + y, DiagramKind.Column, x, y, "Dragon column"));
            return items;
        }

        /// <summary>
        /// A diagram point in the entry's own frame, metres: x to the visitor's right, z ahead.
        /// <paramref name="across"/> and <paramref name="along"/> are metres per diagram unit,
        /// calibrated per world (her diagrams are not to scale, and not to the same scale both ways).
        /// </summary>
        public static (float x, float z) ToLocal(Diagram d, DiagramItem item, float across, float along) =>
            ((item.X - d.EntryX) * across, (d.EntryY - item.Y) * along);

        /// <summary>Her comfort rule: a companion stands within +/-60 degrees of forward, never behind.</summary>
        public static bool WithinForwardCone(float x, float z, float halfAngleDegrees = 60f)
        {
            if (z <= 0f) return false;
            return Math.Abs(Math.Atan2(x, z) * 180.0 / Math.PI) <= halfAngleDegrees;
        }
    }
}
