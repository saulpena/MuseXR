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
        /// The crowd's places: bearing from the body's facing and distance. At +-60 degrees and 2 m a
        /// companion is ~1.7 m to the side - clear of the path - at the edge of a headset's ~100-degree view,
        /// like people walking alongside (Saul, 4 Oct: "peripheral view and sides at most"). The third walks
        /// further out on the right. The head can turn 40 degrees before the body follows, so the places are
        /// also kept out of the gaze itself (<see cref="CrowdOrbit.AvoidGaze"/>).
        /// </summary>
        public static readonly CompanionMarks.Mark[] CrowdPlaces =
            { new CompanionMarks.Mark(-42f, 2.0f), new CompanionMarks.Mark(42f, 2.0f), new CompanionMarks.Mark(-26f, 3.4f) };   // Saul, 5 Oct: on screen, off-centre, each in its own place
        public const float CrowdCatchUpSpeed = 3.2f, CrowdCatchUpPerMetre = 1.1f, CrowdArrive = 0.12f;
        /// <summary>Saul, 5 Oct: they ran to their places. A walk (m/s), a little faster the further behind, plus the
        /// visitor's own speed so they keep up; never above <see cref="CrowdWalkMax"/> over the visitor's speed.</summary>
        public const float CrowdWalk = 1.4f, CrowdWalkPerMetre = 0.25f, CrowdWalkMax = 2.0f;

        /// <summary>Which of <see cref="CrowdPlaces"/> each companion keeps. Chosen once from where they stand, the
        /// arrangement with the least walking and nobody crossing the front (Saul, 5 Oct: the third one always cut
        /// across the view to a place on the far side).</summary>
        readonly Dictionary<string, int> _slot = new Dictionary<string, int>();

        int SlotOf(int i) => _slot.TryGetValue(_ids[i], out var s) ? s : Mathf.Min(i, CrowdPlaces.Length - 1);

        /// <summary>Give each companion the place on their own side: of every arrangement, the one with the least
        /// turning round the visitor, any that sweeps in front costing half a circle more.</summary>
        public void AssignSlots()
        {
            var body = BodyFrame.Get();
            if (body == null) return;
            body.Step(0f);
            if (float.IsNaN(_heading)) _heading = GazeYaw(Yaw(body.Forward));
            var n = Mathf.Min(_ids.Count, CrowdPlaces.Length);
            var feet = body.Feet;
            var bearings = new float[n];
            for (var i = 0; i < n; i++)
            {
                var f = _figures[_ids[i]];
                var rel = f != null ? new Vector3(f.position.x - feet.x, 0f, f.position.z - feet.z) : Vector3.zero;
                bearings[i] = rel.sqrMagnitude > 1e-4f ? Yaw(rel) : _heading + CrowdPlaces[i].Bearing;
            }
            int[] best = null; var bestCost = float.MaxValue;
            foreach (var perm in Permutations(CrowdPlaces.Length, n))
            {
                var cost = 0f;
                for (var i = 0; i < n; i++)
                {
                    var target = _heading + CrowdPlaces[perm[i]].Bearing;
                    var d = Mathf.DeltaAngle(bearings[i], target);
                    cost += Mathf.Abs(d) + (Sweeps(bearings[i], d) ? 180f : 0f);
                }
                if (cost < bestCost) { bestCost = cost; best = perm; }
            }
            _slot.Clear();
            for (var i = 0; i < n && best != null; i++) _slot[_ids[i]] = best[i];
        }

        static IEnumerable<int[]> Permutations(int of, int take)
        {
            var used = new bool[of]; var cur = new int[take];
            IEnumerable<int[]> Go(int k)
            {
                if (k == take) { yield return (int[])cur.Clone(); yield break; }
                for (var v = 0; v < of; v++)
                {
                    if (used[v]) continue;
                    used[v] = true; cur[k] = v;
                    foreach (var p in Go(k + 1)) yield return p;
                    used[v] = false;
                }
            }
            return Go(0);
        }

        /// <summary>Where <paramref name="id"/>'s crowd place is now, on the floor (assigns the places if not yet).</summary>
        public Vector3 CrowdPlaceOf(string id)
        {
            if (_slot.Count == 0) AssignSlots();
            var body = BodyFrame.Get();
            var i = _ids.IndexOf(id);
            if (body == null || i < 0) return _figures.TryGetValue(id, out var f) && f != null ? f.position : Vector3.zero;
            var place = CrowdPlaces[SlotOf(i)];
            var yaw = _heading + place.Bearing;
            var at = body.Feet + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Clear(body.Feet, yaw, place.Distance);
            return new Vector3(at.x, GroundAt(at, body.Feet.y), at.z);
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
            _slot.Clear();
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
        /// <summary>
        /// The floor under a companion standing at <paramref name="at"/>, within <see cref="GroundReach"/> of the visitor's
        /// own feet (<paramref name="feetY"/>). They used to stand at exactly the visitor's foot height, so whenever that
        /// was not the floor - the rig dipping below it, a step, a ramp - all three stood waist-deep in it (Saul, 5 Oct:
        /// "why are the masters half body underground?"). The reach keeps a table top or a gap from lifting or dropping
        /// one far; with nothing found they keep the visitor's height, as before.
        /// </summary>
        float GroundAt(Vector3 at, float feetY)
        {
            var top = new Vector3(at.x, feetY + GroundReach + 0.2f, at.z);
            var n = Physics.RaycastNonAlloc(top, Vector3.down, _hits, GroundReach * 2f + 0.4f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue; var y = feetY;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider is CharacterController || h.normal.y < 0.7f) continue;
                if (Mathf.Abs(h.point.y - feetY) > GroundReach) continue;
                // The nearest floor to the visitor's own level, so a shelf just above it does not win over the floor.
                var d = Mathf.Abs(h.point.y - feetY);
                if (d < best) { best = d; y = h.point.y; }
            }
            return y;
        }

        public const float GroundReach = 1.0f;

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

        /// <summary>A line was skipped (A / Next master) while it was being said: its voice should stop now.</summary>
        public static event Action<CompanionGroup> LineSkipped;

        /// <summary>Its owner voices every line it starts (the Gate company, from each figure): nobody else may voice
        /// them too - the shared voice saying the same line a moment later was the echo (Saul, 5 Oct, headset).</summary>
        public bool VoicedByOwner { get; set; }

        public bool Confirm()
        {
            // Saul, 5 Oct, headset: skipped to the next master, the one cut off kept talking over them.
            if (Turns != null && Turns.Current == TurnTaking.Phase.Speaking) LineSkipped?.Invoke(this);
            Turns?.Advance();
            return Turns != null;
        }
        public bool Redo() => false;

        void Update()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null || _ids.Count == 0) return;

            if (Crowd) { /* placed in LateUpdate, after this frame's turn or teleport */ }
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
            Animate(Time.deltaTime);
            if (Turns == null) return;
            Turns.Tick(Time.deltaTime, FollowVisitor ? AngleFromGaze(Turns.Speaker) : 0f);
            if (TimeLinesByLength && Turns.Current == TurnTaking.Phase.Speaking)
            {
                _lineLeft -= Time.deltaTime;
                if (_lineLeft <= 0f) Turns.LineFinished();
            }
        }

        /// <summary>
        /// The crowd is placed after everything else this frame has moved the head - a snap turn, a teleport,
        /// a chapter's arrival - so it never shows for a frame where the view has just turned onto it (blind
        /// review, 4 Oct: Van Gogh at the centre of the frame a snap turn landed on, in five rooms).
        /// </summary>
        void LateUpdate()
        {
            if (!Crowd || Head == null || _ids.Count == 0) return;
            // A teleport or a chapter arrival: everyone is simply at their place beside the visitor, never left
            // standing where the old world put them (blind review, 4 Oct: a figure right in front of the eye
            // after a teleport, blown white by the eye light).
            if (_placedOnce && Vector3.Distance(Flat3(Head.position), Flat3(_lastPos)) > JumpDistance) { SnapCrowd(); FadeInAtPlaces(false); Remarked?.Invoke(); }
            // A group's first placement too: a chapter's new crowd starts where its layout stood them, and walked
            // from there to the visitor - into the Palace they appeared far off and rushed back (Saul, 5 Oct).
            else if (!_placedOnce) { SnapCrowd(); FadeInAtPlaces(true); }
            // Faded out for a crossing that never jumped them (an arrival close to the gate): never left unseen.
            else if (_fadeInAtPlace && Time.time - _fadedAt > 2f) { SnapCrowd(); FadeInAtPlaces(false); }
            // A snap turn jumps the view in one frame: the crowd jumps to its places with it, rather than being
            // caught mid-step across the new view.
            // (a snap turn is a turn, not a move: they stay where they are)
            CrowdStep(Time.deltaTime); _placedOnce = true;
            _lastPos = Head.position;
            _lastGazeYaw = GazeYaw(_lastGazeYaw);
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Slow enough to read as a person turning, not a snap.</summary>
        public const float TurnDegreesPerSecond = 90f;

        bool _walking;
        float _lastGazeYaw;
        /// <summary>A gaze turn bigger than this in one frame is a snap turn (or a teleport's turn), not a head turn.</summary>
        public const float SnapTurnDegrees = 25f;

        /// <summary>
        /// Each companion moves round the visitor to its place beside the body: as a bearing and a radius
        /// about the feet (<see cref="CrowdOrbit"/>), so nobody walks a straight line across the front of the
        /// visitor or through them. A place in the head's gaze is pushed to the side, and anyone the gaze turns
        /// onto slips out of it at once. Measured before (CompanionClearanceProbe at the Gate): in front of the
        /// eye for 2-3.5 s after turns, glances, teleports and a step back, and inside the visitor after a
        /// teleport and a turn.
        /// </summary>
        /// <summary>Re-form round a new facing only after the head has turned this far: turning to face one of them to
        /// talk moves nobody; turning round always does (Saul, 5 Oct).</summary>
        public const float TurnThreshold = 110f;   // Saul, 5 Oct: at 70 turning to a painting brought them walking up into view
        /// <summary>Re-forming, nobody walks through this cone in front of the visitor.</summary>
        public const float FrontCone = 22f;
        public const float CrowdMinRadius = 1.2f;
        float _heading = float.NaN, _settleYaw, _settledFor;
        bool _reforming;
        /// <summary>How long the head must hold still (below StillDegreesPerSecond) before they re-form.</summary>
        public const float SettleSeconds = 0.4f, StillDegreesPerSecond = 20f;
        /// <summary>Just after a re-form, a leftover turn bigger than this finishes it.</summary>
        public const float FinishDegrees = 30f;

        /// <summary>
        /// Saul, 5 Oct - her WebXR companions, translated to VR: each master keeps their own place relative to the
        /// visitor, on screen but off-centre, and never swaps with another. When the visitor moves they move, always.
        /// The places turn with the visitor only past <see cref="TurnThreshold"/>; then everyone goes round to the
        /// new places the way that does not cross in front. A place inside a wall or object is pulled in short of it.
        /// </summary>
        void CrowdStep(float dt)
        {
            var body = BodyFrame.Get();
            if (body == null) return;
            body.Step(dt);
            _walking = false;
            var headYaw = GazeYaw(float.IsNaN(_heading) ? Yaw(body.Forward) : _heading);
            // Saul, 5 Oct: past the threshold they wait until the visitor has STOPPED turning, then go once to the
            // final places - never chasing a turn still in progress.
            if (float.IsNaN(_heading)) { _heading = headYaw; _settleYaw = headYaw; }
            // "Stopped" means the head really is still: turning slower than StillDegreesPerSecond for SettleSeconds.
            var yawSpeed = dt > 1e-5f ? Mathf.Abs(Mathf.DeltaAngle(_settleYaw, headYaw)) / dt : 0f;
            _settleYaw = headYaw;
            _settledFor = yawSpeed < StillDegreesPerSecond ? _settledFor + dt : 0f;
            // From a settled formation only a real turn re-forms them (facing one of them to talk moves nobody); once
            // re-formed mid-turn, a smaller leftover still finishes the job, so a pause halfway never strands them.
            var off = Mathf.Abs(Mathf.DeltaAngle(_heading, headYaw));
            // Never while the visitor is pointing at something: they turned to it, not away from the company.
            if (_settledFor > SettleSeconds && !Pointing() && (off > TurnThreshold || (_reforming && off > FinishDegrees))) { _heading = headYaw; _reforming = true; _settledFor = 0f; }
            else if (_settledFor > SettleSeconds * 4f) _reforming = false;   // properly settled again
            var feet = body.Feet;
            if (_slot.Count != Mathf.Min(_ids.Count, CrowdPlaces.Length)) AssignSlots();
            var visitorSpeed = new Vector3(body.Velocity.x, 0f, body.Velocity.z).magnitude;
            for (var i = 0; i < _ids.Count; i++)
            {
                var f = _figures[_ids[i]];
                if (f == null) continue;
                var place = CrowdPlaces[SlotOf(i)];
                var targetYaw = _heading + place.Bearing;
                var targetR = Clear(feet, targetYaw, place.Distance);
                var rel = new Vector3(f.position.x - feet.x, 0f, f.position.z - feet.z);
                var r = rel.magnitude;
                var a = r > 1e-3f ? Yaw(rel) : targetYaw;
                var da = Mathf.DeltaAngle(a, targetYaw);
                // The short way to the new place, even across the front for a moment (Saul, 5 Oct: going round behind looked wrong).
                var targetPos = feet + Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward * targetR;
                var behind = new Vector3(targetPos.x - f.position.x, 0f, targetPos.z - f.position.z).magnitude;
                // A dead zone (Saul, 5 Oct, headset: they flicked between idle and walking whenever he looked at them). The
                // places hang off the feet, which shift a little as the head turns; a master standing still sets off only
                // when the place is StartWalking away, and once walking goes all the way in before standing again.
                _moving.TryGetValue(_ids[i], out var moving);
                if (!moving && behind < StartWalking) { _speeds[_ids[i]] = 0f; continue; }
                if (moving && behind < ArriveWithin) { _moving[_ids[i]] = false; _speeds[_ids[i]] = 0f; continue; }
                _moving[_ids[i]] = true;
                var step = (Mathf.Min(CrowdWalk + CrowdWalkPerMetre * behind, CrowdWalkMax) + visitorSpeed) * dt;
                var na = a + Mathf.Sign(da) * Mathf.Min(Mathf.Abs(da), step / Mathf.Max(r, CrowdMinRadius) * Mathf.Rad2Deg);
                var nr = Mathf.Max(CrowdMinRadius, Mathf.MoveTowards(Mathf.Max(r, CrowdMinRadius), targetR, step));
                var next = feet + Quaternion.Euler(0f, na, 0f) * Vector3.forward * nr;
                var moved = new Vector3(next.x - f.position.x, 0f, next.z - f.position.z);
                f.position = new Vector3(next.x, GroundAt(next, feet.y), next.z);
                _speeds[_ids[i]] = moved.magnitude / Mathf.Max(dt, 1e-5f);
                if (moved.magnitude / Mathf.Max(dt, 1e-5f) > 0.25f)
                {
                    _walking = true;
                    f.rotation = Quaternion.RotateTowards(f.rotation, Quaternion.LookRotation(moved.normalized, Vector3.up), 240f * dt);
                }
            }
        }

        /// <summary>Whether going from yaw <paramref name="from"/> by <paramref name="delta"/> passes in front of the visitor.</summary>
        bool Sweeps(float from, float delta)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(_heading, from)) < FrontCone) return false;   // already in front: any way out
            var steps = Mathf.CeilToInt(Mathf.Abs(delta) / 5f);
            for (var k = 1; k < steps; k++)
                if (Mathf.Abs(Mathf.DeltaAngle(_heading, from + delta * k / steps)) < FrontCone) return true;
            return false;
        }

        /// <summary>The place's distance, pulled in short of anything solid on the way to it (never the masters or the rig).</summary>
        float Clear(Vector3 feet, float yaw, float distance)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var origin = feet + Vector3.up * 1.0f;
            var hits = Physics.RaycastAll(origin, dir, distance + 0.35f, ~0, QueryTriggerInteraction.Ignore);
            var best = distance;
            foreach (var h in hits)
            {
                var t = h.collider.transform;
                if (Head != null && t.IsChildOf(Head.root)) continue;
                // A panel that follows the visitor is not a wall: its button boxes pulled Socrates in to arm's length
                // every time a painting's reply panel opened (Saul, 5 Oct: "Socrates approached me").
                if (t.GetComponentInParent<FollowVisitor>() != null) continue;
                var mine = false;
                foreach (var fig in _figures.Values) if (fig != null && t.IsChildOf(fig)) { mine = true; break; }
                if (mine) continue;
                best = Mathf.Min(best, h.distance - 0.35f);
            }
            return Mathf.Max(CrowdMinRadius, best);
        }

        static bool Pointing()
        {
            foreach (var p in Pointer.All) if (p.Hovered != null) return true;
            return false;
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>Where the head looks, flat (the body's way when the head looks straight up or down).</summary>
        /// <summary>
        /// The way the visitor is walking (kept when they stop): the cone the crowd keeps clear. Saul, 4 Oct: keyed to
        /// the head, they fled every time he turned to look at them and were "never in my field of view".
        /// </summary>
        float WalkYaw(float fallback)
        {
            var body = BodyFrame.Get();
            if (body != null)
            {
                var v = body.Velocity; v.y = 0f;
                if (v.sqrMagnitude > 0.09f) _walkYaw = Yaw(v);   // above ~0.3 m/s: walking
            }
            return float.IsNaN(_walkYaw) ? fallback : _walkYaw;
        }
        float _walkYaw = float.NaN;

        float GazeYaw(float fallback)
        {
            if (Head == null) return fallback;
            var g = Head.forward; g.y = 0f;
            return g.sqrMagnitude > 1e-4f ? Yaw(g) : fallback;
        }

        /// <summary>Everyone straight to their crowd place round the body - out of the gaze - facing the way the visitor faces.</summary>
        /// <summary>How long the companions take to fade out at a gate and in again at their places beyond it.</summary>
        public const float CrossFadeOut = 0.35f, CrossFadeIn = 0.6f;
        bool _fadeInAtPlace;
        float _fadedAt;

        /// <summary>
        /// Crossing into another world: the companions fade out here and fade in at their places beside the
        /// visitor once they have arrived (the next snap), never popping out and in, never walking over from
        /// where the old world left them (Saul, 5 Oct).
        /// </summary>
        public void FadeOutForCrossing()
        {
            foreach (var f in _figures.Values) if (f != null && f.gameObject.activeInHierarchy) Appear.Out(f.gameObject, CrossFadeOut);
            _fadeInAtPlace = true; _fadedAt = Time.time;
        }

        void FadeInAtPlaces(bool first)
        {
            if (!first && !_fadeInAtPlace) return;
            _fadeInAtPlace = false;
            // A first placement brings up the figures still hidden (a layout's marks); one already showing is
            // left alone rather than flickered back to nothing.
            foreach (var f in _figures.Values) if (f != null && (!first || !f.gameObject.activeSelf)) Appear.In(f.gameObject, CrossFadeIn);
        }

        /// <summary>
        /// A chapter's masters' marks ("Mark monet" ...), hidden until a group takes them, so nobody is seen standing
        /// at the layout's fixed spots or running from them to the visitor: the group places them and fades them in.
        /// The chapter prefabs carry the marks baked and showing; the journey hides them as it starts.
        /// </summary>
        public static void HideMasterMarks(Transform root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var id in Masters.Row)
                    if (t.name == "Mark " + id) { t.gameObject.SetActive(false); break; }
        }

        /// <summary>Every crowd walking with the visitor fades out for a crossing (a chapter gate's Crossed).</summary>
        public static void FadeOutAllForCrossing()
        {
            foreach (var g in FindObjectsByType<CompanionGroup>(FindObjectsSortMode.None))
                if (g.isActiveAndEnabled && g.Crowd) g.FadeOutForCrossing();
        }

        void SnapCrowd()
        {
            var body = BodyFrame.Get();
            if (body == null) return;
            body.Step(0f);
            _heading = GazeYaw(Yaw(body.Forward));
            if (_slot.Count == 0) AssignSlots();   // a teleport keeps everyone's side
            for (var i = 0; i < _ids.Count; i++)
            {
                var f = _figures[_ids[i]];
                if (f == null) continue;
                var place = CrowdPlaces[SlotOf(i)];
                var yaw = _heading + place.Bearing;
                var at = body.Feet + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Clear(body.Feet, yaw, place.Distance);
                f.SetPositionAndRotation(new Vector3(at.x, GroundAt(at, body.Feet.y), at.z), Quaternion.LookRotation(-(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward), Vector3.up));
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
        static readonly int TalkingParam = Animator.StringToHash("Talking"), SpeedParam = Animator.StringToHash("Speed");
        readonly Dictionary<string, Animator> _anims = new Dictionary<string, Animator>();
        readonly Dictionary<string, float> _speeds = new Dictionary<string, float>();
        /// <summary>Who is on their way to their place: set off past StartWalking, stood again within ArriveWithin.</summary>
        readonly Dictionary<string, bool> _moving = new Dictionary<string, bool>();
        public const float StartWalking = 0.45f, ArriveWithin = 0.08f;

        /// <summary>
        /// Saul, 5 Oct: the speaker plays a talking animation (and faces the visitor - <see cref="Face"/>), the others
        /// go back to idle; walking plays the walk. The masters' Painter controller has Talking and Speed for this.
        /// </summary>
        void Animate(float dt)
        {
            foreach (var id in _ids)
            {
                if (!_figures.TryGetValue(id, out var f) || f == null) continue;
                if (!_anims.TryGetValue(id, out var an) || an == null) { an = f.GetComponentInChildren<Animator>(); _anims[id] = an; }
                if (an == null || an.runtimeAnimatorController == null) continue;
                _speeds.TryGetValue(id, out var v);
                // Walking only while actually on the way (the dead zone above), never on a frame's jitter; a master
                // speaking stays in their talking pose unless they are really moving.
                _moving.TryGetValue(id, out var onTheWay);
                var walking = Crowd && onTheWay && v > 0.1f;
                foreach (var p in an.parameters)
                {
                    if (p.nameHash == TalkingParam) an.SetBool(TalkingParam, id == _active && !walking);
                    else if (p.nameHash == SpeedParam) an.SetFloat(SpeedParam, walking ? Mathf.Clamp(v, 0f, 2f) : 0f, 0.15f, dt);
                }
            }
        }

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
