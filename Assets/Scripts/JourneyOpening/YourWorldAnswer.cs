using System.Collections.Generic;
using MusePico.Dialogue;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using UnityEngine;
using InputAction = UnityEngine.InputSystem.InputAction;
using InputActionType = UnityEngine.InputSystem.InputActionType;

namespace MuseXR.Journey
{
    /// <summary>
    /// Her "B at the answer stone returns to the table" (plan 2.9). The table itself is gone by then - the Monet frame,
    /// rotunda and all, is destroyed on arrival in Your world - so the table comes to the stone: standing at it, B opens
    /// the kept answer again with the table's own three paths (A keep · X rewrite via Socrates · Y say my own, with the
    /// three-rewrite fallback when the spoken words do not come through). Keeping re-engraves the stone and rewrites
    /// the memento (<see cref="YourWorldEnding.SetAnswer"/>).
    /// </summary>
    public sealed class YourWorldAnswer : MonoBehaviour, IConfirmable
    {
        public const float Reach = 2.6f;

        YourWorldEnding _ending;
        InputAction _x, _y;
        int _stage;   // 0 closed, 1 the answer shown (A · X · Y), 2 Socrates' rewrite shown (A use · B back)
        string _draft, _rewrite;
        bool _near, _hinted, _rewriting;
        GameObject _ownRoot;
        List<string> _ownOptions;

        bool Arrived => transform.parent != null && transform.parent.position.sqrMagnitude < 0.01f && transform.parent.rotation == Quaternion.identity;

        void Start()
        {
            _x = new InputAction("yw-x", InputActionType.Button); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_x, "<XRController>{LeftHand}/primaryButton"); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_x, "<Keyboard>/x"); _x.Enable();
            _y = new InputAction("yw-y", InputActionType.Button); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_y, "<XRController>{LeftHand}/secondaryButton"); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_y, "<Keyboard>/y"); _y.Enable();
        }

        void OnDestroy() { _x?.Dispose(); _y?.Dispose(); ConfirmInput.Drop(this); }

        void Update()
        {
            if (!Arrived) return;
            if (_ending == null) _ending = transform.parent.GetComponentInChildren<YourWorldEnding>();
            var stone = _ending != null ? _ending.Stone : null;
            var cam = Camera.main;
            if (stone == null || cam == null) return;
            var d = cam.transform.position - stone.position; d.y = 0f;
            var near = d.magnitude < Reach;
            if (near && !_near && _stage == 0)
            {
                if (ConfirmInput.Focus == null) ConfirmInput.Take(this);
                if (!_hinted) { _hinted = true; DialogueContext.Notice("Your answer", "B  ·  change your answer", 5f); }
            }
            else if (!near && _near && _stage == 0) ConfirmInput.Drop(this);
            _near = near;
            if (_stage == 1 && _x != null && _x.WasPressedThisFrame()) Rewrite();
            if (_stage == 1 && _y != null && _y.WasPressedThisFrame()) SayOwn();
        }

        void Open()
        {
            var rec = JourneyMemory.Record;
            _draft = !string.IsNullOrWhiteSpace(rec.FinalAnswer.Final) ? rec.FinalAnswer.Final.Trim()
                   : !string.IsNullOrWhiteSpace(rec.FinalAnswer.Draft) ? rec.FinalAnswer.Draft.Trim() : "";
            ShowAnswer();
        }

        void ShowAnswer()
        {
            _stage = 1;
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(null, "Your answer  ·  at the stone", "\"" + _draft + "\"", "A keep  ·  X rewrite via " + Masters.Name(ChapterFeatures.Challenger()) + "  ·  Y say my own", "Your answer");
            ConfirmInput.Take(this);
        }

        public bool Confirm()
        {
            if (_stage == 0) return false;
            var final = _stage == 2 ? _rewrite : _draft;
            var rec = JourneyMemory.Record;
            if (_stage == 1 && string.IsNullOrWhiteSpace(rec.FinalAnswer.Draft)) rec.FinalAnswer.Draft = _draft;
            rec.FinalAnswer.Final = final; rec.FinalAnswer.RewrittenBy = _stage == 2 ? ChapterFeatures.Challenger() : "self";
            _stage = 0;
            if (_ending != null) _ending.SetAnswer(final);
            var panel = TorsoPanel.Get(); if (panel != null) panel.ClearLine();
            DialogueContext.Notice("Your answer", "Kept  ·  the stone and the memento carry it now", 5f);
            Debug.Log("[YourWorld] answer changed at the stone: " + final);
            if (!_near) ConfirmInput.Drop(this);
            return true;
        }

        public bool Redo()
        {
            if (_stage == 0) { Open(); return true; }       // her B at the stone: back to the answer
            if (_stage == 2) { ShowAnswer(); return true; }  // from Socrates' rewrite, back to the first
            _stage = 0;                                     // B again: close, nothing changed
            var panel = TorsoPanel.Get(); if (panel != null) panel.ClearLine();
            return true;
        }

        async void Rewrite()
        {
            if (_rewriting) return;
            _rewriting = true;
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(ChapterFeatures.Challenger(), "Rewrite  ·  through " + Masters.Name(ChapterFeatures.Challenger()) + "'s question", Masters.Name(ChapterFeatures.Challenger()) + " is turning your answer over…", null, "Your answer");
            string live = null;
            try { live = await MonetFeatures.RewriteLive(JourneyMemory.Record, _draft); }
            catch (System.Exception ex) { Debug.LogWarning("[YourWorld] rewrite failed: " + ex.Message); }
            _rewriting = false;
            if (this == null || _stage == 0) return;
            _rewrite = !string.IsNullOrWhiteSpace(live) ? live.Trim().Trim('"') : "What I carry back is what I would still choose if nobody handed it to me";
            _stage = 2;
            if (panel != null) panel.ShowLine(ChapterFeatures.Challenger(), "Rewrite  ·  through " + Masters.Name(ChapterFeatures.Challenger()) + "'s question", "\"" + _rewrite + "\"" + (live == null ? "   (local fallback)" : ""), "A use this one  ·  B back to the first", "Your answer");
        }

        void SayOwn()
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue == null) { OfferOwnOptions(); return; }
            DialogueContext.Notice("Your answer", "Hold X and say your answer", 4f);
            dialogue.TextDictated -= OnOwn; dialogue.TextDictated += OnOwn;
            dialogue.DictationFailed -= OnOwnFailed; dialogue.DictationFailed += OnOwnFailed;
            dialogue.ListenForText();
        }

        void OnOwn(string text)
        {
            Unhook();
            if (string.IsNullOrWhiteSpace(text)) { OfferOwnOptions(); return; }
            _draft = text.Trim();
            ShowAnswer();
        }

        void OnOwnFailed(string why) { Unhook(); OfferOwnOptions(); }

        void Unhook()
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue != null) { dialogue.TextDictated -= OnOwn; dialogue.DictationFailed -= OnOwnFailed; }
        }

        async void OfferOwnOptions()
        {
            if (_ownRoot != null || _ending == null || _ending.Stone == null) return;
            DialogueContext.Notice("Your answer", "Your words did not come through\nChoose the one closest to yours", 5f);
            List<string> options = null;
            try { options = await MonetFeatures.OwnOptionsLive(JourneyMemory.Record, _draft); }
            catch (System.Exception ex) { Debug.LogWarning("[YourWorld] own options: " + ex.Message); }
            if (this == null || _stage == 0) return;
            var live = options != null && options.Count == 3;
            if (!live) options = MonetFeatures.OwnOptionsLocal(_draft);
            _ownOptions = options;
            _ownRoot = new GameObject("Say my own");
            _ownRoot.transform.SetParent(transform, true);
            _ownRoot.transform.position = _ending.Stone.position + Vector3.up * 1.9f;
            TurnToVisitor.Attach(_ownRoot);
            var panel = ChoicePanel.Make(_ownRoot.transform, "Own words");
            panel.Build("Your answer  ·  in your words", "Which is closest to what you would say?", options,
                        live ? "Point at one and pull the trigger" : "Point at one and pull the trigger  ·  local fallback", 2.2f,
                        (i, _) => PickOwn(i));
            Appear.In(_ownRoot, 0.4f);
        }

        void PickOwn(int i)
        {
            if (_ownOptions == null || i < 0 || i >= _ownOptions.Count) return;
            _draft = _ownOptions[i];
            if (_ownRoot != null) { Destroy(_ownRoot); _ownRoot = null; }
            ShowAnswer();
        }
    }
}
