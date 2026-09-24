using System.Collections.Generic;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>
    /// The closing roundtable's prompt.
    ///
    /// Ported from <c>muse-infinity/server.mjs:652-696</c>. Two rules carry over and both are
    /// pinned by tests, because neither would fail loudly:
    ///
    /// <b>The interpretive framing is supplied here, not by the lenses.</b> The authored lenses
    /// deliberately omit it, so every call site that puts a lens in front of a model must add it.
    /// Dropping it produces perfectly good readings that quietly stop carrying the disclaimer the
    /// whole piece depends on.
    ///
    /// <b>The shared rules appear exactly once</b>, in the instructions, and in no master's block.
    /// An earlier revision of her dialogue prompt emitted them 2-3x per call while the separating
    /// fields arrived once, so the model was emphatically told to be a careful museum voice and
    /// only briefly told to be <i>this</i> master.
    ///
    /// Pure C#, no Unity.
    /// </summary>
    public static class RoundtablePrompt
    {
        /// <summary>
        /// Her instructions, verbatim in substance. Note the word limit is <b>55</b> here, not the
        /// 50 the per-artwork readings use — a closing remark is allowed to be slightly longer.
        /// </summary>
        public static string Instructions(int threadCount)
        {
            return
                "The visitor has finished walking the gallery and the masters now close the visit together. " +
                "Return exactly " + threadCount + " threads, one for each master described in the context, in " +
                "the order given. Each thread is that master's single closing remark about THIS visitor's walk, " +
                "spoken in that master's own lens and vocabulary, under 55 words, never addressing the other " +
                "masters. Each master's block opens with the SHAPE his remark must take — a different speech act " +
                "for each, and the thing that keeps three closing remarks from reading as one voice; obey it. " +
                "Then name the world these choices built. " +
                "The artworks the visitor stopped at and the questions they asked are listed verbatim in the " +
                "context — draw on those exact items and invent no others; if a list is empty, say so plainly " +
                "rather than inventing a stop or a question. " +
                "worldTitle: three to seven words, no quotation marks. " +
                "synthesis: 45 to 70 words addressed to the visitor as \"you\", naming at least one artwork they " +
                "actually stopped at and at least one question they actually asked. " +
                PerspectivePrompt.InterpretiveFraming + " " +
                "The interface renders that disclaimer beside every thread already, so do NOT write a disclaimer " +
                "sentence into the thread text or the synthesis — spending words restating it makes all three " +
                "threads share the same phrasing, which is the opposite of three distinct readings. " +
                "Reply in English.";
        }

        /// <summary>
        /// The digest as the model sees it: what the visitor stopped at, what they asked, and what
        /// each master has already told them — then the master blocks.
        ///
        /// <b>Empty lists say so explicitly.</b> Her wording is "(none: ...)", and the instructions
        /// tell the model to say so plainly rather than invent a stop. A visitor who walked through
        /// in silence gets an honest closing about having done exactly that.
        /// </summary>
        public static string BuildInput(VisitSession session, IReadOnlyList<MasterLens> masters)
        {
            var sb = new StringBuilder();

            sb.Append("Artworks this visitor actually stopped at, in order:\n");
            if (session == null || session.VisitedArtworks.Count == 0)
            {
                sb.Append("- (none: the visitor stopped at no artwork)\n");
            }
            else
            {
                foreach (var a in session.VisitedArtworks)
                {
                    sb.Append("- ").Append(a.Title);
                    if (!string.IsNullOrEmpty(a.Artist)) sb.Append(" by ").Append(a.Artist);
                    sb.Append('\n');
                }
            }

            sb.Append("\nQuestions this visitor actually asked, in order:\n");
            if (session == null || session.AskedQuestions.Count == 0)
                sb.Append("- (none: the visitor asked nothing aloud)\n");
            else
                foreach (var q in session.AskedQuestions) sb.Append("- ").Append(q).Append('\n');

            sb.Append("\nWhat each master already told them during the walk:\n");
            if (session == null || session.PerspectiveLog.Count == 0)
                sb.Append("- (none)\n");
            else
                foreach (var p in session.PerspectiveLog)
                    sb.Append("- ").Append(p.Speaker).Append(": ").Append(p.Line).Append('\n');

            if (masters != null)
            {
                for (var i = 0; i < masters.Count; i++)
                {
                    sb.Append('\n');
                    sb.Append(PerspectivePrompt.DescribeMaster(masters[i], i));
                }
            }

            return sb.ToString();
        }
    }
}
