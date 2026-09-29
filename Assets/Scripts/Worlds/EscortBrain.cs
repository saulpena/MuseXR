using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>What an escorting painter is doing.</summary>
    public enum EscortMode
    {
        /// <summary>Standing. The default: nothing the visitor does short of walking away moves them.</summary>
        Settled,
        /// <summary>The visitor walked off; following a slot behind them.</summary>
        Trailing,
        /// <summary>Walking to a chosen spot at the edge of the visitor's view.</summary>
        Repositioning,
    }

    /// <summary>Where a painter sits in the visitor's view.</summary>
    public enum ViewZone
    {
        /// <summary>Close and near the centre of the gaze: the visitor is addressing them.</summary>
        LookedAt,
        /// <summary>Near the centre of the gaze but not being addressed: in front of whatever is.</summary>
        InTheWay,
        /// <summary>Visible at the edge of the view. Where they should be.</summary>
        Peripheral,
        /// <summary>Behind the visitor's shoulder.</summary>
        OutOfView,
    }

    /// <summary>
    /// Every tuning number for the escort in one place.
    ///
    /// These were chosen, not measured, except where a comment says otherwise. They are exposed on
    /// <see cref="PainterEscort"/> so they can be judged in a headset.
    /// </summary>
    [System.Serializable]
    public class EscortSettings
    {
        [Tooltip("How far the visitor may wander from where the painters last formed round them " +
                 "before they follow. The setting that stops them moving with every step.")]
        public float followDistance = 2.2f;

        [Tooltip("While holding ground (in a conversation), the visitor must go this far before the " +
                 "painters follow. Talking to someone is not a reason to trail after every step " +
                 "(Saul, 29 Sep 2026: stay in place unless I move away a lot more).")]
        public float holdFollowDistance = 4.5f;

        [Tooltip("Trailing slot while the visitor walks: this far behind, along the direction of travel.")]
        public float trailBack = 1.8f;

        [Tooltip("Trailing slot: this far to the side, so the two walk side by side, not in single file.")]
        public float trailSide = 0.75f;

        [Tooltip("Nobody stands closer to the visitor than this, metres.")]
        public float personalSpace = 1.2f;

        [Tooltip("The two painters never stand closer to each other than this, metres.")]
        public float separation = 1.0f;

        [Tooltip("Preferred distance from the visitor when standing, metres, and the range tried.")]
        public float restDistance = 2.3f;
        public float minRestDistance = 1.8f;
        public float maxRestDistance = 3.0f;

        [Tooltip("Preferred angle off the gaze when standing, degrees, and the range tried. A headset " +
                 "sees ~50 degrees either side; at 52 a painter was cut in half by the frame edge in a " +
                 "100-degree render (28 Sep 2026), so they stand nearer 42.")]
        public float peripheralAngle = 42f;
        public float minRestAngle = 34f;
        public float maxRestAngle = 55f;

        [Tooltip("Inside this angle off the gaze a painter is in front of what the visitor looks at.")]
        public float centralCone = 30f;

        [Tooltip("Inside this angle AND range, the visitor is looking at the painter, not past them.")]
        public float lookedAtCone = 14f;
        public float lookedAtRange = 3.5f;

        [Tooltip("Beyond this angle off the gaze a painter cannot be seen. A headset shows about 50 " +
                 "degrees either side; at 95 a painter standing at 93 counted as 'in view' in Play Mode.")]
        public float outOfViewAngle = 70f;

        [Tooltip("Seconds out of view, with the visitor standing still, before stepping back into view. " +
                 "A glance round is shorter than this, so glances move nobody.")]
        public float outOfViewDelay = 2.5f;

        [Tooltip("Seconds in front of the visitor's gaze before stepping aside.")]
        public float inTheWayDelay = 1.0f;

        [Tooltip("With the conversation panel open, seconds before a painter near the centre of the " +
                 "view steps aside. Short: the panel opens exactly where the visitor pointed, which " +
                 "is where that painter stands.")]
        public float clearCentreDelay = 0.3f;

        [Tooltip("Below this head speed, m/s, the visitor counts as standing.")]
        public float stillSpeed = 0.25f;

        [Tooltip("Seconds the visitor must stand before trailing painters come round to the side.")]
        public float stillDelay = 0.6f;

        [Tooltip("Further than this from the visitor (a teleport), a painter reappears behind them.")]
        public float warpDistance = 12f;

        [Tooltip("Close enough to a destination to count as arrived, metres.")]
        public float arriveRadius = 0.2f;

        [Tooltip("A trailing slot must move this far before the path is re-planned, metres.")]
        public float retargetStep = 0.5f;

        [Tooltip("Never stand within this distance of a work of art, metres.")]
        public float artClearance = 1.5f;

        [Tooltip("Never stand between the visitor and a work of art within this angle of the line to it.")]
        public float artSightCone = 15f;
    }

    /// <summary>What the escort sees this frame. Positions are floor-plane XZ.</summary>
    public struct EscortInput
    {
        public Vector2 visitor;
        /// <summary>Head yaw, degrees, Unity convention (0 = +Z, 90 = +X).</summary>
        public float gazeYaw;
        /// <summary>Direction of travel, degrees. Only read while trailing.</summary>
        public float heading;
        /// <summary>Visitor's horizontal head speed, m/s.</summary>
        public float visitorSpeed;
        public Vector2 self;
        /// <summary>Other painters: their destination if walking, else where they stand.</summary>
        public Vector2[] others;
        /// <summary>Works of art and other things the visitor may be looking at.</summary>
        public Vector2[] art;
        /// <summary>A painter who is talking holds their ground unless the visitor walks off.</summary>
        public bool holdGround;
        /// <summary>
        /// The visitor is looking at something that needs the centre of the view - the
        /// conversation panel. Standing anywhere near the gaze, even being looked at, is then in the
        /// way, and the painter steps aside at once rather than after the usual delay.
        /// </summary>
        public bool clearCentre;
        public float dt;
    }

    /// <summary>What the escort wants done this frame.</summary>
    public struct EscortOutput
    {
        public EscortMode mode;
        public ViewZone zone;
        /// <summary>True while the painter should be walking to <see cref="destination"/>.</summary>
        public bool move;
        public Vector2 destination;
        /// <summary>Place the painter at <see cref="destination"/> instantly, out of sight.</summary>
        public bool warp;
        /// <summary>The visitor is looking at this painter from close by.</summary>
        public bool listening;
    }

    /// <summary>
    /// Decides, frame by frame, where one painter walking with the visitor should be.
    ///
    /// Engine-free apart from Unity's vector maths, so the behaviour is tested in EditMode by
    /// simulation rather than by wearing a headset. <see cref="PainterEscort"/> turns the output
    /// into NavMesh paths and animation.
    ///
    /// <b>The rules, in the visitor's words (Saul, 28 Sep 2026):</b>
    ///   * follow me into every world, but do not get in the way;
    ///   * only move after I have moved some distance, not every inch, and never because I turned;
    ///   * if I turn a lot, come back into the edge of my view, but not in front of the art or
    ///     whatever I am looking at;
    ///   * I can turn to them and talk.
    ///
    /// Hence hysteresis, not tracking. <see cref="CompanionParty"/> learned the same lesson on a
    /// headset on 16 Sep: a head never truly stops, so anything that tracks it every frame feels
    /// attached to it.
    /// </summary>
    public class EscortBrain
    {
        readonly EscortSettings _s;
        readonly float _side;

        EscortMode _mode = EscortMode.Settled;
        Vector2 _anchor;
        Vector2 _destination;
        bool _seeded;
        float _stillFor, _outOfViewFor, _inTheWayFor, _centreFor;

        /// <param name="side">-1 prefers the visitor's left, +1 the right. Keeps two painters apart
        /// without negotiation, and makes each one's place predictable.</param>
        public EscortBrain(EscortSettings settings, float side)
        {
            _s = settings ?? new EscortSettings();
            _side = side >= 0f ? 1f : -1f;
        }

        /// <summary>Optional: is a floor point somewhere a painter can stand? The component supplies
        /// a NavMesh query; left null, everywhere is.</summary>
        public System.Func<Vector2, bool> Walkable;

        public EscortMode Mode => _mode;
        public Vector2 Destination => _destination;
        public float Side => _side;

        public EscortOutput Tick(in EscortInput input)
        {
            if (!_seeded)
            {
                _seeded = true;
                _anchor = input.visitor;
                _destination = input.self;
            }

            _stillFor = input.visitorSpeed < _s.stillSpeed ? _stillFor + input.dt : 0f;

            float toVisitor = Vector2.Distance(input.self, input.visitor);
            float angle = ViewAngle(input.visitor, input.gazeYaw, input.self);
            var zone = Classify(angle, toVisitor, _s);
            bool warp = false;

            if (toVisitor > _s.warpDistance)
            {
                // A teleport, or the visitor ran on far ahead. Walking 12 m to catch up would read as
                // a chase; reappearing behind them, where they cannot see it, reads as nothing.
                _destination = TrailSlot(input.visitor, input.heading, _side, _s);
                _mode = EscortMode.Trailing;
                warp = true;
            }
            else
            {
            float follow = input.holdGround ? _s.holdFollowDistance : _s.followDistance;
            // Asked to hold ground mid-walk (a question was asked while stepping aside): stop now,
            // or the talk animation never plays - a walking painter's Speed wins the Animator.
            if (input.holdGround && _mode == EscortMode.Repositioning &&
                Vector2.Distance(input.visitor, _anchor) <= follow)
                Settle(input.visitor);

            switch (_mode)
            {
                case EscortMode.Settled:
                    if (Vector2.Distance(input.visitor, _anchor) > follow)
                    {
                        _mode = EscortMode.Trailing;
                        _destination = TrailSlot(input.visitor, input.heading, _side, _s);
                        break;
                    }

                    // Out-of-view time only counts while the visitor stands: it is the "turned a
                    // lot" rule, and a painter left at the shoulder by a short walk must not start
                    // strolling the instant the visitor stops.
                    bool standing = input.visitorSpeed < _s.stillSpeed;
                    _outOfViewFor = zone == ViewZone.OutOfView && standing ? _outOfViewFor + input.dt : 0f;
                    _inTheWayFor = zone == ViewZone.InTheWay ? _inTheWayFor + input.dt : 0f;

                    if (input.holdGround) break;

                    bool crowded = toVisitor < _s.personalSpace * 0.75f;
                    bool inTheWay = _inTheWayFor > _s.inTheWayDelay;
                    if (input.clearCentre && (zone == ViewZone.LookedAt || zone == ViewZone.InTheWay))
                    {
                        _centreFor += input.dt;
                        if (_centreFor > _s.clearCentreDelay) inTheWay = true;
                    }
                    else _centreFor = 0f;
                    bool lost = _outOfViewFor > _s.outOfViewDelay;
                    if (crowded || inTheWay || lost) BeginReposition(input);
                    break;

                case EscortMode.Trailing:
                {
                    var slot = TrailSlot(input.visitor, input.heading, _side, _s);
                    if (Vector2.Distance(slot, _destination) > _s.retargetStep) _destination = slot;
                    if (_stillFor > _s.stillDelay) BeginReposition(input);
                    break;
                }

                case EscortMode.Repositioning:
                    if (Vector2.Distance(input.visitor, _anchor) > follow)
                    {
                        _mode = EscortMode.Trailing;
                        _destination = TrailSlot(input.visitor, input.heading, _side, _s);
                    }
                    else if (Vector2.Distance(input.self, _destination) <= _s.arriveRadius)
                    {
                        Settle(input.visitor);
                    }
                    else if (_stillFor > _s.stillDelay &&
                             ViewAngle(input.visitor, input.gazeYaw, _destination) < _s.centralCone)
                    {
                        // The visitor turned while they were walking, and the spot they were heading
                        // for is now in front of them.
                        BeginReposition(input);
                    }
                    break;
            }
            }

            return new EscortOutput
            {
                mode = _mode,
                zone = zone,
                move = _mode != EscortMode.Settled,
                destination = _destination,
                warp = warp,
                listening = _mode == EscortMode.Settled && zone == ViewZone.LookedAt,
            };
        }

        void BeginReposition(in EscortInput input)
        {
            if (PickRestSpot(input.visitor, input.gazeYaw, input.self, _side, input.others, input.art,
                             _s, Walkable, out var spot)
                && Vector2.Distance(spot, input.self) > _s.arriveRadius)
            {
                _anchor = input.visitor;
                _destination = spot;
                _mode = EscortMode.Repositioning;
            }
            else if (_mode == EscortMode.Settled)
            {
                // Nowhere better to stand: stay, and try again after the delays. The anchor must NOT
                // move - found in Play Mode (28 Sep 2026): resetting it on every failed attempt
                // dragged it along with a visitor walking away, and the follow never triggered.
                _outOfViewFor = _inTheWayFor = 0f;
            }
            else Settle(input.visitor);
        }

        void Settle(Vector2 visitor)
        {
            _mode = EscortMode.Settled;
            _anchor = visitor;
            _outOfViewFor = _inTheWayFor = _centreFor = 0f;
        }

        /// <summary>
        /// Replace the destination with one the caller has checked is reachable, e.g. snapped onto
        /// the NavMesh. The brain does not know the floor; the component does.
        /// </summary>
        public void CorrectDestination(Vector2 reachable) => _destination = reachable;

        // ------------------------------------------------------------------ pure geometry

        /// <summary>Unit direction on the floor for a Unity yaw: 0 is +Z, 90 is +X.</summary>
        public static Vector2 YawDirection(float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>Unity yaw, degrees, of the direction from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float YawTo(Vector2 from, Vector2 to)
            => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;

        /// <summary>Degrees between where the visitor looks and where <paramref name="point"/> is, 0-180.</summary>
        public static float ViewAngle(Vector2 visitor, float gazeYaw, Vector2 point)
        {
            if ((point - visitor).sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Abs(Mathf.DeltaAngle(gazeYaw, YawTo(visitor, point)));
        }

        public static ViewZone Classify(float angle, float distance, EscortSettings s)
        {
            if (angle <= s.lookedAtCone && distance <= s.lookedAtRange) return ViewZone.LookedAt;
            if (angle < s.centralCone) return ViewZone.InTheWay;
            if (angle > s.outOfViewAngle) return ViewZone.OutOfView;
            return ViewZone.Peripheral;
        }

        /// <summary>Where to walk while the visitor walks: behind them along their direction of
        /// travel, off to <paramref name="side"/>. Behind is out of view, which is the point.</summary>
        public static Vector2 TrailSlot(Vector2 visitor, float headingYaw, float side, EscortSettings s)
        {
            var fwd = YawDirection(headingYaw);
            var right = new Vector2(fwd.y, -fwd.x);
            return visitor - fwd * s.trailBack + right * (side * s.trailSide);
        }

        /// <summary>
        /// Pick a standing spot at the edge of the visitor's view.
        ///
        /// Candidates fan out over <see cref="EscortSettings.minRestAngle"/>..<see cref="EscortSettings.maxRestAngle"/>
        /// off the gaze on both sides and over the rest-distance range. Rejected outright: within
        /// separation of another painter, within clearance of a work of art, or on the visitor's
        /// line of sight to one, or not walkable. The rest are scored on how far the painter must
        /// walk, how far from the preferred angle and distance, and whether it is their own side.
        /// </summary>
        /// <returns>False when nowhere passes; the painter should then stay where they are.</returns>
        public static bool PickRestSpot(Vector2 visitor, float gazeYaw, Vector2 self, float side,
                                        Vector2[] others, Vector2[] art, EscortSettings s,
                                        System.Func<Vector2, bool> walkable, out Vector2 best)
        {
            if (PickInBand(visitor, gazeYaw, self, side, others, art, s, walkable,
                           s.minRestAngle, s.maxRestAngle, s.minRestDistance, s.maxRestDistance, out best))
                return true;

            // Nowhere in the usual band - a wall, a statue or a doorway fills it (measured: facing the
            // Buddha's back from 1 m, every usual spot was inside its footprint). Take the widest
            // angle still in view and anything just outside personal space before giving up.
            return PickInBand(visitor, gazeYaw, self, side, others, art, s, walkable,
                              s.maxRestAngle, s.outOfViewAngle - 1f, s.personalSpace + 0.1f, s.maxRestDistance, out best);
        }

        static bool PickInBand(Vector2 visitor, float gazeYaw, Vector2 self, float side,
                               Vector2[] others, Vector2[] art, EscortSettings s,
                               System.Func<Vector2, bool> walkable,
                               float minAngle, float maxAngle, float minDistance, float maxDistance,
                               out Vector2 best)
        {
            best = self;
            float bestScore = float.MaxValue;
            bool found = false;

            for (int flip = 0; flip < 2; flip++)
            {
                float sign = flip == 0 ? side : -side;
                // Step 4 degrees but always sample the band's far edge too: in a tight spot the
                // widest angle is often the only one that fits.
                for (float step = minAngle; ; step += 4f)
                {
                    float a = Mathf.Min(step, maxAngle);
                    var dir = YawDirection(gazeYaw + sign * a);
                    for (float d = minDistance; d <= maxDistance + 0.01f; d += 0.2f)
                    {
                        var p = visitor + dir * d;
                        if (!Clear(p, visitor, others, art, s)) continue;
                        if (walkable != null && !walkable(p)) continue;

                        float score = Vector2.Distance(p, self)
                                      + Mathf.Abs(a - s.peripheralAngle) * 0.04f
                                      // Distance matters more than a short walk: at 0.8 painters
                                      // arriving from behind took the nearest ring and stood 1.64 m
                                      // away in Play Mode, close enough to crowd.
                                      + Mathf.Abs(d - s.restDistance) * 2.5f
                                      + (flip == 0 ? 0f : 1.5f);
                        if (score < bestScore) { bestScore = score; best = p; found = true; }
                    }
                    if (a >= maxAngle) break;
                }
            }
            return found;
        }

        /// <summary>True if a painter standing at <paramref name="p"/> is out of everyone's way.</summary>
        public static bool Clear(Vector2 p, Vector2 visitor, Vector2[] others, Vector2[] art, EscortSettings s)
        {
            if (Vector2.Distance(p, visitor) < s.personalSpace) return false;

            if (others != null)
                foreach (var o in others)
                    if (Vector2.Distance(p, o) < s.separation) return false;

            if (art != null)
            {
                float toP = Vector2.Distance(visitor, p);
                float yawP = YawTo(visitor, p);
                foreach (var w in art)
                {
                    if (Vector2.Distance(p, w) < s.artClearance) return false;
                    // Standing between the visitor and the work, near the line to it, blocks it.
                    float toW = Vector2.Distance(visitor, w);
                    if (toP < toW && Mathf.Abs(Mathf.DeltaAngle(yawP, YawTo(visitor, w))) < s.artSightCone)
                        return false;
                }
            }
            return true;
        }
    }
}
