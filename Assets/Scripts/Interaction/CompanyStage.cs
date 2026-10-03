using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her stage 2, Invite companions: "Six standees in a row; ray-select 1-3. The chosen figures step
    /// to your side and each answers the question in one line, in turn. Saved: companions[]."
    ///
    ///   Choosing   point and pull the trigger to toggle a master. An invited standee steps forward
    ///              out of the row (shape, not colour). A fourth is refused: the standee shakes, the
    ///              hand buzzes hard and a dull knock sounds. A proceeds once at least one is chosen.
    ///   Stepping   the chosen walk out to their flank marks (<see cref="CompanionMarks"/>) in 0.8 s;
    ///              the uninvited fade out and are switched off.
    ///   Answering  each speaks one line in her fixed order, behind the gaze gate (<see cref="CompanionGroup"/>).
    ///   Done       <see cref="Completed"/> carries companions[] for the record.
    ///
    /// The standees are given to it: a rigged master or a portrait board, built by the scene. Every
    /// figure's front is its +Z, as on a character model, so the row and the flank marks agree. What
    /// they look like when hovered or chosen is the UI layer's, from the events here.
    /// </summary>
    public sealed class CompanyStage : MonoBehaviour, IConfirmable
    {
        public enum Phase { Choosing, Stepping, Answering, Done }

        public const float StepForward = 0.3f, StepSeconds = 0.8f, ShakeSeconds = 0.35f;

        public Phase Current { get; private set; } = Phase.Choosing;
        public Invitation Invitation { get; } = new Invitation();
        public CompanionGroup Group { get; private set; }
        public string Question { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, Transform> Standees => _standees;

        public event Action<string, Invitation.Result> Toggled;
        public event Action<IReadOnlyList<string>> Completed;
        public event Action<Phase> PhaseChanged;

        readonly Dictionary<string, Transform> _standees = new Dictionary<string, Transform>();
        readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _rowPose = new Dictionary<string, (Vector3, Quaternion)>();
        readonly Dictionary<string, float> _shake = new Dictionary<string, float>();
        readonly Dictionary<string, (Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)> _walk =
            new Dictionary<string, (Vector3, Vector3, Quaternion, Quaternion)>();
        float _walkT;

        /// <summary>Build the stage over standees already standing in a row, keyed by master id.</summary>
        public static CompanyStage Make(GameObject host, IReadOnlyDictionary<string, Transform> standees)
        {
            var s = host.AddComponent<CompanyStage>();
            foreach (var kv in standees)
            {
                s._standees[kv.Key] = kv.Value;
                s._rowPose[kv.Key] = (kv.Value.position, kv.Value.rotation);
                var p = Pointable.Make(kv.Value.gameObject, kv.Key);
                var id = kv.Key;
                p.Selected += (_, pointer) => s.Toggle(id, pointer);
            }
            s.Group = host.AddComponent<CompanionGroup>();
            s.Group.enabled = false;
            ConfirmInput.Take(s);
            return s;
        }

        /// <summary>Her demo route preselects the default trio.</summary>
        public void Preselect(IEnumerable<string> ids)
        {
            foreach (var id in ids) if (!Invitation.IsChosen(id)) Toggle(id, null);
        }

        public Invitation.Result Toggle(string id, Pointer pointer)
        {
            if (Current != Phase.Choosing) return Invitation.Result.Unknown;
            var r = Invitation.Toggle(id);
            // Choosing here takes A and B. Measured in the test scene: with a piece placed at the
            // Palace a moment before, A confirmed the Palace instead of sending the companions.
            if (r != Invitation.Result.Unknown) ConfirmInput.Take(this);
            var hand = pointer != null ? pointer.Source : null;
            switch (r)
            {
                case Invitation.Result.Added:
                    hand?.Buzz(SlotRules.LightAmplitude * 1.5f, SlotRules.LightSeconds);
                    break;
                case Invitation.Result.Refused:
                    _shake[id] = ShakeSeconds;
                    hand?.Buzz(0.8f, 0.2f);
                    ChimePlayer.Play(ChimePlayer.RefuseClip(), _standees[id].position + Vector3.up * 1.2f, 0.8f);
                    break;
            }
            Toggled?.Invoke(id, r);
            return r;
        }

        /// <summary>A: the chosen step out to their marks and answer in turn.</summary>
        public bool Confirm()
        {
            if (Current != Phase.Choosing || !Invitation.CanProceed) return false;
            var order = Invitation.SpeakingOrder();
            var figures = new Dictionary<string, Transform>();
            foreach (var id in order) figures[id] = _standees[id];

            // Where each will stand, then walk them there, then hand them to the group.
            var head = Group.Head != null ? Group.Head : Camera.main != null ? Camera.main.transform : null;
            Group.Head = head;
            Group.Set(order, figures);
            _walk.Clear();
            for (var i = 0; i < order.Count; i++)
            {
                var t = _standees[order[i]];
                var to = Group.MarkPosition(i);
                var face = head != null ? head.position - to : -t.forward; face.y = 0f;
                _walk[order[i]] = (t.position, to, t.rotation, Quaternion.LookRotation(face.normalized, Vector3.up));
            }
            _walkT = 0f;
            // The uninvited fade out (Saul, 3 Oct 2026), so none is left standing half-hidden behind a companion.
            foreach (var kv in _standees) if (!Invitation.IsChosen(kv.Key)) Fader.FadeOut(kv.Value.gameObject);
            Go(Phase.Stepping);
            return true;
        }

        public bool Redo() => false;

        void Go(Phase p)
        {
            Current = p;
            PhaseChanged?.Invoke(p);
        }

        void Update()
        {
            switch (Current)
            {
                case Phase.Choosing:
                    foreach (var id in _standees.Keys)
                    {
                        var (pos, rot) = _rowPose[id];
                        var target = Invitation.IsChosen(id) ? pos + (rot * Vector3.forward) * StepForward : pos;   // +Z is the figure's front
                        var t = _standees[id];
                        var next = Vector3.MoveTowards(t.position, target, Time.deltaTime * 1.2f);
                        if (_shake.TryGetValue(id, out var left) && left > 0f)
                        {
                            _shake[id] = left - Time.deltaTime;
                            t.position = next + (rot * Vector3.right) * Mathf.Sin(left * 60f) * 0.03f * (left / ShakeSeconds);
                        }
                        else t.position = next;
                    }
                    break;
                case Phase.Stepping:
                    _walkT += Time.deltaTime;
                    var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_walkT / StepSeconds));
                    foreach (var kv in _walk)
                        _standees[kv.Key].SetPositionAndRotation(Vector3.Lerp(kv.Value.from, kv.Value.to, k),
                                                                 Quaternion.Slerp(kv.Value.fromRot, kv.Value.toRot, k));
                    if (_walkT >= StepSeconds)
                    {
                        Group.enabled = true;
                        Group.PlaceAll();
                        Group.TurnsFinished += () => { Go(Phase.Done); Completed?.Invoke(Invitation.SpeakingOrder()); };
                        Group.BeginTurns();
                        Go(Phase.Answering);
                    }
                    break;
            }
        }
    }
}
