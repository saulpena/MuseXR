using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The companions in the world (§3.4): who stands where, and who speaks when.
    ///
    ///   Marks       1.5-2.2 m from the visitor, within ±60° of forward, never behind, off the path,
    ///               from <see cref="CompanionMarks"/>. A blocked mark (a wall between, no floor) is
    ///               retried nearer, then mirrored.
    ///   Teleport    when the visitor jumps (a teleport or snap turn), every companion is put on its new
    ///               mark on the next frame - no walking, no clipping. <see cref="Remarked"/> fires so
    ///               the UI layer can fade them.
    ///   Turns       one at a time in her fixed order, A advances, 2 s auto-advance, and the gaze gate:
    ///               nobody starts speaking until the visitor looks within 60° of them.
    ///
    /// What each companion says comes from <see cref="LineFor"/> (DialogueClient in the journey; a
    /// canned line by default). When a line has played, call <see cref="LineFinished"/>; with no
    /// voice, <see cref="EstimateSeconds"/> ends it.
    /// </summary>
    public sealed class CompanionGroup : MonoBehaviour, IConfirmable
    {
        /// <summary>A step this large between frames is a teleport, not walking.</summary>
        public const float JumpDistance = 0.5f, JumpDegrees = 20f;
        /// <summary>
        /// A visitor who slowly turns round leaves the companions behind them. Past this angle from the
        /// gaze, for this long, they are re-marked in front again: "never behind".
        /// </summary>
        public const float BehindDegrees = 100f, BehindSeconds = 1.5f;

        public Transform Head { get; set; }

        /// <summary>
        /// True: the companions stand on marks around the visitor and follow (her §3.4, between
        /// chapters). False: they stay where a chapter's diagram put them (her chapter diagrams mark
        /// M/V/S): they turn to face the visitor and take turns WITHOUT the gaze gate. Measured in the
        /// Palace: standing at the court, her M and V marks are at the visitor's shoulders, so a gate
        /// that waits to be looked at left everyone silent until the visitor went looking for them.
        /// </summary>
        public bool FollowVisitor { get; set; } = true;
        /// <summary>
        /// Her rule: the speaker gets a floor ring and the others turn toward them. Set by the turns,
        /// and by any stage that has a companion speak outside them (the lanterns); null for nobody.
        /// </summary>
        /// <summary>
        /// The crowd: the companions walk with the visitor like people moving through a museum - always
        /// beside or a little behind, never in front or in the way, keeping pace smoothly (no jumps).
        /// Their places hang off the visitor's body (<see cref="BodyFrame"/>), so a glance moves nobody.
        /// </summary>
        public bool Crowd { get; set; }

        /// <summary>
        /// The crowd's places: bearing from the body's facing and distance. At +-50 degrees and 1.8 m a
        /// companion is ~1.4 m to the side - clear of the path - and inside a headset's ~100-degree view,
        /// so the visitor sees them at the edge like people walking alongside (Saul, 4 Oct). Measured: at
        /// +-62 to +-80 no companion appeared in any walking frame. The third walks further out on the right.
        /// </summary>
        public static readonly CompanionMarks.Mark[] CrowdPlaces =
            { new CompanionMarks.Mark(-50f, 1.8f), new CompanionMarks.Mark(50f, 1.8f), new CompanionMarks.Mark(64f, 2.6f) };
        public const float CrowdCatchUpSpeed = 3.2f, CrowdCatchUpPerMetre = 1.1f, CrowdArrive = 0.12f;
        /// <summary>Nobody's route passes closer than this to the visitor.</summary>
        public const float PersonalSpace = 0.9f;
        /// <summary>Inside this cone and range ahead of the body, a companion is in the way.</summary>
        public const float InTheWayDegrees = 35f, InTheWayMetres = 2.2f;

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; ab.y = 0f; var ap = p - a; ap.y = 0f;
            var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            var c = a + ab * t; c.y = p.y;
            return Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(c.x, 0f, c.z));
        }

        public string ActiveSpeaker
        {
            get => _active;
            set { _active = value; RingUnder(value); }
        }
        string _active;
        Transform _ring;
        /// <summary>The marks to try for each speaking position; her answer-time marks by default. A
        /// stage can stand the companions elsewhere (the walk puts them on the visitor's flanks).</summary>
        public Func<int, IEnumerable<CompanionMarks.Mark>> MarkCandidates { get; set; } = CompanionMarks.Candidates;
        public IReadOnlyList<string> Ids => _ids;
        public IReadOnlyDictionary<string, Transform> Figures => _figures;
        public TurnTaking Turns { get; private set; }

        /// <summary>The line a master says. Default: a canned line; the journey wires DialogueClient here.</summary>
        public Func<string, string> LineFor = id => Masters.Name(id) + " considers your question.";

        /// <summary>When true (no voice playing lines), a line ends after <see cref="EstimateSeconds"/>.</summary>
        public bool TimeLinesByLength { get; set; } = true;

        public event Action<string, string> LineStarted;   // id, line
        public event Action<string> LineEnded;
        public event Action TurnsFinished;
        public event Action Remarked;

        readonly List<string> _ids = new List<string>();
        readonly Dictionary<string, Transform> _figures = new Dictionary<string, Transform>();
        Vector3 _lastPos;
        float _lastYaw;
        float _lineLeft, _behindFor;
        bool _placedOnce;

        /// <summary>
        /// The companions, in speaking order, each with the transform that stands for them (a rigged
        /// master, or her standee fallback - "a chosen master must never vanish").
        /// </summary>
        public void Set(IReadOnlyList<string> speakingOrder, IReadOnlyDictionary<string, Transform> figures)
        {
            _ids.Clear(); _figures.Clear();
            foreach (var id in speakingOrder)
            {
                if (!figures.TryGetValue(id, out var f) || f == null) continue;
                _ids.Add(id); _figures[id] = f;
            }
            _placedOnce = false;
        }

        /// <summary>Put every companion on its mark around the visitor now.</summary>
        public void PlaceAll()
        {
            if (Head == null) return;
            for (var i = 0; i < _ids.Count; i++)
            {
                var at = MarkPosition(i);
                var to = Head.position - at; to.y = 0f;
                var rot = to.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(to.normalized, Vector3.up);
                _figures[_ids[i]].SetPositionAndRotation(at, rot);
            }
            _lastPos = Head.position; _lastYaw = Head.eulerAngles.y;
            _placedOnce = true;
        }

        /// <summary>The world position of mark <paramref name="order"/>, on the floor, the first unblocked candidate.</summary>
        public Vector3 MarkPosition(int order)
        {
            var eye = Head.position;
            var fwd = Flat(Head.forward);
            var floor = FloorBelow(eye);
            foreach (var m in MarkCandidates(order))
            {
                var dir = Quaternion.Euler(0f, m.Bearing, 0f) * fwd;
                var p = new Vector3(eye.x, floor, eye.z) + dir * m.Distance;
                if (Blocked(new Vector3(eye.x, floor + 1f, eye.z), dir, m.Distance)) continue;   // a wall between
                return p;
            }
            // Every candidate blocked: the stage's own first mark, not her answer-time marks (that fallback
            // put a companion near the middle of the view during the walk).
            var first = CompanionMarks.For(order);
            foreach (var m in MarkCandidates(order)) { first = m; break; }
            return new Vector3(eye.x, floor, eye.z) + Quaternion.Euler(0f, first.Bearing, 0f) * fwd * first.Distance;
        }

        readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Anything solid on the line except the companions themselves.</summary>
        bool Blocked(Vector3 from, Vector3 dir, float distance)
        {
            var n = Physics.RaycastNonAlloc(from, dir, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                var mine = false;
                foreach (var f in _figures.Values) if (t == f || t.IsChildOf(f)) { mine = true; break; }
                if (!mine) return true;
            }
            return false;
        }

        /// <summary>
        /// The floor the companions stand on. Under a floor-tracked XR Origin that is the origin's own
        /// height - exact, and it never hits the visitor's body. Otherwise the first upward-facing
        /// surface below the eye that is not a CharacterController: measured in the Editor, a plain
        /// downward ray struck the rig's own capsule and stood every companion at y 1.31.
        /// </summary>
        float FloorBelow(Vector3 eye)
        {
            var origin = Head != null ? Head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() : null;
            if (origin != null) return origin.transform.position.y;
            var n = Physics.RaycastNonAlloc(eye, Vector3.down, _hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue; var y = eye.y - 1.6f;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider is CharacterController || h.normal.y < 0.7f || h.distance >= best) continue;
                best = h.distance; y = h.point.y;
            }
            return y;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        // ---- turns ---------------------------------------------------------------------

        /// <summary>Each in turn answers. Lines start only when the gaze gate is open.</summary>
        /// <summary>True while a round of turns (or a single line) is under way.</summary>
        public bool Busy => Turns != null && Turns.Current != TurnTaking.Phase.Done;

        /// <summary>
        /// One master says one line (an insight about something the visitor clicked or walked up to),
        /// without touching the group's speaking order. False when the group is already speaking.
        /// </summary>
        public bool Say(string id, string line)
        {
            if (Busy || id == null || !_figures.ContainsKey(id)) return false;
            Turns = new TurnTaking(new[] { id });
            Turns.Started += sid => { _lineLeft = EstimateSeconds(line); ActiveSpeaker = sid; LineStarted?.Invoke(sid, line); };
            Turns.Ended += sid => { if (ActiveSpeaker == sid) ActiveSpeaker = null; LineEnded?.Invoke(sid); };
            Turns.Finished += () => { ActiveSpeaker = null; TurnsFinished?.Invoke(); ConfirmInput.Drop(this); };
            ConfirmInput.Take(this);
            Turns.Begin();
            return true;
        }

        /// <summary>
        /// Several masters, each with their own line, in the given order (the live readings that follow
        /// an insight's opening), without touching the group's speaking order. False when busy.
        /// </summary>
        public bool SayInTurn(IReadOnlyList<KeyValuePair<string, string>> lines)
        {
            if (Busy || lines == null || lines.Count == 0) return false;
            var ids = new List<string>(); var text = new Dictionary<string, string>();
            foreach (var kv in lines) if (_figures.ContainsKey(kv.Key) && !text.ContainsKey(kv.Key)) { ids.Add(kv.Key); text[kv.Key] = kv.Value; }
            if (ids.Count == 0) return false;
            Turns = new TurnTaking(ids);
            Turns.Started += sid => { var line = text[sid]; _lineLeft = EstimateSeconds(line); ActiveSpeaker = sid; LineStarted?.Invoke(sid, line); };
            Turns.Ended += sid => { if (ActiveSpeaker == sid) ActiveSpeaker = null; LineEnded?.Invoke(sid); };
            Turns.Finished += () => { ActiveSpeaker = null; TurnsFinished?.Invoke(); ConfirmInput.Drop(this); };
            ConfirmInput.Take(this);
            Turns.Begin();
            return true;
        }

        public void BeginTurns()
        {
            Turns = new TurnTaking(_ids);
            Turns.Started += id =>
            {
                var line = LineFor?.Invoke(id) ?? string.Empty;
                _lineLeft = EstimateSeconds(line);
                ActiveSpeaker = id;
                LineStarted?.Invoke(id, line);
            };
            Turns.Ended += id => { if (ActiveSpeaker == id) ActiveSpeaker = null; LineEnded?.Invoke(id); };
            Turns.Finished += () => { ActiveSpeaker = null; TurnsFinished?.Invoke(); ConfirmInput.Drop(this); };
            ConfirmInput.Take(this);
            Turns.Begin();
        }

        /// <summary>
        /// How long a line stays up with no voice to time it: READING time in a headset, not speaking
        /// time - turning to find the panel, then reading it. Two words a second plus a second and a
        /// half, and never under <see cref="MinLineSeconds"/>. Measured in Saul's headset test: at
        /// 2.6 words a second Socrates' four words were up for 2.5 s - "super fast, impossible to
        /// read". A skips ahead.
        /// </summary>
        public static float EstimateSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return MinLineSeconds;
            var words = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
            return Mathf.Max(MinLineSeconds, 1.5f + words / 2f);
        }

        public const float MinLineSeconds = 5f;

        /// <summary>
        /// Stop mid-turns (the piece was lifted back out). Raises <see cref="TurnsFinished"/> so the
        /// subtitle goes; listeners that only want a completed round should track their own state.
        /// </summary>
        public void StopTurns()
        {
            if (Turns == null) return;
            Turns = null;
            ConfirmInput.Drop(this);
            TurnsFinished?.Invoke();
        }

        public void LineFinished() => Turns?.LineFinished();

        /// <summary>The angle between the visitor's gaze and a companion, degrees, flat.</summary>
        public float AngleFromGaze(string id)
        {
            if (Head == null || id == null || !_figures.TryGetValue(id, out var f)) return 180f;
            return Vector3.Angle(Flat(Head.forward), Flat(f.position - Head.position));
        }

        public bool Confirm() { Turns?.Advance(); return Turns != null; }
        public bool Redo() => false;

        void Update()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null || _ids.Count == 0) return;

            if (Crowd)
            {
                // A teleport or a chapter arrival: everyone is simply at their place beside the visitor on the
                // next frame, never left standing where the old world put them (blind review, 4 Oct: a figure
                // right in front of the eye after a teleport, blown white by the eye light).
                if (_placedOnce && Vector3.Distance(Flat3(Head.position), Flat3(_lastPos)) > JumpDistance) { SnapCrowd(); Remarked?.Invoke(); }
                CrowdStep(Time.deltaTime); _placedOnce = true;
                _lastPos = Head.position;
            }
            else if (!FollowVisitor) { _placedOnce = true; }
            else if (!_placedOnce) PlaceAll();
            else
            {
                var moved = Vector3.Distance(Flat3(Head.position), Flat3(_lastPos));
                var turned = Mathf.Abs(Mathf.DeltaAngle(Head.eulerAngles.y, _lastYaw));
                // A jump in a single frame is a teleport or a snap turn: re-mark. A slow walk or a
                // glance is not, so nobody shuffles about while the visitor looks round.
                var behind = false;
                foreach (var id in _ids) if (AngleFromGaze(id) > BehindDegrees) behind = true;
                _behindFor = behind ? _behindFor + Time.deltaTime : 0f;
                var speaking = Turns != null && Turns.Current == TurnTaking.Phase.Speaking;
                // Her rule: companions move only when the visitor teleports. Re-marking on a snap turn or when
                // one fell behind the gaze had them jumping every time the visitor rotated (headset test, 3 Oct).
                if (moved > JumpDistance)
                {
                    PlaceAll(); _behindFor = 0f; Remarked?.Invoke();
                }
                _lastPos = Head.position; _lastYaw = Head.eulerAngles.y;
            }

            if (!Crowd || !_walking) Face(Time.deltaTime);
            if (Turns == null) return;
            Turns.Tick(Time.deltaTime, FollowVisitor ? AngleFromGaze(Turns.Speaker) : 0f);
            if (TimeLinesByLength && Turns.Current == TurnTaking.Phase.Speaking)
            {
                _lineLeft -= Time.deltaTime;
                if (_lineLeft <= 0f) Turns.LineFinished();
            }
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Slow enough to read as a person turning, not a snap.</summary>
        public const float TurnDegreesPerSecond = 90f;

        bool _walking;

        /// <summary>Each companion walks to its place round the body. Someone on the wrong side goes round
        /// behind the visitor, never across the front.</summary>
        void CrowdStep(float dt)
        {
            var body = BodyFrame.Get();
            if (body == null) return;
            body.Step(dt);
            _walking = false;
            for (var i = 0; i < _ids.Count; i++)
            {
                var f = _figures[_ids[i]];
                if (f == null) continue;
                var place = CrowdPlaces[Mathf.Min(i, CrowdPlaces.Length - 1)];
                var target = body.Feet + body.Bearing(place.Bearing) * place.Distance;
                var pos = f.position; var flat = new Vector3(pos.x, body.Feet.y, pos.z);
                // Going round, never across or through: a route that passes in front of the visitor, or
                // within PersonalSpace of them, first heads for a point at their side on the target's side
                // (measured: walking in from behind, one passed 0.2 m from the visitor - through them).
                var rel = flat - body.Feet;
                var ahead = Vector3.Dot(rel, body.Forward) > 0.2f;
                var side = Mathf.Sign(place.Bearing);
                var wrongSide = Mathf.Sign(Vector3.Dot(rel, body.Right)) != side && rel.magnitude < 3f;
                // First of all, out of the way: someone the visitor is walking or turning toward steps
                // aside to whichever side they are already on (measured at a corner: 0.7 m ahead, 43 deg).
                var bearingNow = Vector3.SignedAngle(body.Forward, rel, Vector3.up);
                var inTheWay = Mathf.Abs(bearingNow) < InTheWayDegrees && rel.magnitude < InTheWayMetres;
                if (inTheWay && rel.magnitude < 1.1f)
                {
                    // Close enough to fill the view: out of it at once, not a walk across the visitor's eyes.
                    var aside = body.Feet + body.Bearing(place.Bearing) * place.Distance;
                    f.position = new Vector3(aside.x, body.Feet.y, aside.z);
                    continue;
                }
                if (inTheWay)
                {
                    var out_ = Mathf.Abs(bearingNow) < 3f ? side : Mathf.Sign(bearingNow);
                    target = flat + body.Right * (out_ * 1.3f) - body.Forward * 0.4f;
                }
                else if (ahead && wrongSide) target = body.Feet - body.Forward * 1.3f + body.Right * (side * 0.6f);
                else if (DistanceToSegment(body.Feet, flat, target) < PersonalSpace && (flat - body.Feet).magnitude > PersonalSpace)
                    target = body.Feet + body.Right * (side * (PersonalSpace + 0.4f)) - body.Forward * 0.3f;
                var to = target - flat;
                var d = to.magnitude;
                if (d < CrowdArrive) { f.position = new Vector3(pos.x, body.Feet.y, pos.z); continue; }
                // The visitor's own pace plus a catch-up for the gap: at walking pace alone, someone who
                // starts behind stays behind (measured in the Palace: most of the walk at 150-180 degrees).
                var speed = Mathf.Min(CrowdCatchUpSpeed, body.Velocity.magnitude + CrowdCatchUpPerMetre * d);
                if (d < 0.6f) speed = Mathf.Max(speed, d * 2f);
                if (inTheWay) speed = CrowdCatchUpSpeed;
                var step = Mathf.Min(d, speed * dt);
                var next = flat + to / d * step;
                f.position = new Vector3(next.x, body.Feet.y, next.z);
                if (step / Mathf.Max(dt, 1e-5f) > 0.25f)
                {
                    _walking = true;
                    f.rotation = Quaternion.RotateTowards(f.rotation, Quaternion.LookRotation(to / d, Vector3.up), 240f * dt);
                }
            }
        }

        /// <summary>Everyone straight to their crowd place round the body, facing the way the visitor faces.</summary>
        void SnapCrowd()
        {
            var body = BodyFrame.Get();
            if (body == null) return;
            body.Step(0f);
            for (var i = 0; i < _ids.Count; i++)
            {
                var f = _figures[_ids[i]];
                if (f == null) continue;
                var place = CrowdPlaces[Mathf.Min(i, CrowdPlaces.Length - 1)];
                var at = body.Feet + body.Bearing(place.Bearing) * place.Distance;
                f.SetPositionAndRotation(new Vector3(at.x, body.Feet.y, at.z), Quaternion.LookRotation(body.Forward, Vector3.up));
            }
        }

        void RingUnder(string id)
        {
            if (_ring == null)
            {
                _ring = new GameObject("Speaker Ring").transform;
                _ring.gameObject.AddComponent<MeshFilter>().sharedMesh = Annulus(0.34f, 0.44f);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", new Color(0.78f, 0.6f, 0.3f));   // her gold
                m.SetFloat("_Cull", 0f);
                _ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
            }
            var on = id != null && _figures.TryGetValue(id, out var f) && f != null;
            _ring.gameObject.SetActive(on);
            if (!on) return;
            _ring.SetParent(_figures[id], false);
            _ring.localPosition = new Vector3(0f, 0.03f, 0f); _ring.localRotation = Quaternion.identity;
            var s = _figures[id].lossyScale.y; _ring.localScale = Vector3.one / (s > 1e-4f ? s : 1f);
        }

        static Mesh Annulus(float inner, float outer)
        {
            const int n = 48;
            var v = new Vector3[(n + 1) * 2]; var t = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i * 2] = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
                if (i < n) { int k = i * 2, j = i * 6; t[j] = k; t[j + 1] = k + 1; t[j + 2] = k + 2; t[j + 3] = k + 1; t[j + 4] = k + 3; t[j + 5] = k + 2; }
            }
            var m = new Mesh { vertices = v, triangles = t, name = "Speaker ring" }; m.RecalculateBounds(); return m;
        }

        /// <summary>On their marks, each companion turns its body (yaw only) towards the visitor.</summary>
        /// <summary>The speaker (and everyone, when nobody speaks) faces the visitor; the others turn toward
        /// the speaker. Turning in place only - nobody walks.</summary>
        void Face(float dt)
        {
            _figures.TryGetValue(_active ?? string.Empty, out var speaker);
            foreach (var id in _ids)
            {
                var f = _figures[id];
                if (f == null) continue;
                var target = speaker != null && f != speaker ? speaker.position : Head.position;
                var to = target - f.position; to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) continue;
                f.rotation = Quaternion.RotateTowards(f.rotation, Quaternion.LookRotation(to.normalized, Vector3.up),
                                                      TurnDegreesPerSecond * dt);
            }
        }
    }
}
