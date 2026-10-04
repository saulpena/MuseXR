using MuseXR.Interaction;
using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// A companion group's lines, shown on the visitor's waist panel (<see cref="TorsoPanel"/>): the
    /// speaker's name and their line, while the line runs. There is no panel over a master's head any
    /// more (Saul, 3 Oct: one panel at the waist, attached to the torso, out of the view). One per group;
    /// the name and the add-by-type hook are unchanged so every chapter picks this up as it is.
    /// </summary>
    public sealed class SubtitleRig : MonoBehaviour
    {
        public CompanionGroup Group;
        /// <summary>Kept for scenes and callers that set it; the waist panel never follows the head.</summary>
        public bool Follow;

        void OnEnable()
        {
            if (Group == null) Group = GetComponent<CompanionGroup>();
            if (Group == null) return;
            Group.LineStarted += Show;
            // The line stays through the pause after it and goes when the next starts or the round ends:
            // hiding it as the line ended made each line read as a flash.
            Group.TurnsFinished += HideAll;
        }

        void OnDisable()
        {
            if (Group == null) return;
            Group.LineStarted -= Show;
            Group.TurnsFinished -= HideAll;
            HideAll();
        }

        void Show(string id, string line)
        {
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(MuseXR.Slots.Masters.Name(id), line, "A  next");
        }

        void HideAll()
        {
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ClearLine();
        }
    }
}
