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

        public const float StepForward = 0.45f, StepSeconds = 0.8f, ShakeSeconds = 0.35f;

        public Phase Current { get; private set; } = Phase.Choosing;
        public Invitation Invitation { get; } = new Invitation();
        public CompanionGroup Group { get; private set; }
        public string Question { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, Transform> Standees => _standees;

        public event Action<string, Invitation.Result> Toggled;
        public event Action<IReadOnlyList<string>> Completed;
        public event Action<Phase> PhaseChanged;

        /// <summary>
        /// Optional: the turns wait at the flank marks until this returns true. The journey's lines are
        /// live (~5 s from the model), and turns started after the 0.8 s step would speak placeholders.
        /// </summary>
        public Func<bool> ReadyToAnswer;

        readonly Dictionary<string, Transform> _standees = new Dictionary<string, Transform>();
        readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _rowPose = new Dictionary<string, (Vector3, Quaternion)>();
        readonly Dictionary<string, float> _shake = new Dictionary<string, float>();
        readonly Dictionary<string, (Vector3 from, Vector3 to, Quaternion fromRot, Quaternion toRot)> _walk =
            new Dictionary<string, (Vector3, Vector3, Quaternion, Quaternion)>();
        float _walkT, _stepSeconds = StepSeconds;

        /// <summary>
        /// Walk the chosen straight to their places in the crowd beside the visitor, at walking pace, instead of to
        /// the answer marks (Saul, 5 Oct: they ran to their marks, then the crowd moved one of them again across the
        /// view). Their turns are then taken where they stand.
        /// </summary>
        public bool JoinCrowd { get; set; }

        /// <summary>Walking pace for the step out (m/s).</summary>
        public const float WalkSpeed = 1.4f;
        bool _answer = true, _completed;

        /// <summary>Build the stage over standees already standing in a row, keyed by master id.</summary>
        public static CompanyStage Make(GameObject host, IReadOnlyDictionary<string, Transform> standees)
        {
            var s = host.AddComponent<CompanyStage>();
            foreach (var kv in standees)
            {
                s._standees[kv.Key] = kv.Value;
                s._rowPose[kv.Key] = (kv.Value.position, kv.Value.rotation);
                var p = Pointable.Make(kv.Value.gameObject, kv.Key);
                p.Label = Masters.Name(kv.Key);
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
        public bool Confirm() => Send(answer: true);

        /// <summary>
        /// Send the chosen to their marks. With <paramref name="answer"/> false they arrive and wait
        /// (a later chapter starts their turns), which is how the Palace calls them if nobody has yet.
        /// </summary>
        public bool Send(bool answer)
        {
            if (Current != Phase.Choosing || !Invitation.CanProceed) return false;
            _answer = answer;
            var order = Invitation.SpeakingOrder();
            var figures = new Dictionary<string, Transform>();
            foreach (var id in order) figures[id] = _standees[id];

            // Where each will stand, then walk them there, then hand them to the group.
            var head = Group.Head != null ? Group.Head : Camera.main != null ? Camera.main.transform : null;
            Group.Head = head;
            Group.Set(order, figures);
            _walk.Clear();
            if (JoinCrowd) { Group.Crowd = true; Group.AssignSlots(); }
            var longest = 0f;
            for (var i = 0; i < order.Count; i++)
            {
                var t = _standees[order[i]];
                var to = JoinCrowd ? Group.CrowdPlaceOf(order[i]) : Group.MarkPosition(i);
                longest = Mathf.Max(longest, Vector3.Distance(t.position, to));
                var face = head != null ? head.position - to : -t.forward; face.y = 0f;
                _walk[order[i]] = (t.position, to, t.rotation, Quaternion.LookRotation(face.normalized, Vector3.up));
            }
            _walkT = 0f;
            _stepSeconds = JoinCrowd ? Mathf.Clamp(longest * 1.15f / WalkSpeed, 1.2f, 5f) : StepSeconds;   // the arc is a little longer than the chord
            // The uninvited fade out (Saul, 3 Oct 2026), so none is left standing half-hidden behind a companion.
            foreach (var kv in _standees) if (!Invitation.IsChosen(kv.Key)) Fader.FadeOut(kv.Value.gameObject);
            Go(Phase.Stepping);
            return true;
        }

        public bool Redo() => false;

        static readonly int SpeedParam = Animator.StringToHash("Speed"), ListeningParam = Animator.StringToHash("Listening");

        /// <summary>Painter.controller: Speed walks, Listening holds the Listen pose. A figure without them is left alone.</summary>
        static void Pose(Transform figure, float speed, bool listening)
        {
            if (figure == null) return;
            var an = figure.GetComponentInChildren<Animator>();
            if (an == null || an.runtimeAnimatorController == null) return;
            foreach (var p in an.parameters)
            {
                if (p.nameHash == SpeedParam) an.SetFloat(SpeedParam, speed, 0.1f, Time.deltaTime);
                else if (p.nameHash == ListeningParam) an.SetBool(ListeningParam, listening);
            }
        }

        /// <summary>
        /// The walk from the line-up to the mark, as an arc round the visitor: out to the side first, nearer
        /// last. A straight line from the row ahead to a mark beside the visitor crossed the middle of the view
        /// (CompanionClearanceProbe, 4 Oct: Van Gogh 16 degrees off the gaze at 1.7 m for 0.6 s).
        /// </summary>
        public static Vector3 StepOut(Vector3 eye, Vector3 from, Vector3 to, float t)
        {
            var a = from - eye; a.y = 0f; var b = to - eye; b.y = 0f;
            if (a.sqrMagnitude < 1e-4f || b.sqrMagnitude < 1e-4f) return Vector3.Lerp(from, to, t);
            var angle = Vector3.SignedAngle(a, b, Vector3.up);
            var turn = 1f - (1f - t) * (1f - t) * (1f - t);   // most of the swing early
            var close = t * t * t;                            // most of the approach late
            var dir = Quaternion.Euler(0f, angle * turn, 0f) * a.normalized;
            var r = Mathf.Lerp(a.magnitude, b.magnitude, close);
            var p = eye + dir * r;
            return new Vector3(p.x, Mathf.Lerp(from.y, to.y, t), p.z);
        }

        /// <summary>Done once: later chapters reuse the group's turns without re-completing the stage.</summary>
        void Finish()
        {
            if (_completed) return;
            _completed = true;
            Group.TurnsFinished -= Finish;
            Go(Phase.Done);
            Completed?.Invoke(Invitation.SpeakingOrder());
        }

        void Go(Phase p)
        {
            Current = p;
            // Chosen: the line-up's own targets stop catching the ray, so pointing at a master always reaches their
            // ask target (it used to land on the dead standee box half the time, and the click did nothing).
            if (p != Phase.Choosing)
                foreach (var t in _standees.Values) { var pt = t != null ? t.GetComponent<Pointable>() : null; if (pt != null) pt.Interactive = false; }
            PhaseChanged?.Invoke(p);
        }

        void Update()
        {
            switch (Current)
            {
                case Phase.Choosing:
                    foreach (var id in _standees.Keys)
                    {
                        // Chosen: her attentive Listen pose, so who is in the company reads at a glance (Saul, 5 Oct).
                        Pose(_standees[id], 0f, Invitation.IsChosen(id));
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
                    // Walking, not floating, to their places (Saul, 5 Oct): the walk cycle at the pace they move.
                    foreach (var kv in _walk)
                    {
                        var arc = Vector3.Distance(kv.Value.from, kv.Value.to) * 1.15f;
                        Pose(_standees[kv.Key], _walkT < _stepSeconds ? arc / Mathf.Max(0.1f, _stepSeconds) : 0f, false);
                    }
                    var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_walkT / _stepSeconds));
                    var eye = Group.Head != null ? Group.Head.position : Vector3.zero;
                    foreach (var kv in _walk)
                        _standees[kv.Key].SetPositionAndRotation(StepOut(eye, kv.Value.from, kv.Value.to, Mathf.Clamp01(_walkT / _stepSeconds)),
                                                                 Quaternion.Slerp(kv.Value.fromRot, kv.Value.toRot, k));
                    if (_walkT >= _stepSeconds && (ReadyToAnswer == null || ReadyToAnswer()))
                    {
                        Group.enabled = true;
                        if (!JoinCrowd) Group.PlaceAll();   // in the crowd they are already in their places
                        if (!_answer) { Finish(); break; }
                        Group.TurnsFinished += Finish;
                        Group.BeginTurns();
                        Go(Phase.Answering);
                    }
                    break;
            }
        }
    }
}
